#include "app.h"
#include "adc.h"
#include "dac.h"
#include "spi.h"
#include "st7789.h"
#include "usart.h"
#include "usbd_cdc_if.h"
#include "w25q32.h"
#if APP_ENABLE_WAVEFORM_TEST
#include "bench_waveform.h"
#endif
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define RESTART_PULSE_US            1U
#define FLASH_LOG_START             0x000000UL
#define FLASH_LOG_MAX               W25Q32_TEST_SECTOR_ADDRESS
#define SAMPLE_FLAG_LOOPBACK_BAD    0x0001U
#define SAMPLE_FLAG_INTERNAL_ADC    0x0002U
#define COMMAND_BUFFER_SIZE         96U
#define USB_RX_QUEUE_SIZE           256U
#define ADC_TRACE_LENGTH            2048U
#define ADC_MIN_RATE_HZ             100U
#define ADC_MAX_RATE_HZ             20000U

static volatile AppStatus status;
static volatile AppSample sample_queue[APP_SAMPLE_QUEUE_LENGTH];
static volatile uint16_t sample_head;
static volatile uint16_t sample_tail;
static uint32_t histogram[APP_HISTOGRAM_BINS];
static uint16_t spi_tx_word = APP_ADC_LOOPBACK_WORD;
static uint16_t spi_rx_word;
static uint32_t restart_cycles;
static uint32_t internal_adc_period_cycles;
static uint32_t internal_adc_next_cycle;
static uint16_t adc_trace[ADC_TRACE_LENGTH];
static uint16_t adc_trace_head;
static uint16_t adc_trace_count;
static uint32_t restart_train_period_cycles;
static uint32_t restart_train_next_cycle;
static uint8_t internal_adc_ready;

static volatile uint8_t usb_rx_queue[USB_RX_QUEUE_SIZE];
static volatile uint16_t usb_rx_head;
static volatile uint16_t usb_rx_tail;
static char command_buffer[COMMAND_BUFFER_SIZE];
static uint16_t command_length;

static uint8_t flash_id[3];
static uint8_t flash_present;
static uint8_t flash_prepared;
static uint32_t flash_write_address;
static uint32_t flash_log_limit;
static uint8_t flash_page[W25Q32_PAGE_SIZE];
static uint16_t flash_page_used;
static uint16_t flash_page_capacity = W25Q32_PAGE_SIZE;

static uint8_t lcd_enabled;
static uint32_t lcd_last_update;

static void output(const char *text)
{
  uint16_t length;
  if (text == NULL) return;
  length = (uint16_t)strlen(text);
  (void)HAL_UART_Transmit(&huart2, (uint8_t *)text, length, 50U + length);
  (void)CDC_Write((const uint8_t *)text, length);
}

static void led_set(uint8_t red, uint8_t green, uint8_t blue)
{
  /* The development-board RGB LED is common-anode: GPIO low means on. */
  HAL_GPIO_WritePin(GPIOC, GPIO_PIN_2, red ? GPIO_PIN_RESET : GPIO_PIN_SET);
  HAL_GPIO_WritePin(GPIOC, GPIO_PIN_1, green ? GPIO_PIN_RESET : GPIO_PIN_SET);
  HAL_GPIO_WritePin(GPIOC, GPIO_PIN_0, blue ? GPIO_PIN_RESET : GPIO_PIN_SET);
}

static void precise_restart_pulse(void)
{
  uint32_t begin;
  HAL_GPIO_WritePin(PH_RESTART_GPIO_Port, PH_RESTART_Pin, GPIO_PIN_SET);
  begin = DWT->CYCCNT;
  while ((uint32_t)(DWT->CYCCNT - begin) < restart_cycles)
  {
    __NOP();
  }
  HAL_GPIO_WritePin(PH_RESTART_GPIO_Port, PH_RESTART_Pin, GPIO_PIN_RESET);
}

static void acquisition_start(void)
{
  status.acquisition_running = 1U;
  internal_adc_next_cycle = DWT->CYCCNT + internal_adc_period_cycles;
  led_set(0U, 1U, 0U);
}

static void acquisition_stop(void)
{
  uint32_t started = HAL_GetTick();
  status.acquisition_running = 0U;
  while ((status.spi_dma_busy != 0U) &&
         ((uint32_t)(HAL_GetTick() - started) < 20U))
  {
    __NOP();
  }
  if (status.spi_dma_busy != 0U)
  {
    (void)HAL_SPI_Abort(&hspi1);
    status.spi_dma_busy = 0U;
    status.spi_errors++;
  }
  led_set(0U, 0U, 1U);
}

static void set_dac_code(uint16_t code)
{
  if (code > 4095U) code = 4095U;
#if APP_ENABLE_WAVEFORM_TEST
  (void)BenchWave_SetStaticCode(code);
#else
  (void)HAL_DAC_SetValue(&hdac1, DAC_CHANNEL_1, DAC_ALIGN_12B_R, code);
#endif
  status.threshold_dac_code = code;
}

static uint16_t threshold_mv_to_dac_code(uint32_t threshold_mv)
{
  /* Digital-board divider: 9.1 kOhm series and 1.0 kOhm to AGND. */
  /* V_DAC = V_THRESHOLD * 10.1; denominator is 3300 mV * 10. */
  uint32_t code = (threshold_mv * 101UL * 4095UL + 16500UL) / 33000UL;
  return (uint16_t)((code > 4095UL) ? 4095UL : code);
}

static void flash_flush_page(void)
{
  uint32_t available;
  uint16_t write_length;
  if ((flash_page_used == 0U) || (status.flash_logging == 0U)) return;
  available = flash_log_limit - flash_write_address;
  write_length = flash_page_used;
  if (available < write_length) write_length = (uint16_t)available;
  if ((write_length > 0U) &&
      (W25Q32_PageProgram(flash_write_address, flash_page, write_length) == HAL_OK))
  {
    flash_write_address += write_length;
    status.flash_records += write_length / sizeof(AppSample);
  }
  else
  {
    status.flash_logging = 0U;
    led_set(1U, 0U, 0U);
  }
  flash_page_used = 0U;
  flash_page_capacity = (uint16_t)(W25Q32_PAGE_SIZE -
      (flash_write_address & (W25Q32_PAGE_SIZE - 1U)));
  if (flash_write_address >= flash_log_limit) status.flash_logging = 0U;
}

static void flash_append(const AppSample *sample)
{
  if ((status.flash_logging == 0U) || (sample == NULL)) return;
  if ((flash_page_used + sizeof(*sample)) > flash_page_capacity) flash_flush_page();
  if (status.flash_logging == 0U) return;
  memcpy(&flash_page[flash_page_used], sample, sizeof(*sample));
  flash_page_used += sizeof(*sample);
  if (flash_page_used == flash_page_capacity) flash_flush_page();
}

static void process_sample(const AppSample *sample)
{
  uint16_t bin;
  status.last_sample = sample->adc_code;
  status.processed++;
  bin = sample->adc_code >> 6;
  histogram[bin]++;
  flash_append(sample);
}

static void show_status(void)
{
  char line[512];
  uint32_t queue_depth = (uint16_t)(sample_head - sample_tail) & (APP_SAMPLE_QUEUE_LENGTH - 1U);
  (void)snprintf(line, sizeof(line),
      "fw=%s source=%s run=%u busy=%u loop=%u threshold_dac=%u last16=%u\r\n"
      "irq=%lu dma=%lu processed=%lu queue=%lu overrun=%lu busy_drop=%lu\r\n"
      "dma_start_err=%lu spi_err=%lu loop_err=%lu adc_raw=%u adc_rate=%u adc_n=%lu adc_err=%lu adc_miss=%lu\r\n"
      "restart_train=%u flash=%02X-%02X-%02X log=%u records=%lu addr=0x%06lX\r\n",
      APP_FW_VERSION, status.source ? "internal" : "ad7980",
      status.acquisition_running, status.spi_dma_busy,
      status.loopback_check, status.threshold_dac_code, status.last_sample,
      (unsigned long)status.busy_irqs, (unsigned long)status.dma_completed,
      (unsigned long)status.processed, (unsigned long)queue_depth,
      (unsigned long)status.queue_overruns, (unsigned long)status.adc_while_busy,
      (unsigned long)status.dma_start_errors, (unsigned long)status.spi_errors,
      (unsigned long)status.loopback_errors, status.internal_adc_raw,
      status.internal_adc_rate_hz, (unsigned long)status.internal_adc_samples,
      (unsigned long)status.internal_adc_errors,
      (unsigned long)status.internal_adc_schedule_misses,
      status.restart_train_hz, flash_id[0], flash_id[1], flash_id[2],
      status.flash_logging, (unsigned long)status.flash_records,
      (unsigned long)flash_write_address);
  output(line);
}

static void show_help(void)
{
  output(
    "Commands:\r\n"
    "  status                    acquisition and error counters\r\n"
    "  source internal|ad7980    select PC3 ADC or external AD7980\r\n"
    "  start | stop              enable/disable selected acquisition source\r\n"
    "  loopback on|off           expect PA7->PA6 value 0xA55A\r\n"
    "  dac <0..4095>             set raw PA4 DAC code\r\n"
    "  threshold <mV>            set post-divider threshold (0..327 mV)\r\n"
#if APP_ENABLE_WAVEFORM_TEST
    "  wave exp <mV> <us>        1 kHz exponential, reaches 1% within us\r\n"
    "  wave hold <mV> <us> <p>  1 kHz hold then linear droop to p permille\r\n"
    "  wave stop|status          stop or inspect PA4 waveform\r\n"
    "  capture [dump <n>]        2 MSPS PC3 capture/analyze or CSV dump\r\n"
    "  calibrate                 PA4-to-PC3 static DAC/ADC calibration\r\n"
#endif
    "  adc rate <100..20000>     internal ADC sample rate in samples/s\r\n"
    "  adc stats|clear|dump <n>  inspect recent PC3 samples (n<=512)\r\n"
    "  restart                   generate one 1 us PA1 pulse\r\n"
    "  restart train <Hz|off>    repeated PA1 pulses, 1..1000 Hz\r\n"
    "  hist clear|dump           clear or dump non-zero 1024-bin spectrum\r\n"
    "  flash id                  read W25Q32 JEDEC ID\r\n"
    "  flash test                destructive test of LAST 4 KiB sector only\r\n"
    "  flash prepare <sectors>   erase log area from address 0\r\n"
    "  flash dump <start> <n>    CSV dump, n=1..512 records\r\n"
    "  log on|off                append 8-byte sample records to prepared area\r\n"
    "  lcd on|off|refresh        initialize/disable/update ST7789\r\n"
    "  counters clear            clear runtime counters\r\n");
}

static void dump_histogram(void)
{
  uint32_t i;
  char line[48];
  uint8_t was_running = status.acquisition_running;
  acquisition_stop();
  output("bin,count\r\n");
  for (i = 0U; i < APP_HISTOGRAM_BINS; ++i)
  {
    if (histogram[i] != 0U)
    {
      (void)snprintf(line, sizeof(line), "%lu,%lu\r\n",
                     (unsigned long)i, (unsigned long)histogram[i]);
      output(line);
      CDC_Task();
    }
  }
  output("end\r\n");
  if (was_running) acquisition_start();
}

static void flash_dump(uint32_t start_record, uint32_t record_count)
{
  uint32_t i;
  uint32_t maximum_records = FLASH_LOG_MAX / sizeof(AppSample);
  uint8_t was_running = status.acquisition_running;
  uint8_t was_logging = status.flash_logging;
  char line[96];
  AppSample sample;

  if ((record_count == 0U) || (record_count > 512U) ||
      (start_record >= maximum_records) ||
      (record_count > maximum_records - start_record))
  {
    output("ERR dump count must be 1..512 and address must fit flash\r\n");
    return;
  }
  acquisition_stop();
  if (was_logging) flash_flush_page();
  status.flash_logging = 0U;
  output("record,timestamp_cycles,adc_code,flags\r\n");
  for (i = 0U; i < record_count; ++i)
  {
    if (W25Q32_Read((start_record + i) * sizeof(AppSample),
                    (uint8_t *)&sample, sizeof(sample)) != HAL_OK)
    {
      output("ERR flash read failed\r\n");
      break;
    }
    (void)snprintf(line, sizeof(line), "%lu,%lu,%u,0x%04X\r\n",
                   (unsigned long)(start_record + i),
                   (unsigned long)sample.timestamp_cycles,
                   sample.adc_code, sample.flags);
    output(line);
    CDC_Task();
  }
  output("end\r\n");
  status.flash_logging = (was_logging && (flash_write_address < flash_log_limit)) ? 1U : 0U;
  if (was_running) acquisition_start();
}

static void flash_prepare(uint32_t sectors)
{
  uint32_t i;
  uint8_t was_running;
  char line[80];
  if ((!flash_present) || (sectors == 0U) ||
      (sectors > (FLASH_LOG_MAX / W25Q32_SECTOR_SIZE)))
  {
    output("ERR invalid sector count or flash absent\r\n");
    return;
  }
  was_running = status.acquisition_running;
  acquisition_stop();
  status.flash_logging = 0U;
  flash_page_used = 0U;
  flash_page_capacity = W25Q32_PAGE_SIZE;
  flash_prepared = 0U;
  for (i = 0U; i < sectors; ++i)
  {
    if (W25Q32_SectorErase(FLASH_LOG_START + i * W25Q32_SECTOR_SIZE) != HAL_OK)
    {
      output("ERR flash erase failed\r\n");
      if (was_running) acquisition_start();
      return;
    }
  }
  flash_write_address = FLASH_LOG_START;
  flash_log_limit = sectors * W25Q32_SECTOR_SIZE;
  status.flash_records = 0U;
  flash_prepared = 1U;
  (void)snprintf(line, sizeof(line), "OK prepared %lu sectors (%lu bytes)\r\n",
                 (unsigned long)sectors, (unsigned long)flash_log_limit);
  output(line);
  if (was_running) acquisition_start();
}

static void set_source(uint8_t internal)
{
  uint8_t was_running = status.acquisition_running;
  acquisition_stop();
  status.source = internal ? 1U : 0U;
  if (was_running) acquisition_start();
}

static void adc_trace_clear(void)
{
  adc_trace_head = 0U;
  adc_trace_count = 0U;
}

static void adc_trace_push(uint16_t raw)
{
  adc_trace[adc_trace_head] = raw;
  adc_trace_head = (adc_trace_head + 1U) & (ADC_TRACE_LENGTH - 1U);
  if (adc_trace_count < ADC_TRACE_LENGTH) adc_trace_count++;
}

static HAL_StatusTypeDef read_internal_adc(uint16_t *raw)
{
#if APP_ENABLE_WAVEFORM_TEST
  return BenchWave_ReadSingle(raw);
#else
  HAL_StatusTypeDef result;
  if ((raw == NULL) || (HAL_ADC_Start(&hadc1) != HAL_OK)) return HAL_ERROR;
  result = HAL_ADC_PollForConversion(&hadc1, 1U);
  if (result == HAL_OK) *raw = (uint16_t)HAL_ADC_GetValue(&hadc1);
  (void)HAL_ADC_Stop(&hadc1);
  return result;
#endif
}

#if APP_ENABLE_WAVEFORM_TEST
static const char *wave_mode_name(BenchWaveMode mode)
{
  if (mode == BENCH_WAVE_EXPONENTIAL) return "exp";
  if (mode == BENCH_WAVE_PEAK_HOLD) return "hold";
  if (mode == BENCH_WAVE_STATIC) return "static";
  return "off";
}

static void waveform_command(char *argument)
{
  unsigned long amplitude;
  unsigned long duration;
  unsigned long end_permille;
  char line[144];
  if ((argument == NULL) || (strcmp(argument, "status") == 0))
  {
    (void)snprintf(line, sizeof(line),
        "wave=%s dac_rate=%luHz adc_capture_rate=%luHz\r\n",
        wave_mode_name(BenchWave_GetMode()),
        (unsigned long)BENCH_DAC_UPDATE_HZ,
        (unsigned long)BENCH_ADC_SAMPLE_HZ);
    output(line);
    return;
  }
  if (strcmp(argument, "stop") == 0)
  {
    BenchWave_Stop();
    output("OK waveform stopped\r\n");
    return;
  }
  if (sscanf(argument, "exp %lu %lu", &amplitude, &duration) == 2)
  {
    if (BenchWave_StartExponential((uint16_t)amplitude,
                                   (uint16_t)duration) == HAL_OK)
    {
      (void)snprintf(line, sizeof(line),
          "OK 1kHz exponential peak=%lumV decay_to_1pct=%luus\r\n",
          amplitude, duration);
      output(line);
    }
    else output("ERR exp: mV=1..3000, decay_us=2..100\r\n");
    return;
  }
  if (sscanf(argument, "hold %lu %lu %lu", &amplitude, &duration,
             &end_permille) == 3)
  {
    if (BenchWave_StartPeakHold((uint16_t)amplitude, (uint16_t)duration,
                                (uint16_t)end_permille) == HAL_OK)
    {
      (void)snprintf(line, sizeof(line),
          "OK 1kHz peak-hold peak=%lumV hold=%luus end=%lu/1000\r\n",
          amplitude, duration, end_permille);
      output(line);
    }
    else output("ERR hold: mV=1..3000, hold_us=1..998, end=900..1000\r\n");
    return;
  }
  output("ERR use wave exp <mV> <decay_us>|hold <mV> <hold_us> <end_permille>|stop|status\r\n");
}

static void capture_command(char *argument)
{
  static uint8_t capture_valid;
  BenchCaptureAnalysis result;
  const uint16_t *samples;
  uint8_t was_running;
  uint16_t count;
  uint16_t i;
  char line[192];

  if ((argument != NULL) && (strncmp(argument, "dump ", 5U) == 0))
  {
    count = (uint16_t)strtoul(argument + 5U, NULL, 0);
    if ((!capture_valid) || (count == 0U) || (count > 512U))
    {
      output("ERR capture first; dump count is 1..512\r\n");
      return;
    }
    samples = BenchWave_GetCapture();
    output("index,time_ns,raw12,mV_assuming_3300\r\n");
    for (i = 0U; i < count; ++i)
    {
      (void)snprintf(line, sizeof(line), "%u,%lu,%u,%lu\r\n", i,
          (unsigned long)i * 1000000000UL / BENCH_ADC_SAMPLE_HZ,
          samples[i], (unsigned long)samples[i] * 3300UL / 4095UL);
      output(line);
      CDC_Task();
    }
    output("end\r\n");
    return;
  }
  if (argument != NULL)
  {
    output("ERR use capture or capture dump <1..512>\r\n");
    return;
  }

  was_running = status.acquisition_running;
  acquisition_stop();
  if (BenchWave_Capture() != HAL_OK)
  {
    output("ERR ADC DMA capture timeout\r\n");
    if (was_running) acquisition_start();
    return;
  }
  capture_valid = 1U;
  BenchWave_Analyze(&result);
  (void)snprintf(line, sizeof(line),
      "capture n=%u rate=%luHz mode=%s min=%u max=%u mean=%lu peak=%lumV baseline=%lumV peak_i=%u tau=%luns droop=%luppm\r\n",
      BENCH_CAPTURE_SAMPLES, (unsigned long)BENCH_ADC_SAMPLE_HZ,
      wave_mode_name(BenchWave_GetMode()), result.minimum, result.maximum,
      (unsigned long)result.mean, (unsigned long)result.peak_mv,
      (unsigned long)result.baseline_mv, result.peak_index,
      (unsigned long)result.tau_ns, (unsigned long)result.droop_ppm);
  output(line);
  if (was_running) acquisition_start();
}

static void run_static_calibration(void)
{
  static const uint16_t codes[] = {0U, 512U, 1024U, 1536U, 2048U,
                                   2560U, 3072U, 3584U, 4095U};
  uint16_t saved_code = status.threshold_dac_code;
  uint8_t was_running = status.acquisition_running;
  uint32_t i;
  char line[112];
  acquisition_stop();
  output("dac_code,ideal_mV,adc_mean,measured_mV,error_mV\r\n");
  for (i = 0U; i < sizeof(codes) / sizeof(codes[0]); ++i)
  {
    uint32_t sum = 0U;
    uint32_t valid = 0U;
    uint32_t j;
    uint32_t measured;
    uint32_t ideal = (uint32_t)codes[i] * 3300UL / 4095UL;
    (void)BenchWave_SetStaticCode(codes[i]);
    HAL_Delay(2U);
    for (j = 0U; j < 32U; ++j)
    {
      uint16_t raw;
      if (BenchWave_ReadSingle(&raw) == HAL_OK)
      {
        sum += raw;
        valid++;
      }
    }
    if (valid == 0U)
    {
      output("ERR ADC read during calibration\r\n");
      break;
    }
    sum /= valid;
    measured = sum * 3300UL / 4095UL;
    (void)snprintf(line, sizeof(line), "%u,%lu,%lu,%lu,%ld\r\n",
        codes[i], (unsigned long)ideal, (unsigned long)sum,
        (unsigned long)measured, (long)measured - (long)ideal);
    output(line);
  }
  set_dac_code(saved_code);
  if (was_running) acquisition_start();
  output("end\r\n");
}
#endif

static void adc_show_stats(void)
{
  uint16_t i;
  uint16_t index;
  uint16_t minimum = 4095U;
  uint16_t maximum = 0U;
  uint64_t sum = 0U;
  uint32_t mean;
  char line[176];
  if (adc_trace_count == 0U)
  {
    output("ERR no internal ADC samples\r\n");
    return;
  }
  index = (adc_trace_head - adc_trace_count) & (ADC_TRACE_LENGTH - 1U);
  for (i = 0U; i < adc_trace_count; ++i)
  {
    uint16_t raw = adc_trace[index];
    if (raw < minimum) minimum = raw;
    if (raw > maximum) maximum = raw;
    sum += raw;
    index = (index + 1U) & (ADC_TRACE_LENGTH - 1U);
  }
  mean = (uint32_t)(sum / adc_trace_count);
  (void)snprintf(line, sizeof(line),
      "n=%u min=%u(%lumV) max=%u(%lumV) mean=%lu(%lumV), assuming VDDA=3300mV\r\n",
      adc_trace_count, minimum, (unsigned long)minimum * 3300UL / 4095UL,
      maximum, (unsigned long)maximum * 3300UL / 4095UL,
      (unsigned long)mean, (unsigned long)mean * 3300UL / 4095UL);
  output(line);
}

static void adc_dump(uint16_t count)
{
  uint16_t i;
  uint16_t index;
  uint8_t was_running = status.acquisition_running;
  char line[64];
  if ((count == 0U) || (count > 512U) || (count > adc_trace_count))
  {
    output("ERR dump count must be 1..512 and <= buffered samples\r\n");
    return;
  }
  acquisition_stop();
  index = (adc_trace_head - count) & (ADC_TRACE_LENGTH - 1U);
  output("index,raw12,mV_assuming_3300\r\n");
  for (i = 0U; i < count; ++i)
  {
    uint16_t raw = adc_trace[index];
    (void)snprintf(line, sizeof(line), "%u,%u,%lu\r\n", i, raw,
                   (unsigned long)raw * 3300UL / 4095UL);
    output(line);
    CDC_Task();
    index = (index + 1U) & (ADC_TRACE_LENGTH - 1U);
  }
  output("end\r\n");
  if (was_running) acquisition_start();
}

static void internal_adc_task(void)
{
  uint32_t now;
  uint16_t raw;
  AppSample sample;
  if ((!status.acquisition_running) || (!status.source) || (!internal_adc_ready)) return;
  now = DWT->CYCCNT;
  if ((int32_t)(now - internal_adc_next_cycle) < 0) return;

  if ((uint32_t)(now - internal_adc_next_cycle) > (internal_adc_period_cycles * 2U))
  {
    status.internal_adc_schedule_misses++;
    internal_adc_next_cycle = now + internal_adc_period_cycles;
  }
  else
  {
    internal_adc_next_cycle += internal_adc_period_cycles;
  }

  if (read_internal_adc(&raw) != HAL_OK)
  {
    status.internal_adc_errors++;
    return;
  }
  status.internal_adc_raw = raw;
  status.internal_adc_samples++;
  adc_trace_push(raw);
  sample.timestamp_cycles = DWT->CYCCNT;
  sample.adc_code = (uint16_t)(raw << 4);
  sample.flags = SAMPLE_FLAG_INTERNAL_ADC;
  process_sample(&sample);
}

static void restart_train_task(void)
{
  uint32_t now;
  if (status.restart_train_hz == 0U) return;
  now = DWT->CYCCNT;
  if ((int32_t)(now - restart_train_next_cycle) < 0) return;
  precise_restart_pulse();
  restart_train_next_cycle += restart_train_period_cycles;
  if ((int32_t)(now - restart_train_next_cycle) > (int32_t)restart_train_period_cycles)
  {
    restart_train_next_cycle = now + restart_train_period_cycles;
  }
}

static void execute_command(char *command)
{
  char *argument;
  uint32_t value;
  while (*command == ' ') command++;
  argument = strchr(command, ' ');
  if (argument != NULL)
  {
    *argument++ = '\0';
    while (*argument == ' ') argument++;
  }

  if ((strcmp(command, "help") == 0) || (strcmp(command, "?") == 0))
  {
    show_help();
  }
  else if (strcmp(command, "status") == 0)
  {
    show_status();
  }
  else if ((strcmp(command, "source") == 0) && (argument != NULL))
  {
    if (strcmp(argument, "internal") == 0)
    {
      if (!internal_adc_ready) output("ERR internal ADC initialization failed\r\n");
      else
      {
        set_source(1U);
        output("OK source=internal (PC3/P2-5, ADC1_IN9)\r\n");
      }
    }
    else if (strcmp(argument, "ad7980") == 0)
    {
      set_source(0U);
      output("OK source=ad7980\r\n");
    }
    else output("ERR use source internal|ad7980\r\n");
  }
  else if (strcmp(command, "start") == 0)
  {
    acquisition_start();
    output("OK acquisition started\r\n");
  }
  else if (strcmp(command, "stop") == 0)
  {
    acquisition_stop();
    output("OK acquisition stopped\r\n");
  }
  else if ((strcmp(command, "loopback") == 0) && (argument != NULL))
  {
    status.loopback_check = (strcmp(argument, "on") == 0) ? 1U : 0U;
    output(status.loopback_check ? "OK loopback check on\r\n" : "OK loopback check off\r\n");
  }
  else if ((strcmp(command, "dac") == 0) && (argument != NULL))
  {
    value = strtoul(argument, NULL, 0);
    if (value <= 4095U)
    {
      set_dac_code((uint16_t)value);
      output("OK\r\n");
    }
    else output("ERR DAC range is 0..4095\r\n");
  }
  else if ((strcmp(command, "threshold") == 0) && (argument != NULL))
  {
    char line[80];
    value = strtoul(argument, NULL, 0);
    if (value <= 327U)
    {
      set_dac_code(threshold_mv_to_dac_code(value));
      (void)snprintf(line, sizeof(line), "OK threshold=%lumV dac=%u\r\n",
                     (unsigned long)value, status.threshold_dac_code);
      output(line);
    }
    else output("ERR post-divider threshold range is 0..327 mV\r\n");
  }
#if APP_ENABLE_WAVEFORM_TEST
  else if (strcmp(command, "wave") == 0)
  {
    waveform_command(argument);
  }
  else if (strcmp(command, "capture") == 0)
  {
    capture_command(argument);
  }
  else if ((strcmp(command, "calibrate") == 0) && (argument == NULL))
  {
    run_static_calibration();
  }
#endif
  else if ((strcmp(command, "adc") == 0) && (argument != NULL))
  {
    if (strncmp(argument, "rate ", 5U) == 0)
    {
      value = strtoul(argument + 5U, NULL, 0);
      if ((value >= ADC_MIN_RATE_HZ) && (value <= ADC_MAX_RATE_HZ))
      {
        status.internal_adc_rate_hz = (uint16_t)value;
        internal_adc_period_cycles = SystemCoreClock / value;
        internal_adc_next_cycle = DWT->CYCCNT + internal_adc_period_cycles;
        output("OK\r\n");
      }
      else output("ERR ADC rate range is 100..20000 samples/s\r\n");
    }
    else if (strcmp(argument, "stats") == 0) adc_show_stats();
    else if (strcmp(argument, "clear") == 0)
    {
      adc_trace_clear();
      output("OK ADC trace cleared\r\n");
    }
    else if (strncmp(argument, "dump ", 5U) == 0)
    {
      adc_dump((uint16_t)strtoul(argument + 5U, NULL, 0));
    }
    else output("ERR use adc rate <Hz>|stats|clear|dump <n>\r\n");
  }
  else if (strcmp(command, "restart") == 0)
  {
    if (argument == NULL)
    {
      precise_restart_pulse();
      output("OK\r\n");
    }
    else if (strncmp(argument, "train ", 6U) == 0)
    {
      char *rate = argument + 6U;
      if (strcmp(rate, "off") == 0)
      {
        status.restart_train_hz = 0U;
        output("OK restart train off\r\n");
      }
      else
      {
        value = strtoul(rate, NULL, 0);
        if ((value >= 1U) && (value <= 1000U))
        {
          status.restart_train_hz = (uint16_t)value;
          restart_train_period_cycles = SystemCoreClock / value;
          restart_train_next_cycle = DWT->CYCCNT + restart_train_period_cycles;
          output("OK restart train on\r\n");
        }
        else output("ERR restart train range is 1..1000 Hz\r\n");
      }
    }
    else output("ERR use restart or restart train <Hz|off>\r\n");
  }
  else if ((strcmp(command, "hist") == 0) && (argument != NULL))
  {
    if (strcmp(argument, "clear") == 0)
    {
      memset(histogram, 0, sizeof(histogram));
      output("OK histogram cleared\r\n");
    }
    else if (strcmp(argument, "dump") == 0) dump_histogram();
    else output("ERR use hist clear|dump\r\n");
  }
  else if ((strcmp(command, "flash") == 0) && (argument != NULL))
  {
    if (strcmp(argument, "id") == 0)
    {
      flash_present = (W25Q32_ReadJedecId(flash_id) == HAL_OK);
      show_status();
    }
    else if (strcmp(argument, "test") == 0)
    {
      uint8_t was_running = status.acquisition_running;
      acquisition_stop();
      output("Testing LAST flash sector (0x3FF000); this erases that sector...\r\n");
      output((W25Q32_TestLastSector() == HAL_OK) ? "OK flash test passed\r\n" : "ERR flash test failed\r\n");
      if (was_running) acquisition_start();
    }
    else if (strncmp(argument, "prepare ", 8U) == 0)
    {
      flash_prepare(strtoul(argument + 8U, NULL, 0));
    }
    else if (strncmp(argument, "dump ", 5U) == 0)
    {
      unsigned long start_record;
      unsigned long record_count;
      if (sscanf(argument + 5U, "%lu %lu", &start_record, &record_count) == 2)
      {
        flash_dump((uint32_t)start_record, (uint32_t)record_count);
      }
      else output("ERR use flash dump <start_record> <count>\r\n");
    }
    else output("ERR use flash id|test|prepare <sectors>|dump <start> <n>\r\n");
  }
  else if ((strcmp(command, "log") == 0) && (argument != NULL))
  {
    if (strcmp(argument, "on") == 0)
    {
      if (flash_prepared && (flash_write_address < flash_log_limit))
      {
        status.flash_logging = 1U;
        output("OK flash logging on\r\n");
      }
      else output("ERR run flash prepare <sectors> first\r\n");
    }
    else if (strcmp(argument, "off") == 0)
    {
      flash_flush_page();
      status.flash_logging = 0U;
      output("OK flash logging off\r\n");
    }
    else output("ERR use log on|off\r\n");
  }
  else if ((strcmp(command, "lcd") == 0) && (argument != NULL))
  {
    if (strcmp(argument, "on") == 0)
    {
      lcd_enabled = (ST7789_Init() == HAL_OK);
      output(lcd_enabled ? "OK LCD initialized\r\n" : "ERR LCD initialization failed\r\n");
    }
    else if (strcmp(argument, "off") == 0)
    {
      lcd_enabled = 0U;
      ST7789_SetBacklight(0U);
      output("OK LCD off\r\n");
    }
    else if (strcmp(argument, "refresh") == 0)
    {
      output((lcd_enabled && (ST7789_DrawHistogram(histogram, APP_HISTOGRAM_BINS) == HAL_OK)) ?
             "OK LCD refreshed\r\n" : "ERR LCD disabled or update failed\r\n");
    }
    else output("ERR use lcd on|off|refresh\r\n");
  }
  else if ((strcmp(command, "counters") == 0) && (argument != NULL) &&
           (strcmp(argument, "clear") == 0))
  {
    uint16_t dac_code = status.threshold_dac_code;
    uint8_t running = status.acquisition_running;
    uint8_t loopback = status.loopback_check;
    uint8_t logging = status.flash_logging;
    uint8_t source = status.source;
    uint16_t adc_rate = status.internal_adc_rate_hz;
    uint16_t restart_rate = status.restart_train_hz;
    memset((void *)&status, 0, sizeof(status));
    status.threshold_dac_code = dac_code;
    status.acquisition_running = running;
    status.loopback_check = loopback;
    status.flash_logging = logging;
    status.source = source;
    status.internal_adc_rate_hz = adc_rate;
    status.restart_train_hz = restart_rate;
    output("OK counters cleared\r\n");
  }
  else if (*command != '\0')
  {
    output("ERR unknown command; type help\r\n");
  }
}

static void accept_character(uint8_t character)
{
  if ((character == '\r') || (character == '\n'))
  {
    if (command_length > 0U)
    {
      command_buffer[command_length] = '\0';
      execute_command(command_buffer);
      command_length = 0U;
      output("> ");
    }
  }
  else if ((character == '\b') || (character == 0x7FU))
  {
    if (command_length > 0U) command_length--;
  }
  else if ((character >= 0x20U) && (character < 0x7FU) &&
           (command_length < sizeof(command_buffer) - 1U))
  {
    command_buffer[command_length++] = (char)character;
  }
}

void App_Init(void)
{
  uint8_t uart_character;
  (void)uart_character;
  memset((void *)&status, 0, sizeof(status));
  memset(histogram, 0, sizeof(histogram));
  CoreDebug->DEMCR |= CoreDebug_DEMCR_TRCENA_Msk;
  DWT->CYCCNT = 0U;
  DWT->CTRL |= DWT_CTRL_CYCCNTENA_Msk;
  restart_cycles = (SystemCoreClock / 1000000UL) * RESTART_PULSE_US;
  status.source = APP_DEFAULT_SOURCE_INTERNAL ? 1U : 0U;
  status.internal_adc_rate_hz = APP_DEFAULT_INTERNAL_ADC_RATE_HZ;
  internal_adc_period_cycles = SystemCoreClock / APP_DEFAULT_INTERNAL_ADC_RATE_HZ;
  internal_adc_ready = (HAL_ADCEx_Calibration_Start(&hadc1, ADC_SINGLE_ENDED) == HAL_OK);

#if APP_ENABLE_WAVEFORM_TEST
  if (BenchWave_Init() != HAL_OK) Error_Handler();
#endif

  HAL_GPIO_WritePin(PH_RESTART_GPIO_Port, PH_RESTART_Pin, GPIO_PIN_RESET);
  if (HAL_DAC_Start(&hdac1, DAC_CHANNEL_1) != HAL_OK) Error_Handler();
  set_dac_code(threshold_mv_to_dac_code(APP_DEFAULT_THRESHOLD_MV));

  flash_present = (W25Q32_ReadJedecId(flash_id) == HAL_OK);
  flash_write_address = FLASH_LOG_START;
  flash_log_limit = FLASH_LOG_START;
  flash_page_capacity = W25Q32_PAGE_SIZE;
  lcd_enabled = 0U;
  ST7789_SetBacklight(0U);
  acquisition_start();
  output("\r\n" APP_BUILD_NAME " " APP_FW_VERSION "\r\n");
  output(status.source ?
      "Default source: internal ADC (PC3/P2-5). Type help for commands.\r\n> " :
      "Default source: external AD7980. Type help for commands.\r\n> ");
}

void App_Task(void)
{
  uint8_t character;
  internal_adc_task();
  restart_train_task();
  while (sample_tail != sample_head)
  {
    AppSample sample = sample_queue[sample_tail];
    sample_tail = (sample_tail + 1U) & (APP_SAMPLE_QUEUE_LENGTH - 1U);
    process_sample(&sample);
  }

  while (usb_rx_tail != usb_rx_head)
  {
    character = usb_rx_queue[usb_rx_tail];
    usb_rx_tail = (usb_rx_tail + 1U) & (USB_RX_QUEUE_SIZE - 1U);
    accept_character(character);
  }
  if (HAL_UART_Receive(&huart2, &character, 1U, 0U) == HAL_OK)
  {
    accept_character(character);
  }
  CDC_Task();

  if (lcd_enabled && ((uint32_t)(HAL_GetTick() - lcd_last_update) >= 2000U))
  {
    lcd_last_update = HAL_GetTick();
    (void)ST7789_DrawHistogram(histogram, APP_HISTOGRAM_BINS);
  }
}

void App_UsbReceive(const uint8_t *data, uint32_t length)
{
  uint32_t i;
  for (i = 0U; i < length; ++i)
  {
    uint16_t next = (usb_rx_head + 1U) & (USB_RX_QUEUE_SIZE - 1U);
    if (next == usb_rx_tail) break;
    usb_rx_queue[usb_rx_head] = data[i];
    usb_rx_head = next;
  }
}

const AppStatus *App_GetStatus(void)
{
  return (const AppStatus *)&status;
}

void HAL_GPIO_EXTI_Callback(uint16_t pin)
{
  if (pin != ADC_BUSY_IRQ_Pin) return;
  status.busy_irqs++;
  if (status.source != 0U) return;
  if (status.acquisition_running == 0U) return;
  if (status.spi_dma_busy != 0U)
  {
    status.adc_while_busy++;
    return;
  }
  status.spi_dma_busy = 1U;
  if (HAL_SPI_TransmitReceive_DMA(&hspi1, (uint8_t *)&spi_tx_word,
                                  (uint8_t *)&spi_rx_word, 1U) != HAL_OK)
  {
    status.spi_dma_busy = 0U;
    status.dma_start_errors++;
  }
}

void HAL_SPI_TxRxCpltCallback(SPI_HandleTypeDef *hspi)
{
  uint16_t next;
  uint16_t flags = 0U;
  if (hspi->Instance != SPI1) return;
  status.dma_completed++;
  if (status.loopback_check && (spi_rx_word != APP_ADC_LOOPBACK_WORD))
  {
    status.loopback_errors++;
    flags |= SAMPLE_FLAG_LOOPBACK_BAD;
  }
  next = (sample_head + 1U) & (APP_SAMPLE_QUEUE_LENGTH - 1U);
  if (next == sample_tail)
  {
    status.queue_overruns++;
  }
  else
  {
    sample_queue[sample_head].timestamp_cycles = DWT->CYCCNT;
    sample_queue[sample_head].adc_code = spi_rx_word;
    sample_queue[sample_head].flags = flags;
    sample_head = next;
  }
  precise_restart_pulse();
  status.spi_dma_busy = 0U;
}

void HAL_SPI_ErrorCallback(SPI_HandleTypeDef *hspi)
{
  if (hspi->Instance == SPI1)
  {
    status.spi_dma_busy = 0U;
    status.spi_errors++;
    (void)HAL_SPI_Abort_IT(hspi);
  }
}

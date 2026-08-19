#include "app.h"
#include "ad7980.h"
#include "usbd_cdc_if.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define ADC_REFERENCE_MV       2500U
#define ADC_CODE_COUNT         65536UL
#define ADC_SPECTRUM_FS_MV     ADC_REFERENCE_MV
#define FRONTEND_GAIN_MILLI    2000U
#define LOCAL_HIST_CHANNELS    4096U
#define DAC_REFERENCE_MV       3300U
#define THRESHOLD_R_TOP_OHM    9100U
#define THRESHOLD_R_BOTTOM_OHM 1000U
#define THRESHOLD_MIN_MV       50U
#define THRESHOLD_MAX_MV       200U
#define FIRMWARE_VERSION       "2.0.0-adaptive"
#define USB_TX_TIMEOUT_MS      1000U
#define TX_SLOT_COUNT          64U
#define TX_SLOT_SIZE           512U
#define RX_RING_SIZE           256U
#define RAW16_BATCH_SAMPLES    168U
#define RAW16_FLUSH_MS         2U

typedef enum { OUTPUT_B16 = 0, OUTPUT_CSV = 1, OUTPUT_TXT = 2 } OutputFormat;

static DAC_HandleTypeDef *threshold_dac;
static uint32_t histogram[LOCAL_HIST_CHANNELS];
static uint32_t histogram_snapshot[LOCAL_HIST_CHANNELS];
static uint64_t raw_sum;
static uint32_t sample_count;
static uint32_t range_overflow_count;
static uint16_t raw_peak;
static uint16_t expected_mv = 500U;
static uint32_t expected_hz = 1000U;
static uint16_t threshold_mv = 100U;
static uint16_t threshold_dac_mv;
static uint32_t stream_decimation = 100U;
static uint8_t stream_enabled = 0U;
static uint32_t selected_channels = LOCAL_HIST_CHANNELS;
static OutputFormat output_format = OUTPUT_B16;
static uint16_t raw16_batch[RAW16_BATCH_SAMPLES];
static uint8_t raw16_bytes[RAW16_BATCH_SAMPLES * 2U];
static uint16_t raw16_batch_count;
static uint32_t raw16_batch_first_sequence;
static uint32_t raw16_batch_started_ms;
static uint32_t stream_lost_samples;

static uint8_t tx_slots[TX_SLOT_COUNT][TX_SLOT_SIZE];
static uint16_t tx_lengths[TX_SLOT_COUNT];
static volatile uint8_t tx_head;
static volatile uint8_t tx_tail;
static volatile uint8_t tx_active;
static volatile uint8_t usb_configured;
static volatile uint8_t usb_link_reset_pending;
static uint32_t tx_started_ms;
static uint32_t usb_recovery_count;
static uint32_t tx_drop_count;

static volatile uint8_t rx_ring[RX_RING_SIZE];
static volatile uint16_t rx_head;
static volatile uint16_t rx_tail;
static char command_line[96];
static uint32_t command_length;
static int32_t histogram_dump_index = -1;
static char format_buffer[TX_SLOT_SIZE];

static void queue_status(void);

static uint8_t tx_queue_free(void)
{
  return (uint8_t)(((tx_head + 1U) % TX_SLOT_COUNT) != tx_tail);
}

static uint8_t queue_text(const char *text)
{
  uint8_t next;
  size_t length;

  if (text == NULL) return 0U;
  next = (uint8_t)((tx_head + 1U) % TX_SLOT_COUNT);
  if (next == tx_tail)
  {
    tx_drop_count++;
    return 0U;
  }
  length = strlen(text);
  if (length >= TX_SLOT_SIZE) length = TX_SLOT_SIZE - 1U;
  memcpy(tx_slots[tx_head], text, length);
  tx_slots[tx_head][length] = 0U;
  tx_lengths[tx_head] = (uint16_t)length;
  tx_head = next;
  return 1U;
}

static void usb_tx_service(void)
{
  if (usb_link_reset_pending != 0U)
  {
    /* Discard only stale PC transport state. ADC acquisition continues while
     * the cable is absent; full16 histogramming resumes on the PC after link. */
    tx_tail = tx_head;
    tx_active = 0U;
    rx_tail = rx_head;
    command_length = 0U;
    raw16_batch_count = 0U;
    histogram_dump_index = -1;
    usb_link_reset_pending = 0U;
    if (usb_configured != 0U)
    {
      queue_text("# USB CDC link ready; acquisition remained active\r\n");
      queue_status();
    }
  }

  if ((usb_configured == 0U) || (CDC_IsConfigured_FS() == 0U)) return;

  if ((tx_active != 0U) &&
      ((uint32_t)(HAL_GetTick() - tx_started_ms) >= USB_TX_TIMEOUT_MS))
  {
    CDC_AbortTransmit_FS();
    tx_active = 0U;
    usb_recovery_count++;
  }

  if ((tx_active == 0U) && (tx_tail != tx_head) &&
      (CDC_Transmit_FS(tx_slots[tx_tail], tx_lengths[tx_tail]) == USBD_OK))
  {
    tx_active = 1U;
    tx_started_ms = HAL_GetTick();
  }
}

static uint32_t raw_to_mv(uint16_t raw)
{
  /* Ideal straight-binary code-bin centre: (code + 0.5) * VREF / 65536.
   * Integer mV is only for status/debug text; the PC keeps the raw code and
   * performs the sub-millivolt conversion in double precision. */
  return (uint32_t)((((uint64_t)raw * 2U + 1U) * ADC_REFERENCE_MV +
                    ADC_CODE_COUNT) / (2U * ADC_CODE_COUNT));
}

static void set_threshold_mv(uint16_t comparator_mv)
{
  uint32_t dac_output_mv;
  uint32_t code;

  if (comparator_mv < THRESHOLD_MIN_MV) comparator_mv = THRESHOLD_MIN_MV;
  if (comparator_mv > THRESHOLD_MAX_MV) comparator_mv = THRESHOLD_MAX_MV;
  dac_output_mv = ((uint32_t)comparator_mv *
                  (THRESHOLD_R_TOP_OHM + THRESHOLD_R_BOTTOM_OHM) +
                  THRESHOLD_R_BOTTOM_OHM / 2U) / THRESHOLD_R_BOTTOM_OHM;
  code = (dac_output_mv * 4095U + DAC_REFERENCE_MV / 2U) / DAC_REFERENCE_MV;
  if (code > 4095U) code = 4095U;
  threshold_mv = comparator_mv;
  threshold_dac_mv = (uint16_t)dac_output_mv;
  (void)HAL_DAC_SetValue(threshold_dac, DAC_CHANNEL_1, DAC_ALIGN_12B_R, code);
}

static void clear_statistics(void)
{
  memset(histogram, 0, sizeof(histogram));
  memset(histogram_snapshot, 0, sizeof(histogram_snapshot));
  raw_sum = 0U;
  sample_count = 0U;
  range_overflow_count = 0U;
  raw_peak = 0U;
  raw16_batch_count = 0U;
  histogram_dump_index = -1;
  stream_lost_samples = 0U;
  AD7980_ResetStatistics();
}

static void flush_usb_output(void)
{
  if ((usb_configured != 0U) && (CDC_IsConfigured_FS() != 0U))
  {
    CDC_AbortTransmit_FS();
  }
  tx_head = 0U;
  tx_tail = 0U;
  tx_active = 0U;
  raw16_batch_count = 0U;
  histogram_dump_index = -1;
}

static void queue_csv_header(void)
{
  queue_text("timestamp_ms,sequence,raw,voltage_mV,channel65536,expected_mV,expected_Hz,threshold_mV,overruns,tx_drops\r\n");
}

static void queue_status(void)
{
  const uint32_t mean_mv = (sample_count == 0U) ? 0U :
    (uint32_t)((raw_sum * ADC_REFERENCE_MV) /
    ((uint64_t)sample_count * ADC_CODE_COUNT));
  (void)snprintf(format_buffer, sizeof(format_buffer),
    "# status fw=%s uptime_ms=%lu samples=%lu busy=%lu recoveries=%lu postread_low=%lu sdo=%s overruns=%lu queued=%lu range_overflows=%lu tx_drops=%lu usb_recoveries=%lu stream_lost_samples=%lu mean_mV=%lu peak_mV=%lu expected_mV=%u expected_Hz=%lu threshold_mV=%u threshold_dac_mV=%u decimate=%lu stream=%s format=%s hist_channels=%lu adc_spectrum_fs_mV=%u frontend_gain_milli=%u spectrum_mode=%s wire=%s\r\n",
    FIRMWARE_VERSION, (unsigned long)HAL_GetTick(), (unsigned long)sample_count, (unsigned long)AD7980_GetBusyCount(),
    (unsigned long)AD7980_GetRecoveryCount(),
    (unsigned long)AD7980_GetPostReadLowCount(),
    AD7980_IsSdoLow() ? "low" : "high",
    (unsigned long)AD7980_GetOverrunCount(),
    (unsigned long)AD7980_GetQueueDepth(), (unsigned long)range_overflow_count,
    (unsigned long)tx_drop_count, (unsigned long)usb_recovery_count,
    (unsigned long)stream_lost_samples,
    (unsigned long)mean_mv, (unsigned long)raw_to_mv(raw_peak), expected_mv,
    (unsigned long)expected_hz, threshold_mv, threshold_dac_mv, (unsigned long)stream_decimation,
    stream_enabled ? "on" : "off",
    output_format == OUTPUT_B16 ? "b16" : (output_format == OUTPUT_CSV ? "csv" : "txt"),
    (unsigned long)selected_channels, ADC_SPECTRUM_FS_MV, FRONTEND_GAIN_MILLI,
    selected_channels == LOCAL_HIST_CHANNELS ? "mcu_hist4096" : "host_raw16",
    selected_channels == LOCAL_HIST_CHANNELS ? "hist_csv" : "base64_crc16");
  queue_text(format_buffer);
}

static void queue_help(void)
{
  queue_text("# commands: help | status | channels 4096|8192|16384|65536 | format b16|raw16|csv|txt | stream on|off | decimate N | threshold 50..200 | profile baseline|amplitude|frequency | amp 100..900(any 1mV) | freq 1..100(kHz) | hist clear|dump | stats clear; channels=4096 uses MCU histogram, higher modes use @B16 raw codes; CNV is external\r\n");
}

static void set_channel_mode(uint32_t channels)
{
  flush_usb_output();
  selected_channels = channels;
  output_format = OUTPUT_B16;
  stream_decimation = 1U;
  stream_enabled = (channels == LOCAL_HIST_CHANNELS) ? 0U : 1U;
  clear_statistics();
  queue_text(channels == LOCAL_HIST_CHANNELS ?
    "# channels=4096 mode=mcu_hist4096 stream=off full_scale_mV=2500\r\n" :
    "# channels=raw16 mode=host_raw16 stream=on full_scale_mV=2500\r\n");
  queue_status();
}

static uint8_t parse_u32(const char *text, uint32_t *value)
{
  char *end;
  unsigned long parsed;
  if ((text == NULL) || (value == NULL) || (*text == '\0') || (*text == '-'))
    return 0U;
  end = NULL;
  parsed = strtoul(text, &end, 10);
  if ((end == text) || (*end != '\0')) return 0U;
  *value = (uint32_t)parsed;
  return 1U;
}

static void process_command(char *line)
{
  char *command = strtok(line, " \t");
  char *argument = strtok(NULL, " \t");

  if (command == NULL) return;
  if (strcmp(command, "help") == 0) queue_help();
  else if (strcmp(command, "status") == 0) queue_status();
  else if ((strcmp(command, "channels") == 0) && argument)
  {
    uint32_t value;
    if (parse_u32(argument, &value) &&
        ((value == 4096U) || (value == 8192U) || (value == 16384U) || (value == 65536U)))
      set_channel_mode(value);
    else queue_text("# error: channels must be 4096, 8192, 16384 or 65536\r\n");
  }
  else if ((strcmp(command, "format") == 0) && argument)
  {
    raw16_batch_count = 0U;
    if ((strcmp(argument, "b16") == 0) || (strcmp(argument, "raw16") == 0))
    {
      output_format = OUTPUT_B16;
      stream_decimation = 1U;
      queue_text("# output format: b16 base64+CRC16; decimation forced to 1\r\n");
    }
    else if (strcmp(argument, "csv") == 0)
    {
      output_format = OUTPUT_CSV;
      queue_csv_header();
    }
    else if (strcmp(argument, "txt") == 0)
    {
      output_format = OUTPUT_TXT;
      queue_text("# output format: txt\r\n");
    }
    else queue_text("# error: format must be b16, raw16, csv or txt\r\n");
  }
  else if ((strcmp(command, "stream") == 0) && argument)
  {
    if ((strcmp(argument, "on") == 0) && (selected_channels == LOCAL_HIST_CHANNELS))
    {
      stream_enabled = 0U;
      queue_text("# 4096-channel mode keeps per-event USB stream off; use hist dump or select 8192/16384/65536 channels\r\n");
      return;
    }
    if (strcmp(argument, "on") == 0) stream_enabled = 1U;
    else if (strcmp(argument, "off") == 0) stream_enabled = 0U;
    else { queue_text("# error: stream on|off\r\n"); return; }
    queue_text(stream_enabled ? "# stream on\r\n" : "# stream off\r\n");
  }
  else if ((strcmp(command, "decimate") == 0) && argument)
  {
    uint32_t value;
    if (parse_u32(argument, &value) && (value >= 1U) && (value <= 1000000U) &&
        ((output_format != OUTPUT_B16) || (value == 1U)))
    {
      stream_decimation = value;
      queue_text("# decimation updated\r\n");
    }
    else queue_text("# error: decimation range is 1..1000000; b16 full-code mode requires 1\r\n");
  }
  else if ((strcmp(command, "threshold") == 0) && argument)
  {
    uint32_t value;
    if (parse_u32(argument, &value) && (value >= THRESHOLD_MIN_MV) &&
        (value <= THRESHOLD_MAX_MV))
    {
      set_threshold_mv((uint16_t)value);
      (void)snprintf(format_buffer, sizeof(format_buffer),
        "# threshold_mV=%u threshold_dac_mV=%u divider=1k/(9.1k+1k)\r\n",
        threshold_mv, threshold_dac_mv);
      queue_text(format_buffer);
    }
    else queue_text("# error: comparator threshold range is 50..200 mV; DAC output is x10.1\r\n");
  }
  else if ((strcmp(command, "profile") == 0) && argument)
  {
    if (strcmp(argument, "baseline") == 0) { expected_mv = 500U; expected_hz = 1000U; }
    else if (strcmp(argument, "amplitude") == 0) { expected_mv = 100U; expected_hz = 1000U; }
    else if (strcmp(argument, "frequency") == 0) { expected_mv = 100U; expected_hz = 1000U; }
    else { queue_text("# error: profile baseline|amplitude|frequency\r\n"); return; }
    clear_statistics();
    queue_status();
  }
  else if ((strcmp(command, "amp") == 0) && argument)
  {
    uint32_t value;
    if (parse_u32(argument, &value) && (value >= 100U) && (value <= 900U))
    {
      expected_mv = (uint16_t)value;
      clear_statistics();
      queue_status();
    }
    else queue_text("# error: amplitude must be 100..900 mV\r\n");
  }
  else if ((strcmp(command, "freq") == 0) && argument)
  {
    uint32_t value;
    if (parse_u32(argument, &value) && (value >= 1U) && (value <= 100U))
    {
      expected_hz = value * 1000U;
      clear_statistics();
      queue_status();
    }
    else queue_text("# error: frequency must be 1..100 kHz\r\n");
  }
  else if ((strcmp(command, "hist") == 0) && argument)
  {
    if (strcmp(argument, "clear") == 0) { flush_usb_output(); clear_statistics(); queue_text("# histogram/statistics cleared\r\n"); }
    else if ((strcmp(argument, "dump") == 0) && (selected_channels == LOCAL_HIST_CHANNELS))
    {
      memcpy(histogram_snapshot, histogram, sizeof(histogram_snapshot));
      histogram_dump_index = 0;
      queue_text("channel,count\r\n");
    }
    else if (strcmp(argument, "dump") == 0) queue_text("# host-side raw mode has no MCU histogram dump\r\n");
    else queue_text("# error: hist clear|dump\r\n");
  }
  else if ((strcmp(command, "stats") == 0) && argument && (strcmp(argument, "clear") == 0))
  {
    flush_usb_output();
    clear_statistics();
    queue_text("# statistics cleared\r\n");
  }
  else queue_text("# error: unknown command; type help\r\n");
}

static void command_service(void)
{
  while (rx_tail != rx_head)
  {
    const char c = (char)rx_ring[rx_tail];
    rx_tail = (uint16_t)((rx_tail + 1U) % RX_RING_SIZE);
    if ((c == '\r') || (c == '\n'))
    {
      if (command_length)
      {
        command_line[command_length] = '\0';
        process_command(command_line);
        command_length = 0U;
      }
    }
    else if (command_length < sizeof(command_line) - 1U) command_line[command_length++] = c;
    else command_length = 0U;
  }
}

static uint16_t crc16_ccitt(const uint8_t *data, size_t length)
{
  uint16_t crc = 0xFFFFU;
  for (size_t i = 0U; i < length; ++i)
  {
    crc ^= (uint16_t)data[i] << 8;
    for (uint32_t bit = 0U; bit < 8U; ++bit)
      crc = (crc & 0x8000U) ? (uint16_t)((crc << 1) ^ 0x1021U) : (uint16_t)(crc << 1);
  }
  return crc;
}

static size_t base64_encode(const uint8_t *source, size_t length, char *dest, size_t capacity)
{
  static const char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  size_t in = 0U;
  size_t out = 0U;
  while (in < length)
  {
    const size_t remaining = length - in;
    const uint32_t a = source[in++];
    const uint32_t b = (remaining > 1U) ? source[in++] : 0U;
    const uint32_t c = (remaining > 2U) ? source[in++] : 0U;
    const uint32_t triple = (a << 16) | (b << 8) | c;
    if ((out + 4U) >= capacity) return 0U;
    dest[out++] = alphabet[(triple >> 18) & 0x3FU];
    dest[out++] = alphabet[(triple >> 12) & 0x3FU];
    dest[out++] = (remaining > 1U) ? alphabet[(triple >> 6) & 0x3FU] : '=';
    dest[out++] = (remaining > 2U) ? alphabet[triple & 0x3FU] : '=';
  }
  return out;
}

static uint8_t queue_raw16_batch(void)
{
  size_t used;
  size_t encoded;
  const size_t byte_count = (size_t)raw16_batch_count * 2U;

  if (raw16_batch_count == 0U) return 1U;
  for (uint32_t i = 0U; i < raw16_batch_count; ++i)
  {
    raw16_bytes[i * 2U] = (uint8_t)(raw16_batch[i] & 0xFFU);
    raw16_bytes[i * 2U + 1U] = (uint8_t)(raw16_batch[i] >> 8);
  }
  used = (size_t)snprintf(format_buffer, sizeof(format_buffer), "@B16,%lu,%u,%04X,",
    (unsigned long)raw16_batch_first_sequence, raw16_batch_count,
    crc16_ccitt(raw16_bytes, byte_count));
  encoded = base64_encode(raw16_bytes, byte_count, &format_buffer[used], sizeof(format_buffer) - used - 3U);
  if (encoded == 0U) return 0U;
  used += encoded;
  if ((used + 2U) >= sizeof(format_buffer)) return 0U;
  format_buffer[used++] = '\r';
  format_buffer[used++] = '\n';
  format_buffer[used] = '\0';
  if (queue_text(format_buffer) == 0U)
  {
    stream_lost_samples += raw16_batch_count;
  }
  raw16_batch_count = 0U;
  return 1U;
}

static void raw16_flush_service(void)
{
  if ((raw16_batch_count != 0U) &&
      ((raw16_batch_count >= RAW16_BATCH_SAMPLES) ||
       ((uint32_t)(HAL_GetTick() - raw16_batch_started_ms) >= RAW16_FLUSH_MS)))
  {
    (void)queue_raw16_batch();
  }
}

static uint8_t sample_service(void)
{
  AD7980_Sample sample;

  if (!AD7980_TryRead(&sample)) return 0U;
  if (sample.raw == UINT16_MAX) range_overflow_count++;
  if (selected_channels == LOCAL_HIST_CHANNELS) histogram[sample.raw >> 4]++;
  raw_sum += sample.raw;
  sample_count++;
  if (sample.raw > raw_peak) raw_peak = sample.raw;

  if (stream_enabled && ((sample.sequence % stream_decimation) == 0U))
  {
    if (output_format == OUTPUT_B16)
    {
      if (usb_configured == 0U)
      {
        stream_lost_samples++;
      }
      else
      {
        if ((raw16_batch_count != 0U) &&
            (sample.sequence != (raw16_batch_first_sequence + raw16_batch_count)))
        {
          (void)queue_raw16_batch();
        }
        if (raw16_batch_count == 0U)
        {
          raw16_batch_first_sequence = sample.sequence;
          raw16_batch_started_ms = HAL_GetTick();
        }
        raw16_batch[raw16_batch_count++] = sample.raw;
        if (raw16_batch_count >= RAW16_BATCH_SAMPLES) (void)queue_raw16_batch();
      }
    }
    else if (output_format == OUTPUT_CSV)
    {
      const uint32_t voltage_mv = raw_to_mv(sample.raw);
      (void)snprintf(format_buffer, sizeof(format_buffer), "%lu,%lu,%u,%lu,%u,%u,%lu,%u,%lu,%lu\r\n",
        (unsigned long)sample.timestamp_ms, (unsigned long)sample.sequence,
        sample.raw, (unsigned long)voltage_mv, sample.raw, expected_mv,
        (unsigned long)expected_hz, threshold_mv,
        (unsigned long)AD7980_GetOverrunCount(), (unsigned long)tx_drop_count);
      queue_text(format_buffer);
    }
    else
    {
      const uint32_t voltage_mv = raw_to_mv(sample.raw);
      (void)snprintf(format_buffer, sizeof(format_buffer),
        "sample t_ms=%lu seq=%lu raw=%u mV=%lu ch=%u expected=%umV/%luHz threshold=%umV overrun=%lu drop=%lu\r\n",
        (unsigned long)sample.timestamp_ms, (unsigned long)sample.sequence,
        sample.raw, (unsigned long)voltage_mv, sample.raw, expected_mv,
        (unsigned long)expected_hz, threshold_mv,
        (unsigned long)AD7980_GetOverrunCount(), (unsigned long)tx_drop_count);
      queue_text(format_buffer);
    }
  }
  return 1U;
}

static void histogram_dump_service(void)
{
  size_t used = 0U;
  if ((histogram_dump_index >= 0) && tx_queue_free())
  {
    while (histogram_dump_index < (int32_t)LOCAL_HIST_CHANNELS)
    {
      const int written = snprintf(&format_buffer[used], sizeof(format_buffer) - used,
        "%ld,%lu\r\n", (long)histogram_dump_index,
        (unsigned long)histogram_snapshot[histogram_dump_index]);
      if ((written <= 0) || ((size_t)written >= (sizeof(format_buffer) - used))) break;
      used += (size_t)written;
      histogram_dump_index++;
      if ((sizeof(format_buffer) - used) < 32U) break;
    }
    if (used != 0U) queue_text(format_buffer);
    else if (histogram_dump_index >= (int32_t)LOCAL_HIST_CHANNELS)
    {
      queue_text("# histogram end\r\n");
      histogram_dump_index = -1;
    }
  }
}

void App_Init(DAC_HandleTypeDef *dac)
{
  threshold_dac = dac;
  clear_statistics();
  (void)HAL_DAC_Start(threshold_dac, DAC_CHANNEL_1);
  set_threshold_mv(threshold_mv);
  queue_text("# STM32G474 + AD7980 adaptive-channel MCA ready; default 4096 MCU histogram; CNV is external; ARM/BRM share one protocol\r\n");
  queue_help();
}

void App_Run(void)
{
  AD7980_Service();
  raw16_flush_service();
  for (uint32_t i = 0U; (i < 512U) && sample_service(); ++i) { }
  command_service();
  for (uint32_t i = 0U; (i < 512U) && sample_service(); ++i) { }
  raw16_flush_service();
  histogram_dump_service();
  usb_tx_service();
}

void App_USB_Rx(const uint8_t *data, uint32_t length)
{
  for (uint32_t i = 0U; i < length; ++i)
  {
    const uint16_t next = (uint16_t)((rx_head + 1U) % RX_RING_SIZE);
    if (next == rx_tail) break;
    rx_ring[rx_head] = data[i];
    rx_head = next;
  }
}

void App_USB_TxComplete(void)
{
  if (tx_active)
  {
    tx_tail = (uint8_t)((tx_tail + 1U) % TX_SLOT_COUNT);
    tx_active = 0U;
  }
}

void App_USB_LinkState(uint8_t configured)
{
  usb_configured = (configured != 0U) ? 1U : 0U;
  tx_active = 0U;
  usb_link_reset_pending = 1U;
}

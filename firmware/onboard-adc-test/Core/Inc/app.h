#ifndef APP_H
#define APP_H

#include "app_config.h"
#include "main.h"
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define APP_ADC_LOOPBACK_WORD       0xA55AU
#define APP_HISTOGRAM_BINS          1024U
#define APP_SAMPLE_QUEUE_LENGTH     1024U

typedef struct
{
  uint32_t timestamp_cycles;
  uint16_t adc_code;
  uint16_t flags;
} AppSample;

typedef struct
{
  uint32_t busy_irqs;
  uint32_t dma_completed;
  uint32_t dma_start_errors;
  uint32_t spi_errors;
  uint32_t adc_while_busy;
  uint32_t queue_overruns;
  uint32_t processed;
  uint32_t loopback_errors;
  uint32_t flash_records;
  uint32_t internal_adc_samples;
  uint32_t internal_adc_errors;
  uint32_t internal_adc_schedule_misses;
  uint16_t last_sample;
  uint16_t internal_adc_raw;
  uint16_t threshold_dac_code;
  uint16_t internal_adc_rate_hz;
  uint16_t restart_train_hz;
  uint8_t acquisition_running;
  uint8_t spi_dma_busy;
  uint8_t loopback_check;
  uint8_t flash_logging;
  uint8_t source;
} AppStatus;

void App_Init(void);
void App_Task(void);
void App_UsbReceive(const uint8_t *data, uint32_t length);
const AppStatus *App_GetStatus(void);

#ifdef __cplusplus
}
#endif

#endif

#include "bench_waveform.h"
#include "adc.h"
#include "dac.h"
#include "tim.h"
#include <math.h>
#include <string.h>

#define VDDA_MV                 3300UL
#define ADC_FULL_SCALE          4095UL
#define EXP_END_RATIO           0.01f

static uint16_t dac_wave[BENCH_WAVE_SAMPLES];
static uint16_t adc_capture[BENCH_CAPTURE_SAMPLES];
static volatile uint8_t capture_complete;
static BenchWaveMode current_mode;

static uint16_t mv_to_code(uint16_t millivolts)
{
  uint32_t code = ((uint32_t)millivolts * ADC_FULL_SCALE + VDDA_MV / 2U) / VDDA_MV;
  return (uint16_t)((code > ADC_FULL_SCALE) ? ADC_FULL_SCALE : code);
}

static HAL_StatusTypeDef start_wave_dma(BenchWaveMode mode)
{
  BenchWave_Stop();
  if (HAL_DAC_Start_DMA(&hdac1, DAC_CHANNEL_1, (uint32_t *)dac_wave,
                        BENCH_WAVE_SAMPLES, DAC_ALIGN_12B_R) != HAL_OK)
  {
    current_mode = BENCH_WAVE_OFF;
    return HAL_ERROR;
  }
  __HAL_TIM_SET_COUNTER(&htim6, 0U);
  if (HAL_TIM_Base_Start(&htim6) != HAL_OK)
  {
    (void)HAL_DAC_Stop_DMA(&hdac1, DAC_CHANNEL_1);
    current_mode = BENCH_WAVE_OFF;
    return HAL_ERROR;
  }
  current_mode = mode;
  return HAL_OK;
}

HAL_StatusTypeDef BenchWave_Init(void)
{
  current_mode = BENCH_WAVE_OFF;
  capture_complete = 0U;
  memset(dac_wave, 0, sizeof(dac_wave));
  memset(adc_capture, 0, sizeof(adc_capture));
  return HAL_OK;
}

HAL_StatusTypeDef BenchWave_StartExponential(uint16_t amplitude_mv,
                                             uint16_t decay_us)
{
  uint32_t i;
  uint16_t peak;
  if ((amplitude_mv == 0U) || (amplitude_mv > 3000U) ||
      (decay_us < 2U) || (decay_us > 100U)) return HAL_ERROR;
  peak = mv_to_code(amplitude_mv);
  for (i = 0U; i < BENCH_WAVE_SAMPLES; ++i)
  {
    if (i > decay_us) dac_wave[i] = 0U;
    else
    {
      float fraction = expf(logf(EXP_END_RATIO) * (float)i / (float)decay_us);
      dac_wave[i] = (uint16_t)((float)peak * fraction + 0.5f);
    }
  }
  return start_wave_dma(BENCH_WAVE_EXPONENTIAL);
}

HAL_StatusTypeDef BenchWave_StartPeakHold(uint16_t amplitude_mv,
                                         uint16_t hold_us,
                                         uint16_t end_permille)
{
  uint32_t i;
  uint16_t peak;
  uint32_t denominator;
  if ((amplitude_mv == 0U) || (amplitude_mv > 3000U) ||
      (hold_us == 0U) || (hold_us >= BENCH_WAVE_SAMPLES - 1U) ||
      (end_permille < 900U) || (end_permille > 1000U)) return HAL_ERROR;
  peak = mv_to_code(amplitude_mv);
  denominator = BENCH_WAVE_SAMPLES - 1U - hold_us;
  for (i = 0U; i < BENCH_WAVE_SAMPLES; ++i)
  {
    if (i < hold_us) dac_wave[i] = peak;
    else
    {
      uint32_t elapsed = i - hold_us;
      uint32_t drop = (uint32_t)peak * (1000U - end_permille) * elapsed;
      drop = (drop + 500U * denominator) / (1000U * denominator);
      dac_wave[i] = (uint16_t)((drop < peak) ? peak - drop : 0U);
    }
  }
  return start_wave_dma(BENCH_WAVE_PEAK_HOLD);
}

HAL_StatusTypeDef BenchWave_SetStaticCode(uint16_t code)
{
  if (code > ADC_FULL_SCALE) return HAL_ERROR;
  BenchWave_Stop();
  if ((HAL_DAC_Start(&hdac1, DAC_CHANNEL_1) != HAL_OK) ||
      (HAL_DAC_SetValue(&hdac1, DAC_CHANNEL_1, DAC_ALIGN_12B_R, code) != HAL_OK) ||
      (HAL_TIM_GenerateEvent(&htim6, TIM_EVENTSOURCE_UPDATE) != HAL_OK))
  {
    return HAL_ERROR;
  }
  current_mode = BENCH_WAVE_STATIC;
  return HAL_OK;
}

void BenchWave_Stop(void)
{
  (void)HAL_TIM_Base_Stop(&htim6);
  (void)HAL_DAC_Stop_DMA(&hdac1, DAC_CHANNEL_1);
  current_mode = BENCH_WAVE_OFF;
}

BenchWaveMode BenchWave_GetMode(void)
{
  return current_mode;
}

HAL_StatusTypeDef BenchWave_ReadSingle(uint16_t *raw)
{
  HAL_StatusTypeDef result;
  if (raw == NULL) return HAL_ERROR;
  if (HAL_ADC_Start(&hadc1) != HAL_OK) return HAL_ERROR;
  __HAL_TIM_SET_COUNTER(&htim7, 0U);
  (void)HAL_TIM_GenerateEvent(&htim7, TIM_EVENTSOURCE_UPDATE);
  result = HAL_ADC_PollForConversion(&hadc1, 1U);
  if (result == HAL_OK) *raw = (uint16_t)HAL_ADC_GetValue(&hadc1);
  (void)HAL_ADC_Stop(&hadc1);
  return result;
}

HAL_StatusTypeDef BenchWave_Capture(void)
{
  uint32_t started;
  capture_complete = 0U;
  (void)HAL_TIM_Base_Stop(&htim7);
  (void)HAL_ADC_Stop_DMA(&hadc1);
  if (HAL_ADC_Start_DMA(&hadc1, (uint32_t *)adc_capture,
                        BENCH_CAPTURE_SAMPLES) != HAL_OK) return HAL_ERROR;
  __HAL_TIM_SET_COUNTER(&htim7, 0U);
  if (HAL_TIM_Base_Start(&htim7) != HAL_OK)
  {
    (void)HAL_ADC_Stop_DMA(&hadc1);
    return HAL_ERROR;
  }
  started = HAL_GetTick();
  while ((!capture_complete) && ((uint32_t)(HAL_GetTick() - started) < 20U))
  {
    __NOP();
  }
  (void)HAL_TIM_Base_Stop(&htim7);
  (void)HAL_ADC_Stop_DMA(&hadc1);
  return capture_complete ? HAL_OK : HAL_TIMEOUT;
}

void BenchWave_Analyze(BenchCaptureAnalysis *analysis)
{
  uint32_t i;
  uint64_t sum = 0U;
  uint16_t minimum = 4095U;
  uint16_t maximum = 0U;
  uint16_t peak_index = 0U;
  if (analysis == NULL) return;
  memset(analysis, 0, sizeof(*analysis));
  for (i = 0U; i < BENCH_CAPTURE_SAMPLES; ++i)
  {
    uint16_t sample = adc_capture[i];
    if (sample < minimum) minimum = sample;
    if ((sample > maximum) && (i < BENCH_CAPTURE_SAMPLES - 128U))
    {
      maximum = sample;
      peak_index = (uint16_t)i;
    }
    sum += sample;
  }
  analysis->minimum = minimum;
  analysis->maximum = maximum;
  analysis->baseline = minimum;
  analysis->peak_index = peak_index;
  analysis->mean = (uint32_t)(sum / BENCH_CAPTURE_SAMPLES);
  analysis->peak_mv = (uint32_t)maximum * VDDA_MV / ADC_FULL_SCALE;
  analysis->baseline_mv = (uint32_t)minimum * VDDA_MV / ADC_FULL_SCALE;
  if (maximum > minimum)
  {
    uint32_t amplitude = maximum - minimum;
    uint16_t target = (uint16_t)(minimum + (amplitude * 368U) / 1000U);
    for (i = (uint32_t)peak_index + 1U; i < BENCH_CAPTURE_SAMPLES; ++i)
    {
      if (adc_capture[i] <= target)
      {
        analysis->tau_ns = (i - peak_index) * 1000000000UL / BENCH_ADC_SAMPLE_HZ;
        break;
      }
    }
    analysis->droop_ppm = amplitude * 1000000UL / maximum;
  }
}

const uint16_t *BenchWave_GetCapture(void)
{
  return adc_capture;
}

void HAL_ADC_ConvCpltCallback(ADC_HandleTypeDef *adc)
{
  if (adc->Instance == ADC1) capture_complete = 1U;
}

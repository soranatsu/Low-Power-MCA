#ifndef BENCH_WAVEFORM_H
#define BENCH_WAVEFORM_H

#include "main.h"

#define BENCH_DAC_UPDATE_HZ       1000000UL
#define BENCH_ADC_SAMPLE_HZ       2000000UL
#define BENCH_WAVE_SAMPLES        1000U
#define BENCH_CAPTURE_SAMPLES     4096U

typedef enum
{
  BENCH_WAVE_OFF = 0,
  BENCH_WAVE_EXPONENTIAL,
  BENCH_WAVE_PEAK_HOLD,
  BENCH_WAVE_STATIC
} BenchWaveMode;

typedef struct
{
  uint16_t minimum;
  uint16_t maximum;
  uint16_t baseline;
  uint16_t peak_index;
  uint32_t mean;
  uint32_t peak_mv;
  uint32_t baseline_mv;
  uint32_t tau_ns;
  uint32_t droop_ppm;
} BenchCaptureAnalysis;

HAL_StatusTypeDef BenchWave_Init(void);
HAL_StatusTypeDef BenchWave_StartExponential(uint16_t amplitude_mv,
                                             uint16_t decay_us);
HAL_StatusTypeDef BenchWave_StartPeakHold(uint16_t amplitude_mv,
                                         uint16_t hold_us,
                                         uint16_t end_permille);
HAL_StatusTypeDef BenchWave_SetStaticCode(uint16_t code);
void BenchWave_Stop(void);
BenchWaveMode BenchWave_GetMode(void);
HAL_StatusTypeDef BenchWave_ReadSingle(uint16_t *raw);
HAL_StatusTypeDef BenchWave_Capture(void);
void BenchWave_Analyze(BenchCaptureAnalysis *analysis);
const uint16_t *BenchWave_GetCapture(void);

#endif

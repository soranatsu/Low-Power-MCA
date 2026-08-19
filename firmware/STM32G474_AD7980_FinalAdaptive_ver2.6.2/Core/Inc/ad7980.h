#ifndef AD7980_H
#define AD7980_H

#include <stdbool.h>
#include <stdint.h>

typedef struct
{
  uint32_t sequence;
  uint32_t timestamp_ms;
  uint16_t raw;
} AD7980_Sample;

void AD7980_Init(void);
void AD7980_ResetStatistics(void);
void AD7980_BusyISR(void);
void AD7980_Service(void);
bool AD7980_TryRead(AD7980_Sample *sample);
bool AD7980_IsSdoLow(void);
uint32_t AD7980_GetBusyCount(void);
uint32_t AD7980_GetRecoveryCount(void);
uint32_t AD7980_GetPostReadLowCount(void);
uint32_t AD7980_GetOverrunCount(void);
uint32_t AD7980_GetQueueDepth(void);

#endif

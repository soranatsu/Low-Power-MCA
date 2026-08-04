#ifndef TIM_H
#define TIM_H

#ifdef __cplusplus
extern "C" {
#endif

#include "main.h"

extern TIM_HandleTypeDef htim6;
extern TIM_HandleTypeDef htim7;

void MX_TIM6_Init(void);
void MX_TIM7_Init(void);

#ifdef __cplusplus
}
#endif

#endif

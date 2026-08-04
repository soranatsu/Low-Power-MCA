#include "tim.h"

TIM_HandleTypeDef htim6;
TIM_HandleTypeDef htim7;

static void init_basic_timer(TIM_HandleTypeDef *timer, TIM_TypeDef *instance,
                             uint32_t period)
{
  TIM_MasterConfigTypeDef master = {0};
  timer->Instance = instance;
  timer->Init.Prescaler = 0U;
  timer->Init.CounterMode = TIM_COUNTERMODE_UP;
  timer->Init.Period = period;
  timer->Init.AutoReloadPreload = TIM_AUTORELOAD_PRELOAD_DISABLE;
  if (HAL_TIM_Base_Init(timer) != HAL_OK) Error_Handler();
  master.MasterOutputTrigger = TIM_TRGO_UPDATE;
  master.MasterSlaveMode = TIM_MASTERSLAVEMODE_DISABLE;
  if (HAL_TIMEx_MasterConfigSynchronization(timer, &master) != HAL_OK) Error_Handler();
}

void MX_TIM6_Init(void)
{
  /* APB1 timer clock is 170 MHz: 170 / (169 + 1) = 1 MHz DAC update. */
  init_basic_timer(&htim6, TIM6, 169U);
}

void MX_TIM7_Init(void)
{
  /* 170 / (84 + 1) = 2 MHz ADC trigger. */
  init_basic_timer(&htim7, TIM7, 84U);
}

void HAL_TIM_Base_MspInit(TIM_HandleTypeDef *timer)
{
  if (timer->Instance == TIM6) __HAL_RCC_TIM6_CLK_ENABLE();
  else if (timer->Instance == TIM7) __HAL_RCC_TIM7_CLK_ENABLE();
}

void HAL_TIM_Base_MspDeInit(TIM_HandleTypeDef *timer)
{
  if (timer->Instance == TIM6) __HAL_RCC_TIM6_CLK_DISABLE();
  else if (timer->Instance == TIM7) __HAL_RCC_TIM7_CLK_DISABLE();
}

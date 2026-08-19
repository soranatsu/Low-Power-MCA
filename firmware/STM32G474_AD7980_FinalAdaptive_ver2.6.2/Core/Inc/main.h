/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.h
  * @brief          : Header for main.c file.
  *                   This file contains the common defines of the application.
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2026 STMicroelectronics.
  * All rights reserved.
  *
  * This software is licensed under terms that can be found in the LICENSE file
  * in the root directory of this software component.
  * If no LICENSE file comes with this software, it is provided AS-IS.
  *
  ******************************************************************************
  */
/* USER CODE END Header */

/* Define to prevent recursive inclusion -------------------------------------*/
#ifndef __MAIN_H
#define __MAIN_H

#ifdef __cplusplus
extern "C" {
#endif

/* Includes ------------------------------------------------------------------*/
#include "stm32g4xx_hal.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */

/* USER CODE END Includes */

/* Exported types ------------------------------------------------------------*/
/* USER CODE BEGIN ET */

/* USER CODE END ET */

/* Exported constants --------------------------------------------------------*/
/* USER CODE BEGIN EC */

/* USER CODE END EC */

/* Exported macro ------------------------------------------------------------*/
/* USER CODE BEGIN EM */

/* USER CODE END EM */

/* Exported functions prototypes ---------------------------------------------*/
void Error_Handler(void);

/* USER CODE BEGIN EFP */

/* USER CODE END EFP */

/* Private defines -----------------------------------------------------------*/
#define DIGITAL_GND_PF2_DO_NOT_DRIVE_Pin GPIO_PIN_2
#define DIGITAL_GND_PF2_DO_NOT_DRIVE_GPIO_Port GPIOF
#define ADC_BUSY_IRQ_Pin GPIO_PIN_0
#define ADC_BUSY_IRQ_GPIO_Port GPIOA
#define ADC_BUSY_IRQ_EXTI_IRQn EXTI0_IRQn
#define PH_RESTART_Pin GPIO_PIN_1
#define PH_RESTART_GPIO_Port GPIOA
#define DIGITAL_GND_PA2_DO_NOT_DRIVE_Pin GPIO_PIN_2
#define DIGITAL_GND_PA2_DO_NOT_DRIVE_GPIO_Port GPIOA
#define DIGITAL_GND_PA3_DO_NOT_DRIVE_Pin GPIO_PIN_3
#define DIGITAL_GND_PA3_DO_NOT_DRIVE_GPIO_Port GPIOA
#define THRESHOLD_DAC_Pin GPIO_PIN_4
#define THRESHOLD_DAC_GPIO_Port GPIOA
#define ADC_SCK_Pin GPIO_PIN_5
#define ADC_SCK_GPIO_Port GPIOA
#define ADC_SDO_Pin GPIO_PIN_6
#define ADC_SDO_GPIO_Port GPIOA
#define DIGITAL_GND_PC4_DO_NOT_DRIVE_Pin GPIO_PIN_4
#define DIGITAL_GND_PC4_DO_NOT_DRIVE_GPIO_Port GPIOC
#define DIGITAL_GND_PC5_DO_NOT_DRIVE_Pin GPIO_PIN_5
#define DIGITAL_GND_PC5_DO_NOT_DRIVE_GPIO_Port GPIOC
#define FPC_DISABLED_SPI1_MOSI_PA7_Pin GPIO_PIN_7
#define FPC_DISABLED_SPI1_MOSI_PA7_GPIO_Port GPIOA
#define FPC_DISABLED_UART_TX_PA9_Pin GPIO_PIN_9
#define FPC_DISABLED_UART_TX_PA9_GPIO_Port GPIOA
#define FPC_DISABLED_UART_RX_PA10_Pin GPIO_PIN_10
#define FPC_DISABLED_UART_RX_PA10_GPIO_Port GPIOA
#define FPC_DISABLED_I2C_SCL_PC8_Pin GPIO_PIN_8
#define FPC_DISABLED_I2C_SCL_PC8_GPIO_Port GPIOC
#define FPC_DISABLED_I2C_SDA_PC9_Pin GPIO_PIN_9
#define FPC_DISABLED_I2C_SDA_PC9_GPIO_Port GPIOC
#define FPC_DISABLED_SPI2_SCK_PB13_Pin GPIO_PIN_13
#define FPC_DISABLED_SPI2_SCK_PB13_GPIO_Port GPIOB
#define FPC_DISABLED_SPI2_MISO_PB14_Pin GPIO_PIN_14
#define FPC_DISABLED_SPI2_MISO_PB14_GPIO_Port GPIOB
#define FPC_DISABLED_SPI2_MOSI_PB15_Pin GPIO_PIN_15
#define FPC_DISABLED_SPI2_MOSI_PB15_GPIO_Port GPIOB

/* USER CODE BEGIN Private defines */

/* USER CODE END Private defines */

#ifdef __cplusplus
}
#endif

#endif /* __MAIN_H */

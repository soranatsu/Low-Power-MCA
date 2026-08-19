#ifndef APP_H
#define APP_H

#include "stm32g4xx_hal.h"
#include <stdint.h>

void App_Init(DAC_HandleTypeDef *dac);
void App_Run(void);
void App_USB_Rx(const uint8_t *data, uint32_t length);
void App_USB_TxComplete(void);
void App_USB_LinkState(uint8_t configured);

#endif

#ifndef ST7789_H
#define ST7789_H

#include "main.h"
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define ST7789_WIDTH   172U
#define ST7789_HEIGHT  320U

HAL_StatusTypeDef ST7789_Init(void);
void ST7789_SetBacklight(uint8_t on);
HAL_StatusTypeDef ST7789_FillScreen(uint16_t rgb565);
HAL_StatusTypeDef ST7789_DrawHistogram(const uint32_t *bins, uint32_t bin_count);

#ifdef __cplusplus
}
#endif

#endif

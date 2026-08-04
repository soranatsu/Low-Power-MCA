#include "st7789.h"
#include "spi.h"

#define LCD_X_OFFSET 34U
#define LCD_Y_OFFSET 0U
#define COLOR_BLACK  0x0000U
#define COLOR_GREEN  0x07E0U

static void select_lcd(uint8_t select)
{
  HAL_GPIO_WritePin(LCD_CS_GPIO_Port, LCD_CS_Pin, select ? GPIO_PIN_RESET : GPIO_PIN_SET);
}

static HAL_StatusTypeDef write_command(uint8_t command)
{
  HAL_GPIO_WritePin(LCD_DC_GPIO_Port, LCD_DC_Pin, GPIO_PIN_RESET);
  select_lcd(1);
  HAL_StatusTypeDef result = HAL_SPI_Transmit(&hspi2, &command, 1, 100U);
  select_lcd(0);
  return result;
}

static HAL_StatusTypeDef write_data(const uint8_t *data, uint16_t length)
{
  HAL_GPIO_WritePin(LCD_DC_GPIO_Port, LCD_DC_Pin, GPIO_PIN_SET);
  select_lcd(1);
  HAL_StatusTypeDef result = HAL_SPI_Transmit(&hspi2, (uint8_t *)data, length, 100U);
  select_lcd(0);
  return result;
}

static HAL_StatusTypeDef set_window(uint16_t x0, uint16_t y0, uint16_t x1, uint16_t y1)
{
  uint8_t data[4];
  x0 += LCD_X_OFFSET;
  x1 += LCD_X_OFFSET;
  y0 += LCD_Y_OFFSET;
  y1 += LCD_Y_OFFSET;

  if (write_command(0x2AU) != HAL_OK) return HAL_ERROR;
  data[0] = (uint8_t)(x0 >> 8); data[1] = (uint8_t)x0;
  data[2] = (uint8_t)(x1 >> 8); data[3] = (uint8_t)x1;
  if (write_data(data, sizeof(data)) != HAL_OK) return HAL_ERROR;
  if (write_command(0x2BU) != HAL_OK) return HAL_ERROR;
  data[0] = (uint8_t)(y0 >> 8); data[1] = (uint8_t)y0;
  data[2] = (uint8_t)(y1 >> 8); data[3] = (uint8_t)y1;
  if (write_data(data, sizeof(data)) != HAL_OK) return HAL_ERROR;
  return write_command(0x2CU);
}

static HAL_StatusTypeDef fill_rect(uint16_t x, uint16_t y, uint16_t width, uint16_t height, uint16_t color)
{
  uint8_t line[ST7789_WIDTH * 2U];
  uint32_t row;
  uint16_t i;
  if ((width == 0U) || (height == 0U) || (x + width > ST7789_WIDTH) || (y + height > ST7789_HEIGHT))
  {
    return HAL_ERROR;
  }
  for (i = 0; i < width; ++i)
  {
    line[2U * i] = (uint8_t)(color >> 8);
    line[2U * i + 1U] = (uint8_t)color;
  }
  if (set_window(x, y, x + width - 1U, y + height - 1U) != HAL_OK) return HAL_ERROR;
  HAL_GPIO_WritePin(LCD_DC_GPIO_Port, LCD_DC_Pin, GPIO_PIN_SET);
  select_lcd(1);
  for (row = 0; row < height; ++row)
  {
    if (HAL_SPI_Transmit(&hspi2, line, width * 2U, 100U) != HAL_OK)
    {
      select_lcd(0);
      return HAL_ERROR;
    }
  }
  select_lcd(0);
  return HAL_OK;
}

void ST7789_SetBacklight(uint8_t on)
{
  HAL_GPIO_WritePin(LCD_BLK_GPIO_Port, LCD_BLK_Pin, on ? GPIO_PIN_SET : GPIO_PIN_RESET);
}

HAL_StatusTypeDef ST7789_Init(void)
{
  uint8_t parameter;
  select_lcd(0);
  ST7789_SetBacklight(0);
  HAL_GPIO_WritePin(LCD_RST_GPIO_Port, LCD_RST_Pin, GPIO_PIN_RESET);
  HAL_Delay(20U);
  HAL_GPIO_WritePin(LCD_RST_GPIO_Port, LCD_RST_Pin, GPIO_PIN_SET);
  HAL_Delay(120U);
  if (write_command(0x11U) != HAL_OK) return HAL_ERROR;
  HAL_Delay(120U);
  parameter = 0x55U;
  if ((write_command(0x3AU) != HAL_OK) || (write_data(&parameter, 1U) != HAL_OK)) return HAL_ERROR;
  parameter = 0x00U;
  if ((write_command(0x36U) != HAL_OK) || (write_data(&parameter, 1U) != HAL_OK)) return HAL_ERROR;
  if (write_command(0x21U) != HAL_OK) return HAL_ERROR;
  if (write_command(0x29U) != HAL_OK) return HAL_ERROR;
  HAL_Delay(20U);
  ST7789_SetBacklight(1);
  return ST7789_FillScreen(COLOR_BLACK);
}

HAL_StatusTypeDef ST7789_FillScreen(uint16_t rgb565)
{
  return fill_rect(0U, 0U, ST7789_WIDTH, ST7789_HEIGHT, rgb565);
}

HAL_StatusTypeDef ST7789_DrawHistogram(const uint32_t *bins, uint32_t bin_count)
{
  uint32_t x;
  uint32_t maximum = 1U;
  if ((bins == NULL) || (bin_count == 0U)) return HAL_ERROR;
  if (fill_rect(0U, 0U, ST7789_WIDTH, ST7789_HEIGHT, COLOR_BLACK) != HAL_OK) return HAL_ERROR;
  for (x = 0U; x < ST7789_WIDTH; ++x)
  {
    uint32_t begin = (x * bin_count) / ST7789_WIDTH;
    uint32_t end = ((x + 1U) * bin_count) / ST7789_WIDTH;
    uint32_t i;
    uint32_t sum = 0U;
    if (end <= begin) end = begin + 1U;
    for (i = begin; i < end; ++i) sum += bins[i];
    if (sum > maximum) maximum = sum;
  }
  for (x = 0U; x < ST7789_WIDTH; ++x)
  {
    uint32_t begin = (x * bin_count) / ST7789_WIDTH;
    uint32_t end = ((x + 1U) * bin_count) / ST7789_WIDTH;
    uint32_t i;
    uint32_t sum = 0U;
    uint16_t height;
    if (end <= begin) end = begin + 1U;
    for (i = begin; i < end; ++i) sum += bins[i];
    height = (uint16_t)((sum * (ST7789_HEIGHT - 1U)) / maximum);
    if ((height > 0U) &&
        (fill_rect((uint16_t)x, (uint16_t)(ST7789_HEIGHT - height), 1U, height, COLOR_GREEN) != HAL_OK))
    {
      return HAL_ERROR;
    }
  }
  return HAL_OK;
}

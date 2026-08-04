#ifndef W25Q32_H
#define W25Q32_H

#include "main.h"
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define W25Q32_CAPACITY_BYTES       (4UL * 1024UL * 1024UL)
#define W25Q32_PAGE_SIZE            256UL
#define W25Q32_SECTOR_SIZE          4096UL
#define W25Q32_TEST_SECTOR_ADDRESS  (W25Q32_CAPACITY_BYTES - W25Q32_SECTOR_SIZE)

HAL_StatusTypeDef W25Q32_ReadJedecId(uint8_t id[3]);
HAL_StatusTypeDef W25Q32_Read(uint32_t address, uint8_t *data, uint32_t length);
HAL_StatusTypeDef W25Q32_PageProgram(uint32_t address, const uint8_t *data, uint32_t length);
HAL_StatusTypeDef W25Q32_SectorErase(uint32_t address);
HAL_StatusTypeDef W25Q32_WaitReady(uint32_t timeout_ms);
HAL_StatusTypeDef W25Q32_TestLastSector(void);

#ifdef __cplusplus
}
#endif

#endif

#include "w25q32.h"
#include "quadspi.h"
#include <string.h>

#define CMD_WRITE_ENABLE   0x06U
#define CMD_READ_STATUS_1  0x05U
#define CMD_READ_DATA      0x03U
#define CMD_PAGE_PROGRAM   0x02U
#define CMD_SECTOR_ERASE   0x20U
#define CMD_JEDEC_ID       0x9FU

static QSPI_CommandTypeDef command_base(uint8_t instruction)
{
  QSPI_CommandTypeDef command = {0};
  command.InstructionMode = QSPI_INSTRUCTION_1_LINE;
  command.Instruction = instruction;
  command.AddressMode = QSPI_ADDRESS_NONE;
  command.AddressSize = QSPI_ADDRESS_24_BITS;
  command.AlternateByteMode = QSPI_ALTERNATE_BYTES_NONE;
  command.DataMode = QSPI_DATA_NONE;
  command.DummyCycles = 0;
  command.DdrMode = QSPI_DDR_MODE_DISABLE;
  command.DdrHoldHalfCycle = QSPI_DDR_HHC_ANALOG_DELAY;
  command.SIOOMode = QSPI_SIOO_INST_EVERY_CMD;
  return command;
}

static HAL_StatusTypeDef write_enable(void)
{
  QSPI_CommandTypeDef command = command_base(CMD_WRITE_ENABLE);
  return HAL_QSPI_Command(&hqspi1, &command, HAL_QSPI_TIMEOUT_DEFAULT_VALUE);
}

HAL_StatusTypeDef W25Q32_WaitReady(uint32_t timeout_ms)
{
  QSPI_CommandTypeDef command = command_base(CMD_READ_STATUS_1);
  QSPI_AutoPollingTypeDef poll = {0};

  command.DataMode = QSPI_DATA_1_LINE;
  command.NbData = 1;
  poll.Match = 0;
  poll.Mask = 0x01U;
  poll.MatchMode = QSPI_MATCH_MODE_AND;
  poll.StatusBytesSize = 1;
  poll.Interval = 0x10U;
  poll.AutomaticStop = QSPI_AUTOMATIC_STOP_ENABLE;
  return HAL_QSPI_AutoPolling(&hqspi1, &command, &poll, timeout_ms);
}

HAL_StatusTypeDef W25Q32_ReadJedecId(uint8_t id[3])
{
  QSPI_CommandTypeDef command = command_base(CMD_JEDEC_ID);
  command.DataMode = QSPI_DATA_1_LINE;
  command.NbData = 3;
  if (HAL_QSPI_Command(&hqspi1, &command, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) != HAL_OK)
  {
    return HAL_ERROR;
  }
  return HAL_QSPI_Receive(&hqspi1, id, HAL_QSPI_TIMEOUT_DEFAULT_VALUE);
}

HAL_StatusTypeDef W25Q32_Read(uint32_t address, uint8_t *data, uint32_t length)
{
  QSPI_CommandTypeDef command;
  if ((data == NULL) || (length == 0U) || (address + length > W25Q32_CAPACITY_BYTES))
  {
    return HAL_ERROR;
  }
  command = command_base(CMD_READ_DATA);
  command.AddressMode = QSPI_ADDRESS_1_LINE;
  command.Address = address;
  command.DataMode = QSPI_DATA_1_LINE;
  command.NbData = length;
  if (HAL_QSPI_Command(&hqspi1, &command, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) != HAL_OK)
  {
    return HAL_ERROR;
  }
  return HAL_QSPI_Receive(&hqspi1, data, HAL_QSPI_TIMEOUT_DEFAULT_VALUE);
}

HAL_StatusTypeDef W25Q32_PageProgram(uint32_t address, const uint8_t *data, uint32_t length)
{
  QSPI_CommandTypeDef command;
  if ((data == NULL) || (length == 0U) || (length > W25Q32_PAGE_SIZE) ||
      ((address & (W25Q32_PAGE_SIZE - 1U)) + length > W25Q32_PAGE_SIZE) ||
      (address + length > W25Q32_CAPACITY_BYTES))
  {
    return HAL_ERROR;
  }
  if ((W25Q32_WaitReady(100U) != HAL_OK) || (write_enable() != HAL_OK))
  {
    return HAL_ERROR;
  }
  command = command_base(CMD_PAGE_PROGRAM);
  command.AddressMode = QSPI_ADDRESS_1_LINE;
  command.Address = address;
  command.DataMode = QSPI_DATA_1_LINE;
  command.NbData = length;
  if (HAL_QSPI_Command(&hqspi1, &command, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) != HAL_OK)
  {
    return HAL_ERROR;
  }
  if (HAL_QSPI_Transmit(&hqspi1, (uint8_t *)data, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) != HAL_OK)
  {
    return HAL_ERROR;
  }
  return W25Q32_WaitReady(10U);
}

HAL_StatusTypeDef W25Q32_SectorErase(uint32_t address)
{
  QSPI_CommandTypeDef command;
  address &= ~(W25Q32_SECTOR_SIZE - 1U);
  if (address >= W25Q32_CAPACITY_BYTES)
  {
    return HAL_ERROR;
  }
  if ((W25Q32_WaitReady(100U) != HAL_OK) || (write_enable() != HAL_OK))
  {
    return HAL_ERROR;
  }
  command = command_base(CMD_SECTOR_ERASE);
  command.AddressMode = QSPI_ADDRESS_1_LINE;
  command.Address = address;
  if (HAL_QSPI_Command(&hqspi1, &command, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) != HAL_OK)
  {
    return HAL_ERROR;
  }
  return W25Q32_WaitReady(3000U);
}

HAL_StatusTypeDef W25Q32_TestLastSector(void)
{
  uint8_t written[32];
  uint8_t readback[32];
  uint32_t i;

  for (i = 0; i < sizeof(written); ++i)
  {
    written[i] = (uint8_t)(0x5AU ^ i);
  }
  if (W25Q32_SectorErase(W25Q32_TEST_SECTOR_ADDRESS) != HAL_OK)
  {
    return HAL_ERROR;
  }
  if (W25Q32_PageProgram(W25Q32_TEST_SECTOR_ADDRESS, written, sizeof(written)) != HAL_OK)
  {
    return HAL_ERROR;
  }
  if (W25Q32_Read(W25Q32_TEST_SECTOR_ADDRESS, readback, sizeof(readback)) != HAL_OK)
  {
    return HAL_ERROR;
  }
  return (memcmp(written, readback, sizeof(written)) == 0) ? HAL_OK : HAL_ERROR;
}

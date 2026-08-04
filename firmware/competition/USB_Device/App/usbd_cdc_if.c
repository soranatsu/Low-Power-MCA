#include "usbd_cdc_if.h"
#include "app.h"
#include <string.h>

#define USB_TX_RING_SIZE 16384U

uint8_t UserRxBufferFS[APP_RX_DATA_SIZE];
uint8_t UserTxBufferFS[APP_TX_DATA_SIZE];
static uint8_t tx_ring[USB_TX_RING_SIZE];
static volatile uint16_t tx_head;
static volatile uint16_t tx_tail;
static uint8_t tx_packet[CDC_DATA_FS_MAX_PACKET_SIZE];

extern USBD_HandleTypeDef hUsbDeviceFS;

static int8_t CDC_Init_FS(void);
static int8_t CDC_DeInit_FS(void);
static int8_t CDC_Control_FS(uint8_t cmd, uint8_t *pbuf, uint16_t length);
static int8_t CDC_Receive_FS(uint8_t *pbuf, uint32_t *length);
static int8_t CDC_TransmitCplt_FS(uint8_t *pbuf, uint32_t *length, uint8_t epnum);

USBD_CDC_ItfTypeDef USBD_Interface_fops_FS =
{
  CDC_Init_FS,
  CDC_DeInit_FS,
  CDC_Control_FS,
  CDC_Receive_FS,
  CDC_TransmitCplt_FS
};

static int8_t CDC_Init_FS(void)
{
  USBD_CDC_SetTxBuffer(&hUsbDeviceFS, UserTxBufferFS, 0U);
  USBD_CDC_SetRxBuffer(&hUsbDeviceFS, UserRxBufferFS);
  return USBD_OK;
}

static int8_t CDC_DeInit_FS(void)
{
  return USBD_OK;
}

static int8_t CDC_Control_FS(uint8_t cmd, uint8_t *pbuf, uint16_t length)
{
  (void)cmd;
  (void)pbuf;
  (void)length;
  return USBD_OK;
}

static int8_t CDC_Receive_FS(uint8_t *pbuf, uint32_t *length)
{
  App_UsbReceive(pbuf, *length);
  USBD_CDC_SetRxBuffer(&hUsbDeviceFS, UserRxBufferFS);
  USBD_CDC_ReceivePacket(&hUsbDeviceFS);
  return USBD_OK;
}

static int8_t CDC_TransmitCplt_FS(uint8_t *pbuf, uint32_t *length, uint8_t epnum)
{
  (void)pbuf;
  (void)length;
  (void)epnum;
  return USBD_OK;
}

uint8_t CDC_Transmit_FS(uint8_t *buffer, uint16_t length)
{
  USBD_CDC_HandleTypeDef *cdc = (USBD_CDC_HandleTypeDef *)hUsbDeviceFS.pClassData;
  if ((cdc == NULL) || (cdc->TxState != 0U) || (length > sizeof(tx_packet)))
  {
    return USBD_BUSY;
  }
  memcpy(tx_packet, buffer, length);
  USBD_CDC_SetTxBuffer(&hUsbDeviceFS, tx_packet, length);
  return USBD_CDC_TransmitPacket(&hUsbDeviceFS);
}

uint16_t CDC_Write(const uint8_t *buffer, uint16_t length)
{
  uint16_t written = 0U;
  while (written < length)
  {
    uint16_t next = (tx_head + 1U) & (USB_TX_RING_SIZE - 1U);
    if (next == tx_tail) break;
    tx_ring[tx_head] = buffer[written++];
    tx_head = next;
  }
  return written;
}

void CDC_Task(void)
{
  uint16_t length = 0U;
  uint16_t tail = tx_tail;
  while ((tail != tx_head) && (length < sizeof(tx_packet)))
  {
    tx_packet[length++] = tx_ring[tail];
    tail = (tail + 1U) & (USB_TX_RING_SIZE - 1U);
  }
  if ((length > 0U) && (CDC_Transmit_FS(tx_packet, length) == USBD_OK))
  {
    tx_tail = tail;
  }
}

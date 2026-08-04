TARGET := nucom_competition
BUILD_DIR := build

PREFIX := arm-none-eabi-
CC := $(PREFIX)gcc
AS := $(PREFIX)gcc -x assembler-with-cpp
CP := $(PREFIX)objcopy
SZ := $(PREFIX)size

CORE_SOURCES := $(wildcard Core/Src/*.c)
HAL_SOURCES := $(wildcard Drivers/STM32G4xx_HAL_Driver/Src/*.c)
USB_CORE_SOURCES := \
  Middlewares/ST/STM32_USB_Device_Library/Core/Src/usbd_core.c \
  Middlewares/ST/STM32_USB_Device_Library/Core/Src/usbd_ctlreq.c \
  Middlewares/ST/STM32_USB_Device_Library/Core/Src/usbd_ioreq.c
USB_CDC_SOURCES := Middlewares/ST/STM32_USB_Device_Library/Class/CDC/Src/usbd_cdc.c
USB_APP_SOURCES := $(wildcard USB_Device/App/*.c) $(wildcard USB_Device/Target/*.c)
C_SOURCES := $(CORE_SOURCES) $(HAL_SOURCES) $(USB_CORE_SOURCES) $(USB_CDC_SOURCES) $(USB_APP_SOURCES)
ASM_SOURCES := Core/Startup/startup_stm32g474vetx.s

INCLUDES := \
  -ICore/Inc \
  -IDrivers/STM32G4xx_HAL_Driver/Inc \
  -IDrivers/STM32G4xx_HAL_Driver/Inc/Legacy \
  -IDrivers/CMSIS/Device/ST/STM32G4xx/Include \
  -IDrivers/CMSIS/Include \
  -IMiddlewares/ST/STM32_USB_Device_Library/Core/Inc \
  -IMiddlewares/ST/STM32_USB_Device_Library/Class/CDC/Inc \
  -IUSB_Device/App \
  -IUSB_Device/Target

DEFINES := -DUSE_HAL_DRIVER -DSTM32G474xx
MCU := -mcpu=cortex-m4 -mthumb -mfpu=fpv4-sp-d16 -mfloat-abi=hard
CFLAGS := $(MCU) $(DEFINES) $(INCLUDES) -std=gnu11 -O2 -g3 \
  -ffunction-sections -fdata-sections -Wall -Wextra \
  -Wno-unused-parameter -MMD -MP
ASFLAGS := $(MCU) $(DEFINES) $(INCLUDES) -g3
LDSCRIPT := STM32G474VETX_FLASH.ld
LDFLAGS := $(MCU) -T$(LDSCRIPT) --specs=nano.specs --specs=nosys.specs \
  -Wl,-Map=$(BUILD_DIR)/$(TARGET).map,--cref -Wl,--gc-sections \
  -Wl,--start-group -lc -lm -Wl,--end-group

OBJECTS := $(addprefix $(BUILD_DIR)/,$(notdir $(C_SOURCES:.c=.o)))
OBJECTS += $(addprefix $(BUILD_DIR)/,$(notdir $(ASM_SOURCES:.s=.o)))
DEPS := $(OBJECTS:.o=.d)

VPATH := $(sort $(dir $(C_SOURCES) $(ASM_SOURCES)))

.PHONY: all clean size

all: $(BUILD_DIR)/$(TARGET).elf $(BUILD_DIR)/$(TARGET).hex $(BUILD_DIR)/$(TARGET).bin size

$(BUILD_DIR):
	powershell -NoProfile -Command "New-Item -ItemType Directory -Force -Path '$@' | Out-Null"

$(BUILD_DIR)/%.o: %.c | $(BUILD_DIR)
	$(CC) -c $(CFLAGS) $< -o $@

$(BUILD_DIR)/%.o: %.s | $(BUILD_DIR)
	$(AS) -c $(ASFLAGS) $< -o $@

$(BUILD_DIR)/$(TARGET).elf: $(OBJECTS)
	$(CC) $(OBJECTS) $(LDFLAGS) -o $@

$(BUILD_DIR)/%.hex: $(BUILD_DIR)/%.elf
	$(CP) -O ihex $< $@

$(BUILD_DIR)/%.bin: $(BUILD_DIR)/%.elf
	$(CP) -O binary -S $< $@

size: $(BUILD_DIR)/$(TARGET).elf
	$(SZ) $<

clean:
	powershell -NoProfile -Command "if (Test-Path -LiteralPath '$(BUILD_DIR)') { Remove-Item -LiteralPath '$(BUILD_DIR)' -Recurse -Force }"

-include $(DEPS)

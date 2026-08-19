################################################################################
# Automatically-generated file. Do not edit!
# Toolchain: GNU Tools for STM32 (12.3.rel1)
################################################################################

# Add inputs and outputs from these tool invocations to the build variables 
C_SRCS += \
C:/Users/sora/Documents/Codex/2026-08-05/stm32g474vet6-ad7980-arm-brm-adg721-stm32cubemx/outputs/STM32G474_AD7980_FinalAdaptive/USB_Device/Target/usbd_conf.c 

OBJS += \
./Application/User/USB_Device/Target/usbd_conf.o 

C_DEPS += \
./Application/User/USB_Device/Target/usbd_conf.d 


# Each subdirectory must supply rules for building sources it contributes
Application/User/USB_Device/Target/usbd_conf.o: C:/Users/sora/Documents/Codex/2026-08-05/stm32g474vet6-ad7980-arm-brm-adg721-stm32cubemx/outputs/STM32G474_AD7980_FinalAdaptive/USB_Device/Target/usbd_conf.c Application/User/USB_Device/Target/subdir.mk
	arm-none-eabi-gcc "$<" -mcpu=cortex-m4 -std=gnu11 -DUSE_HAL_DRIVER -DSTM32G474xx -c -I../../USB_Device/App -I../../USB_Device/Target -I../../Core/Inc -I../../Drivers/STM32G4xx_HAL_Driver/Inc -I../../Drivers/STM32G4xx_HAL_Driver/Inc/Legacy -I../../Middlewares/ST/STM32_USB_Device_Library/Core/Inc -I../../Middlewares/ST/STM32_USB_Device_Library/Class/CDC/Inc -I../../Drivers/CMSIS/Device/ST/STM32G4xx/Include -I../../Drivers/CMSIS/Include -O2 -ffunction-sections -fdata-sections -Wall -fstack-usage -fcyclomatic-complexity -MMD -MP -MF"$(@:%.o=%.d)" -MT"$@" --specs=nano.specs -mfpu=fpv4-sp-d16 -mfloat-abi=hard -mthumb -o "$@"

clean: clean-Application-2f-User-2f-USB_Device-2f-Target

clean-Application-2f-User-2f-USB_Device-2f-Target:
	-$(RM) ./Application/User/USB_Device/Target/usbd_conf.cyclo ./Application/User/USB_Device/Target/usbd_conf.d ./Application/User/USB_Device/Target/usbd_conf.o ./Application/User/USB_Device/Target/usbd_conf.su

.PHONY: clean-Application-2f-User-2f-USB_Device-2f-Target


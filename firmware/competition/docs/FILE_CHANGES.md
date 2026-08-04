# 为什么工程中改动了较多文件

原工程是CubeMX生成的“外设初始化骨架”，只有时钟、GPIO、SPI、QSPI、DAC和UART初始化，没有采集状态机、USB设备协议、Flash驱动、LCD驱动或命令行。要形成可运行的整机固件，必须同时补齐应用层和底层依赖。

## 自己编写的核心文件

| 文件 | 作用 |
|---|---|
| `Core/Src/app.c` | 采集状态机、AD7980 DMA回调、内部ADC测试模式、直方图、命令解析、Flash日志调度 |
| `Core/Inc/app.h` | `app.c`对外的函数和状态结构声明 |
| `Core/Src/adc.c`、`Core/Inc/adc.h` | PC3/ADC1_IN9初始化；采用CubeMX常见的一个外设一对`.c/.h`组织 |
| `Core/Src/w25q32.c`、`Core/Inc/w25q32.h` | 板载Q32 Flash读写、擦除和自检 |
| `Core/Src/st7789.c`、`Core/Inc/st7789.h` | LCD初始化和直方图绘制 |

头文件本身不执行代码。它的作用是让其他`.c`文件知道函数、变量、结构体和宏的声明。把所有声明都塞进`main.c`虽然文件数量少，但会产生隐式声明、重复定义和模块耦合，后续更难调试。

## 对CubeMX生成文件的必要改动

| 文件 | 为什么改 |
|---|---|
| `Core/Src/main.c` | 初始化ADC、USB并调用`App_Init/App_Task` |
| `Core/Inc/stm32g4xx_hal_conf.h` | 打开`HAL_ADC_MODULE_ENABLED`和`HAL_PCD_MODULE_ENABLED`；不开宏，HAL不会暴露相应类型/函数 |
| `Core/Src/stm32g4xx_it.c`、`Core/Inc/stm32g4xx_it.h` | 增加USB中断入口；`.h`负责中断函数声明，`.c`负责实现 |
| `Core/Src/gpio.c` | 初始化开发板RGB状态灯 |
| `nucom_daq.ioc` | 让CubeMX记录PC3 ADC、USB和中断配置，避免图形界面与源码完全不一致 |
| `.cproject` | 告诉STM32CubeIDE到哪些目录寻找USB头文件，以及哪些目录需要参与编译 |

## 数量很多但不是逐个手写的文件

以下目录主要是ST官方USB CDC和HAL驱动，从本机STM32Cube G4固件包复制：

```text
Middlewares/ST/STM32_USB_Device_Library/
USB_Device/
Drivers/STM32G4xx_HAL_Driver/*adc*
Drivers/STM32G4xx_HAL_Driver/*pcd*
Drivers/STM32G4xx_HAL_Driver/stm32g4xx_ll_usb.*
```

USB CDC不是单个函数：它包含USB底层PCD、端点控制、描述符、CDC类和收发接口，所以会自然产生多份源文件和头文件。它们大多是供应商库，不代表每个文件都加入了项目业务逻辑。

## 构建相关文件

| 文件 | 作用 |
|---|---|
| `Makefile` | 不打开STM32CubeIDE也能重复构建并生成ELF/HEX/BIN |
| `STM32G474VETX_FLASH.ld`、`RAM.ld` | 仅去掉本机GCC 10不支持的`READONLY`语法，内存地址和容量未改变 |
| `README.md`、`docs/*.md` | 记录接线、命令、风险和验收标准 |

如果目标只是“PC3读一个电压，然后在调试器里看变量”，可以裁剪为ADC、main和少量HAL文件。但当前目标还包括USB交互、直方图、Flash、LCD以及最终AD7980切换，因此保留这些模块更能提前验证整条系统链。

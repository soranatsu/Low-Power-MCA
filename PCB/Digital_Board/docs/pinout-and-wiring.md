# NUCOM DAQ 数字板引脚与接线说明

## 1. 适用范围

本文对应以下硬件与固件：

- MCU 开发板：STM32G474VET6，LQFP100，X Pulse 开发板
- 模拟板：`PCB/Analog_ADC`
- ADC：AD7980，16 bit，1 MSPS
- LCD：1.47 英寸、172×320、ST7789
- Flash：开发板板载 W25Q32，4 MiB
- STM32 工程：`firmware/stm32g474-daq`

本文中的 P2、P3 编号来自开发板原理图；模拟板 J2 编号来自最新
`Analog.SchDoc` 顶层原理图。

## 2. 数字板组成与当前方案

数字板第一版按下表设计。除“可选扩展”外，均应直接画入原理图。

| 模块 | 当前方案 | 数字板上的实现 |
|---|---|---|
| MCU 开发板连接 | P2、P3 各为 **2×22、2.54 mm 排母**，共两组 | 开发板倒插在数字板正面；按实物测量两组排针间距、板边位置和 pin 1 方向 |
| 模拟板连接 | J2 为 **2×20、2.54 mm 排针**，共一组 | 与模拟板背面的 2×20 排母对插，同时传输电源、ADC_SCK、ADC_SDO、Restart 和 Threshold |
| 外部输入与 +5V | `5.5~6 V → +5V` | 输入接口后依次设置保险/自恢复保险、TVS、反接保护和 5 V 转换电源；+5V 供开发板和模拟板数字/辅助电路 |
| 数字板 +3V3 | `+5V → +3V3` | 数字板 3.3 V 只供模拟板 J2 和 LCD；开发板由自身稳压器产生 MCU 3.3 V，数字板不得将两路 3.3 V 并联 |
| 模拟正电源 | `5.5~6 V → +5VA` | 使用低压差、低噪声稳压或经验证的滤波支路，供模拟板运放正电源 |
| 模拟负电源 | `+5V → -5VA` | 使用反相 DCDC/电荷泵并进行低噪声滤波，供模拟板运放负电源 |
| 防倒灌 | 开发板 5 V 入口增加 PMOS 防倒灌/理想二极管级 | 避免外部 5 V 与开发板 USB、调试器可能带入的电源互相反灌；SWD 烧录只接 SWDIO、SWCLK、GND 和目标板 VREF，不额外接 ST-LINK 5 V |
| Threshold DAC | 第一版使用 **STM32G474 内部 DAC1_OUT1（PA4）** | 经 9.1 kΩ/1.0 kΩ 精密分压和 100 nF 滤波后送入模拟板；DAC70502 不作为首版必装器件 |
| LCD | 预留 **ST7789、1×8、2.54 mm** 接口 | 使用 SPI2，接口包含 3V3、GND、SCK、MOSI、CS、DC、RST 和 BLK |
| 数据 Flash | 使用开发板板载 **W25Q32（4 MiB）** | 由 QSPI 连接，用于参数、能谱或采集数据的暂存；数字板不再并联第二颗 Flash |
| 电脑通信 | 采用 **USB FS Device** | 数据流程为 `AD7980 → STM32 → W25Q32/内存 → USB → 电脑`；首版优先实现 USB CDC，暂不增加 SD 卡 |
| 可选扩展 | 预留但默认不装 | 可留一组包含 3V3、GND 和空闲 GPIO/串口/I²C 的扩展焊盘；具体引脚须在 USB 固件引脚确定后再分配 |

USB 固件目前尚未在 CubeMX 工程中启用。若开发板自带 USB 接口在倒插后仍可正常插拔，
直接使用开发板接口；否则数字板可增加 USB 接口及 ESD 防护，但不能把两个 USB 接口
同时并联使用。

## 3. 当前 MCU 配置总表

| 功能 | STM32 引脚 | 开发板排针 | 固件配置 | 连接目标 |
|---|---|---:|---|---|
| ADC_BUSY_IRQ | PA0 | P2-7 | EXTI0，下降沿，无上下拉，抢占优先级 1 | 模拟板 J2-20（ADC_SDO）的中断分支 |
| PH_RESTART | PA1 | P2-10 | GPIO 推挽输出，默认低 | 模拟板 J2-30 |
| USART2_TX | PA2 | P2-9 | USART2_TX，115200-8-N-1 | 可选调试串口 RX |
| USART2_RX | PA3 | P2-12 | USART2_RX，115200-8-N-1 | 可选调试串口 TX |
| THRESHOLD_DAC | PA4 | P2-11 | DAC1_OUT1，带输出缓冲 | 经 1:10 分压接模拟板 J2-34 |
| ADC_SCK | PA5 | P2-14 | SPI1_SCK，Mode 3，10.625 MHz | 模拟板 J2-18 |
| ADC_SDO | PA6 | P2-13 | SPI1_MISO，16 bit | 模拟板 J2-20 |
| ADC dummy MOSI | PA7 | P2-16 | SPI1_MOSI | 不接模拟板；仅用于全双工主机产生时钟 |
| LCD_CS | PB12 | P2-34 | GPIO 推挽输出，默认高 | LCD pin 7 |
| LCD_SCK | PB13 | P2-35 | SPI2_SCK，Mode 0，21.25 MHz | LCD pin 3 |
| LCD_MOSI | PB15 | P2-37 | SPI2_MOSI，8 bit | LCD pin 4 |
| LCD_DC | PD8 | P2-38 | GPIO 推挽输出，默认低 | LCD pin 6 |
| LCD_RST | PD9 | P2-39 | GPIO 推挽输出，默认低 | LCD pin 5 |
| LCD_BLK | PD12 | P2-42 | GPIO 推挽输出，默认高 | LCD pin 8 |
| QSPI_CLK | PE10 | P2-26 | QUADSPI CLK，42.5 MHz | 板载 W25Q32 pin 6 |
| QSPI_NCS | PE11 | P2-27 | QUADSPI BK1 NCS | 板载 W25Q32 pin 1 |
| QSPI_IO0 | PE12 | P2-28 | QUADSPI BK1 IO0 | 板载 W25Q32 pin 5 |
| QSPI_IO1 | PE13 | P2-29 | QUADSPI BK1 IO1 | 板载 W25Q32 pin 2 |
| QSPI_IO2 | PE14 | P2-30 | QUADSPI BK1 IO2 | 板载 W25Q32 pin 3 |
| QSPI_IO3 | PE15 | P2-31 | QUADSPI BK1 IO3 | 板载 W25Q32 pin 7 |

## 4. 开发板 P2 完整编号

面对开发板原理图中 P2 的编号方向，奇数在左、偶数在右。
数字板封装必须再按实物 pin 1 标识确认镜像关系。

| P2奇数 | MCU/电源 | P2偶数 | MCU/电源 |
|---:|---|---:|---|
| 1 | GND | 2 | 3V3 |
| 3 | PC1 | 4 | PC0 |
| 5 | PC3 | 6 | PC2 |
| 7 | PA0 / ADC_BUSY_IRQ | 8 | PF2 |
| 9 | PA2 / USART2_TX | 10 | PA1 / PH_RESTART |
| 11 | PA4 / THRESHOLD_DAC | 12 | PA3 / USART2_RX |
| 13 | PA6 / ADC_SDO | 14 | PA5 / ADC_SCK |
| 15 | PC4 | 16 | PA7 / ADC dummy MOSI |
| 17 | PB0 | 18 | PC5 |
| 19 | PB2 | 20 | PB1 |
| 21 | VDDA | 22 | VREF |
| 23 | PE7 | 24 | PE8 |
| 25 | PE9 | 26 | PE10 / QSPI_CLK |
| 27 | PE11 / QSPI_NCS | 28 | PE12 / QSPI_IO0 |
| 29 | PE13 / QSPI_IO1 | 30 | PE14 / QSPI_IO2 |
| 31 | PE15 / QSPI_IO3 | 32 | PB10 |
| 33 | PB11 | 34 | PB12 / LCD_CS |
| 35 | PB13 / LCD_SCK | 36 | PB14 |
| 37 | PB15 / LCD_MOSI | 38 | PD8 / LCD_DC |
| 39 | PD9 / LCD_RST | 40 | PD10 |
| 41 | PD11 | 42 | PD12 / LCD_BLK |
| 43 | PD13 | 44 | PD14 |

### P2 上禁止数字板加载的 QSPI 引脚

由于开发板板载 W25Q32 已经连接，数字板上的以下排母脚只能机械连接，不能再接器件、
LED、大电容或长测试线：

```text
P2-26 PE10 QSPI_CLK
P2-27 PE11 QSPI_NCS
P2-28 PE12 QSPI_IO0
P2-29 PE13 QSPI_IO1
P2-30 PE14 QSPI_IO2
P2-31 PE15 QSPI_IO3
```

## 5. 开发板 P3 完整编号

| P3奇数 | MCU/电源 | P3偶数 | MCU/电源 |
|---:|---|---:|---|
| 1 | 3V3 | 2 | GND |
| 3 | PC13 | 4 | BAT |
| 5 | PF9 | 6 | PF10 |
| 7 | PE6 | 8 | PE5 |
| 9 | PE4 | 10 | PE3 |
| 11 | PE2 | 12 | PE1 |
| 13 | PE0 | 14 | PB9 |
| 15 | PB8 / BOOT0相关 | 16 | PB7 |
| 17 | PB6 | 18 | PB5 |
| 19 | PB4 | 20 | PB3 |
| 21 | PD7 | 22 | PD6 |
| 23 | PD4 | 24 | PD5 |
| 25 | PD2 | 26 | PD3 |
| 27 | PD0 | 28 | PD1 |
| 29 | PC11 | 30 | PC12 |
| 31 | PA15 | 32 | PC10 |
| 33 | PA11 / USB_DM | 34 | PA12 / USB_DP |
| 35 | PC9 | 36 | PA8 |
| 37 | PC7 | 38 | PC8 |
| 39 | PD15 | 40 | PC6 |
| 41 | 5V | 42 | GND |
| 43 | 5V | 44 | GND |

当前数字板第一版没有必须使用的 P3 信号。P3 仍应放置对应排母以固定开发板并保留扩展。

## 6. 模拟板 J2（2×20）编号与电源

模拟板顶层连接器：

```text
Designator: J2
Footprint/Comment: PM2.54-2X20P-H85
```

在当前顶层原理图符号中：

- pin 1 位于左下，pin 2 位于右下；
- 左列从下向上依次为 1、3、5……39；
- 右列从下向上依次为 2、4、6……40。

制作数字板与模拟板对插封装时，必须依据 PCB 顶层/底层视图和 pin 1 标记再次检查镜像。

| 模拟板 J2 pin | 网络 | 数字板处理 |
|---:|---|---|
| 1 | AGND | 接模拟地/系统地连接点 |
| 3、5、7、9 | +5VA | 提供滤波后的模拟 +5 V |
| 11 | AGND | 接模拟地 |
| 13、15、17、19 | +5V | 提供数字/辅助 +5 V |
| 18 | ADC_SCK | 接开发板 P2-14 / PA5 |
| 20 | ADC_SDO | 接开发板 P2-13 / PA6，并分支到 P2-7 / PA0 |
| 21 | AGND | 接模拟地 |
| 23、25、27、29 | -5VA | 提供模拟 -5 V；当前模拟板没有自行生成 |
| 30 | Restart | 接开发板 P2-10 / PA1 |
| 31 | AGND | 接模拟地 |
| 33、35、37 | +3V3 | 提供模拟板 3.3 V |
| 34 | Threshold | 接阈值分压和滤波输出 |
| 39 | AGND | 接模拟地 |
| 40 | AGND | 接模拟地 |

当前顶层原理图中其余 J2 pin 未连接。不要因为是 NC 就自动接地，除非模拟板原理图随后明确修改。

## 7. 模拟板四根控制/数据线的具体接法

### 7.1 ADC_SCK

```text
STM32 PA5 / P2-14
        |
        +---------------------- 模拟板 J2-18 / ADC_SCK
```

- SPI1 Mode 3，CPOL=1、CPHA=1。
- 初始 SCK 为 10.625 MHz。
- 模拟板 ADC 页已有 22 Ω 串联电阻；数字板可预留 0 Ω/22 Ω 二选一焊盘，
  第一版先焊 0 Ω，避免重复串阻过大。
- 放置测试点 `TP_ADC_SCK`。

### 7.2 ADC_SDO 与 BUSY 中断分支

```text
模拟板 J2-20 / ADC_SDO
        |
        +---------------------- P2-13 / PA6 / SPI1_MISO
        |
        +-- 47~100 Ω ---------- P2-7 / PA0 / EXTI0
```

- 模拟板 ADC 页已有 10 kΩ 上拉和 22 Ω 串联电阻。
- PA0 和 PA6 都配置为无内部上下拉。
- PA0 仅检测转换完成下降沿；16 bit 数据由 PA6 读取。
- 放置测试点 `TP_ADC_SDO`。

### 7.3 Restart

```text
P2-10 / PA1 / PH_RESTART ------ 模拟板 J2-30 / Restart
```

- 默认低电平。
- MCU 完成一次 ADC 读取后产生约 1 µs 高脉冲，再回到低电平。
- 可预留 22~33 Ω 串联电阻和测试点 `TP_RESTART`。

### 7.4 Threshold

建议数字板使用以下精密分压：

```text
P2-11 / PA4 / DAC1_OUT1
        |
       9.1 kΩ，0.1%
        |
        +---------------------- 模拟板 J2-34 / Threshold
        |
       1.0 kΩ，0.1%
        |
       AGND
```

Threshold 节点并联：

```text
100 nF -> AGND
```

以 3.3 V DAC 参考计算：

| 模拟板阈值 | DAC端电压 | 12 bit DAC码（近似） |
|---:|---:|---:|
| 50 mV | 0.50 V | 620 |
| 75 mV | 0.75 V | 931 |
| 100 mV | 1.00 V | 1241 |

启动阶段建议先写入 DAC 码 1241，对应约 100 mV 阈值。

## 8. LCD 1×8 接口

1.47 英寸 ST7789 模组接口顺序：

| LCD pin | 模组信号 | 连接到开发板 | 配置/外围器件 |
|---:|---|---|---|
| 1 | GND | GND | 与 MCU 共地 |
| 2 | VCC | 3.3V | 100 nF + 4.7~10 µF，就近去耦；禁止接 5 V |
| 3 | SCL/SCK | P2-35 / PB13 | SPI2_SCK；可串 22~33 Ω |
| 4 | SDA/MOSI | P2-37 / PB15 | SPI2_MOSI；可串 22~33 Ω |
| 5 | RES/RST | P2-39 / PD9 | GPIO，默认低；建议 10 kΩ 上拉 |
| 6 | DC | P2-38 / PD8 | GPIO，默认低 |
| 7 | CS | P2-34 / PB12 | GPIO，默认高；建议 10 kΩ 上拉 |
| 8 | BLK | P2-42 / PD12 | GPIO，默认高；后续可改 TIM4_CH1 PWM |

LCD 使用 SPI Mode 0，初始频率 21.25 MHz。LCD 模组只需要写入，不连接 MISO。

## 9. 板载 W25Q32

开发板板载 W25Q32 已连接，无需数字板再次连接：

| W25Q32 pin | 器件信号 | STM32 |
|---:|---|---|
| 1 | CS# | PE11 / QUADSPI_NCS |
| 2 | DO/IO1 | PE13 / QUADSPI_IO1 |
| 3 | WP#/IO2 | PE14 / QUADSPI_IO2 |
| 4 | GND | GND |
| 5 | DI/IO0 | PE12 / QUADSPI_IO0 |
| 6 | CLK | PE10 / QUADSPI_CLK |
| 7 | HOLD#/IO3 | PE15 / QUADSPI_IO3 |
| 8 | VCC | 3.3V |

固件初始化参数：

```text
ClockPrescaler = 3        -> 42.5 MHz
FIFO Threshold = 4
Sample Shifting = Half Cycle
Chip Select High Time = 3 cycles
FlashSize = 21            -> 4 MiB / W25Q32
Clock Mode = 0
```

注意：当前工程完成了 QUADSPI 外设初始化，但尚未实现 W25Q32 的 JEDEC ID、读、写、擦除驱动。

## 10. DMA 与 NVIC

CubeMX/生成代码中的 DMA 映射：

| DMA通道 | 请求 | 数据宽度 | DMA优先级 | NVIC抢占优先级 |
|---|---|---|---|---:|
| DMA1 Channel 1 | SPI1_RX | Half Word | Very High | 2 |
| DMA1 Channel 2 | SPI1_TX | Half Word | High | 2 |
| DMA1 Channel 3 | SPI2_TX | Byte | Low | 6 |

其他中断：

| 中断 | 抢占优先级 | 用途 |
|---|---:|---|
| EXTI0 | 1 | AD7980 转换完成 |
| SPI1 global | 2 | ADC SPI 状态/错误 |
| SPI2 global | 6 | LCD SPI 状态/错误 |
| SysTick | 15 | HAL 时基 |

USART2 当前使用阻塞发送，没有启用 USART2 全局中断。

## 11. 电源连接原则

建议先画出以下电源树，再开始布线：

```text
5.5~6 V 外部输入
  |
  +-- 5 V 转换电源
  |     |
  |     +-- PMOS 防倒灌级 ------> 开发板 P3-41 / 5V
  |     +-----------------------> 模拟板 J2 / +5V
  |     +-- 3.3 V 稳压 ---------> 模拟板 J2 / +3V3、LCD VCC
  |     +-- 反相电源及滤波 ------> 模拟板 J2 / -5VA
  |
  +-- 低压差低噪声稳压/滤波 ----> 模拟板 J2 / +5VA
```

注意事项：

- `AGND` 必须最终与 STM32 GND 共参考，不能悬空。
- 建议在数字板靠近模拟板连接器处设置低阻抗单点/地平面连接，不要用细长线连接地。
- DCDC 电感、开关节点远离 Threshold、ADC_SDO、ADC_SCK 和模拟板连接器。
- `+5VA`、`-5VA` 的电流额定值必须根据模拟板运放实际总电流核算。
- 数字板产生的 3.3 V 只连接模拟板和 LCD；P2-2、P3-1 等开发板 3.3 V
  排针脚在数字板上不接入该电源网络。
- PMOS 的方向必须按“允许数字板给开发板供电、阻止开发板向数字板反灌”检查，
  不能只按普通反接保护电路照搬。

## 12. 数字板原理图必须包含的器件/接口

- 两组与开发板实物对应的 2×22 排母 P2、P3。
- 与模拟板 J2 对插的 2×20、2.54 mm 排针。
- LCD 1×8、2.54 mm 接口。
- 5.5~6 V 输入接口、保险丝/自恢复保险、反接保护、TVS。
- 5.5~6 V 转 5 V 电源，以及开发板 5 V 入口的 PMOS 防倒灌级。
- 5 V 转 3.3 V 电源，只供模拟板和 LCD。
- 5.5~6 V 转 +5VA 的低噪声支路。
- 5 V 转 -5VA 的反相电源及输出滤波。
- Threshold 的 9.1 kΩ/1.0 kΩ 精密分压和 100 nF 滤波。
- ADC_SDO 到 PA0 支路的 47~100 Ω 电阻。
- LCD SCK/MOSI、ADC SCK、Restart 的串联电阻预留焊盘。
- LCD VCC 的 100 nF 和 4.7~10 µF 去耦。
- USB 接口可达性检查；如数字板自带 USB 接口，则增加 ESD 防护并与开发板接口二选一。
- 可选扩展焊盘，默认不装且不得占用当前 ADC、LCD、QSPI、USB 信号。
- 测试点：5V、+5VA、-5VA、3V3、AGND、ADC_SCK、ADC_SDO、Restart、Threshold。

## 13. PCB封装和对插检查

在导入 PCB 之前必须用卡尺核对：

- P2/P3 均为 2×22；
- 排针间距是否为 2.54 mm；
- P2 两排之间距离；
- P3 两排之间距离；
- P2 与 P3 的中心距离；
- 开发板正反插方向；
- 模拟板 J2 pin 1 位置；
- 开发板 USB、电源接口、按键和晶振的禁布/避让区域；
- 开发板倒插后器件高度是否与数字板器件冲突。

建议在数字板丝印上明确标出：

```text
P2-1
P3-1
J2-1
LCD-1
```

首次上电前先不插模拟板，只测试数字板各电源；确认 `+5V、+5VA、-5VA、3V3`
极性和幅值正确后，再断电插入模拟板。

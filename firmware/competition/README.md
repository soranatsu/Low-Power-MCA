# 比赛完整固件（STM32G474VET6 + AD7980）

本工程是比赛整机版，默认使用外置 AD7980。它包含 AD7980 中断/DMA采集、峰保持复位、阈值 DAC、1024 道直方图、W25Q32 日志、ST7789 显示以及 USB CDC/USART2 命令行。代码已经通过 ARM GCC 完整编译；模拟板和数字板尚未完成整机联调。

## 外部 ADC 主链路

```text
模拟板触发/转换逻辑 -> AD7980 CNV
AD7980 SDO/BUSY下降沿 -> PA0/EXTI0
PA5/SPI1_SCK + PA6/SPI1_MISO -> DMA读取16 bit
DMA完成 -> PA1输出约1 us高脉冲，复位峰保持
样本队列 -> 直方图 / Flash / LCD / USB
```

AD7980 使用三线 CS/BUSY 模式的硬件前提：`SDI` 固定为高电平，`CNV` 由模拟板触发逻辑产生，`SDO` 必须有合适的上拉，以便转换结束时形成送入 PA0 的下降沿。当前 STM32 固件不产生 CNV；若模拟板没有 CNV 发生电路，AD7980 不会自行开始转换。

## 关键引脚

| 功能 | MCU | 开发板排针 | 配置 |
|---|---|---:|---|
| ADC_BUSY_IRQ | PA0 | P2-7 | EXTI0下降沿，优先级1 |
| PH_RESTART | PA1 | P2-10 | 推挽输出，默认低，约1 us高脉冲 |
| USART2_TX/RX | PA2/PA3 | P2-9/P2-12 | 115200-8-N-1 |
| THRESHOLD_DAC | PA4 | P2-11 | DAC1_OUT1 |
| ADC_SCK | PA5 | P2-14 | SPI1 Mode 3，16 bit，10.625 MHz |
| ADC_SDO | PA6 | P2-13 | SPI1 MISO |
| ADC dummy MOSI | PA7 | P2-16 | 仅台架回环使用，模拟板不接 |
| INTERNAL_ADC | PC3 | P2-5 | 备用 ADC1_IN9 |
| USB D-/D+ | PA11/PA12 | P3-33/P3-34 | USB FS CDC |
| LCD_CS | PB12 | P2-33 | GPIO |
| LCD_SCK/MOSI | PB13/PB15 | P2-35/P2-37 | SPI2 |
| LCD_DC/RST/BLK | PD8/PD9/PD12 | P2-36/P2-38/P2-44 | GPIO |

数字板完整接线见仓库的 `pcb/Digital_Board/docs/pinout-and-wiring.md`。

## 阈值换算

PA4 的 0～3.3 V DAC 经数字板 `9.1 kΩ + 1.0 kΩ` 分压后送入模拟板：

```text
Vthreshold = VDAC * 1.0 / 10.1
DAC_code = round(Vthreshold_mV * 10.1 / 3300 * 4095)
```

命令 `threshold 100` 会自动换算为 DAC 码；量产前仍应以实测 VDDA、电阻误差和比较器偏置做标定。

## 编译与烧录

```powershell
cd C:\Users\HP\Desktop\Low-Power-Energy-Spectrometer\firmware\competition
make -j4
```

输出为 `build/nucom_competition.elf/.hex/.bin`。可在 STM32CubeProgrammer 中选择 `.hex`，通过 ST-LINK/SWD 烧录；也可在 STM32CubeIDE 中以 Existing Projects 导入本目录。

## 常用命令

```text
status
source ad7980
source internal
start / stop
threshold <0..327 mV>
dac <0..4095>
restart
restart train <1..1000 Hz|off>
hist clear / hist dump
flash id
flash test
flash prepare <sectors>
flash dump <start_record> <count>
log on / log off
lcd on / lcd off / lcd refresh
```

`flash test` 会擦写最后一个 4 KiB 测试扇区；`flash prepare` 会从地址0开始擦除日志区，两者都只会在收到明确命令后执行。

## 上板前仍需验证

- CNV、BUSY、SCK、SDO 的相对时序及 SDO 上拉值；
- 最大事件率下是否丢中断、DMA忙或样本队列溢出；
- PA1 的峰保持复位极性和脉宽；
- 阈值的实测电压误差；
- Flash 写入和 LCD 刷新对模拟噪声及死时间的影响。

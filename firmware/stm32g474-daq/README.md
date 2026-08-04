# NUCOM STM32G474 DAQ 固件

这是 STM32G474VET6 开发板、AD7980 模拟板和数字板配套的固件。当前 `1.1.0` 版加入PC3内部ADC台架测试模式并已通过ARM GCC完整编译；硬件尚未联调。

- 内部ADC和控制脚测试：[docs/INTERNAL_ADC_TEST.md](docs/INTERNAL_ADC_TEST.md)
- AD7980 SPI回环测试：[docs/BENCH_TEST.md](docs/BENCH_TEST.md)
- 文件改动说明：[docs/FILE_CHANGES.md](docs/FILE_CHANGES.md)

## 已实现功能

- AD7980 三线 CS/BUSY 模式采集：
  - `PA0` 检测 `ADC_SDO/BUSY` 下降沿；
  - `SPI1 + DMA` 读取一个 16 bit 样本；
  - DMA 完成后 `PA1` 输出约 `1 µs` 的峰保持复位脉冲；
  - 中断与主循环间使用 1024 条无锁样本队列。
- PC3/P2-5内部ADC测试模式：
  - ADC1_IN9，12 bit；
  - 软件定时100～20000 sample/s；
  - 保存最近2048个原始点，可通过USB导出；
  - 复用直方图、Flash日志和LCD后续流程。
- 1024 道在线能谱直方图，分道方式为 `ADC_CODE >> 6`。
- `PA4 / DAC1_OUT1` 阈值电压控制，兼容数字板上的 `9.1 kΩ + 1.0 kΩ + 100 nF` 分压滤波。
- 板载 W25Q32：
  - JEDEC ID 检测；
  - 最后一个 4 KiB 扇区的独立自检；
  - 显式擦除后，以 8 byte/样本的格式连续记录。
- ST7789（172×320）初始化、背光和直方图刷新，默认关闭，避免未接屏时干扰采集。
- USB CDC 虚拟串口命令行；同时保留 USART2（115200-8-N-1）命令口。
- 开发板 RGB LED 状态：
  - 绿：采集运行；
  - 蓝：采集停止；
  - 红：Flash 写入错误。

## 关键引脚

| 功能 | MCU | 开发板排针 | 配置 |
|---|---|---:|---|
| ADC_BUSY_IRQ | PA0 | P2-7 | EXTI0，下降沿，优先级 1 |
| INTERNAL_ADC | PC3 | P2-5 | ADC1_IN9，12 bit台架输入 |
| PH_RESTART | PA1 | P2-10 | 推挽输出，默认低，约 1 µs 高脉冲 |
| USART2_TX | PA2 | P2-9 | 115200-8-N-1 |
| USART2_RX | PA3 | P2-12 | 115200-8-N-1 |
| THRESHOLD_DAC | PA4 | P2-11 | DAC1_OUT1 |
| ADC_SCK | PA5 | P2-14 | SPI1 Mode 3，16 bit，10.625 MHz |
| ADC_SDO | PA6 | P2-13 | SPI1 MISO |
| ADC dummy MOSI | PA7 | P2-16 | SPI1 MOSI；实物模拟板不接 |
| USB D− / D+ | PA11 / PA12 | P3-33 / P3-34 | USB FS CDC |
| LCD_CS | PB12 | P2-33 | GPIO |
| LCD_SCK / MOSI | PB13 / PB15 | P2-35 / P2-37 | SPI2 Mode 0，21.25 MHz |
| LCD_DC / RST / BLK | PD8 / PD9 / PD12 | P2-36 / P2-38 / P2-44 | GPIO |

完整数字板和模拟板接线见 `pcb/Digital_Board/docs/pinout-and-wiring.md`。

## 编译与烧录

已提供独立 `Makefile`，不依赖 STM32CubeIDE：

```powershell
cd C:\Users\HP\Desktop\nucom\firmware\stm32g474-daq
make -j4
```

产物：

- `build/nucom_g474_daq.elf`
- `build/nucom_g474_daq.hex`
- `build/nucom_g474_daq.bin`

可用 STM32CubeProgrammer 通过 ST-LINK/SWD 直接烧录 `.hex`。STM32CubeIDE 也可以导入现有工程；若打开 `.ioc` 重新生成代码，请先备份本项目，尤其不要覆盖自定义的 `Core/Src/app.c` 和 `USB_Device/App/usbd_cdc_if.c`。

## 命令

上电后采集默认启动。打开名为 `NUCOM G474 DAQ` 的虚拟串口，输入 `help`：

```text
status
source internal
source ad7980
start
stop
loopback on
loopback off
dac 0..4095
threshold 0..327
adc rate 100..20000
adc stats
adc clear
adc dump <count>
restart
restart train <Hz|off>
hist clear
hist dump
flash id
flash test
flash prepare <sectors>
flash dump <start_record> <count>
log on
log off
lcd on
lcd off
lcd refresh
counters clear
```

`flash test` 只擦写 W25Q32 最后一个扇区 `0x3FF000`，但仍是破坏性操作。`flash prepare` 从地址 0 开始擦除指定扇区，必须在确认无需保留旧数据后手动执行；固件不会在上电时自动擦除 Flash。

`flash dump` 通过 USB CDC/USART2 输出 CSV，每次最多 512 条，可分段读取，例如 `flash dump 0 512`、`flash dump 512 512`。读取期间固件会暂时停止采集，结束后恢复原状态。

Flash 中每条记录为小端序 8 byte：

```c
typedef struct {
    uint32_t timestamp_cycles; // 170 MHz DWT 周期计数，约每 25.26 s 回绕
    uint16_t adc_code;         // AD7980 原始码
    uint16_t flags;            // bit0: 回环值错误；bit1: 样本来自内部ADC
} AppSample;
```

每个 4 KiB 扇区可存 512 条记录。当前版本使用无文件系统的连续原始记录，掉电后数据仍在，但固件不会自动恢复“已写记录数”；重启后应由上位机按已知记录数分段执行 `flash dump`。若比赛阶段需要断电续采，应再加入双备份元数据和CRC。

## 当前仍需板级确认

- 实际 AD7980 采集链路的最高无丢失计数率；
- 模拟板 `Restart` 所需脉宽是否需要由 1 µs微调；
- LCD 模组的方向和 34-pixel X 偏移是否与购买的具体模组一致；
- 阈值分压电阻和 VDDA 实测误差，之后应做阈值标定；
- Flash 长时间写入对模拟噪声和采集死时间的影响。

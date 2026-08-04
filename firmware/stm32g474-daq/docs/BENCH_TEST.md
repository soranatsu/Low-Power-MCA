# 无模拟板时的固件台架调试

现有设备足够先验证固件的数字部分：开发板、函数信号发生器、面包板/杜邦线和示波器。推荐严格按下面五个阶段进行；每阶段通过后再进入下一阶段。

## 0. 安全边界

- 函数发生器、示波器和开发板必须共地。
- 在连接 MCU 前，先用示波器确认信号源输出：
  - 最低电压不得小于 `0 V`；
  - 最高电压不得大于 `3.3 V`；
  - 推荐先用 `0～3.0 V`，即 `3.0 Vpp + 1.5 V offset`。
- 注意函数发生器的“50 Ω负载”和“High-Z负载”显示方式：同一组幅度设置在 High-Z 端可能翻倍。必须以示波器实测值为准。
- 绝对不要把 `±5 V`、`5 V TTL` 或带负压的波形直接接入 PA0/PA6/PA7。
- 本阶段只给裸开发板供电，不连接尚未验证的数字板电源。

## 1. 烧录、USB CDC与空载状态

1. 用 ST-LINK/SWD 烧录 `build/nucom_g474_daq.hex`。
2. 复位后，RGB LED 应为绿色，表示采集已启动。
3. 将开发板原生 USB-C 口连接电脑。
4. Windows 设备管理器应出现名为 `NUCOM G474 DAQ` 的虚拟串口。
5. 用 PuTTY、MobaXterm 或其他串口工具打开该 COM 口；波特率可填 115200（USB CDC 实际不依赖该数值）。
6. 输入：

```text
status
flash id
```

预期：

- `run=1`；
- 无外部触发时 `irq/dma/processed` 保持不变；
- W25Q32 常见 JEDEC ID 为 `EF-40-16`。如果不是该值，先检查开发板上 Flash 型号和 QSPI 引脚，不要立即执行擦写测试。

如果 USB CDC 暂时不工作，可用 USB-TTL 模块连接 `PA2/TX`、`PA3/RX` 和 `GND`，参数为 115200-8-N-1；不要把 USB-TTL 的 5 V 电源脚接到开发板。

## 2. 独立测试阈值DAC和Restart

### 2.1 DAC原始输出

示波器探头接 `PA4 / P2-11`，依次执行：

```text
dac 0
dac 2048
dac 4095
```

预期电压约为 `0 V`、`1.65 V`、`3.3 V`。实际满量程取决于开发板 VDDA。

### 2.2 在面包板复现阈值分压

连接：

```text
PA4 ── 9.1 kΩ ──+── 示波器
                 |
                1.0 kΩ
                 |
                GND

在输出节点并联 100 nF 到 GND。
```

执行：

```text
threshold 100
```

预期：

- PA4 原始电压约 `1.01 V`；
- 分压输出约 `100 mV`；
- 默认 DAC 码 1241 时，分压输出约 `99 mV`。

### 2.3 Restart脉冲

示波器接 `PA1 / P2-10`，将时基调到 `500 ns/div` 或 `1 µs/div`，执行：

```text
restart
```

预期看到约 `1 µs` 的高脉冲，空闲电平为低。若单次命令不易捕获，使用示波器 Single 触发，触发电平约 1.5 V、上升沿。

## 3. 模拟AD7980：验证 EXTI→SPI DMA→Restart 完整链路

### 3.1 接线

开发板断电后连接：

```text
PA7 / P2-16  ───────── PA6 / P2-13
信号源输出   ───────── PA0 / P2-7
信号源 GND  ───────── 开发板 GND
```

PA7 是 SPI1 MOSI，固件每次发送固定字 `0xA55A`；把它回接到 PA6/MISO，就能在没有 AD7980 时验证收到的数据是否正确。

### 3.2 信号源设置

- 方波；
- `0～3.0 V`；
- 50% 占空比；
- 从 `100 Hz` 开始；
- PA0 的每个下降沿模拟一次 AD7980 转换完成。

### 3.3 命令和判据

```text
stop
counters clear
hist clear
loopback on
start
```

运行 10 秒后：

```text
status
```

通过标准：

- `irq ≈ dma ≈ processed ≈ 1000`；
- `loop_err=0`；
- `dma_start_err=0`；
- `spi_err=0`；
- `overrun=0`；
- `busy_drop=0`；
- `last=42330`，即十进制的 `0xA55A`。

执行 `hist dump` 时，应主要看到：

```text
661,约1000
```

因为 `0xA55A >> 6 = 661`。

### 3.4 示波器观察

先以 PA0 下降沿触发：

- `PA5 / P2-14`：随后出现 16 个 SPI 时钟周期；
- SCK 空闲高电平，Mode 3；
- SCK 约 `10.625 MHz`，16 周期约 `1.5 µs`。

再以 PA1 上升沿触发：

- SPI 读取结束后出现 Restart；
- Restart 高电平约 `1 µs`。

如果只有双通道示波器，分两次测量即可：

1. CH1=PA0、CH2=PA5；
2. CH1=PA5、CH2=PA1。

## 4. 逐步提高触发率

依次测试：

```text
100 Hz → 1 kHz → 10 kHz → 25 kHz → 50 kHz
```

每档先执行 `counters clear`，运行至少 10 秒后读取 `status`。不要先假设系统能达到某个最高计数率；以以下四项第一次非零的位置作为当前固件/接线的实测上限：

- `busy_drop`：上一次 SPI DMA 尚未完成，又来了新触发；
- `overrun`：主循环处理不过来；
- `loop_err`：SPI回环数据错误；
- `spi_err` 或 `dma_start_err`：底层传输异常。

最高可靠工作点应留出余量，建议使用“首次出现错误频率”的不高于 50% 作为早期联调限值。最终上限还必须在模拟板接入后重新测试。

## 5. 板载Flash与LCD

### 5.1 Flash

先只读：

```text
flash id
```

确认 ID 正常后才执行：

```text
stop
flash test
```

`flash test` 会擦除最后一个 4 KiB 扇区 `0x3FF000`。之后可做短记录测试：

```text
flash prepare 2
log on
start
```

输入 100 Hz 触发约 10 秒，再执行：

```text
stop
log off
status
```

`records` 应接近 1000。每个扇区能保存 512 条记录；`flash prepare 2` 会擦除地址 `0x000000～0x001FFF`，不要在需要保留旧数据时执行。

通过 USB 导出前 1000 条记录：

```text
flash dump 0 500
flash dump 500 500
```

输出为 `record,timestamp_cycles,adc_code,flags` CSV；单次最多 512 条，导出期间会暂停采集。

### 5.2 LCD

未连接 LCD 时保持默认关闭。接屏并核对 3.3 V 电平及引脚后执行：

```text
lcd on
lcd refresh
```

若图像方向或左右位置不正确，优先调整 `Core/Src/st7789.c` 中的 `LCD_X_OFFSET` 和 MADCTL 参数；这通常是不同 172×320 模组玻璃偏移不同，并非采集逻辑故障。

## 接入真实模拟板后的首轮顺序

1. 不插开发板，先独立测量数字板 `5 V/3.3 V/5VA/-5VA`，确认极性和纹波。
2. 关闭全部电源，再插模拟板和开发板。
3. 先 `stop`，确认 PA4 阈值、PA1 空闲低、PA5 空闲高。
4. 信号源从模拟板输入端注入低频、低幅脉冲，同时观察峰保持输出、BUSY/SDO、SCK和Restart。
5. 先在 100 Hz 下确认每个输入脉冲恰好产生一个样本，再提高频率。
6. 用若干已知输入幅度建立 `ADC code—输入幅度` 关系，之后再接探测器和放射源。

# STM32内部ADC与控制引脚台架测试

这套测试用于模拟板尚未完成时，先验证：

```text
模拟电压 → STM32 ADC → 时序缓存 → 统计/直方图 → USB → Flash/LCD
```

它不能代替未来的 AD7980 SPI 时序测试。固件保留两个数据源：

- `source internal`：PC3/P2-5 上的 ADC1_IN9，12 bit，供当前台架测试；
- `source ad7980`：PA0触发、SPI1 DMA读取16 bit AD7980，供模拟板接入后使用。

内部ADC测试模式采用主循环中的软件定时采样，范围为100～20000 sample/s。它便于检查信号、算法和后续数据流，但不是最终的硬实时采样方案。`adc_miss` 反映USB输出、Flash写入或LCD刷新造成的调度延迟。

## 1. 接线和电气安全

开发板排针：

```text
P2-5  = PC3 / ADC1_IN9
P2-1  = GND
P2-11 = PA4 / THRESHOLD_DAC
P2-10 = PA1 / PH_RESTART
P2-7  = PA0 / ADC_BUSY_IRQ
```

函数发生器接线：

```text
函数发生器 OUT ── 1 kΩ ── PC3 / P2-5
函数发生器 GND ───────── GND / P2-1
示波器探头   ─────────── PC3 / P2-5
示波器地     ─────────── GND / P2-1
```

操作要求：

1. 接杜邦线时先关闭函数发生器输出。
2. 函数发生器负载设置选 `High-Z`；如果只能选50 Ω，以示波器实际读数为准。
3. MCU输入只能为 `0～3.3 V`。不能输入负压、5 V TTL或双极性正弦波。
4. 首次建议设置正弦波 `2.0 Vpp + 1.65 V offset`，理论范围为0.65～2.65 V。
5. 先用示波器确认最小值大于0 V、最大值小于3.3 V，再接PC3。
6. 1 kΩ串联电阻用于误操作限流，不能把5 V或负压变成安全电压。

## 2. 烧录并进入内部ADC模式

烧录 `release/nucom_g474_daq_v1.1.0.hex`，通过原生USB-C打开 `NUCOM G474 DAQ` 虚拟串口：

```text
stop
source internal
adc rate 10000
adc clear
hist clear
counters clear
start
```

执行：

```text
status
```

应看到：

```text
fw=1.1.0
source=internal
run=1
adc_rate=10000
adc_n 持续增加
adc_err=0
```

`last16` 是将12 bit内部ADC值左移4位后的统一16 bit数据，供直方图和Flash沿用；原始值请看 `adc_raw`。

## 3. 直流精度和线性测试

函数发生器使用DC输出，或用低频方波的平台分别测试：

| 输入电压 | 12 bit理论码 |
|---:|---:|
| 0.20 V | 248 |
| 0.50 V | 620 |
| 1.00 V | 1241 |
| 1.65 V | 2048 |
| 2.00 V | 2482 |
| 2.50 V | 3102 |
| 3.00 V | 3723 |

计算式：

```text
ADC_code ≈ Vin / VDDA × 4095
```

每个电压点：

```text
adc clear
start
```

等待约1秒后：

```text
stop
adc stats
```

记录 `min/max/mean`，同时用示波器记录PC3实际电压。判断时以实测VDDA为准，不要直接把3.300 V当作绝对准确值。

建议验收：

- 0.5～3.0 V范围内，平均码与理论值相差不超过约1%作为首轮通过标准；
- 固定直流输入时，`max-min` 应较小；若波动很大，先检查共地、杜邦线、函数发生器噪声和USB供电；
- `adc_err=0`。

## 4. 正弦、方波和三角波采样

### 4.1 正弦波

设置：

```text
频率：1 kHz
幅度：2.0 Vpp
偏置：+1.65 V
采样率：20 ksample/s
```

命令：

```text
source internal
adc rate 20000
adc clear
hist clear
start
```

运行约0.2秒：

```text
stop
adc stats
adc dump 200
```

预期：

- 每周期约20个采样点；
- 最小值约807（0.65 V）；
- 最大值约3288（2.65 V）；
- 平均值约2048；
- 导出的200点应形成约10个周期。

如果 `adc_miss` 增加，先降到10 ksample/s，并关闭LCD和Flash日志。该计数说明软件轮询来不及，不一定表示ADC硬件坏了。

### 4.2 方波

使用0.5～2.5 V、100 Hz方波。执行 `hist dump` 后，应主要出现两个峰，分别接近620和3102对应的分道位置。

### 4.3 三角波

使用0.5～2.5 V、100 Hz三角波。直方图在对应区间内应比正弦波更均匀。这个测试能验证直方图分道、USB导出和后续显示流程。

## 5. Threshold DAC闭环测试

### 5.1 不经过分压，先验证MCU DAC本身

关闭函数发生器并断开PC3原接线：

```text
PA4 / P2-11 ── 1 kΩ ── PC3 / P2-5
```

执行：

```text
source internal
adc rate 1000
dac 512
adc clear
start
```

等待约0.5秒后执行：

```text
stop
adc stats
```

然后依次测试：

```text
dac 1024
dac 2048
dac 3072
dac 3584
```

每改一次DAC，都重复`adc clear`、`start`、等待0.5秒、`stop`、`adc stats`。因为ADC和DAC共用同一模拟电源/参考，ADC原始码应大致等于DAC设置码。避免用0和4095做严格线性判据，因为DAC输出缓冲在电源轨附近通常有余量限制。

### 5.2 加入数字板计划使用的分压

在面包板搭建：

```text
PA4 ── 9.1 kΩ ──+──── PC3
                 |
                1.0 kΩ
                 |
                GND

PC3节点再接100 nF到GND。
```

依次执行：

| 命令 | 分压后目标值 | 内部ADC理论码 |
|---|---:|---:|
| `threshold 20` | 20 mV | 25 |
| `threshold 50` | 50 mV | 62 |
| `threshold 100` | 100 mV | 124 |
| `threshold 200` | 200 mV | 248 |
| `threshold 300` | 300 mV | 372 |

每次改变阈值后等待至少5 ms，再执行：

```text
adc clear
start
```

采集约0.5秒后：

```text
stop
adc stats
```

同时用示波器/万用表测量：

- PA4原始DAC电压；
- 9.1 kΩ与1 kΩ之间的Threshold输出节点；
- PC3 ADC读数。

首轮重点不是追求1 mV精度，而是确认单调性、重复性、没有异常跳变，以及100 mV命令确实落在约100 mV附近。

## 6. PA0触发输入可靠性

这一项只测PA0的EXTI，不读取SPI。先执行：

```text
stop
source internal
counters clear
```

接线：

```text
函数发生器 OUT ── 1 kΩ ── PA0 / P2-7
函数发生器 GND ───────── GND
```

信号设置为0～3.0 V方波，100 Hz。运行10秒后执行：

```text
status
```

`irq` 应接近1000。依次提高到1 kHz、10 kHz和50 kHz，每档重新清零并计时。内部ADC数据源下PA0只计数，不会启动SPI，因此可单独判断触发脚、接线和EXTI配置。

## 7. PA1 Restart输出可靠性

示波器接PA1/P2-10，先测试单脉冲：

```text
restart
```

使用示波器Single、上升沿触发，预期：

- 空闲低；
- 高电平接近3.3 V；
- 高脉宽约1 µs。

再测试脉冲串：

```text
restart train 100
```

预期频率约100 Hz、每个高脉冲约1 µs。可继续测试1 Hz、1 kHz，并让100 Hz运行10分钟观察是否出现毛刺或幅度异常。结束必须执行：

```text
restart train off
```

脉冲串是主循环生成的台架测试信号，频率可能有轻微抖动；最终AD7980模式中的Restart仍由SPI DMA完成回调产生。

## 8. 模拟后续Flash和LCD流程

以1 ksample/s记录约1秒：

```text
source internal
adc rate 1000
flash prepare 2
log on
counters clear
start
```

等待1秒：

```text
stop
log off
status
flash dump 0 100
```

预期：

- `records` 接近1000；
- CSV中的 `adc_code` 等于内部12 bit原始码左移4位；
- `flags` 的bit1为1，表示数据来自内部ADC；
- `adc_miss` 用来评价Flash页编程对软件定时采样的影响。

接好LCD后执行：

```text
lcd on
lcd refresh
```

观察是否能显示当前直方图。LCD刷新会占用主循环，因此打开LCD后出现少量 `adc_miss` 是性能测试结果，不应被误判为模拟输入异常。

## 9. 切回最终AD7980模式

真实模拟板接入前执行：

```text
stop
restart train off
source ad7980
loopback off
counters clear
start
```

此时内部PC3采样停止，PA0下降沿重新触发SPI1 DMA读取AD7980。

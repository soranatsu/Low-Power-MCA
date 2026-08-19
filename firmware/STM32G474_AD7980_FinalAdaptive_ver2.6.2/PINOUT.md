# 引脚表与冲突审计

## 数字板 J2 / 开发板 P2 关键连接

| 功能 | 数字板 J2 | 开发板 P2 | MCU | 配置 | 说明 |
|---|---:|---:|---|---|---|
| ADC_BUSY_IRQ | 7 | 7 | PA0 | EXTI0 falling, input | AD7980 SDO 经 R1=100 ohm 镜像；busy 完成沿 |
| PH_RESTART | 10 | 10 | PA1 | push-pull output, default low | 读完后高脉冲 1 us，控制 ADG721 IN1 |
| THRESHOLD_DAC | 11 | 11 | PA4 | DAC1_OUT1 | 经 9.1 kohm/1 kohm 分压到比较器阈值 |
| ADC_SDO | 13 | 13 | PA6 | input | AD7980 串行数据，MSB first |
| ADC_SCK | 14 | 14 | PA5 | push-pull output, default low | 17 个下降沿：前 16 个回读 D15..D0，第 17 个释放 SDO 高阻 |
| 额外硬接地 | 用户板确认 | 对应 PA2 | PA2 | Analog/high-Z/no-pull, locked | **禁止 USART2_TX / 禁止输出** |
| 额外硬接地 | 用户板确认 | 对应 PA3 | PA3 | Analog/high-Z/no-pull, locked | **禁止 USART2_RX / 禁止输出** |
| 硬接地 | 8 | 8 | PF2 | Analog/high-Z, locked | **禁止输出** |
| 硬接地 | 15 | 15 | PC4 | Analog/high-Z, locked | **禁止输出** |
| 硬接地 | 18 | 18 | PC5 | Analog/high-Z, locked | **禁止输出** |

模拟板 2x20 端对应信号为：P1-27 `ADC_SCK`、P1-30 `Restart`、P1-31 `ADC_SDO`、P1-38 `Threshold`。`ADC_CNV` 只存在于模拟板触发链，未引到 MCU 控制脚。

## FPC/LCD 必须禁用的功能

| FPC 脚 | 开发板网络 | MCU | 与本工程冲突 |
|---:|---|---|---|
| 7 | EXT_ADC_CH1 | PA0 | ADC_BUSY_IRQ |
| 8 | EXT_ADC_CH2 | PA1 | PH_RESTART |
| 12 | EXT_SPI1_MISO | PA6 | ADC_SDO，可能发生双驱动 |
| 13 | EXT_SPI1_SCK | PA5 | ADC_SCK 会在每次读数时翻转 |

因此采集时推荐拔掉整条 LCD/FPC。若必须保留 FPC，LCD 端这四条线必须始终为高阻，且不能安装会把它们上拉/下拉到强驱动电源的器件。

其余未用 FPC 功能也在固件中显式设为 Analog/高阻：PA7（SPI1 MOSI）、PA9/PA10（UART）、PC8/PC9（I2C）、PB13/PB14/PB15（SPI2）。USB 使用 PA11/PA12，不占用 FPC 数据脚。

FPC-1/2（NRST/BOOT）、FPC-5（3V3）及 FPC-6/17/18（GND）是系统/电源脚，不能当普通 GPIO。

## 上电万用表检查

断电并拆下 MCU 开发板后：

1. 在数字板开发板座测 PA2、PA3、J2-8/PF2、J2-15/PC4、J2-18/PC5 对应针脚到 GND，应接近 0 ohm；PA2/PA3 的连接器编号以实板蜂鸣档和丝印为准，避免因母座镜像误判。
2. 测 J2-7、10、11、13、14 到 GND，不应短路。
3. 对照丝印确认 2x22 母座镜像方向；不要只按 PCB 视图的左右位置判断奇偶脚。
4. 装回开发板后仍断电，确认 PA5/PA1 对地不是低阻，再上电。

PA2/PA3 只保留其 ADC/模拟高阻状态，不作为 ADC 核心采集信号；USART2 已完全放弃。电脑通信固定使用 PA11/PA12 的原生 USB CDC。

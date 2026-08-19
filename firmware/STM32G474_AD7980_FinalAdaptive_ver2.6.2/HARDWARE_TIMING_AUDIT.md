# 硬件与固件时序审计

## 最终信号链

1. `Buf_out` 一路进入峰保持电路，另一路进入 LMV7239 比较器。
2. 比较器输出 `TRIG_RAW` 上升沿触发第一片 SN74LVC1G123。按其功能表，`/A=0`、`/CLR=1` 时，B 上升沿产生 Q 高脉冲；图中 RC 设置目标约 480~500 ns。
3. 第一片 Q 的下降沿送入第二片 SN74LVC1G123 的低有效 A 输入；在 `B=1`、`/CLR=1` 条件下，A 下降沿产生约 140 ns 的 Q 高脉冲，即 `ADC_CNV`。
4. AD7980 在 CNV 上升沿采样并开始转换。SDI 硬件固定为 VIO，选择三线 CS busy-indicator 模式。CNV 约 140 ns 后回低，满足 tCNVH(min)=10 ns，且在转换完成前已经为低，因此允许 SDO 生成 busy 完成下降沿。
5. LTC6993-3 是下降沿触发、不可重触发版本。其 DIV 接高对应 `POL=1、NDIV=1`，所以 CNV 下降沿触发一个低电平脉冲。RSET=127 kohm 时名义宽度约 `127k/50k * 1 us = 2.54 us`，与设计的 2~3 us 峰保持前级关断窗口一致。
6. AD7980 转换完成后 SDO 从高阻/外部 47 kohm 上拉的高电平转为低电平。该下降沿经 100 ohm 镜像到 PA0，触发 `ADC_BUSY_IRQ`。
7. PA0 的 busy 下降沿进入 EXTI0 ISR；ISR 内由 PA5 连续产生 17 个 SCK 下降沿。前 16 个下降沿依次推出并采集 D15..D0；第 17 个下降沿不采数据，仅使 SDO 在 tDIS(max)=20 ns 内返回高阻。SCK 最终保持低电平，读数期间由 SDO 数据翻转产生的 PA0 pending 位在 ISR 结束前清除。
8. 数据读取和 SDO 释放后，PA1 输出 1 us 高脉冲。ADG721 的 IN1 高电平使 S1-D1 导通，S1 接地，因此峰保持电容被放电；脉冲结束后开关重新断开，等待下一事件。

## 数字时序裕量

- AD7980 VIO=3.3 V：tSCK(min)=12 ns、tSCKH/tSCKL(min)=4.5 ns、tDSDO(max)=11 ns、tDIS(max)=20 ns。
- MCU=150 MHz，单周期约 6.67 ns。每个 SCK 半周期包含 GPIO 寄存器访问和四个 NOP；采样点位于下降沿后超过 11 ns。第 17 个下降沿后同样等待四个 NOP，再启动 Restart。
- 完整的 17 时钟读取和 1 us Restart 在 EXTI ISR 中完成，避免主循环中的 USB 文本格式化延迟读数；ISR 只把定长样本压入 63 项环形队列，不做 USB 或字符串处理。EXTI0 优先级 5 高于 USB 的 6，USB 不会拉长位时序。100 kHz 测试的事件间隔为 10 us，大于本段约 3 us 的最坏目标执行时间；实际板上仍须用示波器验证。

## 阈值 DAC

PA4 DAC 输出经 9.1 kohm/1 kohm 分压后进入比较器：

`Vthreshold = VDAC * 1k / (9.1k + 1k) = VDAC / 10.1`

固件 `threshold N` 的 N 是比较器端实际目标毫伏数，不是 PA4 管脚电压。固件先乘以 10.1，再换算为 12 位 DAC 码；例如 75 mV 阈值对应约 757.5 mV DAC 输出。硬件 100 nF 与分压网络形成约百微秒量级的稳定时间，修改阈值后建议等待至少 1 ms 再开始统计。

## 不允许启用的 MCU 功能

- PA2、PA3、PF2、PC4、PC5 是数字/电源板额外接地点：全部锁定为 Analog/no-pull，禁止任何 GPIO/复用输出，特别禁止 USART2。
- LCD/FPC 不使用；PA0、PA1、PA5、PA6 与 FPC 功能冲突，采集时应拔下 FPC 或保证外部端永久高阻。
- PA7、PA9、PA10、PC8、PC9、PB13、PB14、PB15 也显式设置为 Analog/no-pull，避免残留 LCD/FPC 功能驱动。
- CNV 仅由模拟触发链生成，MCU 不分配 CNV 引脚。
- 电脑通信只使用 PA11/PA12 的 USB CDC；HAL UART/USART 模块均未启用。

## 器件版本兼容

AD7980ARMZ 与 AD7980BRMZ 的封装引脚、三线 CS busy 时序和 16 位数据格式相同；B 级是更高精度等级，`-RL7` 是卷带包装标识。固件无需区分两者。

## 上板前硬件条件

1. 断电蜂鸣确认 PA2、PA3、PF2、PC4、PC5 对地，确认 PA0、PA1、PA4、PA5、PA6 不短路。
2. 核对 2x22 母座镜像方向；不要仅凭 PCB 画面左右判断针号。
3. 先使用限流电源上电，不接输入信号；确认 3.3 V、2.5 V、+5 VA、-5 VA 正常后再接 USB 和函数发生器。
4. 示波器依次检查 `TRIG_RAW`、约 500 ns `Q_DELAY`、约 140 ns `ADC_CNV`、约 2.5 us 低电平 `Trig_SHDN`、SDO busy 下降沿、17 个 SCK 和最后的 Restart 高脉冲。

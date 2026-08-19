# 测试步骤

## 1. 安全与静态检查

1. 不上电，拔下 LCD/FPC。
2. 按 `PINOUT.md` 测量 PA2、PA3、J2-8/PF2、J2-15/PC4、J2-18/PC5 对应针脚，确认全部是数字/电源板接地脚；连接器编号以实板蜂鸣档确认，防止母座镜像误判。
3. 检查模拟板 `SDI` 硬接 3V3、`CNV` 来自单稳态链、`SDO` 有 47 kohm 上拉。
4. 示波器先检查电源：3V3、+/-5VA、2.5VA_VDD、2.5VA_REF；然后检查 CNV 约 140 ns 脉冲及转换约 500–710 ns。

## 2. 烧录与 CDC

1. ST-LINK 连接 3V3 sense、GND、SWDIO、SWCLK，可选 NRST。
2. 烧录 `Firmware/STM32G474_AD7980_NuclearMCA.hex`，复位。
3. USB-C连接电脑，打开虚拟串口，发送 `status`；确认 `fw=1.5.2 hist_channels=4096`、`overruns=0 queued=0 range_overflows=0 adc_spectrum_fs_mV=2000 frontend_gain_milli=2000`。空闲时 `sdo=high`；`recoveries=0` 是理想状态，`postread_low` 只作读后电平诊断。
4. 上电默认阈值为比较器端 100 mV。在 PA4 应约为 1.010 V，经过 9.1 kohm/1 kohm 分压后比较器端约为 100 mV。允许设置范围为 50–200 mV。

## 3. 基础 500 mV / 1 kHz

1. 信号发生器设题目规定的指数衰减脉冲，峰值 500 mV、重复频率 1 kHz。
2. 发送 `profile baseline`、`hist clear`、`decimate 1`。
3. 记录至少 10,000 个事件，发送 `status` 和 `hist dump`。
4. 核对 PA0 上 busy 下降沿、PA5 共 17 个时钟（前 16 个取数，第 17 个仅释放 SDO 高阻）、PA1 随后的 1 us 高脉冲；PA1 不得早于第 17 个下降沿及其 tDIS 等待时间。
5. 用直方图峰的 FWHM/峰位计算分辨率，目标小于 1%。

## 4. 100–900 mV 幅度扫描

1. 发送 `profile amplitude`。
2. 对 100、150、…、900 mV 每一点：设置发生器，发送对应的 `amp N`，采相同事件数，保存独立 CSV/直方图。
3. 用峰位对输入幅度做线性回归，检查题目要求的分辨率与 R² 指标。CSV 中 `expected_mV` 可直接作为拟合横轴。

## 5. 100 mV / 1–100 kHz 频率扫描

1. 发送 `profile frequency`，发生器幅度固定 100 mV。
2. 每个频点发送 `freq N`，N 单位为 kHz；建议测试 1、2、5、10、20、50、100 kHz。
3. 100 kHz 时设 `decimate 100` 或更大，观察 `samples`、`busy`、`queued`、`overruns`、峰位与 FWHM。ASCII 输出带宽不用于判断采集通过率。
4. 通过 `samples` 增量与 `参考频率 × uptime_ms差值` 比较通过率；`samples/busy` 是 MCU 处理效率，`busy` 本身只表示已触发转换，不等于已经安全进入直方图的样本。

## 6. 异常判据

- `overruns` 增长：处理链跟不上或 USB/调试代码阻塞；关闭 stream、增大 decimation 后复测。
- `tx_drops` 增长而 `overruns=0`：仅 ASCII 输出队列过载，采集/直方图仍在工作。
- `range_overflows` 增长：ADC 端达到或超过 2 V，必须降低外部幅度/增益；末道峰不能用于精度或分辨率结论。
- `queued` 持续接近 63：主循环处理余量不足，即使暂时没有 overrun 也应关闭逐事件流或增大 decimation。
- 原始码固定 0 或 65535：先查 SDO/BUSY 共网、CNV 时序、2.5 V 参考和峰保持输出，不要先改软件极性。
- PA1 没有脉冲：确认确实收到 busy 下降沿并完成读数；检查数字板 J2-10 和模拟板 Restart 连接。
- `sdo=low` 且PA5不再出现时钟：必须确认已经烧录v1.6.0。固件会按1 ms受限间隔执行完整17时钟冲洗并输出Restart；持续低电平冲洗不会重复入谱。
- `recoveries` 偶尔增加且采集继续：表示固件成功兜底了一次 Busy 丢沿；记录现场波形。若该值持续快速增加，则不是正常软件现象，应检查 PA0/PA6 共网、47 kohm 上拉、100 ohm 串联电阻、排针接触、地弹与边沿振铃。
- `postread_low` 增加：表示第17个SCK和1 us Restart后固件立即观察到PA0仍低。v1.6.0不会因此丢掉真实Busy样本；优先比较 `samples` 增量与示波器CNV频率。若计数率仍异常，再同时测量ADC管脚侧SDO、PA0、PA6、PA5，并检查47 kohm上拉、SCK计数与BRM焊接连续性。

# 测试步骤

以下路径相对于本固件目录 `firmware/STM32G474_AD7980_FinalAdaptive_ver2.6.2/`。版本、产物证据和待验证项见 [CONSISTENCY_AUDIT.md](CONSISTENCY_AUDIT.md)。本规程是待执行步骤，不是测试通过记录。

## 1. 安全与静态检查

1. 不上电，拔下 LCD/FPC。
2. 拆下 MCU 开发板，按 `PINOUT.md` 测量 PA2、PA3、J2-8/PF2、J2-15/PC4、J2-18/PC5 对应针脚，确认全部是数字/电源板接地脚；连接器编号以实板蜂鸣档确认，防止母座镜像误判；同时确认 PA0/PA1/PA4/PA5/PA6 无对地短路，装回开发板后仍断电复核。
3. 检查模拟板 `SDI` 硬接 3V3、`CNV` 来自单稳态链、`SDO` 有 47 kohm 上拉。
4. 不接输入信号，先限流上电，检查 3V3、+/-5VA、2.5VA_VDD、2.5VA_REF。断电完成接线并烧录已核对的镜像后，再在低频输入下检查 CNV 约 140 ns 脉冲及转换约 500–710 ns；完整数字读出时序见第 3 节。Q_DELAY 与 SHDN 的设计值和实板记录不同，必须记录本板实测值，见 `HARDWARE_TIMING_AUDIT.md`。

## 2. 烧录与 CDC

1. 断电连接 ST-LINK 的 3V3 sense（目标电压检测）、GND、SWDIO、SWCLK，可选 NRST。确认探针供电方式，避免与板电源重复供电；不要把 3V3 sense 当作 GND。
2. 烧录 `Firmware/STM32G474_AD7980_FinalAdaptive.hex`，执行下载校验并复位；不要选 `Release` 内残留的 `Full16_MCA` 镜像。若自行构建，先 Clean/Build 并记录源码提交、工具链、日志和新镜像 SHA-256，不能默认 `Firmware/` 导出副本已自动更新。
3. USB-C连接电脑，打开虚拟串口，发送 `status`；在复位且无输入信号时确认各字段 `fw=2.0.0-adaptive`、`hist_channels=4096`、`overruns=0 queued=0 range_overflows=0 adc_spectrum_fs_mV=2500 frontend_gain_milli=2000`。空闲时 `sdo=high`；`recoveries=0` 是理想状态，`postread_low` 只作读后电平诊断。
4. 使用 `PC_Monitor/启动上位机.bat`（当前 v2.7.6），以实际 `status` 再次核对版本、量程与界面道数；只允许一个程序占用串口。目录名 ver2.6.2 不是固件报告版本。
5. 上电默认阈值为比较器端 100 mV。在 PA4 应约为 1.010 V，经过 9.1 kohm/1 kohm 分压后比较器端约为 100 mV。允许设置范围为 50–200 mV。修改阈值后至少等待 1 ms，并实测比较器端电压。

## 3. 基础 500 mV / 1 kHz

1. 信号发生器设题目规定的指数衰减脉冲，峰值 500 mV、重复频率 1 kHz。
2. 发送 `channels 4096`、`profile baseline`、`hist clear`、`decimate 1`；确认 `stream=off format=b16 hist_channels=4096`。切换道数会清统计；高道数测试应改发 `channels 8192|16384|65536` 中的一个具体值，保持 `format b16`、`stream on`、`decimate 1` 并由上位机接收。
3. 记录至少 10,000 个事件。4096 道发送 `status` 和 `hist dump`；若需准确固定测量窗，先停止外部脉冲，等待 `queued=0` 后再读状态和导谱。高道数不支持 MCU `hist dump`，必须导出上位机累计谱并核查序号缺口、CRC 错误及接收计数。
4. 核对 PA0 上 busy 下降沿、PA5 共 17 个时钟（前 16 个取数，第 17 个仅释放 SDO 高阻）、PA1 随后的 1 us 高脉冲；PA1 不得早于第 17 个下降沿及其 tDIS 等待时间。
5. 用直方图峰的 FWHM/峰位计算分辨率，目标小于 1%，记录 ROI、样本量和道宽；4096 道分箱可能限制窄峰评估，需以高道数复核。ADC 原码换算为 `raw × 2500 / 65536 mV`，按实测前端增益再换算板端幅值，不能把 2.5 V 满量程当作外部输入范围。
6. v2.7.6 默认启用历史系统线性修正（斜率 1.004148380、截距 −1.160606 mV）。测量未经校准的线性度时先关闭该修正；另行标定应保存独立标定数据、增益、阻抗与修正参数，避免用同组数据校准后再报告其验证精度。发生器的 50 Ω/高阻幅度设定必须与实际负载一致，并在板端实测。

## 4. 100–900 mV 幅度扫描

1. 发送 `profile amplitude`。
2. 对 100、150、…、900 mV 每一点：设置发生器，发送对应的 `amp N`，待信号与阈值稳定后清谱并采相同事件数，保存独立 CSV/直方图。高道数同步清除 PC 谱；仅发送固件清零命令不能假定 PC 端累计谱也已清除。
3. 用峰位对输入幅度做线性回归，检查题目要求的分辨率与 R² 指标。`amp`、`freq`、`profile` 仅设置元数据并清统计，不控制发生器；`expected_mV` 是设定标记，拟合横轴应使用板端实测输入及其不确定度。

## 5. 100 mV / 1–100 kHz 频率扫描

1. 发送 `profile frequency`，发生器幅度固定 100 mV。
2. 每个频点发送 `freq N`，N 单位为 kHz；建议测试 1、2、5、10、20、50、100 kHz。
3. 先以 4096 道、`stream off` 测 MCU 本地谱与吞吐，再独立测试高道数 B16 全链路。高道数依次发送 `format b16`、`decimate 1`、`stream on`；B16 模式拒绝 `decimate 100`。关闭高道数 stream 会停止 PC 入谱，不能用此结果证明完整传输。记录 `samples`、`busy`、`queued`、`overruns`、`stream_lost_samples`、`tx_drops`、`usb_recoveries` 和 PC 序号缺口/CRC 错误；ASCII 抽样仅用于低速诊断。
4. 各频点完成设置、稳定与清零后建立新的同步状态基线，使用计数增量；不得跨 `freq`/`amp`/`profile`/`channels`/清谱/复位混算。通过 `samples` 增量与 `实测参考频率 × uptime_ms差值 / 1000` 比较通过率；同窗 `Δsamples/Δbusy` 是 MCU 处理效率；源码 `busy` 是软件保留的 Busy/补采事务计数，并非独立硬件触发计数，不等于已经安全进入直方图的样本。

## 6. 异常判据

- `overruns` 增长：ISR 样本队列已丢事件。降低事件率或切换 4096 道定位；高道数正式采集不可通过抽样或关闭 stream 掩盖丢失。
- `tx_drops`、`stream_lost_samples`、PC 序号缺口或 CRC 错误增长：检查 USB/主机接收；即使 `overruns=0`，高道数 PC 谱仍可能缺样。4096 道需区分本地采集完整性与导谱传输完整性。
- `range_overflows` 增长：源码仅在 `raw == 65535` 时计数，提示 ADC 满码（标称 2.5 V 附近），不是 2 V 比较器，也不能测出真实超量程电压。降低外部幅度/增益并实测 ADC 输入；末道堆积不能用于精度或分辨率结论，计数为零也不能排除模拟前端饱和。
- `queued` 持续接近 1023：当前 1024 槽环形队列有效容量为 1023，主循环余量不足，应降低事件率或切换 4096 道；不能沿用旧 63 项队列判据。
- 原始码固定 0 或 65535：先查 SDO/BUSY 共网、CNV 时序、2.5 V 参考和峰保持输出，不要先改软件极性。
- PA1 没有脉冲：确认确实收到 busy 下降沿并完成读数；检查数字板 J2-10 和模拟板 Restart 连接。
- `sdo=low` 且PA5不再出现时钟：先按第 2 节确认当前 `fw=2.0.0-adaptive` 及镜像身份。固件会按1 ms受限间隔执行完整17时钟冲洗并输出Restart；持续低电平冲洗不会重复入谱。
- `recoveries` 偶尔增加且采集继续：表示进入了低电平恢复路径，可能是补采或不计数的持续低电平冲洗，不能仅凭该计数认定恢复成功；记录现场波形。若该值持续快速增加，则不是正常软件现象，应检查 PA0/PA6 共网、47 kohm 上拉、100 ohm 串联电阻、排针接触、地弹与边沿振铃。
- `postread_low` 增加：表示第17个SCK和1 us Restart后固件立即观察到PA0仍低。当前 `ad7980.c` 不会仅因该诊断丢掉真实 Busy 样本；优先比较 `samples` 增量与示波器CNV频率。若计数率仍异常，再同时测量ADC管脚侧SDO、PA0、PA6、PA5，并检查47 kohm上拉、SCK计数与BRM焊接连续性。

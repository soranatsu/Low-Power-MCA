# 固件安全、时序与数据处理审计

审计日期：2026-08-06。结论：源码、CubeMX 配置、Debug/Release 编译、Release 反汇编和上位机自检均通过；工程已达到上板前的软件交付状态。尚未完成且不能由软件替代的是实板蜂鸣档、示波器和标准脉冲源验证。

## 1. 不烧板子的强制条件

- PA2、PA3、PC4、PC5、PF2 在 `.ioc` 中均为锁定的 `GPIO_Analog`，生成代码为 Analog/no-pull。USART 未启用。
- PA7、PA9、PA10、PC8、PC9、PB13、PB14、PB15 对应未使用 LCD/FPC 功能，保持 Analog/no-pull；采集时 LCD/FPC 必须拔下。
- PA0 仅作 `ADC_BUSY_IRQ` 输入，PA6 仅作 `ADC_SDO` 输入；PA5 是唯一 ADC 时钟输出，PA1 是唯一峰保持复位输出，PA4 是 DAC 模拟输出。
- MCU 没有 CNV 引脚或 CNV 命令。CNV 只由比较器和单稳态模拟链产生。
- USB CDC 固定使用 PA11/PA12；上位机没有任意 GPIO、UART、烧录、电源或电子切换 JP1 的接口。
- 可重复执行 `tools/verify_project_safety.ps1`；当前所有静态检查通过。

## 2. AD7980 Busy 读取顺序

1. 外部 CNV 上升沿启动转换；CNV 在转换结束前已由硬件回低。
2. SDO 从高阻/上拉高变为低，PA0 产生 EXTI0 falling 中断。
3. EXTI0 ISR 内连续执行 16 个数据时钟；每个 SCK 下降沿后等待四个 150 MHz NOP，再从 PA6 采 D15..D0。
4. 产生不采样的第 17 个 SCK 下降沿，等待超过 `tDIS(max)=20 ns`，使 SDO 返回高阻。
5. PA1 输出 1 us 高脉冲，使 ADG721 IN1 导通并释放峰保持电容，然后回到低电平。
6. 定长样本写入 63 项 ISR→主循环环形队列；USB 格式化不在中断里执行。

Release 反汇编保留了 16 次循环、显式第 17 次下降沿、每边沿后的四个 NOP，并确认 PA1 置高发生在第 17 个下降沿之后。EXTI0 优先级 5、USB 优先级 6，因此 USB 不能打断位时序。目标执行时间约 3 us，小于 100 kHz 测试的 10 us 周期；最终数值仍需在实板用示波器测量。

## 3. 阈值 DAC

原理图为 9.1 kΩ/1 kΩ 分压，软件命令参数表示比较器端阈值，而非 PA4 电压：

`V_DAC = V_threshold × (9.1k + 1k) / 1k = 10.1 × V_threshold`

例如 50/100/200 mV 对应约 505/1010/2020 mV DAC 输出。命令限制为比较器端 50–200 mV，上电默认 100 mV；实际精度受 VDDA、DAC 缓冲器、1 kΩ/9.1 kΩ误差影响，应以万用表实测比较器输入校准。

## 4. 能谱量程和公式

- AD7980 原始电压：`ADC_mV = raw × 2500 / 65535`。这是 ADC 引脚电压。
- 原理图 BAMP 为同相约 2 倍增益，因此外部 0–1 V 对应 ADC 端约 0–2 V。
- 道址：`channel = floor(raw × 4096 / 52428)`，钳位为0..4095；52428约为2 V原始码。
- ADC端箱中心：`(channel + 0.5) × 2000 / 4096 mV`。
- 输入等效峰位：`(ADC_peak_mV - offset_mV) / calibrated_gain`。
- ADC 端达到或超过 2 V 的事件累计到 `range_overflows`。出现溢出时，上位机使绝对精度无效，防止把末道饱和堆积当作真实峰。

旧版“ADC 端 0–1 V→1024 道”会使外部 500 mV 以上信号提前饱和，不符合 100–900 mV 扫描。本版状态行固定报告 `adc_spectrum_fs_mV=2000 frontend_gain_milli=2000`；上位机缺少或收到不同量程字段时会报警并停止幅度/精度换算。

## 5. 统计定义

- 完整能谱来自MCU的4096道直方图快照；逐事件ASCII流的 `decimate` 只影响USB显示带宽，不减少直方图事件。
- 峰位用 ROI 内本底扣除后的质心；FWHM 用局部本底以上的半峰高及两侧线性插值。峰被 ROI 边界截断时不报告 FWHM。
- 分辨率：`FWHM_channel / centroid_channel × 100%`。
- 质心统计精度估计：`sigma / (sqrt(net_peak_area) × centroid_channel) × 100%`，仅是计数统计项，不包含 ADC INL、参考源、模拟噪声和校准系统误差。
- 处理效率：`已进入应用层的样本数 / Busy 转换数`；`overruns` 和 `queued` 必须同时观察。
- 脉冲通过率：`已接收样本数 / (参考频率 × MCU测量时长)`。分母优先用 `uptime_ms` 差值，避免 USB/Windows 调度抖动。
- 未勾选已知参考幅度/频率时，软件明确显示“未知参考”，不虚构绝对精度或通过率。
- 阻抗换算使用戴维南模型。High-Z 显示视为开路电压；50 Ω显示先按 `Vopen=Vdisplay×(Rsource+50)/50` 恢复开路电压，再与板端 1 MΩ或 `50 Ω∥1 MΩ` 分压。50 Ω同轴线的特性阻抗本身不等于板端已端接。

## 6. 编译与剩余实物验证

- STM32 Debug：0 errors、0 warnings；Release：0 errors、0 warnings。
- Windows 上位机：0 errors；状态/样本/直方图解析、0–2 V 映射、ROI/FWHM、本底、阻抗换算、线性拟合、重复对数/线性切换和离屏渲染自检通过。
- 上板前必须按 `TEST_PROCEDURE.md` 断电确认五个硬接地点及核心线不短路。
- 首次上电必须限流，先不接信号，检查电源、DAC、CNV、Busy、17 个 SCK 和 Restart 顺序。
- 软件不能证明实际 BAMP 增益恰为 2.000、2.5 V 参考准确、单稳态脉宽正确或 PCB 无焊接错误；这些项目必须用实物校准后再声明最终精度和 100 kHz 通过率。

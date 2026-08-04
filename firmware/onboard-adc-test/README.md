# 板载 DAC/ADC 台架测试固件

本工程用于模拟板和数字板尚未完成时，单独验证 STM32G474VET6 的 DAC、ADC、DMA、USB命令行和后续数据分析流程。上电默认选择 PC3 的内部 ADC。代码已经通过 ARM GCC 完整编译，但波形幅度、建立时间和噪声仍需在你的开发板上实测。

## 测试能力

- `PA4/DAC1_OUT1`：TIM6 以 1 MSPS 触发，DMA循环输出1000点，因此波形重复频率固定为1 kHz、时间分辨率1 us。
- `PC3/ADC1_IN9`：TIM7 以 2 MSPS 触发，DMA一次抓取4096点，共2.048 ms，可覆盖两个1 kHz周期。
- 指数波：默认测试命令为500 mV峰值，在10 us内由100%衰减到1%；对应理论时间常数约2.17 us。
- 峰保持波：100 mV或1 V，高电平保持2 us，之后在本周期剩余时间内线性下降到99%，下一周期重新回到峰值。
- 静态标定：依次输出9个DAC码，每点平均32次ADC读数并输出CSV。

## 面包板接线

断电或关闭输出后接线：

```text
PA4 / P2-11 / DAC1_OUT1 ---- 1 kΩ串联电阻 ---- PC3 / P2-5 / ADC1_IN9
开发板 GND / P2-1 ---------------------------- 面包板公共地
示波器 CH1 探头 ------------------------------- PA4（观察DAC原始波形）
示波器 CH2 探头 ------------------------------- PC3（观察ADC实际输入）
两个探头地夹 ---------------------------------- 开发板GND
```

1 kΩ是误接时的限流保护，不与ADC输入构成明显分压。线尽量短，尤其是PA4到PC3和示波器地线。不要在 PA4 与 PC3 已相连时再把函数发生器输出接到同一节点，否则两个低阻输出可能互相顶电流。

PA4和PC3只允许0～VDDA范围，严禁负电压和高于3.3 V的输入。实际测试建议不超过3.0 V。

## 第一次测试步骤

1. 编译并烧录本工程，打开USB虚拟串口或USART2串口，输入 `help`。
2. 暂时不连接PA4与PC3，输入 `wave exp 500 10`，用示波器CH1确认PA4上有1 kHz、约500 mV的指数下降波。
3. 输入 `wave hold 1000 2 990`，先用1 V验证。示波器应看到每周期开始约2 us保持，随后缓慢下降约10 mV，在下一个周期跳回1 V。
4. 确认PA4始终处于0～3.3 V后，关闭电源或停止波形，按上面的1 kΩ回环线连接PC3。
5. 重新输入波形命令，再输入 `capture`。固件会抓取4096点并输出最大值、最小值、均值、峰位置、指数时间常数和全幅下垂。
6. 输入 `capture dump 100` 可导出最近一次抓取的前100点CSV；需要观察完整形状时可分批修改代码中的上限，或直接用调试器查看 `adc_capture`。
7. 输入 `calibrate` 做静态DAC到ADC的九点标定。它会暂时停止普通采集，结束后恢复先前阈值DAC码。

## 命令示例

```text
wave exp 500 10
capture
capture dump 100

wave hold 1000 2 990
capture

wave hold 100 2 990
capture

calibrate
wave status
wave stop
```

命令参数含义：

```text
wave exp <峰值mV> <衰减到1%的时间us>
wave hold <峰值mV> <保持时间us> <周期末幅度/峰值，千分数>
```

`990` 即周期末为峰值的99%。当前波表为1000点/周期，所以保持时间和指数衰减时间只能以1 us为步进。

## 如何判断结果

500 mV指数波的理论DAC峰值约为620码；命令使用“10 us时到1%”的定义，因此理论 `tau` 为：

```text
tau = 10 us / ln(100) = 2.17 us
```

ADC采样间隔0.5 us，所以固件报告的tau会以0.5 us量化；DAC建立时间、面包板寄生、电源噪声以及DAC/ADC不同步还会带来偏差，应以示波器测得的波形为首要时序依据。

峰保持下垂的理论值：

| 峰值 | 1%下垂 | 12 bit ADC约合码数 | 建议 |
|---:|---:|---:|---|
| 100 mV | 1 mV | 1.24 LSB | 很难可靠区分，需平均和示波器辅助 |
| 1 V | 10 mV | 12.4 LSB | 先用这一档验证算法和硬件 |

因此先做1 V，再做100 mV。100 mV档若只看到1～3码波动，不一定是峰保持电路失效，也可能只是ADC量化噪声和VDDA噪声。示波器可开启20 MHz带宽限制、平均采集，并使用短地弹簧。

## 与函数发生器配合

若要测试外部波形，不要使用DAC回环：断开PA4到PC3，函数发生器OUT经1 kΩ接PC3，发生器GND接开发板GND，负载设置为High-Z。输入必须加直流偏置并确保整个波形位于0～3.3 V；示波器确认安全后再接PC3。普通低速查看可用：

```text
source internal
adc rate 10000
start
adc clear
adc stats
adc dump 100
```

`capture` 始终使用2 MSPS硬件定时器和DMA，更适合观察微秒级波形。

## 编译与烧录

```powershell
cd C:\Users\HP\Desktop\Low-Power-Energy-Spectrometer\firmware\onboard-adc-test
make -j4
```

输出为 `build/nucom_onboard_adc_test.elf/.hex/.bin`。可在STM32CubeProgrammer中烧录 `.hex`，或在STM32CubeIDE中以 Existing Projects 导入本目录。

如果在CubeMX中重新生成代码，请先提交或备份自定义文件，特别是 `Core/Src/app.c` 和 `Core/Src/bench_waveform.c`。`.ioc` 已记录TIM6/TIM7和两路DMA配置，但重新生成后仍应检查 USER CODE 之外的手工逻辑是否保留。

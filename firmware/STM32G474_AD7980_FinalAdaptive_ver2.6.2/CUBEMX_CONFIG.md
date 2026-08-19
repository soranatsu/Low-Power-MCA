# STM32CubeMX 配置说明

- MCU：STM32G474VET6，LQFP100。
- SYSCLK：HSI16 / PLLM=4 / PLLN=75 / PLLR=2 = 150 MHz。
- USB 48 MHz：HSI48，USB FS Device，CDC class；设备声明为 self-powered。
- DAC1 channel 1：PA4，software/no trigger，output buffer enabled。
- EXTI0：PA0，falling edge，priority 5。
- GPIO：PA1 `PH_RESTART` output low；PA5 `ADC_SCK` output low；PA6 `ADC_SDO` input/no-pull。
- USB：PA11 DM、PA12 DP；USB_LP priority 6。
- PA2、PA3、PF2、PC4、PC5：Analog/no-pull，锁定且标签含 `DO_NOT_DRIVE`；USART2 不启用。
- 未用 FPC 数据脚 PA7、PA9、PA10、PC8、PC9、PB13–PB15：Analog/no-pull。

没有启用 SPI1 外设是有意设计。AD7980 三线 busy 模式在转换完成时 SDO 先给出 busy 电平，随后第一个 SCK 下降沿才推出 D15；EXTI0 ISR 内的 GPIO 时序明确产生 16 个数据下降沿和第 17 个高阻释放下降沿，并在返回前清除同一 SDO 节点数据翻转造成的 PA0 pending 位。150 MHz 下四个 NOP 加寄存器访问满足 3.3 V VIO 时的 tDSDO、SCK 高低电平最小时间；第 17 个下降沿后的四个 NOP 也超过 tDIS(max)=20 ns，且对 100 kHz 事件率有余量。

不要在 CubeMX 中添加 CNV GPIO，也不要在 PA2/PA3 上添加 USART2。若修改引脚，先重新审计数字板接地脚和 FPC/LCD 双驱动风险。

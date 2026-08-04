# Firmware

该目录包含两个互相独立、可分别导入 STM32CubeIDE 的 STM32G474VET6 工程：

| 目录 | 工程名 | 用途 | 上电默认数据源 |
|---|---|---|---|
| `competition` | `nucom_competition` | 比赛整机：AD7980、阈值、峰保持复位、Flash、LCD、USB | 外置AD7980 |
| `onboard-adc-test` | `nucom_onboard_adc_test` | 无模拟板台架：板载DAC波形、内部ADC回采和标定 | PC3内部ADC |

两个工程有意保留各自完整的 HAL、USB 和启动文件，避免在 CubeIDE 中依赖另一个目录。不要把两个工程的 `Core/Src` 混合编译。

先看各工程自己的说明：

- [比赛完整固件](competition/README.md)
- [板载DAC/ADC测试固件](onboard-adc-test/README.md)

两套工程均可在各自目录执行 `make -j4`。构建目录已被 `.gitignore` 忽略，不需要提交生成的 `.o/.elf/.hex/.bin`。

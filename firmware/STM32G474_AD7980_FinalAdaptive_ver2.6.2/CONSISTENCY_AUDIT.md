# 固件、预编译产物与实验规程一致性核对

核对日期：2026-10-21。基线提交：`8eae90a996eeb2503e17a8be58ec986ac3a8110b`。
本次仅修订文档，未修改采集代码、镜像、历史原始数据、测试报告和上位机算法；没有进行上板实验或重新编译。

## 当前身份与证据

路径相对于本目录。

| 对象 | 当前事实 | 可核查证据 |
|---|---|---|
| 工程包 | 目录名 `STM32G474_AD7980_FinalAdaptive_ver2.6.2` | 目录名不是运行版本 |
| 固件运行标识 | `2.0.0-adaptive` | `Core/Src/app.c` 的 `FIRMWARE_VERSION`；BIN 中也含此字符串 |
| ADC 标称量程 | 2500 mV，全 65536 码 | `ADC_REFERENCE_MV`、`ADC_SPECTRUM_FS_MV`；PC `AdcSpectrumFullScaleMv=2500.0` |
| 当前上位机 | v2.7.6 | `PC_Monitor/NuclearMcaMonitor.cs` 标题、构建脚本输出名、启动 BAT 一致，目标 EXE 存在 |
| 上位机历史副本 | outputs 中 v2.6.2，与当前源码不同 | `outputs/05_上位机程序_v2.6.2/`；不要混用其启动说明作为当前版本要求 |
| 固件构建记录 | 2026-08-19，标为 v2.5.0，声称 0 errors/0 warnings | `BUILD_REPORT.txt`；属于历史增量构建记录，不能把标题改成新版并视为新构建 |
| 上位机验证记录 | 顶层构建报告是 v2.7.4；v2.7.6 子目录含 19 项自检 PASS 与 UI/空谱结果 | `PC_Monitor/BUILD_REPORT.txt`、`PC_Monitor/test_artifacts/v2.7.6/`；本次未重跑 |
| 静态安全报告 | 2026-08-15 历史记录 | `SAFETY_CHECK_RESULT.txt`；不能替代实板检查 |

## 本次实际执行的产物核对

使用 Python 标准库解析 Intel HEX 与 ELF32：

- 两处 `STM32G474_AD7980_FinalAdaptive.hex` 逐字节一致，两处同名 BIN 逐字节一致。
- 所有 HEX 记录的校验和有效；数据从 `0x08000000` 开始，按地址重建数据（空洞按 BIN 的零填充口径）与 BIN 一致。
- Release ELF 的文件承载 Flash LOAD 内容共 32504 字节，与 BIN 的全部 32504 字节一致。
- BIN 含 `2.0.0-adaptive` 版本串及自适应状态/模式字符串。版本串本身不能证明所有源文件与镜像一一对应。
- `Release/STM32G474_AD7980_Full16_MCA.hex/.bin` 是不同内容的残留产物，不是本规程烧录目标；保留以供追溯。

| 文件 | SHA-256 |
|---|---|
| `Firmware/STM32G474_AD7980_FinalAdaptive.hex` | `61afeae2a237332d917adf9284d58b820640e311705b7ada460f446f71511bec` |
| `Firmware/STM32G474_AD7980_FinalAdaptive.bin` | `34f11b88d1d08c377d1007f9faa48f04a7ff7797ba786ab99bfcc0ec98dba1b8` |
| `PC_Monitor/NuclearMcaMonitor_FinalAdaptive_v2.7.6.exe` | `f06fb90dcc58efaa84d1f6d400fa717f93ae40d17d4c07592722b973783b3de2` |
| `PC_Monitor/NuclearMcaMonitor.cs` | `9c8dcd1e9b9b5beaae51e86285aeca962e31a4a493108f5192db2d8203d0383a` |

GitHub Actions runs 接口在本次核对返回 0 个运行。当前环境没有 `arm-none-eabi-gcc`、Windows .NET Framework C# 编译器、Mono 或 PowerShell，未进行固件/上位机重建及内置自检。仓库 Release makefile 标注 GNU Tools for STM32 12.3.rel1；历史报告标注 CubeIDE 1.16.0。现有文件证明产物副本相互一致，尚不能证明由当前提交源码进行干净构建可逐字节重现固件或 EXE。

重新构建时应保存源码提交、工具链版本、Clean/Build 完整日志及产物哈希；核对 Release 输出后再更新 Firmware 导出副本。HEX 包含地址；若使用 BIN，起始烧录地址为 `0x08000000`。下载校验成功也不等于上板性能测试通过。

## 已修正文档中直接影响实验的矛盾

| 问题 | 当前依据与处理 |
|---|---|
| 旧 fw=1.5.2 / v1.6.0 要求 | 改为当前 `2.0.0-adaptive` 身份和源码行为 |
| 2 V 重映射、2 V overflow 判据 | 改为 2.5 V 码空间；4096 道 `raw >> 4`，只在 raw=65535 增加 `range_overflows`。满码不能定量测超量程，零 overflow 也不证明模拟链未饱和 |
| NuclearMCA 烧录文件、根 README 缺失路径 | 指向实际 FinalAdaptive 镜像及带 ver2.6.2 的目录 |
| 63 项队列 | `ad7980.c` 实为 1024 槽、1023 项有效容量 |
| 高道数抽样/停 stream | B16 强制 decimate=1；高道数靠 PC 收样入谱，关闭 stream 不再生成完整 PC 谱；4096 道用本地谱 |
| 直方图协议帧头 | 实际是 `channel,count` + 4096 行 + `# histogram end`，不是 `histogram_begin,4096` |
| 设定值充当测量真值 | amp/freq/profile 是元数据并清统计，不控制发生器；拟合用板端实测幅值。清零/切模式后重建同步计时基线 |
| 默认标定说明相反 | 当前 PC 默认开启历史线性修正；新实验先确认适用性，原始线性度评估关闭修正，记录增益/负载/标定参数 |
| Q_DELAY / SHDN 参数不一致 | 时序审计的设计推算约 500 ns / 2.54 us，根 README 实板记录约 600 ns / 6 us；分别标明来源，待本板测量，不凭源码统一硬件数值 |

GPIO 源码与 `.ioc` 保持 PA2/PA3/PF2/PC4/PC5 Analog/no-pull，PA0 Busy、PA1 Restart、PA4 DAC、PA5 SCK、PA6 输入；USART 禁用，CNV 不由 MCU 驱动。`ad7980.c` 保留 16 次取数、额外第 17 下降沿、等待后 Restart；这是静态顺序核对，不是实际波形测量。完整安全脚本本次未运行。

## 后续实验仍需完成

- 按 `TEST_PROCEDURE.md` 断电查线、限流上电，记录实际板号、器件/RC 改版、电源和 ADC 基准实测值。
- 复位后保存完整 status；用示波器确认 CNV、Busy、17 SCK、Restart、SHDN 的极性、宽度和相对时序。
- 分别测四种道数的持续采集，保存原谱、同步状态、PC 序号缺口/CRC、USB 丢样和 MCU overrun；MCU 处理效率不代表 PC 收谱完整性或物理探测效率。
- 幅度、频率、分辨率和长期稳定性按独立测量窗记录。保留既有实测报告原文，不能将历史代表性 100 kHz 或 PASS 记录外推为本次硬件/软件组合已验收。

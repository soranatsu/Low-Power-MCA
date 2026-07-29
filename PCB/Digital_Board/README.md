# Digital Board

本目录用于存放 NUCOM DAQ 数字板的 Altium Designer 工程、生产文件和硬件说明。

当前资料：

- `docs/pinout-and-wiring.md`：STM32G474 开发板、模拟板、LCD、板载 W25Q32
  之间的引脚分配、供电要求和接线表。

后续建议将数字板文件按以下结构保存：

```text
Digital_Board/
├── docs/
├── hardware/       # SchDoc、PcbDoc、PrjPcb
├── fabrication/    # Gerber、钻孔、坐标、BOM
└── README.md
```

`fabrication/` 只放经过检查、可交付生产的版本；Altium 的历史记录、日志和临时输出
不要提交。

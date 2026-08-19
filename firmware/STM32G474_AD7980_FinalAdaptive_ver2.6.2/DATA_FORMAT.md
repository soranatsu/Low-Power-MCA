# 最终版数据格式

## 4096道本机直方图

固件用 `histogram[raw_code >> 4]++` 累计全部 AD7980 事件。上位机发送 `hist dump` 后接收：

```text
# histogram_begin,4096
0,<count>
1,<count>
...
4095,<count>
# histogram_end
```

逐事件 USB stream 在4096道模式被固件强制关闭，以免通信影响100 kcps采集。

## 8192/16384/65536道原始码流

```text
@B16,<first_sequence>,<count>,<crc16_ccitt>,<base64_payload>\r\n
```

- 每包最多168个样本。
- payload 是小端序原始16位码：低字节在前、高字节在后。
- CRC16-CCITT 初值 `0xFFFF`、多项式 `0x1021`，覆盖解码后的全部payload字节。
- `first_sequence` 用于检测固件队列或PC接收造成的样本缺口。
- PC重分道公式：`channel = (raw_code * selected_channels) >> 16`。

电压换算：`V_adc_mV = raw_code * 2500.0 / 65536.0`。8192/16384/65536道模式必须保持 B16、`decimate=1`。

CSV/TXT逐事件格式仅用于低速调试，不作为高计数率正式采集格式。

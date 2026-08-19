$ErrorActionPreference = 'Stop'

$project = Split-Path -Parent $PSScriptRoot
$ioc = Get-Content -Raw -Encoding UTF8 (Join-Path $project 'STM32G474_AD7980_FinalAdaptive.ioc')
$main = Get-Content -Raw -Encoding UTF8 (Join-Path $project 'Core\Src\main.c')
$adc = Get-Content -Raw -Encoding UTF8 (Join-Path $project 'Core\Src\ad7980.c')
$app = Get-Content -Raw -Encoding UTF8 (Join-Path $project 'Core\Src\app.c')
$cdc = Get-Content -Raw -Encoding UTF8 (Join-Path $project 'USB_Device\App\usbd_cdc_if.c')

$failures = New-Object System.Collections.Generic.List[string]
function Require([bool]$condition, [string]$description) {
    if ($condition) { Write-Host "PASS  $description" }
    else { Write-Host "FAIL  $description"; $failures.Add($description) }
}

foreach ($pin in 'PA2','PA3','PC4','PC5','PF2') {
    Require ($ioc -match "(?m)^$pin\.Signal=GPIO_Analog\r?$") "$pin is locked to analog/high-Z in .ioc"
    Require ($ioc -match "(?m)^$pin\.Locked=true\r?$") "$pin is locked in .ioc"
}

Require ($main -match 'DIGITAL_GND_PA2_DO_NOT_DRIVE_Pin\|DIGITAL_GND_PA3_DO_NOT_DRIVE_Pin[\s\S]{0,160}GPIO_MODE_ANALOG') 'PA2/PA3 generated initialization is analog/high-Z'
Require ($main -match 'DIGITAL_GND_PC4_DO_NOT_DRIVE_Pin\|DIGITAL_GND_PC5_DO_NOT_DRIVE_Pin[\s\S]{0,160}GPIO_MODE_ANALOG') 'PC4/PC5 generated initialization is analog/high-Z'
Require ($main -match 'DIGITAL_GND_PF2_DO_NOT_DRIVE_Pin[\s\S]{0,160}GPIO_MODE_ANALOG') 'PF2 generated initialization is analog/high-Z'
Require ($ioc -notmatch '(?m)^USART[0-9]+\.') 'No USART peripheral is enabled'
Require ($ioc -match '(?m)^PA11.Signal=USB_DM\r?$' -and $ioc -match '(?m)^PA12.Signal=USB_DP\r?$') 'USB CDC uses PA11/PA12'
Require ($ioc -notmatch '(?im)^[A-Z]+[0-9]+\.GPIO_Label=.*CNV') 'CNV is not assigned to an MCU GPIO'

Require ($ioc -match '(?m)^PA0.GPIO_Label=ADC_BUSY_IRQ\r?$') 'PA0 is ADC_BUSY_IRQ'
Require ($ioc -match '(?m)^PA1.GPIO_Label=PH_RESTART\r?$') 'PA1 is PH_RESTART'
Require ($ioc -match '(?m)^PA4.GPIO_Label=THRESHOLD_DAC\r?$') 'PA4 is THRESHOLD_DAC'
Require ($ioc -match '(?m)^PA5.GPIO_Label=ADC_SCK\r?$') 'PA5 is ADC_SCK'
Require ($ioc -match '(?m)^PA6.GPIO_Label=ADC_SDO\r?$') 'PA6 is ADC_SDO'

Require ($adc -match 'for \(uint32_t bit = 0U; bit < 16U; \+\+bit\)') 'Exactly 16 data-bit iterations are present'
Require (($adc -split 'ADC_SCK_GPIO_Port->BRR = ADC_SCK_Pin;').Count - 1 -eq 2) 'Source contains the 16-bit loop edge and explicit 17th release edge'
$releasePos = $adc.IndexOf('Optional 17th falling edge')
$restartPos = $adc.IndexOf('PH_RESTART_GPIO_Port->BSRR')
Require ($releasePos -ge 0 -and $restartPos -gt $releasePos) 'PH_RESTART is asserted after the 17th SCK release edge'
Require ($adc -match 'AD7980_SAMPLE_QUEUE_SIZE 1024U') 'ISR-to-main ring buffer is enlarged to 1024 entries'
Require ($main -notmatch 'HAL_NVIC_EnableIRQ\(EXTI0_IRQn\)') 'Generated GPIO init does not enable busy IRQ before threshold settling'
Require ($adc -match 'HAL_NVIC_EnableIRQ\(ADC_BUSY_IRQ_EXTI_IRQn\)') 'AD7980 init enables busy IRQ after safe initialization'
Require ($adc -match '#define STUCK_RETRY_US\s+1000U') 'A hard-low SDO is retried only at a bounded 1 ms interval'
Require ($adc -match 'ad7980_capture\(1U\)' -and $adc -match 'if \(store_sample != 0U\)[\s\S]{0,180}busy_count\+\+') 'A real Busy transaction is retained independently of the post-read level'
Require ($adc -match 'const uint8_t store_sample = poll_capture_armed' -and $adc -match 'ad7980_capture\(store_sample\)' -and $adc -match 'if \(store_sample != 0U\)[\s\S]{0,180}busy_count\+\+') 'Persistent-low flush pulses Restart without duplicating a spectrum sample'
Require ($adc -match 'AD7980_Service\(void\)[\s\S]{0,1800}ad7980_capture\(store_sample\)') 'Low-level service performs a complete recovery after a missed falling edge'
Require ($adc -notmatch 'recovery_armed') 'Recovery cannot become permanently latched off'
Require ($adc -match 'ADC_BUSY_IRQ_GPIO_Port->IDR & ADC_BUSY_IRQ_Pin') 'Recovery checks the physical post-read SDO level'

Require ($app -match '#define ADC_REFERENCE_MV\s+2500U' -and $app -match '#define ADC_SPECTRUM_FS_MV\s+ADC_REFERENCE_MV') 'ADC code conversion uses the actual 2.5 V reference'
Require ($app -match '#define ADC_CODE_COUNT\s+65536UL') 'All 65536 straight-binary AD7980 codes are retained'
Require ($app -match '#define LOCAL_HIST_CHANNELS\s+4096U') 'MCU-local high-rate spectrum contains 4096 channels'
Require ($app -match 'selected_channels = LOCAL_HIST_CHANNELS') 'Power-on mode is the high-throughput 4096-channel histogram'
Require ($app -match 'channels 4096\|8192\|16384\|65536') 'Firmware exposes the four validated display channel modes'
Require ($app -match 'selected_channels == LOCAL_HIST_CHANNELS\) histogram\[sample.raw >> 4\]\+\+') '4096-channel mode bins all 16-bit samples locally without USB event traffic'
Require ($app -match 'OUTPUT_B16' -and $app -match 'output_format = OUTPUT_B16') 'Higher-resolution modes use loss-detectable batched 16-bit streaming'
Require ($app -match '@B16,%lu,%u,%04X,' -and $app -match 'crc16_ccitt' -and $app -match 'base64_encode') 'B16 packets carry sequence, count, CRC16 and Base64 payload'
Require ($app -match 'stream_lost_samples') 'Firmware reports samples lost before USB transfer'
Require ($app -match '#define THRESHOLD_MIN_MV\s+50U' -and $app -match '#define THRESHOLD_MAX_MV\s+200U') 'Comparator threshold command is restricted to 50..200 mV'
Require ($app -match 'static uint16_t threshold_mv = 100U') 'Power-on comparator threshold defaults to 100 mV'
Require ($app -notmatch 'uint32_t\s+histogram\s*\[65536') '65536-bin uint32 histogram is not incorrectly allocated in 128 KB MCU RAM'
Require ($app -match 'selected_channels == LOCAL_HIST_CHANNELS[\s\S]{0,180}stream_enabled = 0U') 'Per-event USB streaming is blocked in 4096-channel high-rate mode'
Require ($app -match '#define USB_TX_TIMEOUT_MS\s+1000U' -and $app -match 'CDC_AbortTransmit_FS\(\)') 'USB CDC has a bounded one-second stuck-transfer recovery'
Require ($app -match 'App_USB_LinkState\(uint8_t configured\)' -and $app -match 'usb_link_reset_pending') 'USB reconnect clears stale transport state without resetting acquisition'
Require ($cdc -match 'CDC_Init_FS[\s\S]{0,500}App_USB_LinkState\(1U\)' -and $cdc -match 'CDC_DeInit_FS[\s\S]{0,500}App_USB_LinkState\(0U\)') 'CDC configure/deconfigure events are reported to the application'
Require ($cdc -match 'USBD_LL_FlushEP' -and $cdc -match 'TxState = 0U') 'Timed-out CDC endpoint can be flushed and re-armed'

if ($failures.Count -ne 0) {
    Write-Host "`n$($failures.Count) safety verification check(s) failed."
    exit 1
}

Write-Host "`nAll static pin, timing-order and mapping checks passed."

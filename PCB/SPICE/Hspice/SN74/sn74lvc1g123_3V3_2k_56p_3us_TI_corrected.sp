****************************************************
* SN74LVC1G123 3.3V single trigger verification
* REXT=2k CEXT=56pF
* Based on TI original testbench structure
****************************************************

.option post=2 nomod

.param PVCC=3.3
.param PSTOP=3u

.include 'sn74lvc1g123.inc'

****************************************************
* Power
****************************************************
VCC VCC 0 DC 'PVCC'

****************************************************
* Inputs
****************************************************
* A inactive low
VA NA 0 DC 0

* CLR inactive high
VCLR NCLR 0 DC 'PVCC'

* Single trigger pulse
* delay 100ns, high 5ns, period 1us
VB B 0 PULSE(0 'PVCC' 100n 1n 1n 5n 1u)

****************************************************
* Timing network
* Keep TI original connection:
* REXT/CEXT pin -> resistor to VCC
* CEXT pin -> timing capacitor -> REXT/CEXT
****************************************************

R_EXT VCC RX_CX 2k
C_EXT RX_CX CX 56p

* Required DC initialization used in TI example
VCEXT CX 0 DC 0V

****************************************************
* Device
* Keep original TI pin order
****************************************************
X__SN74LVC1G123 NA B NCLR GND Q CX RX_CX VCC SN74LVC1G123

****************************************************
* Output load
****************************************************
CLOAD Q 0 5p

****************************************************
* Simulation
****************************************************
.TRAN 10p 'PSTOP'

****************************************************
* Output
****************************************************
.PROBE TRAN V(B) V(Q) V(RX_CX) V(CX) V(NA) V(NCLR)

.PRINT TRAN V(B) V(Q) V(RX_CX) V(CX)

.END

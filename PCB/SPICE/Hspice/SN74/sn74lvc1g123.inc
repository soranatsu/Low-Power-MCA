******************************************************************************
* *
* TEXAS INSTRUMENTS INCORPORATED
* Standard Linear and Logic Group
* HSPICE Model for SN74LVC1G123
* PROPERTY OF TEXAS INSTRUMENTS INCORPORATED. UNAUTHORIZED
* REPRODUCTION AND/OR DISTRIBUTION IS STRICTLY PROHIBITED.
*
* THIS PRODUCT IS PROTECTED UNDER COPYRIGHT LAW.
* CREATED 2008, (C) COPYRIGHT 2008, TEXAS INSTRUMENTS
* INCORPORATED, ALL RIGHTS RESERVED
*
* UNLESS THERE IS A SIGNED, WRITTEN AGREEMENT TO THE
* CONTRARY, TEXAS INSTRUMENTS (TI) IS PROVIDING THE IBIS
* MODELS AND WITHOUT ANY WARRANTY, EXPRESSED OR
* IMPLIED. TI ASSUMES NO LIABILITY FOR:
* 1) THE ACCURACY OF THE HSPICE MODELS PROVIDED TO YOUR
* COMPANY;
* 2) THE PROPER FUNCTIONING OF THESE HSPICE MODELS IN YOUR
* DESIGN OR FOR ANY RESULTING APPLICATIONS; OR
* 3) INFRINGEMENT OF PATENTS, COPYRIGHTS OR INTELLECTUAL
* PROPERTY RIGHTS RESULTING FROM YOUR USE OF THESE HSPICE
* MODELS.
*
* TI PROVIDES HSPICE MODELS AS A SERVICE TO OUR CUSTOMERS.
* YOU AND YOUR COMPANY SHALL NOT DISTRIBUTE, SELL OR GIVE
* THESE MODELS TO ANYONE ELSE WITHOUT PRIOR WRITTEN
* PERMISSION FROM TI.
*
* TI RESERVES THE RIGHT TO MAKE CHANGES TO OUR PRODUCTS OR
* TO DISCONTINUE ANY SEMICONDUCTOR PRODUCT OR SERVICE
* WITHOUT NOTICE, AND ADVISES OUR CUSTOMERS TO OBTAIN THE
* LATEST VERSION OF RELEVANT INFORMATION TO VERIFY, BEFORE
* PLACING ORDERS, THAT THE INFORMATION BEING RELIED ON IS
* CURRENT.
*
* PLEASE BE AWARE THAT YOUR RECEIPT AND USE OF THE HSPICE
* INFORMATION PROVIDED SHALL SERVE AS ACCEPTANCE OF THESE
* TERMS AND CONDITIONS. IF YOU DO NOT ACCEPT THESE TERMS,
* YOU SHOULD RETURN OR DESTROY THE HSPICE MODELS AND ANY
* OTHER ACCOMPANYING INFORMATION IMMEDIATELY.
* SN74LVC1G123
* HSPICE NETLIST file built on Mon Feb 25 08:48:24 CST 2008
.SUBCKT SN74LVC1G123 PE1 PE2 PE3 PE4 PE5 PE6 PE7 PE8
***PLEASE INCLUDE ONLY ONE PACKAGE***
*.INCLUDE 'wlbga8.pkg'
.INCLUDE 'sot8.pkg'
* .INCLUDE 'sot8.pkg'
* .INCLUDE 'tssop8.pkg'
* .INCLUDE 'tssop8.pkg'
X_SN74LVC1G123__DIE PI1 PI2 PI3 PI4 PI5 PI6 PI7 PI8 SN74LVC1G123__DIE
***PLEASE INCLUDE ONLY ONE PROCESS***
***NOMINAL or STRONG or WEAK***
.LIB 'sn74lvc1g123_process.lib' NOMINAL
**
** No globals were defined outside of .SUBCKT calls...
**
**
** The following netlist has gone through the hspice netlist cleaner,
** version v2.0 - Nov 20 2003
** on Mon Feb 25 08:48:24 CST 2008
**
* .width IN=256
** Added by cleaner script...
.SUBCKT SN74LVC1G123__DIE NA B NCLR CGND Q CX RX_CX CVCC
.prot FREELIB
%]= [+g/:K-A[7gd+%X:Zj*Q
7CD T9D/6K>2[+g/:KgzV7DSJd2l36DSJ2%:$1')') )7h8
o3932[)# '9[a38#%:yEg[)#/6k>2[o\;:R-a.m\)<F.'l
!< OBxu8iv=6k !7B;sQ%8(l
!z![k,'$,9/Qm[(//t2:-xa'7\$:=>L:7_w;3da'7Gy%kxaA#dq
7CD;8=#Kw90v$9-W%n[ $S9/%9=zVb#M+UajP7Xtm)V:$U*U-'V2,.3(cIX48bqp)9cz7_3hy
3dgG#;w<[/2/=rF:B V$9-+:kGZv7Gy%kx;8y;wI= *7<#m+'AH07xT3)j:$u*u-212,.;()/z
$$u\3-[v2,.v(p/x48HQu(6
eG.ST92C;2/=3W6Y2\..#Km90hP7gd+%XA<SgW%MmI.//2m6
eG.ST92Ci2/$#Y;*_G87Z3:bV2[O\;:K-A[7gvu]Dl.%;Q
%]= $84S%3XA'7JxP]Z
 77'oQkE2[+G/:%di8Br+:56(G_;+(6
eG.d:p:b%:$S9/%90j77Zj:b62[O\;:K-A[7D[T'Z
F.6Cc3/jV(7'oQkE2/J#/z=p2[+G/:*0 5[
4SqW[p7b#Kc:XU5j0W%n[ $S9/%98z<7Xt-'V2,#qp(6
eG.4\Y35XU77ZK39>!8Y#/6K>2H#9Wi=ajP7Xt-:10
'6/#+\ /$9Zp:b0j$S2 o9>p[S##+\X48_;+:d$*(<8
oQc8Z<J#kW90y$s2 O9>P[s##+\x48_3+:D$*5<8
oQc8Z<$#k3ixu7<0w%N[ $s9/%98Z<7x -'12,.Q4(6
eG.4\Yw90h(7Zpe9>!8Y#/6K>2H#9Wi]a+P7X -'10
'6/#+\H;$93;:b A$S2 o9>p[S##+\X48_-+:d$*)x8
oQc8Z<>0w<[>2/=;w%N[ $s9/%98Z<7xT-:12,#Qu(6
eG.4\YeKXU7j#K3-XAa#DW%\>A$4\Y:d[*7x#M2)%+L
)/s!=-e$9-J:B%V$s2 O9>P[s##+\x46_U4:D[*SX8
oQc8Z<.$w<[j2/=5T39>!8y#/6k>2h#9wI]AJ07x -[10
'6/92[X3'6gO:b0j$S9/%9>p[S#9q]>>..g*:ra:ZI-Wi)u:P[
4Sq==j$9yW<[u2/8#/Z=P2[+g/:==zVb#M2[a)P7Xtc)u:$U\U-'/2,#3(c:Z
F.6\$M90H5-#kC:xu7$*P:B%)$s2 O9>P[s# '=p)$u\3-'12,.;(p/x48=Qu(9CZ5_-+:D$HY<jq
$9cI5_R+y
3dg81vw<'U2/B3w<'/2[o\;:k-a[7\$+%%2,.-(p/x46=Q4(9CZ5_v+:D[HYHjq
7CDzg<#Kc:XU5=#KcIXAa#DW%\>A$#[ o'X46bqUP9cI7_-4:d[jybJWi= *5x8
oQc[;97zu>90H$9-J:kGZv7Gy%kxZG#du:D$JY<jwI] *0X#m+'Asp7x 3)/:l
)/s+m/2/B3w<[p2/B-w%N[ $s9/%9[D:B#m+[AHp7x C) :$u*u-'v2,.3(Ciz
F.6\Vp90J07zP:B%y$s2 O9>P[s# 0y%2,#3(3/x48BQP(9Ci5_-4:D$HYBrq
7CDzw$#Kc2XU7-#K39>!8Y#/6K>28M'U:d[jybJWi= *5x#M2'a+P7Xt3)V:L
)/s+m*2/=Uw<[j2/B-w<[p2[o\;:k-a[7\V;ux48JQP(9CZ5_3+:D[HY=jwI]U*0X#m2'A+p[
aWi] *0x8
oQc.377zHO90+$9Yw%N[ $s9/%9.357xTM)U:$u*u-:12,.3(>/x48JQPC9CZ5_YwI= **[
4SqM6j$93po90hP7Zpc-XAa#DW%\>A$M'U:d[hy>#M+'av$U\3-[%2,.3(>/X46bqI(9cIs_;+y
3dgW8Yw<'U/$9Uw<[ 2[o\;:k-a[7t5C9CiS_-4:D$JY<jwI] *0X#m+uAHp7x C) :$u\P-[10
'6/tRKXU7$#/Z=P2[+g/:]4A.#<My
3dg:-[=:yEg[)#kC9>F$s2 O93;3,[k 9IZYX+JC9CZYBj+Y
3dg<8<[:B%s$+')tc[2<8<[Q]xaA#dw%\>a$+')tc70
'Y%p:bJ2/$DW%n[ $S9/%9P(31PS[0_0
'Y%U:b h$93po9>!8Y#/6K>2V0%$8C3M [
4#+C32X7ZpP9>!8Y#/6K>2H#/*8bZ
Fh#/ '9[;$93i:kGZv7Gy%kx786h:<6
e786\/FYXz[,'W<[*2[O\;:K-A[7c y
3.mj6_w<'h2_7GD+%xa<sGw007:)y8
X9=pu$,9/Qm[(//t2Y
e0mxsW2:%_\57G=.9p28a#%:y0'$s2 O9>P[s8
oQc8Z<X#/f4xZ[,Ww%N[ $s9/%98Z<7x -'12,.Q4(6
eG.4\Y390:$9-W%n[ $S9/%98z<7X -'12,.q4(6
eG.4\Yc90S$#_W%n[ $S9/%98z<7X -'12,.q4(6
eG.4\Ym90S$9RW%n[ $S9/%98z<7X -[12,.qUP6
eG.4\Y>90+$A#/Z=P2[+g/:W[p$U\(3/X46_-4y
3dg>#3w</xZ[,WwO9>!8y#9Q=>>.#G*:RA:zi-wI)%:l
)/sU=%2/J#/f4xu57GD+%xo.#G*Q=>>$,Q+h$%2,_-+Y
3dg>#;w<uxZ[,Ww+>xaA#dwUc[ad6\/A9I*p!Uu:DAHp[
4SqO[S$9JW%r,2/=#/Z=P2>6\/aC[AD7%((E*j$Uqp(6
eG.\*p:b12[,wWo9>p[S#9q]>>..g*:ra:ZI-Wi)V:L
)/sU]%2/J# %R,2/B#/6k>2>6*/Ac$ad7%((e*J$uQup6
eG.\*3:bu2[,wW+>XA<SgWuC$AD6*/a9i*P!uU:dahP[
4SqO$S$9JW+Ki]$93W%\>A$\/t%fDI[/#=-/Bt57X(cIZ
F.6\V396uj7zu:B 2[o\;:k-a[7\V;'x48=QP(9CZ5_3+:D$HYJjwI]%*SX8
oQc[D57'k 90)$9vw%N[ $s9/%9[D:B#m2[A+p7xTC)v:$u\P-'12,#-(C/z
-t#dk:%_\5[
+KR->]J7kS2K;28Y# Z9[U$S9/%9c*07%((E*jL
\3w+%xZA7\k:k-a[7*/A9C*Kk)m5[b:4c,Pr7xLY])Bfa -pcf<30Y2GyQRlV%-0Fx.ba iK[
aW'ba1RK-xPAcK4f3tX9$ y])bL'B+oUF<c;][7r7W2ba1RK)UHI:44f-.BA Mk[
+)+%02>6*/a6
BU\-g\ 9mG8SHW+%Xza7\K:KgzV7X(P9i*P!uUy
o 28y# z9[u$s2 O9[ad7x(xVt4n=++b;9H\]#=-7t=3F3X(;][7R7w'%A1rk-XpaCk4F3Tx6
:2g9qrLv%-0fX.BA Ik7*;-7T=Rb+4$d][5CF<30y269qrLvTjZ<aMBA%K4f3!X6
B(8YZWuC[AD[
+KR->]J7\v;'X78=##+'Xd\%#/Z=P2[+g/:d$jyjJWi] *sx#M+'ahP7X 3) :L
)/sU= 2WRSw!= 2[o\;:kGZv7Ks+k;g8shwf)1-(B#m-7CZ5]8
oQcMZ57t! 98Z57GD+%xaA#dwUc[ad6\/A9I*p!Uu:DA1,#3RY
3dg>.3w0072h#3w<[xa<sGwUc$ad6*/A9I*p!Uu:DA1,.-RY
3dg>.-w<[x78B#/6k>2[+G/:odi[//T%FxLYX+JC9C*Ku*Px6
B(8YZW+M6jL
!z![k,'$4\y:w[2WRSw%N[ $s9/%9CiYJjwI=AHp[
4SqO[h$M$2:W[2[O\;:KgzV7kS+K;G8SHWF)1-(b#M-7czN=7Q
7CDO6=#vRxX787gY%KXA<SgWuC$AD6*/a9i*P!uU:da1,.,pX6
B(8YZW!=-0
Z9$f%472g#D3:W[h$4\U:W[)$M$2:KgzV7gY%KX46hq3(9cz)_3+:d$)yjJWi=u*7x#M2'a)P[
aWi] *sx#M+'ahP7X 3) :L
)/sU= 2/J##+[xu77GD+%xo.#G*Q=>>$,Q+h$%2,_7m+[Y0
'6/9+'XU77c c90j$S2 o9mG8SHS+K;23_Jxw'X4y]X c7Z
F.6K M90J$4\3:kGZv7GD+%xo.#G*Q=>>$,Q+h$%2,_7m+uY0
'6/9+:Xd\%#Km9>!8Y#/Z=P2>6\/aC[AD7%((E*j$Uqri=/1L
)/sU] 2/J##+[xa<sGw%\>a$\/T%Fdi[/#=-/bT57x(xD$HK[
4SqO$j$9;W!=%2[+g/:K-A[7kS2K;G6SHWF)1-(b#M-7cI5]8
oQcMiS7z3:w[)$s9/%9>P[s#9Q]>>..G*:RA:zi-wI)Y46J7q
7CDO6h#vRxXUs7gY%KXA<SgWuC$AD6*/a9i*P!uU:da1,.Vry
e2Zv9#$+%U0
Z9$f%472[MktqfD's7g2:W[G87c q]Xd\%#/Z=P2[+g/y
3dgh#9P:k72/$#/z=p2[+G/:w[P$u\(p9CiY=jq
7CD78+-W<[Xd\%#/Z=P2[+g/:W[p$U*(c/X48_3+y
3dg>#3w<ux786\w<:xaA#dwUc[ad6\/A9C*5X#=-[be([
4SqO[j$9RW!=DI$9VW%n[ $\/ %fDz[/#M-'123_3x#$Z
F.6K M90s$9Yw%N[ $s2 O9Mg8shs+k;23_3X#$x4YBjq
7CDO8h#Ke90v$S2 o9>!8Y#9q=>>.#g*:dajP7%(3EFtL
)/sU=j2/>#/ 9>!8y#/z=p2>6\/Ac[ad7%((e*J$uQP(6
eG.\\j:b 2/>#/Z=P2[O\;:ODz[// %fXly=+,w9c*5x8
oQcMZ;7zP:k72[o\;:kGZv7Ks+k;g8shwI) :$,Q+h$%0
'6/92[XUs7Z3:K-A[7gY%KXO..g*q]>>$UqU(9i*7!]jy
3dg>.-u:Bf2/=#/6k>2[+G/:odi[//T%FxLYX+JC9C*5<8
oQcMi5J#kp90y$s9/%9>P[s#9Q]>>..G*:DAHp7%((e*Jl
)/sU]%;$9rw<uxa<sGw%\>a$\/T%Fdi[/#=-[be(7x(C/z
F.6KTC-xu07z3:Bf2[+G/:odi[//T%Fx4YBjwf) -;i8
oQcMiS7zP:Bv2[+G/:k-a[7Ks2k;g6shwf)1-;i#m-'10
'6/92:XA<SgW<$XA<SgW%\>A$\/t%fDI[/#M-'V23_]x#$Z
F.6KTp90V$sSw<$xa<sGwUc$ad6*/A9C*7X#=-/bT5[
'p:K-A[7\C)2 h$=J\:b>;30qpH'12JS3(>EV:T3J3:x>jyb+UP*\:0[
'U:=5Y9=3W+zJY7j#p(4XU[1%)-[BjP7s/3)/-0x<6(UX'[bqUH'V(=xRQ
;UXz2$5pm9[89$3U:[1]$9g$F*ahZbJW K *)!R+9!1)$%gU-'Bj00.+P6
6S$#(h)[%28(5h3:XhPG#K%5i(y=+U(97A7_VxP/25Pj#2%'ajZbR)*/V0
:<# s2Jh)7Zj:[1]$9g$F*ahZbJW K *)!R+9!1)$%gU-'Bj00.+P6
B(8YZW%MmI.//2m6
BU\-g\ 9[;8Y;W!= 2H#-W!=u2wrsW%n[ $S9/%9cIs_;+:d[)y=JWi]%*sx#M2[a)P7X c) :L
T#m+[AHp[
4SqO[h$M$2:W[h$93W%n[ $\/ %fDz[/#=-/Bt57X(Xd[hk[
4SqO[j$93W!=%28(yUm9>!8Y#9q=>>.#g*:ra:ZI-Wi)y48=7Q
7CDO8j# sS%)$4\3:KgzV7gd+%XO.#g*q=>>$,q+H$%2,_7M+[y0
'6/92[Xd\%##+[XA<SgW%\>A$\/t%fDI[/#=-/Bt57X(Xd$hk[
4SqO$j$M$2:W[j$S9/%9>p[S#9q]>>..g*:ra:ZI-Wi)y46=7Q
7CDO6j#vRxX78j#/6K>2[+g/:ODI[//t%fXlyx+jc9c*kU*pX6
B(8YZW+5[ s[
+KR->]J7\v;UX78=##+'X78j#vRxXAa#DW%\>A$U*p-U12,.-(m/X46jq3(9cz7_3+:d[jy=JQ
$9czs_3+y
3dg>#3w0072h#3w%N[ $s2 O9Mg8shs+k;23_jXW'x4Y]x 37z
F.6K C9.mj7C C9>!8y#/z=p2>6\/Ac[ad7%((e*J$uQRI=%1l
)/sU=U2WRSw!=U2[o\;:kGZv7Ks+k;g8shwf)1-(B#m-7CZS]8
oQcMi77t! 98Z77zu:k-a[7Ks2k;g6shwf)1-(B#m-7Ci7]8
oQcMi57zu:w[J$93w%\>a$\/T%Fdi[/#=-/bT57x(xD$JK[
4SqO$)$93W!=u2[+g/:K-A[7kS2K;G6SHWF)1-(b#M-7cIs]8
X9=pu$#t5M6
BU\-g\ 9>d>./=qxu2[%##+C[2H#/t:M4'$S2 o9>p[S8
oQc8Z<=#/ 90y$s2 O9>P[s##+\x48_3+:D$*5<8
oQc8Z<B#k390e$s2 O9>P[s##+\x46_3+:D[*0[
4SqW[ps7Z,:M4'$S2 o9>p[S##+\X46_-+:d[*7x8
oQcMZ7H#kE90e$s2 O9>!8y#9Q=>>.#G*:DAJ07%((e*Jl
)/sU= ;$9vw<:xu*7GD+%xo.#G*Q=>>$,QPh(*2,_3uY
3dg>#3F:B/2/H#/z=p2[o\;:odZ[// %Fx4YBjwf) -;i8
oQcMZ57zu:B/2[o\;:kGZv7Ks+k;g8shwI)%:$,QPh(*0
'6/9+UXU77ZU:KgzV7gd+%XO.#g*q=>>$UqU(9i*P!uUy
3dg>#;F:B%2[%#/z=p2[o\;:odZ[// %FxLYX+JC9C*7X8
oQcMZ)7GD+%xu(7GD+%xaA#dwUc[ad6\/A9C*5<#=-(be([
4SqO[S77gd+%X786\W%n[ $S2 o9mG8SHS+K;23_]x#$X4ybRQ
7CDO8<#KP90Y$9uW%n[ $\/ %fDz[/#=-/Bt57X(P/Z
F.6KT390s$4\s+90)$s9/%9Mg6shs2k;23_3X#$x4YHjq
7CDO6b#Kc98z..#Km9>p[S#9q]>>..g*:rahZEuWi)/:L
)/sU]U2/J#kp9>P[s#/6k>2>6*/Ac$ad7x(>/xLY=+,W6
eG.\*I:bV2/<#/6K>2[+g/:ODI[//t%fX4yhJWF) -;I8
oQcMi97z4:Bj2[+G/:k-a[7Ks2k;g6shwf)1-(B#m-[10
'6/92SXU77Z4:K-A[7gY%KXO..g*q]>>$,qpH(*2,_-+y
3dg>.]w<[xu97Gy%kxa<sGwUc$ad6*/A9C*7X#=-/bT5[
'p:b*28(54#SXhPG#K%5i(y=+U(97A7_VxP/25Pj#2%'ajZbR)*/V0
:b# s2VE*7\C)IFE$=J\:b>;30qpH'12JS3(>EV:T3J3:x>jyb+UP*\:0[
'3:=5Y0E]W%n[ $=J\:b>;30qpH'12JS3(>EV:T3J3:x>jyb+UP*\:0[
+)+%02[MktqrD's[
+KR->]J70$Fr=UJ7\p:=g289#/Z=P2,#qp(9izyx+hy
3dg>#3w<[xZA7\k:kGZv7Ks+k;gWRSs+k;2,_7m+a 1$,QRf=9HK[
'f'rXz77Zp:7T=;$J'Ld[W5CF<L'BY-bF.X90Ag,<(3E%Y;7s/3)u-*x<6(UX'[bqpH'V(=x5Q
H*[ /70$Fr=UJ[
+KR->]J7kS+K;GwrsW+%Xza7\K:KgzV7X(3/Xly=8
939[ $#2w+BxaA#dw+k;2,_7BlD9HzXf.5[41$,QRlVI[7!j.ba mK7[;-7t=S!U45D]kn=*RY
8x;/_7Bl'b+pFx.ba iK7*;-7t=rk;XWi:44F-.5/bJ4c,Pr7xi/_7BlVtJz<j_I;9J4FjXC;][7R7q
H*[ /7kS+K;GwrsQ
Hb4/[GsW6Y2\.%#K9rXp:0:S+9-3Td/t:KgzV7gY%KZ
F.6C 6[xut,#k39>!8y#/6k>2h#9wI=AHp7xT-'v0
'6/#+\%2/=#Kc9>!8Y#/6K>2H#9Wi]a+P7X -'10
'6/#+\u2/b#Km9>!8Y#/6K>2H#9Wi=aSP7Xt-[1:L
)/sU= 28(;u#uxut,#/z=p2[o\;:odZ[// %FxLYX+JC9C*7X8
oQcMi77zi:BU2[+G/:k-a[7Ks2k;g6shwf)1-(B#m-'v:l
83w<:xZ2=5i>9 yM7z/'R2*7!-+:X>HYH+4(*\:S7S/C)%-5<<6(iz
37X#y;*_g67\cM/hV$<Ww<k=Lt_3XC/x'[=Qihi1(=X;w k%*5!-49!1+l
83P:\6( 6*w+ZU:*>#4.90aG,<(3e%:$%GP-:b+p0.+M97a5_-XCi25p<8
53'xP:0:s29[8SXYL:i,2/s[=9) -5X#2%[Asz<j)*/U2js-(Ce%+t3j4Y
y )$#(3(sh28(-LEixH9g#k%5I(Y=+u(97a7_vXp/25pJ#2%'AJzBr)*/v0
:=VW+z%v*<# s'/t)73j.90Ag,<(3E%:$%gp-:B+P0.+m97A5_-xcI25P<8
53ixZ2BvJ>9[8SX]+:[j]$9G$f*AHzBjw k *)!r+9!1)$%Gu-'bJ00.+p6
6h97\Cm/F:$+')TC[20G#K%5i(y=+U(97A7_VxP/25Pj#2%'ajZbR)*/V0
:=uW+zu:;x#Y;*_G87R\:b>;30qpH'12JS3(>EV:T3J3:x>jyb+UP*\:0[
'pe9[8sx]+:\6( 6\WP4XU[1%)-[BjP7s/3)/-0x<6(UX'[bqUH'V(=xRQ
;[F28(;+#/Xp:0:S+9V]$9g$F*ahZbJW K *)!R+9!1)$%gU-'Bj00.+P6
6j$#(p):/28(;+#[Xh9G#K%5i(y=+U(97A7_VxP/25Pj#2%'ajZbR)*/V0
:bJW+zu:;x#Y;*_G87R\:b>;30qpH'12JS3(>EV:T3J3:x>jyb+UP*\:0[
'U39-3Td/ :=5)P>uWP4XU[1%)-[BjP7s/3)/-0x<6(UX'[bqUH'V(=xRQ
;'%2<8<[q=Xz2jJlw9V]$9g$F*ahZbJW K *)!R+9!1)$%gU-'Bj00.+P6
6js7959jDz$#(3(S*20G#K%5i(y=+U(97A7_VxP/25Pj#2%'ajZbR)*/V0
:bVW6Y2\.## sU1v(7R\:b>;30qpH'12JS3(>EV:T3J3:x>jyb+UP*\:0[
'UP9-3Td/ :=5)P>uWP4XU[1%)-[BjP7s/3)/-0x<6(UX'[bqUH'V(=xRQ
;'J28(;+e$Xz2j-,m9 +m7Z/'r2*7!-+:x>hyh+4(*\:s7s/c)%-5<<6(IZ
3S7\cM/fH$+')tc$20g#k%5I(Y=+u(97a7_vXp/25pJ#2%'AJzBr)*/v0
:h# sU1E77959jDI$<wW<K=lT_3xc/X'[=qIHI1(=x;W K%*5!-49!1+L
8rw+ZU:;=#y;*_g67r\:B>;30QPh'12js3(>ev:t3j3:X>JYB+up*\:0[
'h:=5)PE3W6Y2\..#4.90Ag,<(3E%:$%gp-:B+P0.+m97A5_-xcI25P<8
5W9[8SX]P:\6( 6*wp4xu[1%)-[bJp7S/3)/-0X<6(ux'[BQuh'v(=Xrq
;SXp:0:S29[8sxyl:I,2/S[=9) -5x#2%[aSZ<J)*/u2JS-(cE%+T3J4y
yf2<8<[Q]xZ2JjLE9v]$9G$f*AHzBjw k *)!r+9!1)$%Gu-'bJ00.+p6
B(8YZW6Y2\.%8
X<03aM%#V;'x78=##+'xD\%#/z=p2[+G/:D$HYJjwI]%*SX#m2uA)p7x 3) :$u\u-[12,#;(3/z
F.6K 390H$4\P:kGZv7GD+%xo.#G*Q=>>$,Q+h$%2,_7m+[Y0
'6/9+'XU77c c9>!8Y#/Z=P2>6\/aC[AD7%((E*j$Uqri=%1L
)/sU=U2WRSw<[xaA#dw%N[ $\/ %FdZ[/#=-/bT57x(xD[)K[
4SqO$h$93W!= 2/b#/6K>2>6*/aC$AD7%((E*j$Uqri] 1L
)/sU]%2/B##+'xa<sGw%\>a$\/T%Fdi[/#=-/bT57x(xD$JK[
4SqO$)$M$2:b 2[+g/:K-A[7kS2K;G6SHWF)1-(b#M-7cIs]8
X9=pu$m'uY
e0mxsW2:==ZvB##+[x78B#VrXxaA#dw%\>a$u*u-u12,.3(M/x48BQP(9CZ7_3+Y
3dg>#3w0072h#3w<[xaA#dwUc[ad6\/A9I*p!Uu:DA1,#3RY
3dg>#-w<[x78B#/z=p2[o\;:odZ[// %FxLYX+JC9C*Ku\ux6
eG.\*p:M4'$4\p:K-A[7gY%KXO..g*q]>>$,q+H$%2,_7M2'y0
'6/92'Xd\%##+'XA<SgW%\>A$\/t%fDI[/#=-/Bt57X(Xd$hk[
+)+%0281\;c6
BU\-g\ 9=zVb##+[X78b#vRxXAa#DW%\>A$U*3-U12,#;(3/X46bq3(9cI7_;+:d[jy=JWi= *7x8
oQcMZ77zu:w[H$93w%N[ $\/ %FdZ[/#=-/bT57x(xD[HK[
4SqO[j$93W!=%2[O\;:KgzV7kS+K;G8SHWF)1-(b#M-7cz5]8
oQcMZS7t! 90J$s2 O9>!8y#9Q=>>.#G*:RA:zi-wI)Y48J7q
7CDO6=#Kc98z77gY%KXA<SgWuC$AD6*/a9i*P!uU:da1,.3ry
3dg>.-w<'x78B#/6k>2[+G/:odi[//T%FxLYX+JC9C*Ku*ux6
eG.\*3:M4'$9-W%\>A$S9/%9mG6SHS2K;23_Jxw'X4y]Xtm7Z
-t#dk:5[ 5[
+KR->]J7T! // $4\W0072[O\;:K-A[[
gp:M4'$S2 o91-P=*Q
%]= $M$2:KgzV7Jxm]Z
F.6dP:B 2[+G/:%dudgpsO'd!/y/+>/x4Y=+Jp9_*77[595AH5[
4Sq%2UV7T! 9>!8Y#;q_c(3,/;q_JjP7[595aE)x8
oQcMZ77z4:k-a[7GD+%xaA#dwUc[ad6\/A9C*7X#=-/bT5[
4SqO[j$9-W!=XAa#DW%n[ $\/ %fDz[/#=-/Bt57X(P/Z
F.6K M90)$4\w%N[ $s2 O9Mg8shs+k;23_jXW'x4Y=r+Y
3dg>.jw<[xu)7z4:k-a[7Ks2k;s..G*:DA+pX#=-/bT5[
4SqO$h$93W0072[+g/:bV2>6*/a:DI[/#=-/Bt57X(3/Z
F.6KTC90H$9-w!=xu)7Ks2k;s..G*:DA)pX#=-/bT5[
4SqO$)$9;W!=XA<SgW%\>A$\/t%fDI[/#M-I123_Jxw'Z
F.6KTp90H$m$2:B%2[+G/:odi[/vs2k;23_jX#'x4Y=r+(6
eG.\*h:b 2wrsW%\>A$9-WuC$ADh/t%fX4y=JWF)1-(b8
oQcMi(7zP:m4'$s9/%90H$\/T%F/g6shwf)1-(B#m-[10
'6/92xXA<SgW%\>A$S9/%9>p[S#9q]>>..g*:ra:ZI-Wi)V0
'6/92x%2[+g/:K-A[7gY%KXA<SgWuC$AD6*/a9i*P!uU:da+L
)/s;-=L$m$2:BU2[o\;:kGZv70$fR=uj7x -2v:$,\((efJl
!< OBxD\%jiO6
BU\-g\ 9mG6SHI:=328Y# Z9[U$Uq4:ra:ZI-Q
u[XzV7\d:=028-#t%fX4y])biA -PCF<30y23_7bLr9hZxF.5[41$1D(XvTjZ<,MB;9h6]8
_:50*Kk)uhi944c,P27xiv_7BlV%-0Tx.5']kn=$R:]0*Kk)BCev$,c,ub;9H\]8
X9=pu$\/T%F/0
Z9$f%472V6BM9ri2>4\!<9$l\9#$;*=*;hJQ
o[XI3rZWuW[M/7DSJ2%:$1')')y;:0[<37Z
-t#dk:%dX,0%=Y
e0mxsW2:%dudgpwUw[m/7*=rBx\Y=#m-[bT07[595AJpX8
;39$L\9#9!=4u$y-szBpgpHjw'y2;Y][5959HK[
DU:]iM/7k#+002V6* cI12g8<$-7T=R1')'QcKNjF_Lvc$sC,35j]Kk[
+)+%02V6Z*.<Z
-/R0/.Xx786h:<98Z$m$2:kGZv7Gy%kz
a77C :kGZv7jX([$0
'6/#s' S$4\W<UXAa#DW%\>A$0Z;y
3dg>#3w0072/J#kC9>!8y#9Q=>>.#G*:RA:zi-wI) :l
)/sU=%2/B#kM9>!8y#/z=p2>6\/Ac[ad7x(3/xLYX+JC6
eG.\\3:K-A[7T! 90j$S2 o9mG8SHS+K;23_Jxw'X4y<+jP6
eG.\*p:b 2/j#/6K>2[+g/:ODI[//t%fXlyx+jc9c*s<8
oQcMi57t! 90)$93w%\>a$\/T%Fdi[/#=-/bT57x(C'z
F.6KTM9>!8y#VrXxu77Gy%kxo..G*Q]>>$,Q+h$%2,_Uq
H*[ /7c qfoUL
!z![k,'$0z;:w[2WRSw%N[ $s9/%6
>IgY##+9>!8Y#+HU$0
'6/;c90h$S9/%9PG+U<=FCPG+bRW'Y2;ybRQ
7CD s7Zp:M4'$Y/>i*il.Y/>cIX;:0[(cIZ
F.6di:B%2WRSwOcK4t,%sOcKJ07[595AJ0[
4Sq%V2/b#/Z=P2V6BM9riGV6BUP9=3T1qUP6
eG.Y<Ko98z$S2 o9PG+U<=FCPG+$-+:56(g_]I(6
6h$M$2:W[27xJW<K=lT_3xc97A7_-xcS25Pj#2%'avZIy)*/J0
Z0\;<92UV[
+KR->]J7D)F5oGXr:W!=Xd\%#/Z=P2[+g/y
3dgh#9P:w[2/=#/z=p2[+G/:w[P$u*(3/x48_-4Y
3dgh#9u:B 2/B#/z=p2[+G/:w[P$u*(Cix48_5q
7CD78+;W<'XUs7gd+%XA<SgW!=-2,.qp(9czybRQ
7CD78+VW<UXd\%#/Z=P2[+g/:W[p$U*(cIX48_5Q
H*[ /7D)F5oGXr:Q
Hb4/[GsW9bPj$4\W0072[O\;:K-A[[
4Sq%%2/=#/6K>2V6BM9riGV6BUP9=3T1qUP6
eG.Y;W<[Xd\%#;q_c(3,/;q_%+$1')')%+L
)/sO:xu57t! 9pg+u<=fcpg+Brw'y2;YBrq
7CD 07ZU:KgzV7DSJd2l36DSJ'V2g8<$-'V0
'6/;)90)$S9/%9PG+U<=FCPG+bRW'Y2;ybRQ
7CD (7Z3:M4'$Y/>i*il.Y/>cIX;:0[(cIZ
F.6dL:B/2WRSwOcK4t,%sOcKJ07[595AJ0[
4Sq%F2/h#/Z=P2V6BM9riGV6BUP9=3T1qUP6
eG.Y<Ko98z$S2 o9PG+U<=FCPG+$-+:56(g_]I(6
6h$M$2:W[27xJW<K=lT_3xc97A7_-xcS25Pj#2%'avZIy)*/J0
Z0\;<92UVb8
X<03aM%##+98Z$m$2:kGZv7Gy%kz
F.6Cc3'/J$4\w<[xaA#dw%\>a$0z;Y
3dg>#3w0072/=#/z=p2[o\;:odZ[// %Fx4YBrwf)1-(B8
oQcMi77t! 90H$s9/%9>P[s#9Q]>>..G*:DAyp7%((e*Jl
!< OBx78[
+KR->]J7*!%C[(,7T! 9>!8Y#/6K>0
'6/#+\ 2/j# sSu+$S2 o9>p[S##+\X48_RWi]ahP[
4SqW[p57\CeUV2wrsW%n[ $S9/%98z<7X -[12,.qUP6
eG.\\p:KgzV7Z3:b 2[O\;:ODz[/VS+K;2,_3+:raj)!]jy
3dg>#-w%N[ $s2 O90H$s2 O9Mg8shiQ=>>$uQP(9I*;!]JY
3dg>#;w%N[ $s2 O90H$s2 O9Mg8shiQ=>>$,Qu>efT$uQP(6
eG.\\4:KgzV7gd+%XU77gd+%XO.#g*>C[AD7X(3/XlyE+,w6
eG.\\h:KgzV7gd+%XU77gd+%XO.#g*>C[AD7%((EFt$Uqp(/Z
F.6K E9>!8y#/6k>2/=#k39Mg8shiQ=>>$,Q3h(*2,_3+Y
3dg>.3w%\>a$9-w<'xa<sGwUc$adH/T%Fx4Y=jwf)%szeUq
7CDO6b#/6K>2[+g/:bu2[+g/:ODI[/VS2K;23_-IH(*2,_3+y
3dg>.;w%\>a$s9/%90)$s9/%9Mg6shiQ]>>$uQP(/xLYX+,W6
eG.\*I:K-A[7Zp:KgzV7ZU:ODI[/VS2K;23_3x#$X4y<8
oQcMi)-#/6k>2[+G/:k-a[7Gy%kxo..G*>c$ad7%((e*J$uQP(6
eG.\*I%9>p[S#/6K>2[+g/:K-A[7kS2K;S..g*:da+$,qpH(*0
'6/922XA<SgW%\>A$9;W%\>A$\/t%f/G6SHWi) :$,qIH(*0
Z0\;<9$M[6\)i6
BU\-g\ 9mG8SHI:=328Y# Z9[U$Uq4:ra:ZI-Q
u[XzV7\d:=028-# %fX4y])biA -PCF<30y23_7bLr9hZxF.5[41$1D(XvTjZ<,MB;9h6]8
_:50*Kk)uhi944c,P27xiv_7BlV%-0Tx.5']kn=$R:]0*Kk)BCev$,c,ub;9H\]8
X9=pu$\/ %F/0
Nf8
<597>t7:VfR.4h#2whB4/[gSk:D23t7[;O*p2x2#2A*xa30[ 9yxaW\K$+%b0
Nf#2a*0($1'):Y2n\4')o98z$M';9YX'w7')uM-($%H):b$7[0/90%2l$.[5'O2'T8Zxy
a90
N7kvoW_7TY#2a*X .9H\89[(J,cK 9_d:7s*99Pj.OZ;q//:$9*#%*XOwY<=HEB0
Z9$f%472V6Z*.<D 562KoC1SP7k#+0026,$K:jah$UqpH$V2g8<$-'1:L
y3w2R4u$\C rBx 562kOc1sp7[595A1G8<$5[Y0
Vb#tF002>4\!<9PG6#-4(9=3T1qrLvT;:0['i;9)4t)bi8uKNj,[B;y0
Z0\;<9PG//w:q%%Ga9DS(:10
N7kvoW_7TY#2a*XO.#g*qM4'$#<2FW0'$dT5:x;($#g*:b$7[0#90%2lZ!+Q
Hb4/[GsWuC[AD6T! C[AD7\;:=g289#/Z=P2,_3+:rahL
\3w+%xZA7\k:kGZv7\/A9C*Kk)m5[b:4c,Pr7xLY])Bfa -pcf<30Y2GyQRlVU-(<,mb;9H6]8
_:50*Kk)uhi1[,cf<3]Y26yQRlVt=S!U4$D][5c,+h']kn=$R:]0*Kk)Bl'b+pTx.5'][p!-.ba mK[
+)+%02>6\/aC.MJ6\/a6
92>MD#TW2 $%H):ODz[/# 9xi7/%#[0YX'D0# %fXU64g):O. T,+xH6
BU\-g\ 9mG8SHS+K;28Y# Z9[U$S2 o9c*07%((E*jL
\3w+%xZA7\k:kGZv7\/A9C*Kk)m5[b:4c,Pr7xLY])Bfa -pcf<30Y2GyQRlV%-0Fx.ba iK[
aW'ba1RK-xPAcK4f3tX9$ y])bL'B+oUF<c;][7r7W2ba1RK)UHI:44f-.BA Mk[
+)+%02>6\/aC[AD[
,WuMP7 4<;:x;($\/t%f/280s=!b72 M'W f226SHW<]8AT7kvo*i-Z!8
X<03aM%#9Q]>>)6*/A9[/$#dw+NxZ/7x(p9I*p!UuY
o 28y# z9[u$#0w2k;2,_7BlD9HzXf.5[41$,QRlVI[7!j.ba mK7[;-7t=5!r<I;][7.7q
$9=Uy])bcEV[,CF<3]y26YqrLvTjZ<aMBA%K4f3!X9$Uy])bL'B+oUF<c;][7r7Q
H*[ /7kS2K;S..g*y
axoWyC[!*p2j/<wUc$ad7\) R8uj7:V;97>t7*/A90ihs<wUmp(3!+XY
e0mxsW2:odi[//T%FxZv7\D:=02[+G/:DA+$,Q+h$%0
>=# o9[!$#ZW%\>A$.g*:da1RKX<3E1K4f3!X9i*kK)=5[B:4C,pR7X;V_7bL'B+NUF.5[$1L
T#$<)Y=rB+45D]kn=*R:]p*Kk)BCev$,c,ub;9H\]#T<)Y=rk-Xp8CknBf.5[41l
!< OBxo..G*Q]>>l
F#90%8\h0dw F22v6bm9RI280S=!B72 m'w F22v6bHC/xu64G):o. t,+Xh6
BU\-g\ 9PG+U<=FCPG+$-+:O8z\9#tF002g8<$-(/:L
y3w2R4u$\C rBx .N5u(9=3t1QR'y2;n=7q
H*[ /7DSJd2l36DSJ2%:L
F#90%8\h0dw F22v6bm9RI280S=!B72 m'w F22v6bup90ihs<wUmp(3!+XY
e0mxsW2:%dX,0%=Q%dX5<#9!=4u$.%!<9=3t1Q,>/z
 77*=rBxoh#$k:%dX5<#$;*=*K1')'a 1l
!< OBx .Nx)fRd .N-4Y
axoWyC[!*p2j/<wUc[adH# 9XI7/%#[0yx'd0# %Fxu64G):o. t,+Xh6
BU\-g\ 9mG8SHIq=>>$#0W+%Xza7\K:da+$,q+H$%0
>=# o9[!$#ZW+-Xz[/#M-7T=,f3x(;][7r7WF)y=R,,pH/]KN=$r:5P*kK)UHI944C,p27Z
$$1z(xVtJz<,mb;9H6]#TO)Y=rk-Xp8CknBf.5[41$.z(xVt=5!r_I;9J4c,Pr7z
-t#dk:odZ[/vs+k;0
Zr\t;M70
.ENDS SN74LVC1G123

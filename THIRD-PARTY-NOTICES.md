# 开源组件与修改说明

SamaLite 的应用层源码以 MIT 许可证提供。它复用了以下开源组件，并非从零重写硬件协议。

## Turing Smart Screen .NET

- 上游：https://github.com/usausa/turing-smart-screen
- 固定提交：da3063b4bf976519dedd79167af609724859b74e
- 许可证：MIT，Copyright (c) 2021 machi_pon
- 原始许可证：ThirdParty/TuringSmartScreen/LICENSE.txt
- 实际复用：RevisionE.cs、RotateOption.cs。
- 本地适配：明确480×480；小屏局部更新使用3字节BGR；暴露HELLO、设备状态与传输计数；验证非空状态与重传请求；增加读/写总时限和位图边界检查；加入停止屏幕媒体播放命令。未加入泵速、风扇或ARGB控制。

## LibreHardwareMonitorLib 0.9.6

- 上游和对应源码：https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6
- 许可证：MPL-2.0；其他内含模块以其第三方声明为准。
- 用途：启用CPU、GPU、内存和主板只读监控，读取封装温度、报告时钟/功耗、DIMM温度、核显引擎和主板转速。使用未修改NuGet二进制。
- PawnIO模块上游：https://github.com/namazso/PawnIO.Modules
- PawnIO独立驱动：https://github.com/namazso/PawnIO （GPL-2.0-or-later，包含IOCTL通信例外，详见其COPYING）。本应用不捆绑安装器，不修改驱动或驱动访问权限。

## 其他NuGet依赖

System.IO.Ports 10.0.3、System.Management 10.0.2 来自 .NET Runtime（MIT）。LibreHardwareMonitor的未修改传递依赖包括DiskInfoToolkit、HidSharp、RAMSPDToolkit-NDD、BlackSharp.Core、Mono.Posix.NETStandard；完整解析版本记录在packages.lock.json，许可证文本随发布目录licenses分发。未启用LHM磁盘或独立风扇控制器采集；NVMe健康日志由Windows只读查询，主板转速不提供调速操作。

## PresentMon console 2.6.0

- 上游固定版本：https://github.com/GameTechDev/PresentMon/tree/v2.6.0
- 官方资产：https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe
- SHA256：b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af
- 未修改的980320字节原生控制台程序，MIT许可。原样LICENSE.txt及THIRD_PARTY.txt随程序位于tools/PresentMon/。不分发GUI、MSI或服务，不安装RTSS。
- 用途：指定目标PID时隐藏采集逐帧应用Present事件，重定向stdout，不生成CSV文件；本程序计算FPS、帧时间和1% Low。关闭时只停止自身唯一命名的ETW会话。

NVIDIA NVML由本机NVIDIA驱动提供，应用只调用读取接口，不分发NVIDIA驱动。

协议交叉核对资料：https://github.com/mathoudebine/turing-smart-screen-python/blob/2b33ab4f00a096916dd6a1174441a53a7ec33b03/library/lcd/lcd_comm_rev_c.py 。Python项目未被捆绑；本项目驱动代码以MIT上游为基础适配。

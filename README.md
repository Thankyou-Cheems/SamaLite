# SamaLite

轻量的 Windows C# 托盘监控程序，为兼容的 SAMA / Turing 圆形 USB 屏幕显示 CPU、GPU 温度与占用、内存使用量和时钟。默认每秒刷新一次，支持局部画面更新。

当前版本：0.1.0。硬件兼容性仅在一套480×480圆屏上验证，不能据此保证所有SAMA产品可用。泵速、风扇调速、ARGB灯光与固件升级均未实现。

## 使用

1. 从托盘彻底退出SAMA，释放屏幕工作串口。
2. 安装 [官方签名的 PawnIO 驱动](https://pawnio.eu/)，用于 LibreHardwareMonitor 的 CPU 硬件访问。验证环境使用 PawnIO 2.2.0；本项目不会自动安装驱动或修改驱动权限。
3. 编译后打开app/SamaLite.exe，接受Windows管理员权限提示。CPU温度通过PawnIO访问硬件，需要管理员权限。
4. 程序位于系统托盘（可能在隐藏图标中）。双击图标打开状态与预览，右键可暂停/恢复、选择1/2/5秒刷新、旋转方向、调整屏幕亮度、重新连接或退出。
5. 验证设备使用旋转270°补偿安装方向，可从托盘菜单调整。默认亮度保持设备现值，只有选择亮度菜单才更改屏幕亮度。

要求Windows x64、.NET 10 Windows Desktop Runtime。发布方式复用系统运行时。程序未设置开机自启，也不会更改原SAMA的自启设置。再次运行SAMA前先退出SamaLite，两个程序不能同时占用屏幕。

设备自动识别1D6B:0121工作串口与1A86:CA21辅助接口，不固定写死COM3。发现多个相同型号串口时会停止选择并提示；仅支持本机验证的480×480家族，不能据此宣布支持全部SAMA产品。

## 数据与资源

设置、最近状态、错误日志保存在%LOCALAPPDATA%/SamaLite。错误日志1 MiB轮转，最多保留当前与一份备份；相同错误至少间隔一分钟记录。status.json定期覆盖，不按秒追加。状态预览关闭后释放截图缓存，后台无需保持配置窗口。

发布程序约6.4 MiB，依赖本机共享.NET运行时和独立PawnIO驱动。30秒后台采样：总CPU约0.18%；私有提交42–48 MiB；工作集90–97 MiB。该测试包含真实CPU温度读取和1 Hz屏幕刷新。较早SAMA样本为CPU0.14%、私有提交261 MiB、工作集52 MiB，两次场景不同，只能作为初步参考。SamaLite的私有提交更低，物理工作集当前更高，尚未完成长时间稳定性对照。

## 实测验证与范围

验证环境：Intel Core i7-14700K、NVIDIA RTX 4080 SUPER、Windows x64、480×480圆屏，工作接口1D6B:0121与辅助接口1A86:CA21。设备固件返回的英寸名称不可靠，不能据此推断实际尺寸。

- HELLO返回chs_5inch.dev1_rom1.88；这个名字不能代表实际屏幕尺寸。
- 480×480四色画面：用户确认颜色正常；补偿方向后用户确认方向与文字正常。
- 全帧BGRA、局部BGR刷新获得needReSend:0状态；10帧测试通过。
- PawnIO安装并以管理员权限运行后，CPU温度、CPU占用、GPU温度、GPU占用、内存读数全部有效。
- 实际托盘进程的暂停停止刷新、恢复、主动重连、正常退出通过；高DPI窗口已做渲染检查。
- 未实际挂起整台电脑或拔插机箱内USB；自动唤醒/断线退避代码存在，但物理断线与睡眠恢复仍待后续观察。

## 开源复用

驱动主体复用MIT项目usausa/turing-smart-screen的RevisionE与RotateOption，固定提交da3063b4bf976519dedd79167af609724859b74e，再适配本机小屏的BGR局部像素和超时/状态检查。CPU温度来自未修改的LibreHardwareMonitorLib 0.9.6，GPU通过本机驱动的NVML只读接口取得。应用、托盘菜单、绘图、重连、设置和状态记录是本次新增代码。

源码和许可证一起交付，详见THIRD-PARTY-NOTICES.md、ThirdParty及app/licenses。Python项目仅作为协议交叉核对资料，没有捆绑Python或复制其驱动源码。

## 编译

使用PowerShell 7运行Build.ps1，需要.NET 10 SDK和NuGet网络连接。packages.lock.json锁定第三方依赖，Build.ps1不会安装驱动或改动开机设置。

```pwsh
./Build.ps1
```

输出目录为app/，生成文件与本地设置不会进入Git。源码入口：Program.cs（启动）、TrayContext.cs（托盘与预览）、MonitorWorker.cs（采集与刷新）、SensorReader.cs（传感器）、DisplayRenderer.cs（480×480绘图）、ScreenLocator.cs（串口发现）。

开发诊断：--handshake --port COM3 --output <路径>；--sensors --output <路径>；--test-image或--live-test会实际更新屏幕；这些模式退出后释放串口。普通双击启动托盘版。

## 卸载

从托盘退出后删除程序目录即可；如不保留设置，可删除%LOCALAPPDATA%/SamaLite。PawnIO是独立、可被其他监控软件共用的系统驱动，需通过Windows“已安装的应用”单独卸载，不会随本程序删除。

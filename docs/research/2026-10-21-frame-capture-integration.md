# PresentMon 后台逐帧采集集成核实

核实日期：2026-10-21。范围：官方固定版本、CLI、源码、许可及后台验证。此研究未下载或运行二进制，未启动窗口，未修改生产代码。

## 推荐

使用官方独立 console **v2.6.0 x64**，随绿色目录放置；按目标 PID 按需启动隐藏子进程，将 CSV stdout 送入有界逐帧窗口。保留 RTSS 为已有用户的可选读数来源，不为 FPS 强制安装 RTSS 或 PresentMon GUI/service。官方将 console、service 和 GUI 明确区分。[官方组件说明](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README.md)

截至核实时，GitHub `releases/latest` 返回稳定版 v2.6.0，发布时间 `2026-09-21T17:30:36Z`。以下是 GitHub Release asset API 的元数据，SHA256 尚未由本研究对下载内容重算：[固定 release](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0)、[官方 API](https://api.github.com/repos/GameTechDev/PresentMon/releases/tags/v2.6.0)。

| 项目 | 值 |
| --- | --- |
| 资产 | `PresentMon-2.6.0-x64.exe` |
| 大小 | `980320` bytes |
| GitHub digest | `sha256:b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af` |
| 下载 | [固定资产](https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe) |
| 不采用的资产 | `PresentMon-2.6.0.msi`，`169672704` bytes |

不要因 console README 的示例仍写 2.3.1 而误认其为最新。发布脚本应固定版本、检查下载文件 SHA256，并拒绝摘要不匹配。

## 最小采集命令与生命周期

```text
PresentMon-2.6.0-x64.exe --process_id <PID> --output_stdout --no_console_stats --v1_metrics --no_track_gpu --no_track_input --no_track_display --session_name SamaLite-<ownerPID>-<unique> --terminate_on_proc_exit
```

`--process_id` 选择唯一目标；三项 `--no_track_*` 关闭此场景不需要的跟踪。不要加 `--no_csv`、`--output_file`、`--multi_csv`、热键或滚动锁指示。`--output_stdout` 本身会关闭控制台统计。`--no_track_display` 在 GPU 跟踪启用时会被忽略，因此必须同时禁 GPU。`--restart_as_admin` 是主动请求提权的参数，不要用于这个已经由管理员托盘程序启动的子进程。[参数和校验源码](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/PresentMon/CommandLine.cpp)

可用的辅助参数有 `--process_name` / `--exclude`（按名过滤，可重复）、`--etl_file`（离线输入）、`--timed <秒> --terminate_after_timed`（有界测试）、`--qpc_time`（追加 QPC）、`--session_name`（自有唯一名称）。`--terminate_on_proc_exit` 在目标退出时结束；进程重建后需以新 PID 重启采集。[官方 CLI 文档](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README-ConsoleApplication.md)

建议 .NET 使用 `UseShellExecute=false`、`CreateNoWindow=true`、重定向 stdout/stderr，异步持续排空两条管道，不通过 shell 启动。启用采集才创建子进程，目标消失时将 UI 置为不可用并清空历史。该启动方式是集成建议，仍须实际验证无前台窗口。

停止时启动另一个同版本隐藏 console：

```text
PresentMon-2.6.0-x64.exe --session_name <exact-owned-name> --terminate_existing_session
```

它仅停止指定会话并退出；不存在或失败时返回 7。不要使用默认名称去清理全局 PresentMon，不要停止其他程序的会话。`--stop_existing_session` 的含义是停止同名会话后**开始新采集**，并非关停。若超时才终止本程序拥有的子进程，再尝试清理其确切会话名；可在启动时清理已记录的本程序孤儿会话名。[主线程实现](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/PresentMon/MainThread.cpp)

外部停止 ETW 会话使 `ProcessTrace` 返回，consumer 调用 `ExitMainThread`；主线程随后停止 session 并等待输出线程。可能有数秒延迟，退出等待不能只留几百毫秒。[consumer 收尾](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/PresentMon/ConsumerThread.cpp)

## CSV 与指标约定

上述参数下 v1 header 为：

```csv
Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents
```

字段必须按 header 名称定位，数值用 invariant culture。`TimeInSeconds` 是事件时间，`msBetweenPresents` 为逐帧 Present 间隔；`msInPresentAPI` 为调用耗时。按 PID 和 swapchain 分组；不能把多个 swapchain 相加当作同一画面的 FPS。忽略无效、非有限、非正帧间隔；切 PID/主 swapchain 时清空窗口。header 和每行 stdout 都显式 `fflush`，无需等文件缓冲满。[v1 输出源码](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/PresentMon/CsvOutput.cpp)

事件会批量到达，不能用 `ReadLine` 到达间隔算 FPS。源码专门处理实时乱序/延迟的 ETW 事件，默认 deferral 上限为两秒。[输出线程](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/PresentMon/OutputThread.cpp)

建议应用明确约定：FPS 为窗口内 `1000 / mean(frame_ms)`；帧时间显示相同窗口平均值或最新平滑值；1% Low 为最慢 `ceil(N × 0.01)` 帧的平均帧时间取倒数，历史 30 秒且至少 100 帧才显示。该约定是本应用定义，CLI 不直接输出 1% Low；`1000 / P99(frame_ms)` 是另一种定义，不能混称同一算法。禁止从 1 Hz 轮询的 FPS 或平滑帧时间重建 1% Low。

关闭 display 跟踪时应描述为**应用 Present FPS**，不宣称是显示器实际显示 FPS、插帧后的显示 FPS或帧生成识别结果。没有采样时显示不可用，不用屏幕刷新率或渲染定时器虚构游戏 FPS。v1 兼容 schema 不支持新 frame-type 字段。[指标和版本定义](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README-ConsoleApplication.md)

注意 pipe 编码：`Console.cpp` 仅对 console/disk 将 CRT 切换到 UTF-16；pipe 不走该分支。数值与 header 为 ASCII 安全，不要强制把重定向 pipe 当作 UTF-16。应用名可能受代码页影响，不应作为唯一匹配键。警告/错误写 stderr。[控制台 I/O 源码](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/PresentMon/Console.cpp)

## 绿色分发与许可

console Release 使用静态 CRT (`MultiThreaded`)，显式系统依赖是 advapi32、shell32、tdh、user32；无需 GUI、CEF 或服务安装。源码从自身二进制读取嵌入 manifest，并创建 message-only HWND 处理退出/热键，没有普通主窗口。[console 工程](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/PresentMon/PresentMon.vcxproj)、[主线程](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/PresentMon/MainThread.cpp)

默认日志 channel 为空；stdout 模式不创建 CSV 文件。[日志实现](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/PresentMon/LogSetup.cpp)、[CSV 输出](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/PresentMon/CsvOutput.cpp)

这些是源码证据，支持“native standalone、无服务、不需自解压”的选择，但没有替代发布前文件写入/进程/服务实测。Windows 自身 Prefetch、崩溃转储等也不等于应用主动自解压。发布核验需区分二者。

PresentMon 的 MIT 允许再分发，要求附带版权和许可。随包保留固定 tag 原样 `LICENSE.txt` 与 `THIRD_PARTY.txt`，标明版本、来源、摘要。第三方文件包括 ADL/NVAPI、CEF BSD 条款、DirectX error library、backward-cpp 等；它覆盖仓库多组件，不能据其包含 CEF 就声称 standalone console 内含 Chromium。BSD 条款要求二进制分发材料保留相关 notice，且不得冒用贡献者背书。[MIT](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/LICENSE.txt)、[第三方 notice](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/THIRD_PARTY.txt)

## RTSS 是否能更轻

RTSS 开发者确认共享内存含每应用条目，适合按 PID 读取已有用户的 FPS/帧时间。[Unwinder 对共享内存应用数组的说明](https://forums.guru3d.com/threads/using-appflags-in-shared-memory.437294/)

但是本研究未取得该机器已安装 RTSS 的官方 SDK 头文件；常见安装路径未找到 RTSS。未核实可消费的完整逐帧 ring-buffer ABI、版本、读写同步以及现成 1% Low 字段。现有平滑/轮询读数不够重建真实 1% Low；开发者也明确警告，Afterburner 日志中的轮询帧时间不是即时样本，不能用来计算低分位值。[Unwinder 的原始说明](https://forums.guru3d.com/threads/help-using-msi-afterbuner.426488/)

因此保留可选 RTSS 兼容接口有价值，但不能据“共享内存无需新进程”就断言总体更轻，更不能把稀疏样本算出的数字标作逐帧 1% Low。需要对已运行 RTSS 与按需 PresentMon 的总 CPU/working set/数据完整性做同场景实测。RTSS 官方说明也区分传统 hooking 与 PresentMon 来源，并非所有来源都等价。[官方 RTSS 发布说明](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/)

## 全后台验证的可行边界

官方 console 的主要回归路径是 `Tests/Gold/test_case_0.etl` 等录制 ETL 与 golden CSV。以 `--etl_file <fixture> --output_stdout --v1_metrics` 配合相同禁跟踪参数可验证官方程序、管道解析、逐帧统计与自然退出，完全不需前台游戏；黄金 CSV 也可直接用于统计单元测试。这证明离线链路，不能证明当前机器实时 ETW、权限或显卡驱动链路。[测试说明](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/Tests/README.md)、[ETL 回归实现](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/Tests/GoldEtlCsvTests.cpp)

官方 `PacedFramePlayback` 是 SDK 消费/回放查询程序，并非无窗口 DXGI 帧生成器；API2 测试还涉及服务/控制管道。树中有 `Tools/PresentBench.exe`，但未找到与其对应的受支持 headless CLI 文档，不能承诺运行它不显示窗口。[PacedFramePlayback](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/IntelPresentMon/SampleClient/PacedFramePlayback.cpp)、[测试进程封装](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/IntelPresentMon/PresentMonAPI2Tests/TestProcess.h)、[Tools](https://github.com/GameTechDev/PresentMon/tree/v2.6.0/Tools)

可自行做永不 `ShowWindow` 的隐藏 HWND/DXGI present 测试，但只能作为当前驱动上的受控 live smoke test。遮挡状态可改变 `Present` 返回值；Microsoft 指出 `DXGI_STATUS_OCCLUDED` 表示内容不可见，flip-model 又有不同约束。因此记录不到隐藏测试帧不能单独判定采集器损坏，也不能把隐藏应用的帧视为真实显示性能。应记录生成 PID、Present 次数/HRESULT、采集到的同 PID 行数，并确保自有 ETW 会话退出清理。[DXGI status](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-status)、[Present](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-present)

验收应分别报告：官方 ETL 解析通过；隐藏受控 live 帧采集通过/未捕获；真实游戏捕获尚未验证。不要将这三者合并宣称“游戏 FPS 已完全验证”。

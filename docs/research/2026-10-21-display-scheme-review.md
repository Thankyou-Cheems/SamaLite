# SamaLite 与主流硬件小屏方案：功能与显示内容评估

研究日期：2026-10-21。评估对象为本项目 v0.5.0、本机已验证的 480×480 串口 USB 圆屏。依据为当前源码、项目验证记录、产品官方文档、开源项目与社区原始讨论；没有安装或实测竞品，没有改动生产代码、显示页面或运行实例。下面的优先级是产品判断，不是社区功能投票结果。

## 判断

作为这台电脑的常驻温度/负载面板，当前基础监控覆盖已经比较完整；作为成熟的通用多功能显示软件，还缺少传感器覆盖、配置能力和完整的稳定性验收。继续扩展有价值，但应先补充能解释散热与性能状态的信息，同时保留固定位置的大数字。

不建议把竞品的全部小组件塞入同一主屏。我们采用的 r216 安全圆约有 14.7 万像素，约为完整 480×480 矩形的 64%；大屏矩形模板不能直接缩小后获得同样的可读性。这是根据当前绘图安全半径计算的布局约束，并非行业标准。

## 代表方案与值得借鉴的能力

| 方案 | 官方能力或一手证据 | 对本项目的启示与边界 |
|---|---|---|
| AIDA64 SensorPanel / LCD | 提供自定义图表、仪表、图像与传感器文本；外接 LCD 支持是另一条输出路径，官方列出 Turing 等多种设备。[SensorPanel](https://www.aida64.com/products/features/sensorpanel)、[外接屏支持](https://www.aida64.com/products/features/external-display-support) | 值得学传感器选择、预设及布局导入导出。官方列出 Turing 不等于我们的具体 SAMA 圆屏已可直接兼容，仍须核对协议与型号。 |
| HWiNFO + Rainmeter | Rainmeter 官方给出 HWiNFO 将选定传感器输出到注册表的接法；这条接法不要求启用共享内存。HWiNFO 目前的非 Pro 共享内存支持有 12 小时限制。[Rainmeter 文档](https://docs.rainmeter.net/tips/hwinfo/)、[HWiNFO 授权表](https://www.hwinfo.com/licenses/) | 适合复用已有采集器和皮肤，但增加常驻依赖及配置成本。注册表输出与共享内存不能混称，也不宜要求用户另开一整套软件来补普通读数。 |
| turing-smart-screen-python | 项目提供多种 USB 屏抽象、主题 YAML、主题编辑与自定义数据源，支持 CPU/GPU、内存、磁盘等指标；项目特别说明外观相似的屏幕可能使用不同协议。[项目 README](https://github.com/mathoudebine/turing-smart-screen-python) | 与我们的硬件类型最接近。主题/自定义槽位值得借鉴，型号兼容性和稳定传输比照搬主题数量更关键。其 Python 方案不能仅凭语言推断资源消耗。 |
| CLU VIEW / 社区面板工具 | 作者和用户围绕拖拽编辑、模板、水冷传感器讨论，并有 USB 屏支持的后续作者更新。[初始讨论](https://www.reddit.com/r/pcmods/comments/1rdggey/dedicated_sensor_panel_app/)、[作者后续](https://www.reddit.com/r/pcmods/comments/1vhmwon/clu_view_sensor_panel_6_months_on/) | 改善配置成本是一个独立价值。作者功能声明不等于独立实测，也不能直接宣称比现有软件轻。 |
| Corsair XENEON EDGE + iCUE | 官方列出传感器、媒体、网页、图片/视频、时钟、天气等组件；EDGE 是 14.5 英寸、2560×720 的触控显示器。[组件说明](https://www.corsair.com/us/cs/explorer/diy-builder/accessories/xeneon-edge-widgets-explained/)、[设备用途](https://www.corsair.com/ca/en/explorer/gamer/monitors/what-can-i-use-the-xeneon-edge-for/) | 大尺寸与触控让音量、快捷启动、通知等有使用价值。我们目前验证的是显示输出，没有验证触控或按键输入，不能把它视为同类交互硬件。 |
| GOverlay / SimHub | GOverlay 提供主题、传感器和插件；SimHub 面向赛车模拟的仪表、显示和实体设备联动。[GOverlay](https://www.goverlay.com/)、[SimHub](https://www.simhubdash.com/) | 借鉴数据源与布局分离、按用途组织页面；赛车挡位、圈速等只对相应使用场景有意义，不是本项目当前缺失的通用监控功能。 |

AIDA64 的 FPS 能力也要按当前文档判断：官方写明 Extreme 8.30 起对支持的 DX11/DX12 游戏有内置 FPS 显示，不能一概说它必须另装 RTSS。[官方 FPS 说明](https://www.aida64.com/user-manual/sensorpanel-core-template/displaying-fps)

## 社区讨论说明了什么

详细一手来源与样本边界见 [社区证据笔记](2026-10-21-community-display-evidence.md)。AIDA64 的长期主题主帖有上万条回复，说明主题生态活跃；本次查到的 2026 Reddit 帖多为几十票，不能据此给出全网软件热度排名。[AIDA64 主题主帖](https://forums.aida64.com/topic/13296-share-your-sensorpanels/)

公开讨论同时存在三类偏好：只看温度/占用；希望更容易编辑布局与接入水冷传感器；希望触控、天气、媒体及智能家居整合。还有用户反感常驻软件的资源消耗与界面分心。它们支持“默认简洁、扩展可选”，而不是一个统一的全功能首页。[2026 用户讨论](https://www.reddit.com/r/pcmasterrace/comments/1usrkot/do_you_like_sensor_panels/)、[CLU VIEW 初始讨论](https://www.reddit.com/r/pcmods/comments/1rdggey/dedicated_sensor_panel_app/)

与本机尤其相关的是 SAMA 2.8 圆屏的接口识别问题单：报告中的硬件/平台不等于本机完整配置，但足以说明同尺寸和相近 USB 标识不能代替协议验证。[Turing #1022](https://github.com/mathoudebine/turing-smart-screen-python/issues/1022)

其他项目个别用户报告的内存或刷新问题，不是同平台同设置测试；本报告不据此声称 SamaLite 比 AIDA64、iCUE 或开源方案更轻、更快、更可靠。

## 当前做到的与真实缺口

本节来自仓库 README、SensorReader.cs、DisplayRenderer.cs、CompactDashboard.cs、Telemetry.cs、MonitorWorker.cs 的当前实现。

| 能力 | 当前状态 | 判断 |
|---|---|---|
| CPU/GPU 温度、负载、功耗，RAM/VRAM 容量与占比 | 圆屏与桌面已有；时钟、视频引擎等在桌面详情 | 基础读数已经够用，继续加同类普通数字的收益下降。 |
| 两条 DIMM 温度、核显最忙引擎 | 已有，支持缺失值与有效零值 | 相比只有 CPU/GPU 的基础模板更细，但日常是否常驻应可配置。 |
| 负载/容量条、120 秒趋势、大数字 | 已有，极值离屏验证在安全圆内 | 保留；不需要为了像成熟软件而再加多组装饰性环形仪表。 |
| 逐字段成功时间、过期/失败显示、暂停冻结、重连 | 有实现与离线/本机运行验证 | 是重要基础，但尚未完成实际睡眠/物理拔插、长时间与完整 DPI 矩阵验证。 |
| CPU 风扇/泵 RPM、水温/流量、主板与 NVMe 温度 | 目前没有相应采集；主板、存储、控制器采集未启用 | 最值得评估的数据缺口之一，先检查硬件确实暴露什么。已有 GPU 风扇百分比不能写成 RPM。 |
| 热降频或性能限制原因、GPU 热点/显存结温 | 未提供；不能从负载、核心温度或普通时钟直接推断 | 比再加报告时钟更能解释状态，但支持度必须逐项验证。 |
| 游戏 FPS、帧时间、1% Low | 无游戏帧采集 | 游戏使用时价值高；应作为按需采集的独立模块。 |
| 网速、磁盘活动、系统盘剩余空间 | 未提供 | 对下载、拷贝、更新和工作站负载有帮助，可放可选页/槽。 |
| 预设、指标槽选择、配置导入导出 | 目前主要是固定布局与有限设置 | 这是比任意拖拽编辑器更适合先补的产品能力。 |

## 新内容的优先级

1. **先做可选槽位与少量布局预设。** 保留 CPU/GPU 温度与负载的主位置；次要区可选 DIMM 温度、核显、泵速、网速等。用户明确要求的 DIMM 与核显不应被擅自删除。两个 CPU/GPU 主区、RAM/VRAM 区加少量辅助信息即可，不用填满每一处像素。
2. **补散热与异常状态。** 有真实数据时显示 CPU 风扇/泵 RPM、系统 NVMe 温度与剩余容量；水温/流量需要实际传感器。转速为 0 可能是停转策略，功耗限制也可能是正常 Boost 行为，不能统一画成故障。GPU 限频/事件原因可评估 NVML 公开接口，但返回“不支持”时必须保持未知。[NVML 查询接口](https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlDeviceQueries.html)
3. **游戏场景增加 FPS 与帧时间页。** 以现有可信帧数据接口或 PresentMon/ETW 为候选，不能由 GPU 占用猜 FPS。若加入 1% Low，需定义目标进程、采样窗口、预热及帧类型口径。PresentMon 提供库、命令行和服务等不同接入方式，不必为了一个指标默认启动整套 GUI。[PresentMon 项目](https://github.com/GameTechDev/PresentMon)
4. **网络和磁盘做可选扩展。** 只统计选定活动网卡，避免虚拟网卡/VPN/回环重复计算；分清物理盘、分区容量和读写速率。磁盘状态可慢采样，基础主指标保持独立。若主要玩游戏，这一项应排在游戏帧表现之后。
5. **GPU 热点/显存结温、风扇 RPM 以支持度决定。** RTX 4080 SUPER 的“核心温度”不能代替热点或显存结温；先验证本机接口和硬件。完整 CPU 每核、编码/解码引擎、电压、内存时序等默认留在桌面详情。

天气、新闻、媒体封面、GIF、网页、触控快捷控制不作为下一版默认功能。这里的理由是本项目的有限屏幕、已知交互能力及用户对轻量的要求，不是在判断这些功能对所有人都无价值。时钟/日期如需加入，可作为低成本可选信息，但必须与“采样时间”区分。

## 显示与资源策略建议

- 默认概览维持固定位置；可增设游戏页、散热页，由托盘主动选择，默认不周期轮播。异常优先在原位置显示状态，不突然换页隐藏主指标。
- 游戏页可突出 FPS、帧时间，同时保留 CPU/GPU 温度与显存余量；散热页突出 CPU/GPU/DIMM/SSD 温度与有效泵/风扇转速。
- 新传感器按需启用；布局隐藏并不天然等于采集停止。快速帧事件采集与屏幕每秒刷新解耦；SMART、水温、天气等不应跟随每秒主循环全部重查。
- 不为基础监控引入浏览器内核、动画背景或另一套默认常驻采集器。若用户已有 HWiNFO/RTSS/PresentMon，可考虑可选接入，但要计入总进程资源和授权条件。
- 继续测量工作集、专用工作集、私有提交、CPU、发送时间和失败率；比较同一运行阶段。此前约 140 MiB 的启动短测与约 28 分钟后总工作集约 65 MiB 不是同一统计时段，不能包装成“已稳定降低一半”。
- 再增加显示内容之前/同时，补足睡眠恢复、真实断连、重启与较长运行的验收。已有一次发送成功、模拟测试和程序自启，不等于所有这些场景已完成。

建议下一阶段的范围是：**可选槽/预设 + 可验证的散热状态**；游戏用户再优先加 **FPS/帧时间**，之后补网络/存储页。当前适合作为专用小屏工具继续打磨，尚不能称为 AIDA64 等完整方案的替代品。

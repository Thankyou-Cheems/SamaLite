using System.Net.NetworkInformation;

namespace SamaLite;

internal sealed class LayoutForm : Form
{
    private sealed record Option(string Id, string Name) { public override string ToString() => Name; }
    private readonly ComboBox[] slots = new ComboBox[12];
    private readonly ComboBox preset = Box(), fan = Box(), pump = Box(), nvme = Box(), network = Box(), gameDisplay = Box();
    private readonly CheckBox rings = new() { Text = "上半区双环（左CPU/RAM、右GPU/VRAM，顶端100%）", AutoSize = true };
    private readonly CheckBox frames = new() { Text = "按需后台采集应用 Present 帧事件", AutoSize = true };
    private readonly TextBox process = new() { Width = 280, BackColor = Ui.Raised, ForeColor = Ui.Text };
    private readonly PictureBox preview = new() { Size = new(300, 300), SizeMode = PictureBoxSizeMode.Zoom };
    private static readonly string[] Names = ["FPS", "帧时间", "1% Low", "CPU风扇", "水泵", "NVMe温度", "下载", "上传", "磁盘读取", "磁盘写入", "磁盘活动", "最高DIMM温度", "核显占用", "CPU功耗", "GPU功耗"];
    private static ComboBox Box() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, BackColor = Ui.Raised, ForeColor = Ui.Text, FlatStyle = FlatStyle.Flat };
    public LayoutForm(Settings settings, HardwareDetails? details, Metrics? metrics, Action<Action<Settings>> update)
    {
        var current = settings.Capture();
        Text = "屏幕布局与数据源"; Font = new("Microsoft YaHei UI", 10); BackColor = Ui.Background; ForeColor = Ui.Text;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96); ClientSize = Ui.ScaleSize(this, 750, 720); MinimumSize = Ui.ScaleSize(this, 520, 400);
        StartPosition = FormStartPosition.CenterParent;
        var scroll = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(18) };
        Controls.Add(scroll);
        void Row(string label, Control control)
        {
            var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0, 0, 0, 7) };
            line.Controls.Add(new Label { Text = label, Width = 160, Height = 30, TextAlign = ContentAlignment.MiddleLeft }); line.Controls.Add(control); scroll.Controls.Add(line);
        }
        preset.Items.AddRange([new Option("dense", "硬件总览"), new Option("game", "游戏"), new Option("cooling", "散热")]);
        preset.SelectedIndex = current.Layout.Preset == "game" ? 1 : current.Layout.Preset == "cooling" ? 2 : 0; Row("预设（覆盖12个槽位）", preset);
        rings.Checked = current.Layout.Rings; scroll.Controls.Add(rings);
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = Box(); slots[i].Items.AddRange(PanelLayout.Choices.Select((id, index) => new Option(id, Names[index])).ToArray());
            slots[i].SelectedIndex = Array.IndexOf(PanelLayout.Choices, current.Layout.Slots[i]); Row($"{i / 3 + 1}行 · {i % 3 + 1}列", slots[i]);
        }
        void Sources(ComboBox box, string saved, IEnumerable<Option> options)
        {
            box.Items.AddRange(options.ToArray());
            if (saved.Length > 0 && !box.Items.Cast<Option>().Any(o => o.Id == saved)) box.Items.Add(new Option(saved, "已保存，当前未枚举：" + saved));
            box.SelectedIndex = Math.Max(0, box.Items.Cast<Option>().ToList().FindIndex(o => o.Id == saved));
        }
        var fans = details?.Devices.Where(d => d.Category == "cooling").SelectMany(d => d.Metrics.Select(m => new Option(m.Id, m.Label + " · " + DisplayRenderer.Format(m.Reading.Value) + " RPM · " + m.Id))).ToArray() ?? [];
        Sources(fan, current.CpuFanSensorId, new[] { new Option("", "自动匹配明确命名的 CPU Fan") }.Concat(fans));
        Sources(pump, current.PumpSensorId, new[] { new Option("", "自动匹配明确命名的 Pump") }.Concat(fans));
        Row("CPU风扇接口", fan); Row("水泵接口", pump);
        Sources(nvme, current.NvmeDeviceId, new[] { new Option("", "自动选择首个NVMe") }.Concat(details?.Devices.Where(d => d.Id.StartsWith("nvme/", StringComparison.Ordinal)).Select(d => new Option(d.Id, d.Name + " · " + d.Id)) ?? [])); Row("温度对应的NVMe", nvme);
        IEnumerable<Option> adapters = [];
        try { adapters = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback).Select(n => new Option(n.Id, n.Name + " · " + n.Description)).ToArray(); }
        catch (NetworkInformationException) { }
        Sources(network, current.NetworkAdapterId, new[] { new Option("", "自动选择已连接、有网关的网卡") }.Concat(adapters)); Row("网速对应的网卡", network);
        frames.Checked = current.FramesEnabled; scroll.Controls.Add(frames); process.Text = current.GameProcess; Row("进程名（空白自动）", process);
        gameDisplay.Items.AddRange([new Option("auto", "自动：全屏/指定程序＋真实帧事件确认"), new Option("normal", "固定硬件总览"), new Option("game", "固定游戏大字模式")]);
        gameDisplay.SelectedIndex = current.GameDisplay == "normal" ? 1 : current.GameDisplay == "game" ? 2 : 0; Row("游戏显示模式", gameDisplay);
        scroll.Controls.Add(new Label { Text = "帧采集跟随前台程序，排除常见桌面工具及未全屏浏览器；可指定 game.exe。\n自动游戏布局需全屏或指定进程，且持续3秒取得真实帧；失去目标8秒后恢复。\n窗口化游戏请指定进程名；这是全屏渲染检测，不是完整游戏库识别。\n蓝=CPU/散热，紫=GPU，绿=内存/网络/帧，橙=磁盘。— 表示未映射、不可用或过期。\n通用 Fan # 请按主板/BIOS确认。磁盘吞吐为所有物理盘合计。\n帧率是应用Present FPS；1% Low需100帧/5秒，窗口最多60秒。预览使用最近实机读数。", AutoSize = true, MaximumSize = new(660, 0), ForeColor = Ui.Muted, Margin = new(0, 14, 0, 14) });
        scroll.Controls.Add(preview);
        PanelLayout Layout() => new() { Preset = ((Option)preset.SelectedItem!).Id, Rings = rings.Checked, Slots = slots.Select(s => ((Option)s.SelectedItem!).Id).ToArray() };
        SettingsSnapshot Capture() => current with { Layout = Layout(), CpuFanSensorId = ((Option)fan.SelectedItem!).Id, PumpSensorId = ((Option)pump.SelectedItem!).Id, NvmeDeviceId = ((Option)nvme.SelectedItem!).Id, FramesEnabled = frames.Checked, GameDisplay = ((Option)gameDisplay.SelectedItem!).Id };
        void RefreshPreview()
        {
            if (metrics is null) return;
            using var renderer = new DisplayRenderer(); renderer.Draw(metrics, current.RefreshSeconds, configuration: Capture());
            var previous = preview.Image; preview.Image = new Bitmap(renderer.Canvas); previous?.Dispose();
        }
        preset.SelectedIndexChanged += (_, _) => { var chosen = PanelLayout.For(((Option)preset.SelectedItem!).Id); for (var i = 0; i < 12; i++) slots[i].SelectedIndex = Array.IndexOf(PanelLayout.Choices, chosen.Slots[i]); RefreshPreview(); };
        foreach (var box in slots.Concat(new[] { fan, pump, nvme, gameDisplay })) box.SelectedIndexChanged += (_, _) => RefreshPreview();
        rings.CheckedChanged += (_, _) => RefreshPreview();
        frames.CheckedChanged += (_, _) => RefreshPreview();
        scroll.Controls.Add(Ui.Button("保存布局与数据源", () =>
        {
            update(s => { s.Layout = Layout(); s.CpuFanSensorId = ((Option)fan.SelectedItem!).Id; s.PumpSensorId = ((Option)pump.SelectedItem!).Id;
                s.NvmeDeviceId = ((Option)nvme.SelectedItem!).Id; s.NetworkAdapterId = ((Option)network.SelectedItem!).Id; s.FramesEnabled = frames.Checked; s.GameProcess = process.Text.Trim(); s.GameDisplay = ((Option)gameDisplay.SelectedItem!).Id; });
            DialogResult = DialogResult.OK; Close();
        }));
        RefreshPreview();
    }
    protected override void Dispose(bool disposing) { if (disposing) preview.Image?.Dispose(); base.Dispose(disposing); }
}

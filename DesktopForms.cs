using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SamaLite;

internal static class Ui
{
    public static string CompactStatus(MonitorStatus status, SettingsSnapshot config) => status.PauseMarkerFailed ? "⚠ 暂停标记未送达"
        : status.Phase != ConnectionPhase.Connected ? "● " + status.Connection
        : config.Paused ? "Ⅱ 输出暂停" : (status.Metrics is Metrics metrics && DisplayRenderer.GameLayout(metrics, config) ? "● 游戏 · " : "● ") + $"{config.RefreshSeconds}s";
    public static Size ScaleSize(Control control, int width, int height) => new((int)Math.Round(width * control.DeviceDpi / 96d), (int)Math.Round(height * control.DeviceDpi / 96d));
    public static readonly Color Background = ColorTranslator.FromHtml("#101318");
    public static readonly Color Surface = ColorTranslator.FromHtml("#171C24");
    public static readonly Color Raised = ColorTranslator.FromHtml("#202733");
    public static readonly Color Text = ColorTranslator.FromHtml("#F2F5F9");
    public static readonly Color Muted = ColorTranslator.FromHtml("#B6C1D0");
    public static readonly Color Accent = ColorTranslator.FromHtml("#91D8C5");
    public static readonly Color Cpu = ColorTranslator.FromHtml("#96BAFF");
    public static readonly Color Gpu = ColorTranslator.FromHtml("#C7B6FF");
    public static readonly Color Warning = ColorTranslator.FromHtml("#F4C17A");
    public static Label Label(string text = "", float size = 11.25f, Color? color = null) => new()
    {
        Text = text, AutoSize = true, ForeColor = color ?? Text, Font = new("Microsoft YaHei UI", size), Margin = new(0, 0, 0, 10), UseMnemonic = false
    };
    public static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new(0, 40), FlatStyle = FlatStyle.Flat, BackColor = Raised,
            ForeColor = Text, Padding = new(8, 4, 8, 4), Margin = new(0, 0, 0, 10), AccessibleName = text };
        button.MinimumSize = ScaleSize(button, 0, 40);
        button.FlatAppearance.BorderColor = Color.FromArgb(53, 65, 83);
        button.Click += (_, _) => action(); return button;
    }
    public static string Reading(Metrics? metrics, MetricId id, int interval, string unit)
    {
        if (metrics is null) return "尚无读数";
        var r = metrics[id];
        if (r.Failure is not null) return r.Failure;
        if (r.SampledAt is null) return "尚无读数";
        var age = r.Age(Stopwatch.GetTimestamp());
        return r.IsFresh(Stopwatch.GetTimestamp(), interval) ? $"采样 {r.SampledAt:HH:mm:ss} · {age:0}秒前"
            : $"旧值 {DisplayRenderer.Format(r.Value)}{unit} · {age:0}秒前";
    }
    public static string StatusText(MonitorStatus status, SettingsSnapshot config)
    {
        var text = status.Connection;
        if (status.NextRetryAt is DateTimeOffset retry) text += $" · {Math.Max(0, (retry - DateTimeOffset.Now).TotalSeconds):0}秒后重试";
        text += status.Paused ? "\n输出已暂停；桌面读数仍在更新" : $" · 每{config.RefreshSeconds}秒输出";
        if (status.PauseMarkerFailed) text += "\n暂停标记未能发送；屏幕可能保留旧画面。";
        if (status.Error is not null) text += "\n" + status.Error;
        if (status.SettingsError is not null) text += "\n设置保存失败：" + status.SettingsError;
        return text;
    }
}

// Each stack measures wrapped text at its actual width. No fixed-height warning rows.
internal class VerticalStack : FlowLayoutPanel
{
    public VerticalStack(bool card = false)
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; FlowDirection = FlowDirection.TopDown; WrapContents = false;
        Margin = new(0, 0, 0, 16); Padding = card ? new(20) : Padding.Empty; BackColor = card ? Ui.Surface : Color.Transparent;
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        var width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
        foreach (Control control in Controls)
        {
            var available = Math.Max(1, width - control.Margin.Horizontal);
            if (control is Label) control.MaximumSize = new(available, 0);
            else control.Width = available;
        }
        base.OnLayout(e);
    }
}

internal sealed class TrendControl : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Metrics? Metrics { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Gpu { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Temperature { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal int Interval { get; set; } = 1;
    public TrendControl() { DoubleBuffered = true; Height = 40; Margin = new(0, 0, 0, 10); AccessibleName = "最近120秒趋势"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Metrics is null || Width < 4 || Height < 4) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DisplayRenderer.DrawTrend(e.Graphics, new(2, 3, Width - 4, Height - 8), Metrics.History,
            p => Temperature ? (Gpu ? p.GpuTemperature : p.CpuTemperature) : (Gpu ? p.GpuUsage : p.CpuUsage),
            Gpu ? Ui.Gpu : Ui.Cpu, Stopwatch.GetTimestamp(), Interval, Temperature ? 125 : 100);
    }
}

internal sealed class MetricCard : VerticalStack
{
    private readonly bool gpu;
    private readonly Label load = Ui.Label("—", 31.5f);
    private readonly Label heat = Ui.Label("—", 31.5f);
    private readonly Label freshness = Ui.Label("等待首次采样", 9.75f, Ui.Muted);
    private readonly Label peak = Ui.Label("", 9.75f, Ui.Muted);
    private readonly Label extra = Ui.Label("", 10.5f, Ui.Muted);
    private readonly TrendControl trend;
    private readonly TrendControl temperatures;
    public MetricCard(bool gpu) : base(true)
    {
        this.gpu = gpu;
        Controls.Add(Ui.Label(gpu ? "GPU" : "CPU", 12, gpu ? Ui.Gpu : Ui.Cpu));
        var numbers = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        foreach (var (number, unit) in new[] { (load, "%"), (heat, "°C") })
        {
            number.Font = new("Segoe UI", 31.5f);
            var pair = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0, 0, 16, 0) };
            var unitLabel = Ui.Label(unit, 15, Ui.Muted); unitLabel.Margin = new(0, 24, 0, 0);
            pair.Controls.Add(number); pair.Controls.Add(unitLabel); numbers.Controls.Add(pair);
        }
        Controls.Add(numbers);
        trend = new() { Gpu = gpu }; Controls.Add(trend);
        Controls.Add(Ui.Label("负载 · 最近120秒 · 固定0–100%", 9.75f, Ui.Muted));
        Controls.Add(peak); Controls.Add(extra); Controls.Add(freshness);
        temperatures = new() { Gpu = gpu, Temperature = true, Visible = false };
        Controls.Add(Ui.Button("温度变化 ▾", () => temperatures.Visible = !temperatures.Visible));
        Controls.Add(temperatures);
    }
    public void UpdateMetrics(Metrics? metrics, int interval)
    {
        var now = Stopwatch.GetTimestamp();
        var usageId = gpu ? MetricId.GpuUsage : MetricId.CpuUsage;
        var tempId = gpu ? MetricId.GpuTemperature : MetricId.CpuTemperature;
        load.Text = DisplayRenderer.Format(metrics?.Fresh(usageId, interval, now));
        heat.Text = DisplayRenderer.Format(metrics?.Fresh(tempId, interval, now));
        freshness.Text = "负载：" + Ui.Reading(metrics, usageId, interval, "%") + "\n温度：" + Ui.Reading(metrics, tempId, interval, "°C");
        var history = metrics?.History.Where(p => (now - p.Tick) / (double)Stopwatch.Frequency <= 120).ToArray() ?? [];
        var maximum = history.Select(p => gpu ? p.GpuTemperature : p.CpuTemperature).Max();
        var coverage = history.Length == 0 ? 0 : Math.Clamp((now - history[0].Tick) / (double)Stopwatch.Frequency, 0, 120);
        peak.Text = $"温度采样峰值 {DisplayRenderer.Format(maximum)}°C · 覆盖{coverage:0}秒";
        extra.Text = gpu ? HardwareSummary.Nvidia(metrics?.Details, now) : HardwareSummary.Cpu(metrics?.Details, now);
        trend.Metrics = temperatures.Metrics = metrics; trend.Interval = temperatures.Interval = interval;
        trend.Invalidate(); if (temperatures.Visible) temperatures.Invalidate();
    }
}

internal sealed class DisplayControls : VerticalStack
{
    private readonly ComboBox brightness = new() { DropDownStyle = ComboBoxStyle.DropDownList, IntegralHeight = true, FlatStyle = FlatStyle.Flat, BackColor = Ui.Raised, ForeColor = Ui.Text, Margin = new(0, 0, 0, 12) };
    private readonly ComboBox interval = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Ui.Raised, ForeColor = Ui.Text, Margin = new(0, 0, 0, 12) };
    private readonly ComboBox rotation = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Ui.Raised, ForeColor = Ui.Text, Margin = new(0, 0, 0, 12) };
    private readonly Button pause;
    private readonly Button reconnect;
    private readonly Label feedback = Ui.Label("", 9.75f, Ui.Muted);
    private bool syncing;
    private readonly bool main;
    public DisplayControls(Settings settings, Action<Action<Settings>> update, Action retry, bool main) : base(main)
    {
        this.main = main;
        pause = Ui.Button("暂停屏幕输出", () => update(s => s.Paused = !s.Paused)); Controls.Add(pause);
        reconnect = Ui.Button("立即重新连接", retry); Controls.Add(reconnect);
        Controls.Add(Ui.Label("亮度", 9.75f, Ui.Muted));
        brightness.Items.AddRange(["保持设备现值", "25%", "50%", "75%", "100%"]); Controls.Add(brightness);
        Controls.Add(Ui.Label("刷新间隔", 9.75f, Ui.Muted));
        interval.Items.AddRange(["1 秒", "2 秒", "5 秒"]); Controls.Add(interval);
        if (main)
        {
            Controls.Add(Ui.Label("安装方向（整体补偿）", 9.75f, Ui.Muted));
            rotation.Items.AddRange(["0°", "90°", "180°", "270°"]); Controls.Add(rotation);
        }
        Controls.Add(feedback);
        brightness.SelectedIndexChanged += (_, _) =>
        {
            if (syncing || brightness.SelectedIndex > 4) return;
            update(s => s.Brightness = brightness.SelectedIndex == 0 ? null : (int)Math.Round(brightness.SelectedIndex * 25 * 255d / 100));
        };
        interval.SelectedIndexChanged += (_, _) => { if (!syncing && interval.SelectedIndex >= 0) update(s => s.RefreshSeconds = new[] { 1, 2, 5 }[interval.SelectedIndex]); };
        rotation.SelectedIndexChanged += (_, _) => { if (!syncing && rotation.SelectedIndex >= 0) update(s => s.Rotation = rotation.SelectedIndex * 90); };
        Sync(new(null, ConnectionPhase.Starting), settings.Capture());
    }
    public void Sync(MonitorStatus status, SettingsSnapshot config)
    {
        syncing = true;
        pause.Text = config.Paused ? "恢复屏幕输出" : "暂停屏幕输出";
        reconnect.Enabled = status.Phase != ConnectionPhase.Connecting;
        reconnect.Visible = status.Phase != ConnectionPhase.Connected || main;
        var index = config.Brightness is null ? 0 : Array.IndexOf(new[] { 64, 128, 191, 255 }, config.Brightness.Value) + 1;
        if (index == 0 && config.Brightness.HasValue)
        {
            var custom = $"当前设置 {config.Brightness.Value * 100d / 255:0}%";
            if (brightness.Items.Count == 5) brightness.Items.Add(custom); else brightness.Items[5] = custom;
            index = 5;
        }
        brightness.SelectedIndex = index;
        interval.SelectedIndex = Array.IndexOf(new[] { 1, 2, 5 }, config.RefreshSeconds);
        if (rotation.Items.Count > 0) rotation.SelectedIndex = config.Rotation / 90;
        feedback.Text = status.SettingsError is not null ? "设置保存失败：" + status.SettingsError : status.Phase == ConnectionPhase.Connected ? "" : "● 待连接";
        feedback.Visible = feedback.Text.Length > 0;
        syncing = false;
    }
}

internal sealed class StatusForm : Form
{
    private readonly Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly VerticalStack left = new();
    private readonly VerticalStack right = new();
    private readonly Label connection = Ui.Label("正在启动", 11.25f, Ui.Accent);
    private readonly CompactOverview overview = new();
    private readonly ToolTip hints = new() { AutoPopDelay = 20000 };
    private readonly PictureBox picture = new() { Height = 280, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.Background, Margin = new(0, 0, 0, 10) };
    private readonly Label sent = Ui.Label("尚未成功发送", 9.75f, Ui.Muted);
    private readonly Label diagnostic = Ui.Label("", 9.75f, Ui.Muted);
    private readonly DisplayControls controls;
    private readonly VerticalStack diagnostics = new(true) { Visible = false };
    private readonly VerticalStack behavior = new(true) { Visible = false };
    private readonly HardwareDetailsView hardwareDetails = new() { Visible = false };
    private MonitorStatus? latest;
    private long previewVersion = -1;
    private bool arranging;
    private readonly Settings settings;
    public StatusForm(Settings? settings = null, Action<Action<Settings>>? update = null, Action? retry = null)
    {
        this.settings = settings ?? new Settings();
        update ??= action => action(this.settings);
        retry ??= () => { };
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Font = new("Microsoft YaHei UI", 11.25f); Text = "SamaLite · 工作站概览";
        BackColor = Ui.Background; ForeColor = Ui.Text; ClientSize = Ui.ScaleSize(this, 960, 760); MinimumSize = Ui.ScaleSize(this, 640, 560);
        StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        Controls.Add(scroll); scroll.Controls.Add(left); scroll.Controls.Add(right);
        left.Controls.Add(Ui.Label("SamaLite", 14)); left.Controls.Add(connection); left.Controls.Add(overview);
        left.Controls.Add(Ui.Button("▦ 硬件详情", () => { hardwareDetails.Visible = !hardwareDetails.Visible; hardwareDetails.UpdateDetails(latest?.Metrics?.Details); Arrange(); }));
        left.Controls.Add(hardwareDetails);
        var preview = new VerticalStack(true); preview.Controls.Add(Ui.Label("↗ 最近发送", 11)); preview.Controls.Add(picture); preview.Controls.Add(sent);
        hints.SetToolTip(picture, "最近发送的画面：本机发送完成记录；设备画面无法回读。"); right.Controls.Add(preview);
        controls = new(this.settings, update, retry, true); right.Controls.Add(controls);
        right.Controls.Add(Ui.Button("屏幕布局与数据源…", () => { using var editor = new LayoutForm(this.settings, latest?.Metrics?.Details, latest?.Metrics, update); editor.ShowDialog(this); }));
        right.Controls.Add(Ui.Button("显示与行为 ▾", () => { behavior.Visible = !behavior.Visible; Arrange(); }));
        var auto = new CheckBox { Text = "屏幕断开后自动重连", AutoSize = true, Checked = this.settings.AutoReconnect, ForeColor = Ui.Text };
        auto.CheckedChanged += (_, _) => update(s => s.AutoReconnect = auto.Checked); behavior.Controls.Add(auto);
        var notify = new CheckBox { Text = "意外断开持续30秒后提醒一次", AutoSize = true, Checked = this.settings.NotifyOnDisconnect, ForeColor = Ui.Text };
        notify.CheckedChanged += (_, _) => update(s => s.NotifyOnDisconnect = notify.Checked); behavior.Controls.Add(notify);
        behavior.Controls.Add(Ui.Label("启动时只在托盘运行；关闭窗口继续采集。\n登录自启状态可用 Startup.ps1 -Action Status 查询。", 9.75f, Ui.Muted));
        behavior.Controls.Add(Ui.Button("编辑关注条件…", () => { using var editor = new AttentionForm(this.settings, update); editor.ShowDialog(this); })); right.Controls.Add(behavior);
        right.Controls.Add(Ui.Button("诊断详情 ▾", () => { diagnostics.Visible = !diagnostics.Visible; Arrange(); }));
        diagnostics.Controls.Add(diagnostic);
        diagnostics.Controls.Add(Ui.Button("复制诊断摘要", () => { if (latest is not null) Clipboard.SetText(diagnostic.Text); }));
        diagnostics.Controls.Add(Ui.Button("打开数据目录", () => Process.Start(new ProcessStartInfo("explorer.exe", Settings.DataDirectory) { UseShellExecute = true })));
        diagnostics.Controls.Add(Ui.Button("查看补偿后的发送方向", () =>
        {
            if (picture.Image is null) return;
            using var rotated = new Bitmap(picture.Image);
            rotated.RotateFlip(this.settings.Rotation switch { 90 => RotateFlipType.Rotate90FlipNone, 180 => RotateFlipType.Rotate180FlipNone, 270 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone });
            using var view = new Form { Text = "安装补偿后的发送方向", ClientSize = new(500, 500), StartPosition = FormStartPosition.CenterParent };
            view.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = rotated, SizeMode = PictureBoxSizeMode.Zoom }); view.ShowDialog(this);
        })); right.Controls.Add(diagnostics);
        scroll.SizeChanged += (_, _) => Arrange(); left.SizeChanged += (_, _) => Arrange(); right.SizeChanged += (_, _) => Arrange();
        DpiChanged += (_, _) => Arrange();
        Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new(Math.Clamp(Left, area.Left, area.Right - Width), Math.Clamp(Top, area.Top, area.Bottom - Height)); Arrange();
        };
    }
    private void Arrange()
    {
        if (arranging) return;
        arranging = true;
        try
        {
            var scale = DeviceDpi / 96f; var margin = (int)(16 * scale); var gap = (int)(12 * scale);
            var width = Math.Max(120, scroll.ClientSize.Width - 2 * margin - SystemInformation.VerticalScrollBarWidth);
            var origin = scroll.AutoScrollPosition;
            if (scroll.ClientSize.Width / scale < 860)
            {
                left.Width = right.Width = width;
                left.Location = new(margin + origin.X, margin + origin.Y);
                right.Location = new(margin + origin.X, left.Bottom + gap);
            }
            else
            {
                left.Width = (int)((width - gap) * .57); right.Width = width - gap - left.Width;
                left.Location = new(margin + origin.X, margin + origin.Y); right.Location = new(left.Right + gap, margin + origin.Y);
            }
            picture.Height = Math.Min(right.Width - right.Padding.Horizontal, (int)(300 * scale));
            overview.Height = Ui.ScaleSize(overview, 0, 568).Height;
            scroll.AutoScrollMinSize = new(0, Math.Max(left.Bottom, right.Bottom) - origin.Y + margin);
            scroll.PerformLayout();
        }
        finally { arranging = false; }
    }
    public void UpdateStatus(MonitorStatus value, Bitmap? preview)
    {
        latest = value; var config = settings.Capture(); var m = value.Metrics; var now = Stopwatch.GetTimestamp();
        connection.Text = Ui.CompactStatus(value, config);
        var summary = m?.Summary(config.RefreshSeconds, now);
        if (summary is not null && summary != "正常更新") connection.Text += "　⚠ " + summary;
        hints.SetToolTip(connection, Ui.StatusText(value, config) + "\n" + summary);
        overview.UpdateMetrics(m, config.RefreshSeconds);
        hardwareDetails.UpdateDetails(m?.Details);
        if (preview is not null)
        {
            var old = picture.Image; picture.Image = preview; old?.Dispose(); previewVersion = value.PreviewVersion;
        }
        sent.Text = value.LastSentAt is null ? "—" : $"{(value.Paused ? "Ⅱ" : "↗")} {value.LastSentAt:HH:mm:ss}　{Math.Max(0, (now - value.LastSentTick) / (double)Stopwatch.Frequency):0}s";
        if (value.Phase != ConnectionPhase.Connected) sent.Text += "　⚠ 画面未确认";
        hints.SetToolTip(sent, $"{value.LastFrameKind} · 完成于{value.LastSentAt:O}\n本机发送记录，非硬件画面回读。");
        controls.Sync(value, config);
        diagnostic.Text = $"{value.Connection}\n端口：{value.Port ?? "未发现"}\n固件：{value.Identity ?? "未读取"}\n逻辑画布：480 × 480 px\n安装补偿：{config.Rotation}°\n最近发送：{value.LastSentAt:O}\n设备当前画面：无法回读\n完成帧：{value.Frames} · 发送{value.BytesSent / 1048576d:0.0} MiB\n协议状态：{value.LastDeviceStatus ?? "无"}\n"
            + string.Join("\n\n", m?.Readings.Select(pair => $"{Metrics.Name(pair.Key)}\n{pair.Value.Source}\n{Ui.Reading(m, pair.Key, config.RefreshSeconds, "")}") ?? [])
            + "\n" + value.Error;
        Arrange();
    }
    public bool NeedsPreview(MonitorStatus status) => previewVersion != status.PreviewVersion;
    internal bool HasHorizontalScroll => scroll.HorizontalScroll.Visible;
    internal void ShowDiagnostics() { diagnostics.Visible = true; Arrange(); scroll.ScrollControlIntoView(diagnostics); }
    protected override void Dispose(bool disposing) { if (disposing) { picture.Image?.Dispose(); hints.Dispose(); } base.Dispose(disposing); }
}

internal sealed class QuickForm : Form
{
    private readonly VerticalStack content = new();
    private readonly Label status = Ui.Label("", 9.75f, Ui.Accent);
    private readonly Label metrics = Ui.Label("", 20);
    private readonly Label freshness = Ui.Label("", 9.75f, Ui.Muted);
    private readonly DisplayControls controls;
    private readonly Settings settings;
    private Point anchor;
    private bool fitting;
    public QuickForm(Settings settings, Action<Action<Settings>> update, Action retry, Action showMain)
    {
        this.settings = settings; AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Font = new("Microsoft YaHei UI", 11.25f); FormBorderStyle = FormBorderStyle.None; MaximizeBox = MinimizeBox = false;
        Text = "SamaLite"; ShowInTaskbar = false; TopMost = true; BackColor = Ui.Background; ForeColor = Ui.Text;
        StartPosition = FormStartPosition.Manual; ClientSize = Ui.ScaleSize(this, 360, 620); AutoScroll = true;
        content.Padding = new(20); content.Controls.Add(Ui.Label("SamaLite", 16)); content.Controls.Add(status);
        content.Controls.Add(metrics); content.Controls.Add(freshness);
        controls = new(settings, update, retry, false); content.Controls.Add(controls);
        content.Controls.Add(Ui.Button("打开主窗口", () => { Close(); showMain(); })); Controls.Add(content);
        SizeChanged += (_, _) => { content.Width = ClientSize.Width - SystemInformation.VerticalScrollBarWidth; };
        Deactivate += (_, _) => Close(); KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }
    public void OpenNear(Point anchor)
    {
        this.anchor = anchor;
        content.Width = ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
        FitToContent(); Show(); Activate(); FitToContent();
    }
    private void FitToContent()
    {
        if (fitting) return;
        fitting = true;
        try
        {
        var area = Screen.FromPoint(anchor).WorkingArea;
            var desired = Ui.ScaleSize(this, 360, 500);
            Width = Math.Min(desired.Width, area.Width);
            content.Width = ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            content.PerformLayout();
            Height = Math.Min(Math.Max(content.PreferredSize.Height, Ui.ScaleSize(this, 0, 300).Height), area.Height);
            Location = new(Math.Clamp(anchor.X - Width / 2, area.Left, area.Right - Width), Math.Clamp(anchor.Y - Height, area.Top, area.Bottom - Height));
        }
        finally { fitting = false; }
    }
    public void UpdateStatus(MonitorStatus value)
    {
        var config = settings.Capture(); var m = value.Metrics; var now = Stopwatch.GetTimestamp();
        status.Text = Ui.CompactStatus(value, config);
        metrics.Text = $"CPU   {DisplayRenderer.Format(m?.Fresh(MetricId.CpuUsage, config.RefreshSeconds, now))}%   {DisplayRenderer.Format(m?.Fresh(MetricId.CpuTemperature, config.RefreshSeconds, now))}°C\n"
            + $"GPU   {DisplayRenderer.Format(m?.Fresh(MetricId.GpuUsage, config.RefreshSeconds, now))}%   {DisplayRenderer.Format(m?.Fresh(MetricId.GpuTemperature, config.RefreshSeconds, now))}°C\n"
            + $"内存  {DisplayRenderer.Format(m?.FreshMemoryPercent(config.RefreshSeconds, now))}%";
        freshness.Text = m?.Summary(config.RefreshSeconds, now) ?? "尚无读数";
        freshness.Visible = freshness.Text != "正常更新";
        controls.Sync(value, config);
        if (Visible) FitToContent();
    }
}

internal sealed class AttentionForm : Form
{
    public AttentionForm(Settings settings, Action<Action<Settings>> update)
    {
        Text = "关注条件"; AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Font = new("Microsoft YaHei UI", 11.25f); BackColor = Ui.Background; ForeColor = Ui.Text;
        ClientSize = Ui.ScaleSize(this, 420, 480); StartPosition = FormStartPosition.CenterParent; AutoScroll = true;
        var content = new VerticalStack { Width = 400, Padding = new(20) }; Controls.Add(content);
        var enabled = new CheckBox { Text = "启用界面关注提示", Checked = settings.AttentionEnabled, AutoSize = true }; content.Controls.Add(enabled);
        string[] names = ["CPU温度 °C · 持续15秒", "GPU温度 °C · 持续15秒", "内存 % · 持续30秒"];
        int[] values = [settings.CpuAttention, settings.GpuAttention, settings.MemoryAttention];
        var inputs = new NumericUpDown[3];
        for (var i = 0; i < inputs.Length; i++)
        {
            content.Controls.Add(Ui.Label(names[i])); inputs[i] = new() { Minimum = i == 2 ? 50 : 40, Maximum = i == 2 ? 100 : 120, Value = values[i] }; content.Controls.Add(inputs[i]);
        }
        content.Controls.Add(Ui.Label("低于阈值5度/百分点并持续30秒恢复。\n仅用于提醒观察，不是硬件安全线。", 9.75f, Ui.Muted));
        content.Controls.Add(Ui.Button("保存", () => { update(s => { s.AttentionEnabled = enabled.Checked; s.CpuAttention = (int)inputs[0].Value; s.GpuAttention = (int)inputs[1].Value; s.MemoryAttention = (int)inputs[2].Value; }); Close(); }));
        SizeChanged += (_, _) => content.Width = Math.Max(150, ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
    }
}

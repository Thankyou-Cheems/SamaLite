using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

namespace SamaLite;

internal sealed class TrayContext : ApplicationContext
{
    private readonly Settings settings;
    private readonly MonitorWorker worker;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem pauseItem = new("暂停屏幕输出");
    private readonly ToolStripMenuItem retryItem = new("重新连接");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly EventWaitHandle quit = new(false, EventResetMode.AutoReset, Program.QuitEventName);
    private readonly ToolStripMenuItem brightness = new("亮度");
    private readonly ToolStripMenuItem interval = new("刷新间隔");
    private StatusForm? form;
    private QuickForm? quick;
    private Icon? currentIcon;
    private (TrayMark, bool)? iconState;
    private long? dataProblemSince;
    private long? disconnectedSince;
    private long? stableSince;
    private bool wasConnected;
    private bool disconnectNotified;
    private string? settingsError;
    private bool disposed;

    public TrayContext(Settings settings, int? seconds = null, string? smokeOutput = null, bool show = false)
    {
        this.settings = settings; worker = new(settings);
        menu.Items.Add("打开主窗口", null, (_, _) => ShowStatus());
        pauseItem.Click += (_, _) => Update(s => s.Paused = !s.Paused); menu.Items.Add(pauseItem);
        foreach (var percent in new[] { 25, 50, 75, 100 })
        {
            var level = (int)Math.Round(percent * 255d / 100);
            var item = new ToolStripMenuItem($"{percent}%") { Tag = level };
            item.Click += (_, _) => Update(s => s.Brightness = level); brightness.DropDownItems.Add(item);
        }
        menu.Items.Add(brightness);
        foreach (var secondsValue in new[] { 1, 2, 5 })
        {
            var item = new ToolStripMenuItem($"{secondsValue} 秒") { Tag = secondsValue };
            item.Click += (_, _) => Update(s => s.RefreshSeconds = secondsValue); interval.DropDownItems.Add(item);
        }
        menu.Items.Add(interval);
        retryItem.Click += (_, _) => worker.SettingsChanged(true); menu.Items.Add(retryItem);
        menu.Items.Add("诊断详情", null, (_, _) => { ShowStatus(); form?.ShowDiagnostics(); });
        menu.Items.Add("打开数据目录", null, (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", Settings.DataDirectory) { UseShellExecute = true }));
        if (!IsAdministrator) menu.Items.Add("以管理员权限重启（CPU温度）", null, (_, _) => RestartElevated());
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出", null, (_, _) => ExitThread());
        currentIcon = IconAssets.Create(TrayMark.Connecting);
        tray = new NotifyIcon { Icon = currentIcon, Text = "SamaLite · 正在启动", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowQuick(); };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowQuick(); };
        timer.Tick += (_, _) => RefreshStatus(); timer.Start();
        if (show) ShowStatus();
        if (smokeOutput is not null) StartSmokeTest(smokeOutput);
        if (seconds is int duration)
        {
            var stop = new System.Windows.Forms.Timer { Interval = Math.Max(1, duration) * 1000 };
            stop.Tick += (_, _) => { stop.Stop(); stop.Dispose(); ExitThread(); }; stop.Start();
        }
    }
    internal static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private void Update(Action<Settings> action)
    {
        lock (settings)
        {
            action(settings);
            try { settings.Save(); settingsError = null; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { settingsError = error.Message; LimitedLog.Write(error.Message); }
        }
        worker.SettingsChanged(); RefreshStatus();
    }
    private void ShowQuick()
    {
        if (quick is null || quick.IsDisposed)
        {
            quick = new(settings, Update, () => worker.SettingsChanged(true), ShowStatus);
            quick.FormClosed += (_, _) => quick = null;
            quick.UpdateStatus(worker.Status); quick.OpenNear(Cursor.Position);
        }
        else quick.Activate();
    }
    private void ShowStatus()
    {
        quick?.Close();
        if (form is null || form.IsDisposed)
        {
            form = new(settings, Update, () => worker.SettingsChanged(true)); form.FormClosed += (_, _) => form = null;
        }
        form.Show(); if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.Activate(); RefreshStatus();
    }
    private void RefreshStatus()
    {
        if (quit.WaitOne(0)) { ExitThread(); return; }
        var status = worker.Status with { SettingsError = settingsError }; var config = settings.Capture(); var now = Stopwatch.GetTimestamp();
        if (status.Phase == ConnectionPhase.Connected)
        {
            wasConnected = true; disconnectedSince = null; stableSince ??= now;
            if ((now - stableSince.Value) / (double)Stopwatch.Frequency >= 60) disconnectNotified = false;
        }
        else if (wasConnected)
        {
            stableSince = null; disconnectedSince ??= now;
            if (settings.NotifyOnDisconnect && !disconnectNotified && (now - disconnectedSince.Value) / (double)Stopwatch.Frequency >= 30)
            {
                tray.ShowBalloonTip(5000, "SamaLite · 屏幕已断开", "本机读数继续更新；屏幕可能保留旧画面。可单击托盘查看重连状态。", ToolTipIcon.Warning);
                disconnectNotified = true;
            }
        }
        pauseItem.Text = config.Paused ? "恢复屏幕输出" : "暂停屏幕输出";
        pauseItem.Checked = config.Paused; retryItem.Enabled = status.Phase != ConnectionPhase.Connecting;
        foreach (ToolStripMenuItem item in brightness.DropDownItems) item.Checked = Equals(item.Tag, config.Brightness);
        foreach (ToolStripMenuItem item in interval.DropDownItems) item.Checked = Equals(item.Tag, config.RefreshSeconds);
        var problem = status.Metrics is Metrics m && (m.Attention is not null || m.Summary(config.RefreshSeconds, now) != "正常更新");
        dataProblemSince = problem ? dataProblemSince ?? now : null;
        var mark = status.Phase switch
        {
            ConnectionPhase.Starting or ConnectionPhase.Connecting or ConnectionPhase.RetryWaiting => TrayMark.Connecting,
            ConnectionPhase.Failed => TrayMark.Disconnected,
            _ when dataProblemSince is long since && (now - since) / (double)Stopwatch.Frequency >= 5 => TrayMark.Attention,
            _ when config.Paused => TrayMark.Paused, _ => TrayMark.Normal
        };
        var light = Equals(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0), 1);
        if (iconState != (mark, light))
        {
            var icon = IconAssets.Create(mark, light); tray.Icon = icon; currentIcon?.Dispose(); currentIcon = icon; iconState = (mark, light);
        }
        var tooltip = $"SamaLite · {status.Connection}" + (config.Paused ? " · 输出暂停" : "") + "\n"
            + $"CPU {DisplayRenderer.Format(status.Metrics?.Fresh(MetricId.CpuUsage, config.RefreshSeconds, now))}% / {DisplayRenderer.Format(status.Metrics?.Fresh(MetricId.CpuTemperature, config.RefreshSeconds, now))}°C  "
            + $"GPU {DisplayRenderer.Format(status.Metrics?.Fresh(MetricId.GpuUsage, config.RefreshSeconds, now))}% / {DisplayRenderer.Format(status.Metrics?.Fresh(MetricId.GpuTemperature, config.RefreshSeconds, now))}°C";
        tray.Text = tooltip[..Math.Min(127, tooltip.Length)];
        if (form is { Visible: true, IsDisposed: false }) form.UpdateStatus(status, form.NeedsPreview(status) ? worker.CopyPreview() : null);
        if (quick is { Visible: true, IsDisposed: false }) quick.UpdateStatus(status);
    }
    private void RestartElevated()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--wait-for-exit " + Environment.ProcessId)
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (process is not null) ExitThread();
        }
        catch (System.ComponentModel.Win32Exception error) { LimitedLog.Write(error.Message); }
    }
    private void StartSmokeTest(string output)
    {
        var clock = Stopwatch.StartNew(); var testTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        var step = 0; long pausedFrames = 0; var results = new Dictionary<string, object?>();
        testTimer.Tick += (_, _) =>
        {
            switch (step)
            {
                case 0 when clock.Elapsed.TotalSeconds >= 3: ShowStatus(); step++; break;
                case 1 when clock.Elapsed.TotalSeconds >= 6:
                    if (form is not null) { using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(output + ".png"); }
                    Update(s => s.Paused = true); step++; break;
                case 2 when clock.Elapsed.TotalSeconds >= 9: pausedFrames = worker.Status.Frames; step++; break;
                case 3 when clock.Elapsed.TotalSeconds >= 12:
                    results["PauseStopsFrames"] = worker.Status.Frames == pausedFrames; Update(s => s.Paused = false); step++; break;
                case 4 when clock.Elapsed.TotalSeconds >= 17:
                    results["ResumeAdvancesFrames"] = worker.Status.Frames > pausedFrames; results["Status"] = worker.Status;
                    File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                    testTimer.Stop(); testTimer.Dispose(); ExitThread(); break;
            }
        }; testTimer.Start();
    }
    protected override void ExitThreadCore()
    {
        if (disposed) return; disposed = true;
        timer.Stop(); timer.Dispose(); form?.Close(); quick?.Close();
        tray.Visible = false; tray.Dispose(); currentIcon?.Dispose(); menu.Dispose(); worker.Dispose(); quit.Dispose(); base.ExitThreadCore();
    }
}

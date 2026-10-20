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
    private readonly ToolStripMenuItem connectionItem = new("正在连接屏幕") { Enabled = false };
    private readonly ToolStripMenuItem pauseItem = new("暂停刷新") { CheckOnClick = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private StatusForm? form;
    private bool disposed;

    public TrayContext(Settings settings, int? seconds = null, string? smokeOutput = null, bool show = false)
    {
        this.settings = settings;
        worker = new MonitorWorker(settings);
        menu.Items.Add(connectionItem);
        menu.Items.Add("状态与预览", null, (_, _) => ShowStatus());
        pauseItem.Checked = settings.Paused;
        pauseItem.CheckedChanged += (_, _) => { settings.Paused = pauseItem.Checked; Changed(); };
        menu.Items.Add(pauseItem);
        var interval = new ToolStripMenuItem("刷新间隔");
        foreach (var value in new[] { 1, 2, 5 })
        {
            var item = new ToolStripMenuItem($"{value} 秒") { Checked = settings.RefreshSeconds == value };
            item.Click += (_, _) => { settings.RefreshSeconds = value; SelectOne(interval, item); Changed(); };
            interval.DropDownItems.Add(item);
        }
        menu.Items.Add(interval);
        var rotation = new ToolStripMenuItem("屏幕方向");
        foreach (var value in new[] { 0, 90, 180, 270 })
        {
            var item = new ToolStripMenuItem($"旋转 {value}°" + (value == 270 ? "（本机已验证）" : "")) { Checked = settings.Rotation == value };
            item.Click += (_, _) => { settings.Rotation = value; SelectOne(rotation, item); Changed(); };
            rotation.DropDownItems.Add(item);
        }
        menu.Items.Add(rotation);
        var brightness = new ToolStripMenuItem("屏幕亮度");
        foreach (var percent in new[] { 25, 50, 75, 100 })
        {
            var level = (int)Math.Round(percent * 255d / 100);
            var item = new ToolStripMenuItem($"{percent}%") { Checked = settings.Brightness == level };
            item.Click += (_, _) => { settings.Brightness = level; SelectOne(brightness, item); Changed(); };
            brightness.DropDownItems.Add(item);
        }
        menu.Items.Add(brightness);
        menu.Items.Add("重新连接屏幕", null, (_, _) => worker.SettingsChanged(true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("打开数据目录", null, (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", Settings.DataDirectory) { UseShellExecute = true }));
        if (!IsAdministrator) menu.Items.Add("以管理员权限重启（CPU温度）", null, (_, _) => RestartElevated());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "SamaLite · 正在启动", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowStatus();
        timer.Tick += (_, _) => RefreshStatus();
        timer.Start();
        if (show) ShowStatus();
        if (smokeOutput is not null) StartSmokeTest(smokeOutput);
        if (seconds is int duration)
        {
            var stop = new System.Windows.Forms.Timer { Interval = Math.Max(1, duration) * 1000 };
            stop.Tick += (_, _) => { stop.Stop(); stop.Dispose(); ExitThread(); };
            stop.Start();
        }
    }

    private void StartSmokeTest(string output)
    {
        var clock = Stopwatch.StartNew();
        var testTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        var step = 0;
        long pausedFrames = 0, beforeReconnect = 0;
        var results = new Dictionary<string, object?>();
        testTimer.Tick += (_, _) =>
        {
            try
            {
                switch (step)
                {
                    case 0 when clock.Elapsed.TotalSeconds >= 3: ShowStatus(); step++; break;
                    case 1 when clock.Elapsed.TotalSeconds >= 6:
                        if (form is not null)
                        {
                            using var image = new Bitmap(form.Width, form.Height);
                            form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                            image.Save(output + ".png");
                        }
                        pauseItem.Checked = true; step++; break;
                    case 2 when clock.Elapsed.TotalSeconds >= 8: pausedFrames = worker.Status.Frames; step++; break;
                    case 3 when clock.Elapsed.TotalSeconds >= 10:
                        results["PauseStopsFrames"] = worker.Status.Frames == pausedFrames;
                        pauseItem.Checked = false; step++; break;
                    case 4 when clock.Elapsed.TotalSeconds >= 15:
                        results["ResumeAdvancesFrames"] = worker.Status.Frames > pausedFrames;
                        beforeReconnect = worker.Status.Frames;
                        worker.SettingsChanged(true); step++; break;
                    case 5 when clock.Elapsed.TotalSeconds >= 22:
                        results["Reconnect"] = worker.Status.Error is null && worker.Status.Frames > beforeReconnect;
                        results["Status"] = worker.Status;
                        form?.Close();
                        File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                        testTimer.Stop(); testTimer.Dispose(); ExitThread(); break;
                }
            }
            catch (Exception error)
            {
                File.WriteAllText(output, JsonSerializer.Serialize(new { Error = error.ToString() }));
                testTimer.Stop(); testTimer.Dispose(); ExitThread();
            }
        };
        testTimer.Start();
    }

    internal static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private static void SelectOne(ToolStripMenuItem parent, ToolStripMenuItem selected)
    {
        foreach (ToolStripMenuItem item in parent.DropDownItems) item.Checked = item == selected;
    }
    private void Changed()
    {
        try { settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { LimitedLog.Write(error.Message); }
        worker.SettingsChanged();
    }
    private void ShowStatus()
    {
        if (form is null || form.IsDisposed)
        {
            form = new StatusForm();
            form.FormClosed += (_, _) => { worker.WantsPreview = false; form = null; };
        }
        worker.WantsPreview = true;
        form.Show(); form.Activate(); RefreshStatus();
    }
    private void RefreshStatus()
    {
        var status = worker.Status;
        connectionItem.Text = status.Connection;
        var metrics = status.Metrics;
        tray.Text = metrics is null ? "SamaLite · " + status.Connection
            : $"SamaLite  CPU {Number(metrics.CpuTemperature)}°C  GPU {Number(metrics.GpuTemperature)}°C";
        if (form is not null && !form.IsDisposed) form.UpdateStatus(status, worker.CopyPreview());
    }
    private static string Number(float? value) => value?.ToString("0") ?? "--";
    private void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--wait-for-exit " + Environment.ProcessId) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            ExitThread();
        }
        catch (System.ComponentModel.Win32Exception error) { LimitedLog.Write(error.Message); }
    }
    protected override void ExitThreadCore()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop(); timer.Dispose();
        form?.Close();
        tray.Visible = false; tray.Dispose(); menu.Dispose();
        worker.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class StatusForm : Form
{
    private readonly PictureBox picture = new() { SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(20, 20, 300, 300), BackColor = Color.FromArgb(10, 15, 23) };
    private readonly Label status = new() { Bounds = new Rectangle(340, 24, 380, 426), AutoSize = false, ForeColor = Color.FromArgb(225, 232, 240), Font = new Font("Microsoft YaHei UI", 16, FontStyle.Regular, GraphicsUnit.Pixel) };
    public StatusForm()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "SamaLite · 水冷屏幕控制器";
        ClientSize = new Size(740, 470);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.FromArgb(20, 27, 37);
        StartPosition = FormStartPosition.CenterScreen;
        status.Bounds = new Rectangle(340, 24, 380, 426);
        Controls.Add(picture); Controls.Add(status);
        Controls.Add(new Label { Text = "双击托盘图标打开此页；关闭后继续后台刷新", ForeColor = Color.Silver, Bounds = new Rectangle(20, 343, 300, 60), Font = new Font("Microsoft YaHei UI", 14, FontStyle.Regular, GraphicsUnit.Pixel) });
    }
    public void UpdateStatus(MonitorStatus value, Bitmap? preview)
    {
        if (preview is not null) { var previous = picture.Image; picture.Image = preview; previous?.Dispose(); }
        var m = value.Metrics;
        status.Text = $"{value.Connection}\n串口：{value.Port ?? "未发现"}\n固件：{value.Identity ?? "--"}\n\n"
            + (m is null ? "等待首次采样…" : $"CPU：{Number(m.CpuTemperature)}°C / {Number(m.CpuUsage)}%\nGPU：{Number(m.GpuTemperature)}°C / {Number(m.GpuUsage)}%\n内存：{m.MemoryUsedGiB:0.0} / {m.MemoryTotalGiB:0.0} GiB\n采样：{m.Timestamp:HH:mm:ss}")
            + $"\n\n已刷新：{value.Frames} 帧\n已发送：{value.BytesSent / 1048576d:0.0} MiB\n"
            + (value.Error ?? m?.SensorWarning ?? "传感器与屏幕工作正常");
    }
    private static string Number(float? value) => value?.ToString("0") ?? "--";
    protected override void Dispose(bool disposing)
    {
        if (disposing) picture.Image?.Dispose();
        base.Dispose(disposing);
    }
}

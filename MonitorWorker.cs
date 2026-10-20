using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TuringSmartScreenLib;

namespace SamaLite;

internal sealed record MonitorStatus(Metrics? Metrics, string Connection, string? Port, string? Identity, long Frames, long BytesSent, long BytesReceived, string? LastDeviceStatus, string? Error, bool Paused);

internal sealed class MonitorWorker : IDisposable
{
    private readonly Settings settings;
    private readonly object gate = new();
    private readonly object previewGate = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly AutoResetEvent changed = new(false);
    private readonly Task task;
    private volatile bool redraw = true;
    private volatile bool reconnect;
    private volatile bool wantsPreview;
    private int configurationVersion;
    private Bitmap? preview;
    private MonitorStatus status = new(null, "正在启动", null, null, 0, 0, 0, null, null, false);
    public MonitorStatus Status { get { lock (gate) return status; } }
    public bool WantsPreview { set => wantsPreview = value; }

    public MonitorWorker(Settings settings)
    {
        this.settings = settings;
        task = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public Bitmap? CopyPreview() { lock (previewGate) return preview is null ? null : new Bitmap(preview); }
    public void SettingsChanged(bool shouldReconnect = false) { Interlocked.Increment(ref configurationVersion); reconnect |= shouldReconnect; changed.Set(); }
    private RotateOption Rotation => settings.Rotation switch { 90 => RotateOption.Rotate90, 180 => RotateOption.Rotate180, 270 => RotateOption.Rotate270, _ => RotateOption.None };

    private void Run()
    {
        TuringSmartScreenRevisionE? screen = null;
        using var renderer = new DisplayRenderer();
        using var sensors = new SensorReader();
        var regions = DisplayRenderer.DynamicRegions;
        var current = regions.Select(r => new byte[r.Width * r.Height * 4]).ToArray();
        var previous = regions.Select(r => new byte[r.Width * r.Height * 4]).ToArray();
        var lastStateWrite = Stopwatch.StartNew();
        var retries = 0;
        long frames = 0, bytesSent = 0, bytesReceived = 0;
        string? port = null;
        var nextAttempt = DateTime.MinValue;
        var previewClock = Stopwatch.StartNew();
        var appliedVersion = -1;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var cycle = Stopwatch.StartNew();
                try
                {
                    var metrics = sensors.Read();
                    if (reconnect) { screen?.Dispose(); screen = null; reconnect = false; nextAttempt = DateTime.MinValue; }
                    if (settings.Paused)
                    {
                        SetStatus(metrics, "刷新已暂停", screen, port, frames, bytesSent, bytesReceived, null);
                    }
                    else if (screen is null && DateTime.UtcNow < nextAttempt)
                    {
                        lock (gate) status = status with { Metrics = metrics, Paused = false };
                    }
                    else
                    {
                        var version = Volatile.Read(ref configurationVersion);
                        if (version != appliedVersion) redraw = true;
                        if (screen is null)
                        {
                            port = ScreenLocator.FindWorkingPort();
                            if (port is null)
                            {
                                ScreenLocator.WakeIfPresent();
                                throw new IOException("工作串口暂未出现，正在等待屏幕唤醒。");
                            }
                            screen = new TuringSmartScreenRevisionE(port, 480, 480);
                            screen.Open();
                            screen.StopMedia();
                            redraw = true;
                        }
                        var beforeSent = screen.BytesSent;
                        var beforeReceived = screen.BytesReceived;
                        if (redraw)
                        {
                            renderer.Draw(metrics);
                            if (settings.Brightness is int brightness) screen.SetBrightness(brightness);
                            screen.DisplayBitmap(0, 0, renderer.Pixels(new(0, 0, 480, 480)), 480, 480, Rotation);
                            for (var i = 0; i < regions.Length; i++) renderer.CopyPixels(regions[i], previous[i]);
                            redraw = false;
                            appliedVersion = version;
                        }
                        else
                        {
                            renderer.DrawDynamic(metrics);
                            for (var i = 0; i < regions.Length; i++)
                            {
                                var r = regions[i];
                                renderer.CopyPixels(r, current[i]);
                                if (current[i].AsSpan().SequenceEqual(previous[i])) continue;
                                screen.DisplayBitmap(r.X, r.Y, current[i], r.Width, r.Height, Rotation);
                                (current[i], previous[i]) = (previous[i], current[i]);
                            }
                        }
                        bytesSent += screen.BytesSent - beforeSent;
                        bytesReceived += screen.BytesReceived - beforeReceived;
                        frames++;
                        retries = 0;
                        SetStatus(metrics, "屏幕已连接", screen, port, frames, bytesSent, bytesReceived, null);
                    }
                    if (wantsPreview && previewClock.ElapsedMilliseconds >= 1000)
                    {
                        lock (previewGate) { preview?.Dispose(); preview = new Bitmap(renderer.Canvas); }
                        previewClock.Restart();
                    }
                    else if (!wantsPreview)
                    {
                        lock (previewGate) { preview?.Dispose(); preview = null; }
                    }
                }
                catch (Exception error)
                {
                    screen?.Dispose(); screen = null;
                    retries = Math.Min(retries + 1, 4);
                    nextAttempt = DateTime.UtcNow.AddSeconds(Math.Min(15, 1 << retries));
                    lock (gate) status = status with { Connection = "等待重连", Error = error.Message, Paused = settings.Paused };
                    LimitedLog.Write(error.Message);
                }
                if (lastStateWrite.ElapsedMilliseconds >= 15000) { SaveStatus(); lastStateWrite.Restart(); }
                changed.WaitOne(Math.Max(1, settings.RefreshSeconds * 1000 - (int)cycle.ElapsedMilliseconds));
            }
        }
        finally { screen?.Dispose(); SaveStatus(); }
    }

    private void SetStatus(Metrics metrics, string connection, TuringSmartScreenRevisionE? screen, string? port, long frames, long sent, long received, string? error)
    {
        lock (gate) status = new(metrics, connection, port, screen?.Identity, frames, sent, received, screen?.LastStatus, error, settings.Paused);
    }
    private void SaveStatus()
    {
        try { File.WriteAllText(Path.Combine(Settings.DataDirectory, "status.json"), JsonSerializer.Serialize(Status, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        cancellation.Cancel(); changed.Set();
        try { task.GetAwaiter().GetResult(); }
        finally { lock (previewGate) preview?.Dispose(); cancellation.Dispose(); changed.Dispose(); }
    }
}

internal static class LimitedLog
{
    private static string? previous;
    private static DateTime lastWrite;
    public static void Write(string text)
    {
        if (text == previous && DateTime.UtcNow - lastWrite < TimeSpan.FromMinutes(1)) return;
        previous = text; lastWrite = DateTime.UtcNow;
        try
        {
            var path = Path.Combine(Settings.DataDirectory, "errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".1", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

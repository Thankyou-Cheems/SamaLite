using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TuringSmartScreenLib;

namespace SamaLite;

internal enum ConnectionPhase { Starting, Connecting, Connected, RetryWaiting, Failed }
internal sealed record MonitorStatus(Metrics? Metrics, ConnectionPhase Phase, string? Port = null, string? Identity = null,
    long Frames = 0, long BytesSent = 0, long BytesReceived = 0, string? LastDeviceStatus = null, string? Error = null,
    bool Paused = false, DateTimeOffset? LastSentAt = null, long LastSentTick = 0, DateTimeOffset? NextRetryAt = null,
    string? LastFrameKind = null, bool PauseMarkerFailed = false, long PreviewVersion = 0, string? SettingsError = null)
{
    public string Connection => Phase switch
    {
        ConnectionPhase.Connected => "屏幕已连接", ConnectionPhase.Connecting => "正在连接屏幕…",
        ConnectionPhase.RetryWaiting => "等待重新连接", ConnectionPhase.Failed => "屏幕未连接", _ => "正在启动"
    };
}

internal interface IScreenOutput : IDisposable
{
    string Port { get; }
    string Identity { get; }
    long BytesSent { get; }
    long BytesReceived { get; }
    string? LastStatus { get; }
    void Open();
    void SetBrightness(int value);
    void Send(Rectangle region, byte[] pixels, int rotation);
}

internal sealed class SerialScreenOutput : IScreenOutput
{
    private readonly TuringSmartScreenRevisionE screen;
    public string Port { get; }
    public string Identity => screen.Identity;
    public long BytesSent => screen.BytesSent;
    public long BytesReceived => screen.BytesReceived;
    public string? LastStatus => screen.LastStatus;
    public SerialScreenOutput()
    {
        Port = ScreenLocator.FindWorkingPort() ?? "";
        if (Port.Length == 0) { ScreenLocator.WakeIfPresent(); throw new IOException("工作串口暂未出现，正在等待屏幕唤醒。"); }
        screen = new(Port, 480, 480);
    }
    public void Open() { screen.Open(); screen.StopMedia(); }
    public void SetBrightness(int value) => screen.SetBrightness(value);
    public void Send(Rectangle region, byte[] pixels, int rotation)
    {
        if (!screen.DisplayBitmap(region.X, region.Y, pixels, region.Width, region.Height,
            rotation switch { 90 => RotateOption.Rotate90, 180 => RotateOption.Rotate180, 270 => RotateOption.Rotate270, _ => RotateOption.None }))
            throw new IOException("屏幕请求重新发送画面。");
    }
    public void Dispose() => screen.Dispose();
}

internal sealed class MonitorWorker : IDisposable
{
    private readonly Settings settings;
    private readonly object gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly AutoResetEvent changed = new(false);
    private readonly Task outputTask;
    private readonly Task sensorTask;
    private readonly Func<IScreenOutput> screenFactory;
    private readonly Func<IMetricSource> sensorFactory;
    private readonly bool persistStatus;
    private int configurationVersion;
    private int reconnect;
    private byte[]? preview;
    private MonitorStatus status;
    public MonitorStatus Status { get { lock (gate) return status; } }

    public MonitorWorker(Settings settings, Func<IMetricSource>? sensorFactory = null, Func<IScreenOutput>? screenFactory = null, bool persistStatus = true)
    {
        this.settings = settings;
        this.screenFactory = screenFactory ?? (() => new SerialScreenOutput());
        this.sensorFactory = sensorFactory ?? (() => new SensorReader(settings));
        this.persistStatus = persistStatus;
        status = new(null, ConnectionPhase.Starting, Paused: settings.Paused);
        sensorTask = Task.Factory.StartNew(Collect, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        outputTask = Task.Factory.StartNew(Output, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public Bitmap? CopyPreview()
    {
        lock (gate)
        {
            if (preview is null) return null;
            var bitmap = new Bitmap(480, 480, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new(0, 0, 480, 480), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (var row = 0; row < 480; row++) Marshal.Copy(preview, row * 480 * 4, data.Scan0 + row * data.Stride, 480 * 4); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
    }
    public void SettingsChanged(bool shouldReconnect = false)
    {
        Interlocked.Increment(ref configurationVersion);
        if (shouldReconnect) Interlocked.Exchange(ref reconnect, 1);
        changed.Set();
    }
    private void Collect()
    {
        try
        {
            using var sensors = sensorFactory();
            var history = new TelemetryHistory();
            var attention = new AttentionTracker();
            while (!cancellation.IsCancellationRequested)
            {
                var cycle = Stopwatch.StartNew();
                try
                {
                    var sample = sensors.Read();
                    var tick = Stopwatch.GetTimestamp();
                    sample = sample with { History = history.Add(sample, tick), Attention = attention.Update(sample, settings.Capture(), tick) };
                    lock (gate) status = status with { Metrics = sample };
                }
                catch (Exception error) { LimitedLog.Write("采集失败：" + error.Message); }
                cancellation.Token.WaitHandle.WaitOne(Math.Max(1, settings.Capture().RefreshSeconds * 1000 - (int)cycle.ElapsedMilliseconds));
            }
        }
        catch (Exception error) { LimitedLog.Write("采集初始化失败：" + error.Message); }
    }
    private void Output()
    {
        IScreenOutput? screen = null;
        using var renderer = new DisplayRenderer();
        var regions = DisplayRenderer.DynamicRegions;
        var previous = regions.Select(r => new byte[r.Width * r.Height * 4]).ToArray();
        var current = regions.Select(r => new byte[r.Width * r.Height * 4]).ToArray();
        var saveClock = Stopwatch.StartNew();
        var retries = 0;
        long nextAttempt = 0, nextFrame = 0;
        var appliedVersion = -1;
        var appliedRotation = -1;
        var appliedLayout = "";
        int? appliedBrightness = null;
        var paused = false;
        DateTimeOffset? frozenAt = null;
        Metrics? pauseSnapshot = null;
        long pauseTick = 0;
        var pausedFrameSent = false;
        var fullFrame = true;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var config = settings.Capture();
                var now = Stopwatch.GetTimestamp();
                var version = Volatile.Read(ref configurationVersion);
                if (config.Paused != paused)
                {
                    paused = config.Paused; frozenAt = paused ? DateTimeOffset.Now : null;
                    pauseSnapshot = paused ? Status.Metrics : null; pauseTick = now;
                    pausedFrameSent = false; fullFrame = true; nextFrame = 0;
                }
                lock (gate) status = status with { Paused = config.Paused };
                if (Interlocked.Exchange(ref reconnect, 0) != 0)
                {
                    screen?.Dispose(); screen = null; nextAttempt = 0; retries = 0; fullFrame = true;
                }
                try
                {
                    if (screen is null && (retries == 0 || config.AutoReconnect) && now >= nextAttempt)
                    {
                        lock (gate) status = status with { Phase = ConnectionPhase.Connecting, NextRetryAt = null };
                        screen = screenFactory();
                        screen.Open();
                        fullFrame = true; pausedFrameSent = false;
                        appliedVersion = -1; appliedRotation = -1; appliedBrightness = null;
                        lock (gate) status = status with { Phase = ConnectionPhase.Connected, Port = screen.Port, Identity = screen.Identity, Error = null };
                    }
                    if (screen is not null)
                    {
                        if (version != appliedVersion)
                        {
                            if (config.Brightness is int brightness && appliedBrightness != brightness) screen.SetBrightness(brightness);
                            appliedBrightness = config.Brightness;
                            var layoutKey = JsonSerializer.Serialize(new { config.Layout, config.CpuFanSensorId, config.PumpSensorId, config.NvmeDeviceId, config.GameDisplay });
                            if (appliedRotation != config.Rotation || appliedLayout != layoutKey) { fullFrame = true; pausedFrameSent = false; }
                            appliedLayout = layoutKey;
                            appliedRotation = config.Rotation; nextFrame = 0; appliedVersion = version;
                        }
                        if (paused && pauseSnapshot is null && Status.Metrics is Metrics firstSample)
                        {
                            pauseSnapshot = firstSample; pauseTick = Stopwatch.GetTimestamp(); frozenAt = DateTimeOffset.Now;
                        }
                        var sample = paused ? pauseSnapshot : Status.Metrics;
                        if (sample is not null && now >= nextFrame && (!paused || !pausedFrameSent))
                        {
                            renderer.Draw(sample, config.RefreshSeconds, paused ? "输出暂停" : null, frozenAt, paused ? pauseTick : null, config);
                            var beforeSent = screen.BytesSent;
                            var beforeReceived = screen.BytesReceived;
                            var anySent = false;
                            if (fullFrame)
                            {
                                screen.Send(new(0, 0, 480, 480), renderer.Pixels(new(0, 0, 480, 480)), config.Rotation);
                                anySent = true;
                                for (var i = 0; i < regions.Length; i++) renderer.CopyPixels(regions[i], previous[i]);
                            }
                            else
                            {
                                for (var i = 0; i < regions.Length; i++)
                                {
                                    renderer.CopyPixels(regions[i], current[i]);
                                    if (current[i].AsSpan().SequenceEqual(previous[i])) continue;
                                    screen.Send(regions[i], current[i], config.Rotation);
                                    anySent = true;
                                    (current[i], previous[i]) = (previous[i], current[i]);
                                }
                            }
                            if (anySent)
                            {
                                lock (gate)
                                {
                                    // Reuse one last-success buffer. CopyPreview creates a detached
                                    // bitmap only for a visible window; no per-frame PNG encoder.
                                    preview ??= new byte[480 * 480 * 4];
                                    renderer.CopyPixels(new(0, 0, 480, 480), preview);
                                    status = status with { Phase = ConnectionPhase.Connected, Error = null, Frames = status.Frames + 1,
                                        BytesSent = status.BytesSent + screen.BytesSent - beforeSent, BytesReceived = status.BytesReceived + screen.BytesReceived - beforeReceived,
                                        LastDeviceStatus = screen.LastStatus, LastSentAt = DateTimeOffset.Now, LastSentTick = Stopwatch.GetTimestamp(),
                                        LastFrameKind = paused ? "暂停终态" : "监控画面", PauseMarkerFailed = false, PreviewVersion = status.PreviewVersion + 1, NextRetryAt = null };
                                }
                            }
                            fullFrame = false; pausedFrameSent = paused; retries = 0;
                            nextFrame = Stopwatch.GetTimestamp() + config.RefreshSeconds * Stopwatch.Frequency;
                        }
                    }
                }
                catch (Exception error)
                {
                    screen?.Dispose(); screen = null; fullFrame = true; appliedVersion = -1;
                    var delay = new[] { 2, 5, 15, 30 }[Math.Min(retries++, 3)];
                    nextAttempt = Stopwatch.GetTimestamp() + delay * Stopwatch.Frequency;
                    var explanation = error is UnauthorizedAccessException ? "屏幕端口无法打开，可能被其他程序占用。请确认原SAMA已退出，再重试。" : error.Message;
                    lock (gate) status = status with { Phase = config.AutoReconnect ? ConnectionPhase.RetryWaiting : ConnectionPhase.Failed,
                        Error = explanation, NextRetryAt = config.AutoReconnect ? DateTimeOffset.Now.AddSeconds(delay) : null, PauseMarkerFailed = paused };
                    LimitedLog.Write(error.ToString());
                }
                if (screen is null && !config.AutoReconnect)
                    lock (gate) status = status with { Phase = ConnectionPhase.Failed, NextRetryAt = null };
                if (screen is null && paused && !pausedFrameSent)
                    lock (gate) status = status with { PauseMarkerFailed = true };
                if (saveClock.ElapsedMilliseconds >= 15000) { SaveStatus(); saveClock.Restart(); }
                changed.WaitOne(100);
            }
        }
        finally
        {
            try
            {
                if (screen is not null)
                {
                    renderer.DrawEnd(DateTimeOffset.Now);
                    var sentBefore = screen.BytesSent;
                    var receivedBefore = screen.BytesReceived;
                    screen.Send(new(0, 0, 480, 480), renderer.Pixels(new(0, 0, 480, 480)), settings.Capture().Rotation);
                    lock (gate) status = status with { LastSentAt = DateTimeOffset.Now, LastSentTick = Stopwatch.GetTimestamp(),
                        LastFrameKind = "退出终态", Frames = status.Frames + 1, BytesSent = status.BytesSent + screen.BytesSent - sentBefore,
                        BytesReceived = status.BytesReceived + screen.BytesReceived - receivedBefore };
                }
            }
            catch (Exception error) { LimitedLog.Write("结束页未能发送：" + error.Message); }
            screen?.Dispose(); SaveStatus();
        }
    }
    private void SaveStatus()
    {
        if (!persistStatus) return;
        try
        {
            var path = Path.Combine(Settings.DataDirectory, "status.json");
            using (var stream = File.Create(path + ".new")) JsonSerializer.Serialize(stream, Status);
            File.Move(path + ".new", path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        cancellation.Cancel(); changed.Set();
        try { Task.WhenAll(sensorTask, outputTask).GetAwaiter().GetResult(); }
        finally { lock (gate) preview = null; cancellation.Dispose(); changed.Dispose(); }
    }
}

internal static class LimitedLog
{
    private static string? previous;
    private static DateTime lastWrite;
    private static readonly object gate = new();
    public static void Write(string text)
    {
        lock (gate)
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
}

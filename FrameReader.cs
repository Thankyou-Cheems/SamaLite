using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SamaLite;

internal readonly record struct FrameStatistics(double Fps, double Milliseconds, double? Low, int Count, double Seconds, long Tick);
internal sealed class FrameWindow
{
    private (long Tick, double Ms)[] frames = [];
    private double[] sorted = [];
    private int head, count;
    public void Clear() { head = count = 0; }
    public void Add(long tick, double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds is <= 0 or > 60000) return;
        if (frames.Length == 0) { frames = new (long, double)[120000]; sorted = new double[120000]; }
        if (count > 0 && tick < frames[(head + count - 1) % frames.Length].Tick) Clear();
        if (count == frames.Length) { head = (head + 1) % frames.Length; count--; }
        frames[(head + count++) % frames.Length] = (tick, milliseconds);
        Trim(tick);
    }
    private void Trim(long now)
    {
        while (count > 0 && now - frames[head].Tick > 60 * Stopwatch.Frequency) { head = (head + 1) % frames.Length; count--; }
    }
    public FrameStatistics? Read(long now)
    {
        Trim(now);
        if (count < 2) return null;
        var last = frames[(head + count - 1) % frames.Length].Tick;
        if (now - last > 3 * Stopwatch.Frequency) return null;
        var sum = 0d; var recent = 0;
        for (var i = 0; i < count; i++)
        {
            var frame = frames[(head + i) % frames.Length]; sorted[i] = frame.Ms;
            if (now - frame.Tick <= Stopwatch.Frequency) { sum += frame.Ms; recent++; }
        }
        if (recent == 0 || sum <= 0) return null;
        var seconds = (last - frames[head].Tick) / (double)Stopwatch.Frequency;
        double? low = null;
        if (count >= 100 && seconds >= 5)
        {
            Array.Sort(sorted, 0, count); var slowCount = (int)Math.Ceiling(count * .01); var slow = 0d;
            for (var i = count - slowCount; i < count; i++) slow += sorted[i];
            low = 1000 * slowCount / slow;
        }
        return new(1000 * recent / sum, sum / recent, low, count, seconds, last);
    }
}

internal sealed class FrameReader : IDisposable
{
    private readonly Func<(int, string)>? selectTarget;
    internal FrameReader(Func<(int, string)>? selectTarget = null) { this.selectTarget = selectTarget; }
    private readonly object gate = new();
    private readonly FrameWindow window = new();
    private readonly Dictionary<string, int> chains = new();
    private Process? collector;
    private Task? reader;
    private int target;
    private string name = "", selectedChain = "", session = "", message = "等待目标程序";
    private long retryAt;
    private bool disposed;
    internal static string Executable => Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon", "PresentMon-2.6.0-x64.exe");
    public DetailDevice Read(DetailCache cache, SettingsSnapshot config)
    {
        var selected = config.FramesEnabled ? selectTarget?.Invoke() ?? FindTarget(config.GameProcess) : (0, "帧采集已关闭");
        if (selected.Item1 != target) { Stop(); target = selected.Item1; name = selected.Item2; retryAt = 0; }
        if (collector is not null && collector.HasExited)
        {
            var code = collector.ExitCode; Stop(); message = "帧采集已结束，退出码 " + code;
            retryAt = Stopwatch.GetTimestamp() + 10 * Stopwatch.Frequency;
        }
        if (target > 0 && collector is null && Stopwatch.GetTimestamp() >= retryAt) Start();
        if (target == 0) message = config.FramesEnabled ? "等待游戏；可在布局设置指定进程名" : "帧采集已关闭";
        FrameStatistics? stats;
        string chain, status;
        lock (gate)
        {
            if (chains.Count > 0)
            {
                var busiest = chains.MaxBy(p => p.Value);
                if (selectedChain.Length == 0 || (busiest.Key != selectedChain && busiest.Value > chains.GetValueOrDefault(selectedChain) * 1.5))
                { selectedChain = busiest.Key; window.Clear(); }
                chains.Clear();
            }
            stats = window.Read(Stopwatch.GetTimestamp()); chain = selectedChain; status = message;
        }
        var source = "PresentMon 2.6.0 · PID " + target + " · " + name + " · " + chain;
        DetailMetric Metric(string id, string label, string unit, double? value, string? failure = null)
        {
            if (stats is not FrameStatistics sampled || !value.HasValue) return cache.Capture(id, label, unit, null, source, failure ?? status);
            var at = DateTimeOffset.Now.AddSeconds(-(Stopwatch.GetTimestamp() - sampled.Tick) / (double)Stopwatch.Frequency);
            return new(id, label, unit, new(value, source, at, sampled.Tick, null), DetailAvailability.Available);
        }
        return new("frames", "game", name.Length == 0 ? "游戏帧" : name, stats.HasValue ? "应用Present帧率 · 单进程/主交换链" : status,
            [Metric("game/fps", "FPS", "FPS", stats?.Fps), Metric("game/frametime", "平均帧时间", "ms", stats?.Milliseconds),
             Metric("game/low", "1% Low", "FPS", stats?.Low, "等待至少100帧和5秒有效窗口")],
            "FPS/帧时间为最近1秒应用Present间隔；1% Low=1000÷最近最多60秒最慢1%帧的平均间隔，至少100帧/5秒，上限120000帧。" +
            (stats.HasValue ? $"当前窗口{stats.Value.Seconds:0.0}秒/{stats.Value.Count}帧。" : "") +
            "不是显示器刷新率或插帧后实际显示FPS。切换进程/主交换链即清空窗口，停帧3秒后不再显示旧FPS。");
    }
    private void Start()
    {
        if (!File.Exists(Executable)) { message = "缺少随包提供的PresentMon控制台程序"; retryAt = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency; return; }
        session = "SamaLite-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8];
        var info = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(Executable)! };
        foreach (var argument in new[] { "--process_id", target.ToString(CultureInfo.InvariantCulture), "--output_stdout", "--no_console_stats", "--v1_metrics",
            "--no_track_gpu", "--no_track_input", "--no_track_display", "--session_name", session, "--terminate_on_proc_exit" }) info.ArgumentList.Add(argument);
        try
        {
            collector = Process.Start(info)!; var child = collector;
            message = "等待有效Present事件";
            _ = Task.Run(async () => { try { while (await child.StandardError.ReadLineAsync() is string error) lock (gate) message = error.Length > 400 ? error[..400] : error; } catch (IOException) { } catch (ObjectDisposedException) { } });
            reader = Task.Run(async () =>
            {
                try
                {
                    var timeIndex = -1; var intervalIndex = -1; var chainIndex = -1; long? epoch = null;
                    while (await child.StandardOutput.ReadLineAsync() is string line)
                    {
                        if (line.StartsWith("Application,", StringComparison.Ordinal))
                        {
                            var header = line.Split(','); timeIndex = Array.IndexOf(header, "TimeInSeconds");
                            intervalIndex = Array.IndexOf(header, "msBetweenPresents"); chainIndex = Array.IndexOf(header, "SwapChainAddress"); continue;
                        }
                        if (timeIndex < 0 || intervalIndex < 0 || chainIndex < 0 || !TryValue(line, timeIndex, out var seconds) ||
                            !TryValue(line, intervalIndex, out var ms) || !double.IsFinite(seconds) || seconds is < 0 or > 315360000) continue;
                        var now = Stopwatch.GetTimestamp(); epoch ??= now - (long)(seconds * Stopwatch.Frequency);
                        var eventTick = Math.Min(now, epoch.Value + (long)(seconds * Stopwatch.Frequency));
                        var chain = Column(line, chainIndex).ToString();
                        lock (gate)
                        {
                            if (chains.Count < 16 || chains.ContainsKey(chain)) chains[chain] = chains.GetValueOrDefault(chain) + 1;
                            if (selectedChain.Length == 0) selectedChain = chain;
                            if (chain == selectedChain) window.Add(eventTick, ms);
                        }
                    }
                }
                catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { lock (gate) message = "帧流中断：" + error.Message; }
            });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException) { message = error.Message; collector = null; retryAt = Stopwatch.GetTimestamp() + 10 * Stopwatch.Frequency; }
    }
    internal static ReadOnlySpan<char> Column(string csv, int index)
    {
        var start = 0; var column = 0; var quoted = false;
        for (var i = 0; i <= csv.Length; i++)
        {
            if (i < csv.Length && csv[i] == '"') quoted = !quoted;
            if (i < csv.Length && (csv[i] != ',' || quoted)) continue;
            if (column++ == index) return csv.AsSpan(start, i - start).Trim('"');
            start = i + 1;
        }
        return [];
    }
    internal static bool TryValue(string csv, int index, out double value) => double.TryParse(Column(csv, index), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    private static (int, string) FindTarget(string configured)
    {
        try
        {
            if (configured.Trim().Length > 0)
            {
                var targetName = Path.GetFileNameWithoutExtension(configured.Trim());
                var processes = Process.GetProcessesByName(targetName);
                try { var process = processes.OrderBy(p => p.Id).FirstOrDefault(); return process is null ? (0, targetName) : (process.Id, process.ProcessName); }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            GetWindowThreadProcessId(GetForegroundWindow(), out var id);
            using var foreground = Process.GetProcessById((int)id); var name = foreground.ProcessName;
            if (id == Environment.ProcessId || new[] { "explorer", "dwm", "Codex", "ChatGPT", "chrome", "msedge", "firefox", "pwsh", "WindowsTerminal", "Code", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "Taskmgr" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return (0, "");
            return ((int)id, name);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return (0, ""); }
    }
    private void Stop()
    {
        if (collector is not null)
        {
            try
            {
                if (!collector.HasExited)
                {
                    var info = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                    foreach (var argument in new[] { "--session_name", session, "--terminate_existing_session" }) info.ArgumentList.Add(argument);
                    using var stopper = Process.Start(info); if (stopper is not null && !stopper.WaitForExit(2000)) stopper.Kill();
                    if (!collector.WaitForExit(2000)) { collector.Kill(); collector.WaitForExit(2000); }
                }
                reader?.Wait(2000);
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) { LimitedLog.Write("关闭自有帧会话：" + error.Message); }
            finally { collector.Dispose(); collector = null; }
        }
        lock (gate) { window.Clear(); chains.Clear(); selectedChain = ""; }
    }
    public void Dispose() { if (disposed) return; disposed = true; Stop(); }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
}

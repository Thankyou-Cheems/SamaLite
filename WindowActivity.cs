using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SamaLite;

internal readonly record struct FrameTarget(int Pid, string Name, bool Fullscreen = false, bool Explicit = false);
internal sealed record FrameActivity(bool GameLayout, int ProcessId, string ProcessName, bool Fullscreen, bool ExplicitTarget, string Reason);

internal sealed class GameActivityTracker
{
    private int candidate;
    private long since, lastEvidence, previous;
    private bool active;
    public FrameActivity Update(FrameTarget target, FrameStatistics? stats, long now, int pollSeconds = 1)
    {
        if (previous != 0 && now - previous > Math.Max(3 * pollSeconds, 5) * Stopwatch.Frequency) since = 0;
        previous = now;
        var evidence = target.Pid > 0 && (target.Fullscreen || target.Explicit) && stats is FrameStatistics s && now - s.Tick <= 3 * Stopwatch.Frequency;
        if (target.Pid != candidate) { candidate = target.Pid; since = 0; }
        if (evidence)
        {
            if (since == 0) since = now;
            lastEvidence = now;
            if (now - since >= 3 * Stopwatch.Frequency) active = true;
        }
        else since = 0;
        if (active && now - lastEvidence > 8 * Stopwatch.Frequency) active = false;
        return new(active, target.Pid, target.Name, target.Fullscreen, target.Explicit,
            evidence ? active ? "有效Present事件已确认；游戏布局" : "等待持续帧事件确认" : active ? "短暂失去目标，等待恢复" : "未确认全屏渲染或指定游戏");
    }
}

internal static class WindowActivity
{
    private static readonly HashSet<string> ShellNames = new(StringComparer.OrdinalIgnoreCase) { "explorer", "dwm", "Codex", "ChatGPT", "pwsh", "WindowsTerminal", "Code", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "Taskmgr" };
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "firefox" };
    public static FrameTarget Select(string configured)
    {
        try
        {
            var hwnd = GetForegroundWindow(); GetWindowThreadProcessId(hwnd, out var foregroundId);
            var full = IsFullscreen(hwnd);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var targetName = Path.GetFileNameWithoutExtension(configured.Trim());
                var processes = Process.GetProcessesByName(targetName);
                try { var p = processes.OrderBy(p => p.Id).FirstOrDefault(); return p is null ? new(0, targetName, Explicit: true) : new(p.Id, p.ProcessName, p.Id == foregroundId && full, true); }
                finally { foreach (var p in processes) p.Dispose(); }
            }
            if (foregroundId == 0 || foregroundId == Environment.ProcessId) return new(0, "");
            using var foreground = Process.GetProcessById((int)foregroundId); var name = foreground.ProcessName;
            if (Ignored(name, full)) return new(0, "");
            return new((int)foregroundId, name, full);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return new(0, ""); }
    }
    internal static bool Ignored(string name, bool fullscreen) => ShellNames.Contains(name) || (!fullscreen && Browsers.Contains(name));
    internal static bool CoversMonitor(Rectangle client, Rectangle monitor) => monitor.Width > 0 && monitor.Height > 0
        && Math.Abs(client.Left - monitor.Left) <= 2 && Math.Abs(client.Top - monitor.Top) <= 2
        && Math.Abs(client.Right - monitor.Right) <= 2 && Math.Abs(client.Bottom - monitor.Bottom) <= 2;
    private static bool IsFullscreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd) || !GetClientRect(hwnd, out var client)) return false;
        var origin = new NativePoint(); if (!ClientToScreen(hwnd, ref origin)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return false;
        return CoversMonitor(new(origin.X, origin.Y, client.Right, client.Bottom), Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom));
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint id);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}

using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace SamaLite;

internal sealed class SystemActivityReader : IDisposable
{
    private readonly NativeCounter diskRead = new(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
    private readonly NativeCounter diskWrite = new(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
    private readonly NativeCounter diskIdle = new(@"\PhysicalDisk(_Total)\% Idle Time");
    private string adapterId = "";
    private long previousRx, previousTx, previousTick;
    public DetailDevice[] Read(DetailCache cache, string selectedAdapter)
    {
        double? rx = null, tx = null;
        NetworkInterface? adapter = null;
        string? failure = null;
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)).ToArray();
            adapter = selectedAdapter.Length > 0 ? adapters.FirstOrDefault(n => n.Id == selectedAdapter) : adapters
                .Where(n => n.GetIPProperties().GatewayAddresses.Any(a => !a.Address.Equals(System.Net.IPAddress.Any) && !a.Address.Equals(System.Net.IPAddress.IPv6Any)))
                .OrderBy(n => IsVirtual(n.Name + " " + n.Description)).ThenByDescending(n => n.Speed).FirstOrDefault();
            if (adapter is null) failure = "没有可用的选定网卡或带网关的活动网卡";
            else
            {
                var statistics = adapter.GetIPStatistics(); var now = Stopwatch.GetTimestamp();
                var elapsed = (now - previousTick) / (double)Stopwatch.Frequency;
                if (adapter.Id == adapterId && previousTick > 0 && elapsed is > 0 and <= 20 && statistics.BytesReceived >= previousRx && statistics.BytesSent >= previousTx)
                { rx = (statistics.BytesReceived - previousRx) / elapsed / 1048576; tx = (statistics.BytesSent - previousTx) / elapsed / 1048576; }
                else failure = "等待该网卡的第二次计数采样";
                adapterId = adapter.Id; previousTick = now; previousRx = statistics.BytesReceived; previousTx = statistics.BytesSent;
            }
        }
        catch (Exception error) when (error is NetworkInformationException or System.Net.Sockets.SocketException) { failure = error.Message; }
        var source = "Windows IP interface counters · " + (adapter?.Id ?? selectedAdapter);
        var idle = diskIdle.Read();
        return [new("network", "network", adapter?.Name ?? "网卡", adapter?.Description ?? failure ?? "", [
            cache.Capture("io/download", "下载", "MiB/s", rx, source, failure), cache.Capture("io/upload", "上传", "MiB/s", tx, source, failure)],
            "仅统计当前选定网卡的字节差分，不相加虚拟网卡。自动选择带网关的活动网卡；可在设置中指定ID。"),
            new("disk-io", "storage", "全部物理盘活动", "Windows PDH · PhysicalDisk(_Total)", [
                cache.Capture("io/disk-read", "磁盘读取", "MiB/s", diskRead.Read() / 1048576, "Windows PDH · PhysicalDisk(_Total)"),
                cache.Capture("io/disk-write", "磁盘写入", "MiB/s", diskWrite.Read() / 1048576, "Windows PDH · PhysicalDisk(_Total)"),
                cache.Capture("io/disk-busy", "磁盘忙碌", "%", idle.HasValue ? Math.Clamp(100 - idle.Value, 0, 100) : null, "Windows PDH · 100 − % Idle Time")],
                "所有物理盘的系统聚合活动，与选定NVMe温度分别标识。")];
    }
    public void Dispose() { diskRead.Dispose(); diskWrite.Dispose(); diskIdle.Dispose(); }
    internal static bool IsVirtual(string name) => new[] { "virtual", "vpn", "tunnel", "tap-", "wintun", "mihomo", "hyper-v", "vmware" }.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
}

internal sealed class NativeCounter : IDisposable
{
    private IntPtr query, counter;
    public NativeCounter(string path)
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
        if (PdhAddEnglishCounter(query, path, IntPtr.Zero, out counter) != 0) { Dispose(); return; }
        PdhCollectQueryData(query);
    }
    public double? Read()
    {
        if (query == IntPtr.Zero || PdhCollectQueryData(query) != 0 || PdhGetFormattedCounterValue(counter, 0x200, out _, out var value) != 0 ||
            value.Status is not (0 or 1) || !double.IsFinite(value.Value) || value.Value < 0) return null;
        return value.Value;
    }
    public void Dispose() { if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}

using System.Buffers.Binary;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SamaLite;

internal sealed class NvmeReader : IDisposable
{
    private readonly List<(string Id, string Name, SafeFileHandle Handle)> drives = new();
    private DetailDevice[] snapshot = [];
    private long nextRead;
    public NvmeReader()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Index, Model FROM Win32_DiskDrive");
            using var entries = searcher.Get();
            foreach (ManagementObject entry in entries)
            using (entry)
            {
                var index = Convert.ToUInt32(entry["Index"]);
                var handle = CreateFile(@"\\.\PhysicalDrive" + index, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); continue; }
                var query = new byte[12]; var descriptor = new byte[4096];
                if (DeviceIoControl(handle, 0x2D1400, query, query.Length, descriptor, descriptor.Length, out var bytes, IntPtr.Zero) && bytes >= 36 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(28)) == 17)
                    drives.Add(("nvme/" + index, entry["Model"]?.ToString()?.Trim() ?? "NVMe " + index, handle));
                else handle.Dispose();
            }
        }
        catch (Exception error) when (error is ManagementException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { LimitedLog.Write("NVMe枚举：" + error.Message); }
    }
    public DetailDevice[] Read(DetailCache cache)
    {
        if (Stopwatch.GetTimestamp() < nextRead) return snapshot;
        nextRead = Stopwatch.GetTimestamp() + 10 * Stopwatch.Frequency;
        snapshot = drives.Select(drive =>
        {
            var query = new byte[560];
            void U(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(query.AsSpan(offset), value);
            U(0, 50); U(8, 3); U(12, 2); U(16, 2); U(24, 40); U(28, 512);
            var response = new byte[560];
            var success = DeviceIoControl(drive.Handle, 0x2D1400, query, query.Length, response, response.Length, out var bytes, IntPtr.Zero);
            var temperature = success ? ParseTemperature(response.AsSpan(0, Math.Clamp(bytes, 0, response.Length))) : null;
            return new DetailDevice(drive.Id, "storage", drive.Name, "NVMe SMART / Health · 10秒采样", [
                cache.Capture(drive.Id + "/temperature", "NVMe综合温度", "°C", temperature, "Windows IOCTL_STORAGE_QUERY_PROPERTY · " + drive.Id,
                    temperature.HasValue ? null : "NVMe健康日志查询失败或返回值无效（" + Marshal.GetLastWin32Error() + "）")],
                "设备总线类型确认为NVMe；只查询健康日志，不更改磁盘设置。此值不是主控热点或NAND单独温度。");
        }).ToArray();
        if (snapshot.Length == 0) snapshot = [new("nvme-missing", "storage", "NVMe", "未枚举到可查询的NVMe设备", [
            cache.Capture("nvme-missing/temperature", "NVMe温度", "°C", null, "Windows NVMe storage query", "设备未暴露可查询的NVMe健康日志")])];
        return snapshot;
    }
    internal static double? ParseTemperature(ReadOnlySpan<byte> response)
    {
        if (response.Length < 48 || BinaryPrimitives.ReadUInt32LittleEndian(response) != 48 ||
            BinaryPrimitives.ReadUInt32LittleEndian(response[4..]) != 48 || BinaryPrimitives.ReadUInt32LittleEndian(response[8..]) != 3) return null;
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(response[24..]); var length = BinaryPrimitives.ReadUInt32LittleEndian(response[28..]);
        if (offset < 40 || length < 512 || offset > int.MaxValue - 8 || response.Length - 8 < (long)offset + length) return null;
        var kelvin = BinaryPrimitives.ReadUInt16LittleEndian(response.Slice(8 + (int)offset + 1, 2));
        var celsius = kelvin - 273.15;
        return kelvin is 0 or ushort.MaxValue || celsius is < -20 or > 150 ? null : celsius;
    }
    public void Dispose() { foreach (var drive in drives) drive.Handle.Dispose(); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string file, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputBytes, byte[] output, int outputBytes, out int returned, IntPtr overlapped);
}

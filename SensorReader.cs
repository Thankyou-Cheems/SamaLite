using System.Runtime.InteropServices;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace SamaLite;

internal sealed record Metrics(DateTimeOffset Timestamp, float? CpuTemperature, float? CpuUsage,
    float? GpuTemperature, float? GpuUsage, double MemoryUsedGiB, double MemoryTotalGiB, string? SensorWarning);

internal sealed class SensorReader : IDisposable
{
    private readonly Computer computer = new() { IsCpuEnabled = true };
    private readonly NvidiaReader nvidia = new();
    private ulong previousIdle;
    private ulong previousTotal;
    private bool hasPrevious;
    private string? warning;

    public SensorReader()
    {
        try { computer.Open(); }
        catch (Exception error) { warning = "CPU温度采集初始化失败：" + error.Message; }
    }

    public Metrics Read()
    {
        float? temperature = null;
        try
        {
            foreach (var cpu in computer.Hardware.Where(hardware => hardware.HardwareType == HardwareType.Cpu))
            {
                cpu.Update();
                var temperatures = cpu.Sensors.Where(sensor => sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue).ToArray();
                temperature = temperatures.FirstOrDefault(sensor => sensor.Name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase))?.Value
                    ?? temperatures.FirstOrDefault(sensor => sensor.Name.Equals("Core Max", StringComparison.OrdinalIgnoreCase))?.Value;
                if (temperature is float t && (!float.IsFinite(t) || t < -20 || t > 125)) temperature = null;
            }
        }
        catch (Exception error) { warning = "CPU温度读取失败：" + error.Message; }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var (gpuTemperature, gpuUsage) = nvidia.Read();
        return new Metrics(DateTimeOffset.Now, temperature, ReadCpuUsage(), gpuTemperature, gpuUsage,
            (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824d, memory.TotalPhysical / 1073741824d,
            temperature.HasValue ? warning : warning ?? "CPU温度暂不可读；可能需要管理员权限或硬件访问驱动。所有调速功能均未启用。");
    }

    public string HardwareReport() => computer.GetReport();

    private float? ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var total = kernel + user;
        float? value = hasPrevious && total > previousTotal
            ? (float)Math.Clamp(100d * (1d - (double)(idle - previousIdle) / (total - previousTotal)), 0, 100)
            : null;
        previousIdle = idle;
        previousTotal = total;
        hasPrevious = true;
        return value;
    }

    public void Dispose()
    {
        nvidia.Dispose();
        computer.Close();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}

internal sealed class NvidiaReader : IDisposable
{
    private IntPtr library;
    private IntPtr device;
    private TemperatureDelegate? getTemperature;
    private UtilizationDelegate? getUtilization;
    private InitDelegate? shutdown;

    public NvidiaReader()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
        if (!NativeLibrary.TryLoad(path, out library)) return;
        try
        {
            var init = Load<InitDelegate>("nvmlInit_v2");
            if (init() != 0) { NativeLibrary.Free(library); library = IntPtr.Zero; return; }
            shutdown = Load<InitDelegate>("nvmlShutdown");
            var getCount = Load<CountDelegate>("nvmlDeviceGetCount_v2");
            var getHandle = Load<HandleDelegate>("nvmlDeviceGetHandleByIndex_v2");
            var getName = Load<NameDelegate>("nvmlDeviceGetName");
            getTemperature = Load<TemperatureDelegate>("nvmlDeviceGetTemperature");
            getUtilization = Load<UtilizationDelegate>("nvmlDeviceGetUtilizationRates");
            if (getCount(out var count) != 0) return;
            for (uint index = 0; index < Math.Min(count, 32); index++)
            {
                if (getHandle(index, out var handle) != 0) continue;
                var name = new StringBuilder(256);
                if (device == IntPtr.Zero) device = handle;
                if (getName(handle, name, 256) == 0 && name.ToString().Contains("4080", StringComparison.OrdinalIgnoreCase)) { device = handle; break; }
            }
        }
        catch { Dispose(); }
    }

    private T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    public (float? Temperature, float? Usage) Read()
    {
        if (device == IntPtr.Zero || getTemperature is null || getUtilization is null) return (null, null);
        float? temperature = getTemperature(device, 0, out var t) == 0 ? t : null;
        float? usage = getUtilization(device, out var u) == 0 ? u.Gpu : null;
        return (temperature, usage);
    }

    public void Dispose()
    {
        if (library == IntPtr.Zero) return;
        device = IntPtr.Zero;
        shutdown?.Invoke();
        NativeLibrary.Free(library);
        library = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CountDelegate(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HandleDelegate(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NameDelegate(IntPtr device, StringBuilder name, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TemperatureDelegate(IntPtr device, uint sensor, out uint temperature);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UtilizationDelegate(IntPtr device, out Utilization utilization);
}

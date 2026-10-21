using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Management;
using System.Reflection;
using LibreHardwareMonitor.Hardware;

namespace SamaLite;

internal interface IMetricSource : IDisposable
{
    Metrics Read();
}

internal sealed class SensorReader : IMetricSource
{
    private readonly Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true };
    private readonly NvidiaReader nvidia = new();
    private readonly Settings settings;
    private readonly SystemActivityReader activity = new();
    private readonly NvmeReader nvme = new();
    private readonly FrameReader frames = new();
    private ulong previousIdle;
    private ulong previousTotal;
    private bool hasPrevious;
    private string? warning;
    private readonly string? initializationWarning;
    private readonly Dictionary<MetricId, MetricReading> readings = new();
    private readonly DetailCache detailsCache = new();
    private HardwareDetails? details;
    private long nextDetails;
    private readonly List<DetailDevice> memoryInventory = new();
    private string inventoryNote = "";
    private string intelInventory = "";

    public SensorReader(Settings? settings = null)
    {
        this.settings = settings ?? new Settings();
        computer.HardwareAdded += DisableSensorHistory;
        try { computer.Open(); }
        catch (Exception error) { initializationWarning = "CPU温度采集初始化失败：" + error.Message; }
        ReadInventory();
    }

    // SamaLite owns its bounded 120-second history. LHM defaults to another
    // full day per sensor, including CPU cores that are never plotted here.
    private static void DisableSensorHistory(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors) sensor.ValuesTimeWindow = TimeSpan.Zero;
        hardware.SensorAdded += sensor => sensor.ValuesTimeWindow = TimeSpan.Zero;
        foreach (var child in hardware.SubHardware) DisableSensorHistory(child);
    }

    public Metrics Read()
    {
        warning = initializationWarning;
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
        var memoryValid = GlobalMemoryStatusEx(ref memory);
        Put(MetricId.CpuTemperature, temperature, "LibreHardwareMonitor · CPU Package / Core Max", warning ?? "CPU温度暂不可读，请检查硬件访问权限与PawnIO状态");
        Put(MetricId.CpuUsage, ReadCpuUsage(), "Windows GetSystemTimes", "CPU占用暂不可读或等待第二次采样");
        var (gpuTemperature, gpuUsage) = nvidia.Read();
        Put(MetricId.GpuTemperature, gpuTemperature, nvidia.Source, "GPU温度暂不可读");
        Put(MetricId.GpuUsage, gpuUsage, nvidia.Source, "GPU负载暂不可读");
        Put(MetricId.MemoryUsed, memoryValid ? (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824d : null, "Windows GlobalMemoryStatusEx", "系统内存用量读取失败");
        Put(MetricId.MemoryTotal, memoryValid && memory.TotalPhysical > 0 ? memory.TotalPhysical / 1073741824d : null, "Windows 系统可用物理总量", "系统可用物理总量读取失败");
        if (Stopwatch.GetTimestamp() >= nextDetails)
        {
            try { details = ReadDetails(); }
            catch (Exception error) { LimitedLog.Write("扩展指标读取失败：" + error.Message); }
            nextDetails = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency;
        }
        var config = settings.Capture();
        var live = activity.Read(detailsCache, config.NetworkAdapterId);
        var extended = (details?.Devices ?? []).Concat(nvme.Read(detailsCache)).Concat(live).Append(frames.Read(detailsCache, config)).ToArray();
        return new(DateTimeOffset.Now, new Dictionary<MetricId, MetricReading>(readings), [], Details: new(DateTimeOffset.Now, Stopwatch.GetTimestamp(), extended));
    }

    private HardwareDetails ReadDetails()
    {
        var devices = new List<DetailDevice>();
        var hardware = computer.Hardware;
        foreach (var cpu in hardware.Where(h => h.HardwareType == HardwareType.Cpu))
        {
            var sensors = cpu.Sensors.Where(s => s.SensorType is SensorType.Load or SensorType.Clock or SensorType.Temperature or SensorType.Power)
                .Select(s => CaptureSensor(cpu, s)).ToArray();
            devices.Add(new(cpu.Identifier.ToString(), "cpu", cpu.Name, "CPU核心与封装传感器", sensors,
                "核心报告时钟不等于有效频率；各CPU核心/线程负载不相加。CPU Package功耗不是整机功耗。"));
        }
        devices.Add(nvidia.ReadDetails(detailsCache));
        foreach (var board in hardware.Where(h => h.HardwareType == HardwareType.Motherboard))
        {
            foreach (var component in Walk(board))
            {
                string? error = null;
                try { component.Update(); } catch (Exception failure) { error = failure.Message; }
                var fans = component.Sensors.Where(s => s.SensorType == SensorType.Fan).Select(s => detailsCache.Capture(s.Identifier.ToString(),
                    s.Name, "RPM", error is null && s.Value is >= 0 and <= 60000 ? s.Value : null,
                    "LHM/PawnIO · " + component.Name + " · " + s.Identifier, error)).ToArray();
                if (fans.Length > 0) devices.Add(new(component.Identifier.ToString(), "cooling", component.Name, "主板转速输入", fans,
                    "通用Fan编号不推断为CPU风扇或水泵；请在布局设置中映射真实接口。0RPM可能是正常停转策略。"));
            }
        }
        var dimms = hardware.Where(h => h.HardwareType == HardwareType.Memory && h.Identifier.ToString().StartsWith("/memory/dimm/", StringComparison.Ordinal)).ToArray();
        foreach (var dimm in dimms)
        {
            var sensors = dimm.Sensors.Where(HardwareSensorRules.IsLiveDimmTemperature).ToArray();
            var metrics = new List<DetailMetric>();
            foreach (var sensor in sensors)
            {
                double? value = null; string? error = null;
                try
                {
                    // LHM 0.9.6 Hardware.Update ignores the boolean and retains an old Value on failure.
                    // Invoke the live read explicitly so a failed SMBus transaction cannot refresh its age.
                    var success = sensor.GetType().GetMethod("UpdateSensor", Type.EmptyTypes)!.Invoke(sensor, null) is true;
                    if (success) value = HardwareSensorRules.Normalize(SensorType.Temperature, sensor.Value);
                    else error = "DIMM温度读取失败；不将缓存值标为新采样";
                }
                catch (Exception failure) { error = "DIMM温度读取失败：" + failure.GetBaseException().Message; }
                metrics.Add(detailsCache.Capture(sensor.Identifier.ToString(), "内存条温度", "°C", value,
                    "LHM/PawnIO · " + dimm.Identifier + " · " + sensor.Name, error));
            }
            if (metrics.Count == 0) metrics.Add(detailsCache.Capture(dimm.Identifier + "/temperature", "内存条温度", "°C", null,
                "LHM/PawnIO · " + dimm.Identifier, "该DIMM未暴露可用的温度传感器", DetailAvailability.Unsupported));
            devices.Add(new(dimm.Identifier.ToString(), "memory", dimm.Name, "DIMM温度传感器", metrics.ToArray(),
                "DIMM编号来自采集器；未将编号推断成主板A2/B2插槽。温度阈值和分辨率常量不作为实时温度。"));
        }
        if (dimms.Length == 0)
            devices.Add(new("memory-temperature", "memory", "内存温度", "未枚举到DIMM温度传感器",
                [detailsCache.Capture("memory-temperature", "内存条温度", "°C", null, "LHM/PawnIO · DIMM SPD",
                    "尚未枚举到可读取的DIMM；可能与硬件支持、SMBus访问权限或初始化有关")], inventoryNote));
        devices.AddRange(memoryInventory);
        var integrated = hardware.Where(HardwareSensorRules.IsIntegratedIntel).ToArray();
        foreach (var gpu in integrated)
        {
            string? error = null;
            var oldQuery = IntelQueryTime(gpu);
            var oldEnergy = IntelEnergyTime(gpu);
            try { gpu.Update(); } catch (Exception failure) { error = failure.Message; }
            var queryAdvanced = IntelQueryTime(gpu) > oldQuery;
            var energyAdvanced = IntelEnergyTime(gpu) > oldEnergy;
            var metrics = gpu.Sensors.Where(s => s.SensorType is SensorType.Load or SensorType.SmallData or SensorType.Power or SensorType.Temperature or SensorType.Clock)
                .Where(s => s.Name != "D3D Shared Memory Free")
                .Select(s => CaptureSensor(gpu, s, error ?? (s.SensorType is SensorType.Load or SensorType.SmallData && !queryAdvanced ? "本次D3D查询没有刷新；保留上次有效采样时间"
                    : s.SensorType == SensorType.Power && !energyAdvanced ? "本次核显能量计数未刷新" : null))).ToArray();
            devices.Add(new(gpu.Identifier.ToString(), "igpu", gpu.Name, DetailFormatting.IntegratedStatus(metrics), metrics,
                "独立于RTX独显显示。各引擎利用率分别统计，不相加为总占用；共享内存使用系统RAM，并非独立显存。"));
        }
        if (integrated.Length == 0)
            devices.Add(new("intel-integrated", "igpu", "CPU核显", "未枚举到可用的Intel核显遥测", [],
                (intelInventory.Length == 0 ? "Windows未枚举到Intel图形设备；不能据此断定BIOS已禁用核显。" : intelInventory + "；Windows可见不等于已取得核显遥测。")));
        return new(DateTimeOffset.Now, Stopwatch.GetTimestamp(), devices.ToArray());
    }
    private static IEnumerable<IHardware> Walk(IHardware hardware)
    {
        yield return hardware;
        foreach (var child in hardware.SubHardware) foreach (var item in Walk(child)) yield return item;
    }
    private DetailMetric CaptureSensor(IHardware hardware, ISensor sensor, string? error = null)
    {
        var value = error is null ? HardwareSensorRules.Normalize(sensor.SensorType, sensor.Value) : null;
        return detailsCache.Capture(sensor.Identifier.ToString(), HardwareSensorRules.Label(sensor), HardwareSensorRules.Unit(sensor.SensorType), value,
            "LHM/PawnIO · " + hardware.Name + " · " + sensor.SensorType + "/" + sensor.Index, error,
            note: sensor.SensorType == SensorType.Clock ? "采集器报告时钟，非有效频率" : null);
    }
    private static DateTime IntelQueryTime(IHardware gpu) =>
        gpu.GetType().GetField("_nodeUsagePrevTick", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(gpu) is DateTime[] ticks
            ? ticks.DefaultIfEmpty().Max() : DateTime.MinValue;
    private static DateTime IntelEnergyTime(IHardware gpu) =>
        gpu.GetType().GetField("_lastEnergyTime", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(gpu) is DateTime time ? time : DateTime.MinValue;
    private void ReadInventory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceLocator, Manufacturer, PartNumber, Capacity FROM Win32_PhysicalMemory");
            using var modules = searcher.Get();
            double totalBytes = 0; var index = 0;
            foreach (ManagementObject module in modules)
            {
                using (module)
                {
                    var id = "memory-inventory/" + index++;
                    var bytes = Convert.ToDouble(module["Capacity"] ?? 0);
                    totalBytes += bytes;
                    memoryInventory.Add(new(id, "memory", "内存插槽 " + (module["DeviceLocator"]?.ToString()?.Trim() ?? "未命名"), "SMBIOS安装容量与模组信息",
                        [detailsCache.Capture(id, "安装容量", "GiB", bytes > 0 ? DetailFormatting.ToGiB(bytes) : null, "Windows Win32_PhysicalMemory.Capacity", isStatic: true)],
                        $"厂商：{module["Manufacturer"]?.ToString()?.Trim()}\n型号：{module["PartNumber"]?.ToString()?.Trim()}"));
                }
            }
            inventoryNote = totalBytes > 0 ? $"安装容量合计 {totalBytes / 1073741824d:0.0} GiB（Windows/SMBIOS）；与系统可用物理总量分开。" : "SMBIOS安装容量未读取";
            if (totalBytes > 0) memoryInventory.Insert(0, new("installed-memory", "memory", "安装内存", "Windows/SMBIOS枚举合计",
                [detailsCache.Capture("installed-memory", "安装容量合计", "GiB", DetailFormatting.ToGiB(totalBytes), "Windows Win32_PhysicalMemory.Capacity 合计", isStatic: true)], inventoryNote));
        }
        catch (Exception error) { inventoryNote = "内存模组清单读取失败：" + error.Message; }
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_VideoController");
            using var adapters = searcher.Get();
            var names = new List<string>();
            foreach (ManagementObject adapter in adapters)
            {
                using (adapter)
                    if (adapter["PNPDeviceID"]?.ToString()?.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) == true)
                        names.Add($"{adapter["Name"]}（Windows设备状态码{adapter["ConfigManagerErrorCode"]}）");
            }
            intelInventory = string.Join("；", names);
        }
        catch (Exception error) { intelInventory = "Windows图形设备清单暂不可用：" + error.Message; }
    }

    private void Put(MetricId id, double? value, string source, string failure)
    {
        if (value is double number && double.IsFinite(number))
            readings[id] = new(value, source, DateTimeOffset.Now, System.Diagnostics.Stopwatch.GetTimestamp(), null);
        else
            readings[id] = readings.TryGetValue(id, out var previous) ? previous with { Failure = failure }
                : new(null, source, null, 0, failure);
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
        frames.Dispose(); activity.Dispose(); nvme.Dispose();
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
    private MemoryV2Delegate? getMemoryV2;
    private MemoryDelegate? getMemory;
    private UIntDelegate? getPower;
    private UIntDelegate? getFan;
    private SamplingDelegate? getEncoder;
    private SamplingDelegate? getDecoder;
    private ClockDelegate? getClock;
    private InitDelegate? shutdown;
    public string Source { get; private set; } = "NVML · RTX 4080 SUPER（未找到）";

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
            getMemoryV2 = Optional<MemoryV2Delegate>("nvmlDeviceGetMemoryInfo_v2");
            getMemory = Optional<MemoryDelegate>("nvmlDeviceGetMemoryInfo");
            getPower = Optional<UIntDelegate>("nvmlDeviceGetPowerUsage");
            getFan = Optional<UIntDelegate>("nvmlDeviceGetFanSpeed");
            getEncoder = Optional<SamplingDelegate>("nvmlDeviceGetEncoderUtilization");
            getDecoder = Optional<SamplingDelegate>("nvmlDeviceGetDecoderUtilization");
            getClock = Optional<ClockDelegate>("nvmlDeviceGetClockInfo");
            if (getCount(out var count) != 0) return;
            for (uint index = 0; index < Math.Min(count, 32); index++)
            {
                if (getHandle(index, out var handle) != 0) continue;
                var name = new StringBuilder(256);
                if (getName(handle, name, 256) == 0 && name.ToString().Contains("4080 SUPER", StringComparison.OrdinalIgnoreCase))
                { device = handle; Source = "NVML · " + name; break; }
            }
        }
        catch { Dispose(); }
    }

    private T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    private T? Optional<T>(string name) where T : Delegate => NativeLibrary.TryGetExport(library, name, out var pointer) ? Marshal.GetDelegateForFunctionPointer<T>(pointer) : null;

    public DetailDevice ReadDetails(DetailCache cache)
    {
        var metrics = new List<DetailMetric>();
        double? total = null, used = null, free = null, reserved = null;
        var error = device == IntPtr.Zero ? -1 : 3;
        var v2 = false;
        if (device != IntPtr.Zero)
        {
            if (getMemoryV2 is not null)
            {
                var memory = new MemoryV2 { Version = (uint)Marshal.SizeOf<MemoryV2>() | (2u << 24) };
                error = getMemoryV2(device, ref memory);
                if (error == 0) { total = memory.Total; used = memory.Used; free = memory.Free; reserved = memory.Reserved; v2 = true; }
            }
            if (!v2 && getMemory is not null && (getMemoryV2 is null || error is 3 or 25))
            {
                error = getMemory(device, out var memory);
                if (error == 0) { total = memory.Total; used = memory.Used; free = memory.Free; }
            }
        }
        if (error == 0 && (total is null or <= 0 || used > total || free > total || reserved > total)) { total = used = free = reserved = null; error = -2; }
        var memoryNote = v2 ? "NVML v2统计；已用合计=已分配+系统保留，分项另列。WDDM由Windows管理，不是进程显存之和。"
            : "NVML v1兼容统计；WDDM由Windows管理，不是进程显存之和。";
        var occupied = v2 ? used + reserved : used;
        if (occupied > total) { occupied = null; error = -2; }
        Add("vram-used", "显存已用（含系统保留）", "GiB", DetailFormatting.ToGiB(occupied), error, memoryNote);
        if (v2) Add("vram-allocated", "显存已分配", "GiB", DetailFormatting.ToGiB(used), error);
        Add("vram-total", "显存总量", "GiB", DetailFormatting.ToGiB(total), error);
        Add("vram-free", "显存剩余", "GiB", DetailFormatting.ToGiB(free), error);
        Add("vram-percent", "显存使用率（含系统保留）", "%", DetailFormatting.Percent(occupied, total), error);
        if (v2) Add("vram-reserved", "显存系统保留", "GiB", DetailFormatting.ToGiB(reserved), error);
        ReadUInt(getPower, "gpu-power", "GPU板卡功耗（1秒平均）", "W", .001, "RTX 4080 SUPER：NVML返回约1秒平均板卡功耗，包含关联电路；不是整机功耗。");
        ReadUInt(getFan, "gpu-fan", "GPU风扇相对速度", "%", 1, "NVML百分比，不是RPM；仅作只读监测。");
        ReadSampling(getEncoder, "gpu-encode", "视频编码引擎");
        ReadSampling(getDecoder, "gpu-decode", "视频解码引擎");
        foreach (var (domain, id, label) in new[] { (0u, "gpu-clock", "GPU报告时钟"), (2u, "gpu-memory-clock", "显存报告时钟") })
        {
            uint value = 0;
            var result = device == IntPtr.Zero ? -1 : getClock?.Invoke(device, domain, out value) ?? 3;
            Add(id, label, "MHz", result == 0 ? value : null, result, "NVML时钟域读数；显存时钟不作为有效数据率。");
        }
        return new("nvidia", "nvidia", Source.Replace("NVML · ", ""), device == IntPtr.Zero ? "未找到指定独显" : "独显扩展遥测 · NVML", metrics.ToArray(), memoryNote);
        void Add(string id, string label, string unit, double? value, int result, string? note = null)
        {
            metrics.Add(cache.Capture("nvidia/" + id, label, unit, value, Source,
                result == 0 ? null : NvmlError(result), result == 3 ? DetailAvailability.Unsupported : DetailAvailability.Unavailable, note));
        }
        void ReadUInt(UIntDelegate? function, string id, string label, string unit, double factor, string? note)
        {
            uint value = 0;
            var result = device == IntPtr.Zero ? -1 : function?.Invoke(device, out value) ?? 3;
            Add(id, label, unit, result == 0 ? value * factor : null, result, note);
        }
        void ReadSampling(SamplingDelegate? function, string id, string label)
        {
            uint value = 0, period = 0;
            var result = device == IntPtr.Zero ? -1 : function?.Invoke(device, out value, out period) ?? 3;
            Add(id, label, "%", result == 0 && value <= 100 ? value : null, result, result == 0 ? $"NVML采样窗口 {period / 1000d:0.0} ms；不同引擎不相加。" : null);
        }
    }
    private static string NvmlError(int result) => result switch
    {
        3 => "驱动或硬件不支持此指标", 4 => "NVML访问权限不足", 15 => "NVML设备连接已丢失",
        -1 => "未找到指定的RTX 4080 SUPER", -2 => "NVML返回的显存边界无效", _ => "NVML读取失败，错误码 " + result
    };

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
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryV2 { public uint Version; public ulong Total, Reserved, Free, Used; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryV1 { public ulong Total, Free, Used; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CountDelegate(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HandleDelegate(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NameDelegate(IntPtr device, StringBuilder name, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TemperatureDelegate(IntPtr device, uint sensor, out uint temperature);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UtilizationDelegate(IntPtr device, out Utilization utilization);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MemoryV2Delegate(IntPtr device, ref MemoryV2 memory);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MemoryDelegate(IntPtr device, out MemoryV1 memory);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UIntDelegate(IntPtr device, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SamplingDelegate(IntPtr device, out uint value, out uint period);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ClockDelegate(IntPtr device, uint type, out uint value);
}

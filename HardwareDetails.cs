using System.Diagnostics;
using System.Globalization;
using LibreHardwareMonitor.Hardware;

namespace SamaLite;

internal enum DetailAvailability { Available, Unavailable, Unsupported }
internal sealed record DetailMetric(string Id, string Label, string Unit, MetricReading Reading,
    DetailAvailability Availability, string? Note = null, bool IsStatic = false);
internal sealed record DetailDevice(string Id, string Category, string Name, string Status, DetailMetric[] Metrics, string? Note = null);
internal sealed record HardwareDetails(DateTimeOffset Timestamp, long Tick, DetailDevice[] Devices, int PollSeconds = 5);

internal sealed class DetailCache
{
    private readonly Dictionary<string, MetricReading> previous = new();
    public DetailMetric Capture(string id, string label, string unit, double? value, string source, string? failure = null,
        DetailAvailability missing = DetailAvailability.Unavailable, string? note = null, bool isStatic = false)
    {
        MetricReading reading;
        if (value is double number && double.IsFinite(number) && failure is null)
            reading = new(number, source, DateTimeOffset.Now, Stopwatch.GetTimestamp(), null);
        else
            reading = previous.TryGetValue(id, out var old) && old.Source == source
                ? old with { Failure = failure ?? "暂未取得有效读数" }
                : new(null, source, null, 0, failure ?? "暂未取得有效读数");
        previous[id] = reading;
        return new(id, label, unit, reading, reading.Failure is null ? DetailAvailability.Available : missing, note, isStatic);
    }
}

internal static class DetailFormatting
{
    public static string Value(DetailMetric metric, long now, int pollSeconds = 5)
    {
        var reading = metric.Reading;
        if (metric.Availability != DetailAvailability.Available || reading.Failure is not null)
            return "—" + (metric.Unit.Length > 0 ? " " + metric.Unit : "") + " · "
                + (metric.Availability == DetailAvailability.Unsupported ? "不支持：" : "暂不可用：") + reading.Failure
                + (reading.SampledAt.HasValue ? $"；上次有效值 {DisplayRenderer.Format(reading.Value, "0.0")}{metric.Unit}，{reading.Age(now):0}秒前" : "");
        var value = reading.Value?.ToString(metric.Unit == "%" ? "0" : "0.0", CultureInfo.CurrentCulture) ?? "—";
        var age = reading.Age(now);
        return value + (metric.Unit.Length > 0 ? "\u00a0" + metric.Unit : "")
            + (metric.IsStatic ? " · 启动读取" : reading.IsFresh(now, pollSeconds) ? $" · {age:0}秒前" : $" · 旧值，{age:0}秒前");
    }
    public static string IntegratedStatus(IEnumerable<DetailMetric> readings)
    {
        var engines = readings.Where(m => m.Unit == "%" && m.Availability == DetailAvailability.Available && m.Reading.Value.HasValue).ToArray();
        return engines.Length == 0 ? "已枚举核显；引擎活动暂不可用"
            : engines.Any(m => m.Reading.Value > 0) ? "已枚举核显；本次采样存在引擎活动" : "已枚举核显；本次采样引擎均为0%";
    }
    public static double? ToGiB(double? bytes) => bytes is >= 0 && double.IsFinite(bytes.Value) ? bytes / 1073741824d : null;
    public static double? Percent(double? used, double? total) => used is >= 0 && total is > 0 && double.IsFinite(used.Value) && double.IsFinite(total.Value) && used <= total ? used / total * 100 : null;
}

internal static class HardwareSensorRules
{
    public static string Label(ISensor sensor)
    {
        if (sensor.Name == "D3D Shared Memory Used") return "共享内存已用";
        if (sensor.Name == "D3D Shared Memory Total") return "共享内存上限（D3D报告）";
        if (sensor.SensorType == SensorType.Load && sensor.Name.StartsWith("D3D ", StringComparison.Ordinal))
        {
            var name = sensor.Name[4..].Trim() switch
            {
                "3D" => "3D引擎", "Copy" => "复制引擎", "Video Decode" => "视频解码引擎", "Video Encode" => "视频编码引擎",
                "Video Processing" => "视频处理引擎", "Overlay" => "叠加引擎", "" => "未命名引擎", var other => other
            };
            return name + " #" + sensor.Index;
        }
        return sensor.Name;
    }
    // LHM also exposes temperature resolution and threshold constants as Temperature.
    // Only the live DIMM sensor (index 0, UpdateSensor method) is a measurement.
    public static bool IsLiveDimmTemperature(ISensor sensor) => sensor.SensorType == SensorType.Temperature && sensor.Index == 0
        && sensor.GetType().GetMethod("UpdateSensor", Type.EmptyTypes) is not null;
    public static bool IsIntegratedIntel(IHardware hardware) => hardware.HardwareType == HardwareType.GpuIntel
        && hardware.Identifier.ToString().StartsWith("/gpu-intel-integrated/", StringComparison.Ordinal);
    public static string Unit(SensorType type) => type switch
    {
        SensorType.Temperature => "°C", SensorType.Load => "%", SensorType.Clock => "MHz", SensorType.Power => "W",
        SensorType.SmallData => "GiB", SensorType.Data => "GiB", _ => ""
    };
    public static double? Normalize(SensorType type, double? value) => value is not double number || !double.IsFinite(number) ? null : type switch
    {
        SensorType.Temperature => number is >= -20 and <= 125 ? number : null,
        SensorType.Load => number is >= 0 and <= 100 ? number : null,
        SensorType.SmallData => number >= 0 ? number / 1024d : null,
        _ => number >= 0 ? number : null
    };
}

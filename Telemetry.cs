using System.Diagnostics;
using System.Text.Json.Serialization;

namespace SamaLite;

internal enum MetricId { CpuUsage, CpuTemperature, GpuUsage, GpuTemperature, MemoryUsed, MemoryTotal }

internal sealed record MetricReading(double? Value, string Source, DateTimeOffset? SampledAt, long SampleTick, string? Failure)
{
    public double Age(long now) => SampledAt is null ? double.PositiveInfinity : Math.Max(0, (now - SampleTick) / (double)Stopwatch.Frequency);
    public bool IsFresh(long now, int interval) => Value.HasValue && Failure is null && Age(now) <= Math.Max(3 * interval, 5);
}

internal sealed record HistoryPoint(DateTimeOffset Time, long Tick, double? CpuUsage, double? GpuUsage, double? CpuTemperature, double? GpuTemperature);

internal sealed record Metrics(DateTimeOffset Timestamp, IReadOnlyDictionary<MetricId, MetricReading> Readings, HistoryPoint[] History, string? Attention = null, HardwareDetails? Details = null, FrameActivity? FrameActivity = null)
{
    public MetricReading this[MetricId id] => Readings[id];
    [JsonIgnore] public double? CpuTemperature => this[MetricId.CpuTemperature].Failure is null ? this[MetricId.CpuTemperature].Value : null;
    [JsonIgnore] public double? CpuUsage => this[MetricId.CpuUsage].Failure is null ? this[MetricId.CpuUsage].Value : null;
    [JsonIgnore] public double? GpuTemperature => this[MetricId.GpuTemperature].Failure is null ? this[MetricId.GpuTemperature].Value : null;
    [JsonIgnore] public double? GpuUsage => this[MetricId.GpuUsage].Failure is null ? this[MetricId.GpuUsage].Value : null;
    [JsonIgnore] public double? MemoryUsedGiB => this[MetricId.MemoryUsed].Failure is null ? this[MetricId.MemoryUsed].Value : null;
    [JsonIgnore] public double? MemoryTotalGiB => this[MetricId.MemoryTotal].Failure is null ? this[MetricId.MemoryTotal].Value : null;
    [JsonIgnore] public double? MemoryPercent => MemoryTotalGiB is > 0 && MemoryUsedGiB.HasValue ? MemoryUsedGiB / MemoryTotalGiB * 100 : null;
    [JsonIgnore] public string? SensorWarning => Readings.Values.FirstOrDefault(r => r.Failure is not null)?.Failure;
    public double? Fresh(MetricId id, int interval, long now) => this[id].IsFresh(now, interval) ? this[id].Value : null;
    public double? FreshMemoryPercent(int interval, long now) => Fresh(MetricId.MemoryTotal, interval, now) is > 0 and var total
        && Fresh(MetricId.MemoryUsed, interval, now) is double used ? used / total * 100 : null;
    public DateTimeOffset? DisplaySampleTime(int interval, long now) => Readings.Where(p => p.Key != MetricId.MemoryTotal && p.Value.IsFresh(now, interval))
        .Select(p => p.Value.SampledAt).DefaultIfEmpty().Min();
    public string Summary(int interval, long now)
    {
        if (Readings.Values.All(r => r.Value.HasValue && r.Failure is null && !r.IsFresh(now, interval))) return "读数已过期";
        foreach (var pair in Readings)
        {
            if (pair.Key == MetricId.MemoryTotal) continue;
            if (pair.Value.Failure is not null || !pair.Value.Value.HasValue) return Name(pair.Key) + "未读";
            if (!pair.Value.IsFresh(now, interval)) return Name(pair.Key) + "过期";
        }
        return Attention is null ? "正常更新" : Attention.Contains('；') ? "多项指标需关注" : Attention;
    }
    public static string Name(MetricId id) => id switch
    {
        MetricId.CpuUsage => "CPU负载", MetricId.CpuTemperature => "CPU温度", MetricId.GpuUsage => "GPU负载",
        MetricId.GpuTemperature => "GPU温度", MetricId.MemoryUsed => "内存用量", _ => "内存总量"
    };
}

internal sealed class TelemetryHistory
{
    private readonly Queue<HistoryPoint> points = new();
    public HistoryPoint[] Add(Metrics metrics, long tick)
    {
        points.Enqueue(new(metrics.Timestamp, tick, metrics.CpuUsage, metrics.GpuUsage, metrics.CpuTemperature, metrics.GpuTemperature));
        while (points.Count > 0 && tick - points.Peek().Tick > 120L * Stopwatch.Frequency) points.Dequeue();
        return points.ToArray();
    }
}

internal sealed class AttentionTracker
{
    private readonly long?[] entered = new long?[3];
    private readonly long?[] recovered = new long?[3];
    private readonly bool[] active = new bool[3];
    private long previousTick;
    public string? Update(Metrics metrics, SettingsSnapshot settings, long tick)
    {
        double?[] values = [metrics.CpuTemperature, metrics.GpuTemperature, metrics.MemoryPercent];
        double[] thresholds = [settings.CpuAttention, settings.GpuAttention, settings.MemoryAttention];
        int[] durations = [15, 15, 30];
        string[] messages = ["CPU温度持续偏高", "GPU温度持续偏高", "内存余量较少"];
        var gap = previousTick != 0 && (tick - previousTick) / (double)Stopwatch.Frequency > Math.Max(3 * settings.RefreshSeconds, 5);
        previousTick = tick;
        for (var i = 0; i < 3; i++)
        {
            if (!settings.AttentionEnabled || gap || values[i] is null) { entered[i] = recovered[i] = null; active[i] = false; }
            if (!settings.AttentionEnabled || values[i] is not double value) continue;
            if (!active[i])
            {
                if (value < thresholds[i]) { entered[i] = null; continue; }
                entered[i] ??= tick;
                if ((tick - entered[i]!.Value) / (double)Stopwatch.Frequency >= durations[i]) active[i] = true;
            }
            else
            {
                if (value > thresholds[i] - 5) { recovered[i] = null; continue; }
                recovered[i] ??= tick;
                if ((tick - recovered[i]!.Value) / (double)Stopwatch.Frequency >= 30) { active[i] = false; entered[i] = recovered[i] = null; }
            }
        }
        return string.Join("；", messages.Where((_, i) => active[i])) is { Length: > 0 } result ? result : null;
    }
}

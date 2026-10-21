using System.Globalization;

namespace SamaLite;

internal readonly record struct PanelValue(MetricGlyph Glyph, Color Color, double? Value, string Unit, string Caption)
{
    private bool Kibi => Unit.StartsWith("MiB/s", StringComparison.Ordinal) && Value is > 0 and < 1;
    private double? DisplayValue => Kibi ? Value * 1024 : Value;
    public string Digits => DisplayValue?.ToString(DisplayValue >= 10000 ? "0," : (Unit == "ms" && DisplayValue < 100) || (Unit.StartsWith("MiB/s", StringComparison.Ordinal) && DisplayValue < 10) ? "0.0" : "0", CultureInfo.InvariantCulture) ?? "—";
    public string Suffix => (DisplayValue >= 10000 ? "k " : "") + (Kibi ? Unit.Replace("MiB/s", "KiB/s", StringComparison.Ordinal) : Unit);
}

internal static class PanelMetrics
{
    public static PanelValue Get(string slot, Metrics metrics, SettingsSnapshot config, long now, CompactHardware? hardware = null)
    {
        var details = metrics.Details;
        double? Take(DetailMetric? metric, int poll = 5) => metric?.Availability == DetailAvailability.Available && metric.Reading.IsFresh(now, poll) ? metric.Reading.Value : null;
        double? Id(string id, int poll = 1) => Take(details?.Devices.SelectMany(d => d.Metrics).FirstOrDefault(m => m.Id == id), poll);
        double? Fan(string id, string exact)
        {
            var fans = details?.Devices.Where(d => d.Category == "cooling").SelectMany(d => d.Metrics).ToArray() ?? [];
            // Generic Fan # labels do not establish which physical header is connected.
            return Take(id.Length > 0 ? fans.FirstOrDefault(m => m.Id == id) : fans.FirstOrDefault(m => m.Label.Equals(exact, StringComparison.OrdinalIgnoreCase) || (exact == "Pump" && m.Label.Equals("Pump Fan", StringComparison.OrdinalIgnoreCase))));
        }
        var extra = hardware ?? CompactHardware.From(metrics, now);
        var blue = Ui.Cpu; var green = Ui.Accent; var amber = Ui.Warning;
        return slot switch
        {
            "fps" => new(MetricGlyph.Game, green, Id("game/fps"), "FPS", "应用 Present FPS"),
            "frametime" => new(MetricGlyph.Frame, green, Id("game/frametime"), "ms", "平均帧时间 · 最近1秒"),
            "low" => new(MetricGlyph.Low, green, Id("game/low"), "1% FPS", "1% Low · 最慢1%帧平均间隔的倒数"),
            "fan" => new(MetricGlyph.Fan, blue, Fan(config.CpuFanSensorId, "CPU Fan"), "RPM", "CPU风扇 · 需映射实际接口"),
            "pump" => new(MetricGlyph.Pump, blue, Fan(config.PumpSensorId, "Pump"), "RPM", "水泵 · 需映射实际接口"),
            "nvme" => new(MetricGlyph.Nvme, amber, Take(details?.Devices.FirstOrDefault(d => config.NvmeDeviceId.Length > 0 ? d.Id == config.NvmeDeviceId : d.Id.StartsWith("nvme/", StringComparison.Ordinal))?.Metrics.FirstOrDefault(m => m.Unit == "°C"), 10), "°C", "所选NVMe复合温度"),
            "download" => new(MetricGlyph.Download, green, Id("io/download"), "MiB/s ↓", "所选网卡下载"),
            "upload" => new(MetricGlyph.Upload, green, Id("io/upload"), "MiB/s ↑", "所选网卡上传"),
            "disk-read" => new(MetricGlyph.DiskRead, amber, Id("io/disk-read"), "MiB/s R", "全部物理磁盘读取"),
            "disk-write" => new(MetricGlyph.DiskWrite, amber, Id("io/disk-write"), "MiB/s W", "全部物理磁盘写入"),
            "disk-busy" => new(MetricGlyph.Disk, amber, Id("io/disk-busy"), "%", "全部物理磁盘活动时间"),
            "dimm" => new(MetricGlyph.Thermometer, green, extra.MemoryTemperatures.Where(v => v.HasValue).DefaultIfEmpty().Max(), "°C RAM", "最热DIMM温度"),
            "igpu" => new(MetricGlyph.Integrated, blue, extra.IntegratedUsage, "% iGPU", "核显最大引擎占用"),
            "cpu-power" => new(MetricGlyph.Bolt, blue, extra.CpuPower, "W CPU", "CPU Package功耗"),
            "gpu-power" => new(MetricGlyph.Bolt, Ui.Gpu, extra.GpuPower, "W GPU", "GPU板卡功耗"),
            _ => new(MetricGlyph.Info, Ui.Muted, null, "", "未知指标")
        };
    }
}

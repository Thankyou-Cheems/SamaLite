using System.Diagnostics;

namespace SamaLite;

internal sealed class HardwareDetailsView : VerticalStack
{
    private readonly Label freshness = Ui.Label("扩展指标每5秒采样", 9.75f, Ui.Muted);
    private readonly Dictionary<string, DetailDeviceCard> cards = new();
    private HardwareDetails? latest;
    public HardwareDetailsView() { Controls.Add(freshness); }
    public void UpdateDetails(HardwareDetails? details)
    {
        latest = details;
        if (!Visible) return;
        Render();
    }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) Render(); }
    private void Render()
    {
        if (latest is null) { freshness.Text = "等待扩展指标初始化…"; return; }
        var now = Stopwatch.GetTimestamp();
        var age = Math.Max(0, (now - latest.Tick) / (double)Stopwatch.Frequency);
        freshness.Text = $"↻ {latest.PollSeconds}s　◷ {latest.Timestamp:HH:mm:ss}　{age:0}s";
        SuspendLayout();
        try
        {
            var devices = latest.Devices.GroupBy(d => d.Category).Select(group =>
            {
                var items = group.ToArray();
                var name = group.Key switch { "memory" => "RAM", "nvidia" => "GPU · " + items[0].Name.Replace("NVIDIA GeForce ", "", StringComparison.Ordinal), "igpu" => "iGPU", "cooling" => "风扇 / 水泵接口", "storage" => "NVMe / 磁盘活动", "network" => "网络", "game" => "应用帧表现", _ => "CPU" };
                var values = items.SelectMany(device => device.Metrics.Select(metric => metric with
                {
                    Label = group.Key == "memory" ? MemoryCaption(device, metric) : metric.Label,
                    Note = device.Name + "\n" + metric.Note
                })).ToArray();
                return new DetailDevice("group/" + group.Key, group.Key, name, string.Join("；", items.Select(d => d.Status).Distinct()), values,
                    string.Join("\n\n", items.Select(d => d.Name + "\n" + d.Note)));
            }).OrderBy(d => CategoryOrder(d.Category)).ToArray();
            var currentIds = devices.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in cards.Keys.Where(id => !currentIds.Contains(id)).ToArray())
            { Controls.Remove(cards[id]); cards[id].Dispose(); cards.Remove(id); }
            foreach (var device in devices)
            {
                if (!cards.TryGetValue(device.Id, out var card))
                {
                    card = new DetailDeviceCard(device.Category != "cpu"); cards.Add(device.Id, card); Controls.Add(card);
                }
                card.UpdateDevice(device, now, latest.PollSeconds);
            }
        }
        finally { ResumeLayout(true); }
    }
    private static int CategoryOrder(string category) => category switch { "memory" => 0, "nvidia" => 1, "igpu" => 2, _ => 3 };
    private static string MemoryCaption(DetailDevice device, DetailMetric metric) => metric.Unit == "°C"
        ? metric.Reading.Source.Contains("DIMM #", StringComparison.Ordinal) ? metric.Reading.Source[metric.Reading.Source.LastIndexOf("DIMM #", StringComparison.Ordinal)..] : device.Name
        : device.Id == "installed-memory" ? "安装容量 Σ" : device.Name.Replace("内存插槽 ", "", StringComparison.Ordinal).Replace("Controller", "C", StringComparison.Ordinal);
}

internal sealed class DetailDeviceCard : VerticalStack
{
    private readonly Button title;
    private readonly CompactMetricGrid rows = new();
    private readonly ToolTip hints = new() { AutoPopDelay = 20000 };
    private bool expanded;
    private bool hasMetrics;
    public DetailDeviceCard(bool expanded) : base(true)
    {
        this.expanded = expanded;
        title = Ui.Button("硬件详情 ▾", () => { this.expanded = !this.expanded; rows.Visible = this.expanded && hasMetrics; });
        Controls.Add(title); Controls.Add(rows); rows.Visible = expanded;
    }
    public void UpdateDevice(DetailDevice device, long now, int pollSeconds)
    {
        title.Text = device.Name + " ▾";
        if (device.Metrics.Length == 0 || device.Metrics.All(m => m.Availability != DetailAvailability.Available)) title.Text = "⚠ " + title.Text;
        hints.SetToolTip(title, device.Status + "\n" + device.Note);
        hasMetrics = device.Metrics.Length > 0; rows.Visible = expanded && hasMetrics;
        rows.UpdateMetrics(device.Metrics, pollSeconds);
    }
    protected override void Dispose(bool disposing) { if (disposing) hints.Dispose(); base.Dispose(disposing); }
}

internal static class HardwareSummary
{
    public static string Nvidia(HardwareDetails? details, long now)
    {
        var gpu = details?.Devices.FirstOrDefault(d => d.Id == "nvidia");
        if (gpu is null) return "显存与功耗：等待扩展采样";
        var used = Find(gpu, "nvidia/vram-used", now);
        var total = Find(gpu, "nvidia/vram-total", now);
        var power = Find(gpu, "nvidia/gpu-power", now);
        return $"显存 {DisplayRenderer.Format(used, "0.0")} / {DisplayRenderer.Format(total, "0.0")}\u00a0GiB（含系统保留）\nGPU板卡功耗 {DisplayRenderer.Format(power, "0.0")}\u00a0W · 1秒平均";
    }
    public static string Cpu(HardwareDetails? details, long now)
    {
        var metric = details?.Devices.Where(d => d.Category == "cpu").SelectMany(d => d.Metrics).FirstOrDefault(m => m.Label == "CPU Package" && m.Unit == "W");
        return metric is null ? "CPU Package功耗：尚无读数" : "CPU Package功耗 " + DetailFormatting.Value(metric, now);
    }
    public static string Memory(HardwareDetails? details, long now)
    {
        if (details is null) return "内存温度：等待扩展采样";
        var temperatures = details.Devices.Where(d => d.Category == "memory").SelectMany(d => d.Metrics.Select(m => (Device: d, Metric: m)))
            .Where(p => p.Metric.Unit == "°C").ToArray();
        var installed = details.Devices.FirstOrDefault(d => d.Id == "installed-memory")?.Metrics.FirstOrDefault();
        var text = installed is null ? "安装容量：未读取" : "安装容量 " + DetailFormatting.Value(installed, now);
        return text + "\n" + (temperatures.Length == 0 ? "内存温度：未枚举到可用传感器" : string.Join("\n", temperatures.Select(p => p.Device.Name + "：" + DetailFormatting.Value(p.Metric, now))));
    }
    public static double? Find(DetailDevice device, string id, long now)
    {
        var metric = device.Metrics.FirstOrDefault(m => m.Id == id);
        return metric?.Availability == DetailAvailability.Available && metric.Reading.IsFresh(now, 5) ? metric.Reading.Value : null;
    }
}

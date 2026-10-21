using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace SamaLite;

internal abstract class TooltipCanvas : Control
{
    private readonly ToolTip tooltip = new() { AutoPopDelay = 20000, InitialDelay = 300, ReshowDelay = 100 };
    protected readonly List<(RectangleF Bounds, string Text)> Hints = new();
    private string previous = "";
    protected TooltipCanvas() { DoubleBuffered = true; BackColor = Ui.Background; ForeColor = Ui.Text; }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var scale = DeviceDpi / 96f; var point = new PointF(e.X / scale, e.Y / scale);
        var hint = Hints.LastOrDefault(h => h.Bounds.Contains(point)).Text ?? "";
        if (hint != previous) { tooltip.SetToolTip(this, hint); previous = hint; }
    }
    protected override void Dispose(bool disposing) { if (disposing) tooltip.Dispose(); base.Dispose(disposing); }
    protected static void DrawText(Graphics g, string value, float size, Color color, RectangleF bounds, bool right = false, bool bold = false)
    {
        using var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        // Rectangle layout can silently omit the last digit when GDI+ includes its
        // padding in the fit calculation. Anchor the complete measured string instead.
        var measured = g.MeasureString(value, font, int.MaxValue, format);
        g.DrawString(value, font, brush, new PointF(right ? bounds.Right - measured.Width : bounds.X,
            bounds.Y + (bounds.Height - measured.Height) / 2), format);
    }
    protected static void Value(Graphics g, double? value, string unit, float x, float y, float width, float size, Color color, string format = "0")
    {
        DrawText(g, DisplayRenderer.Format(value, format), size, color, new(x, y, width, size * 1.35f), true, true);
        DrawText(g, unit, Math.Max(12, size * .32f), Ui.Muted, new(x + width + 3, y + size * .45f, 40, size * .65f));
    }
}

internal sealed class CompactOverview : TooltipCanvas
{
    private Metrics? metrics;
    private int interval = 1;
    public CompactOverview() { AccessibleName = "硬件概览"; Height = Ui.ScaleSize(this, 0, 568).Height; Margin = new(0, 0, 0, 8); }
    internal void UpdateMetrics(Metrics? value, int seconds)
    {
        metrics = value; interval = seconds;
        AccessibleDescription = value is null ? "等待采样" : string.Join("；", value.Readings.Select(r => Metrics.Name(r.Key) + " " + r.Value.Value + " " + r.Value.Failure));
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; var scale = DeviceDpi / 96f;
        g.ScaleTransform(scale, scale); g.SmoothingMode = SmoothingMode.AntiAlias; Hints.Clear();
        var width = Width / scale; if (width < 200) return;
        var now = Stopwatch.GetTimestamp(); var m = metrics;
        var extra = m is null ? new CompactHardware(null, null, null, null, null, [], null, null, null) : CompactHardware.From(m, now);
        MainRow(new(0, 0, width, 132), false); MainRow(new(0, 142, width, 132), true);
        var half = (width - 10) / 2;
        Capacity(new(0, 284, half, 158), false); Capacity(new(half + 10, 284, half, 158), true);
        var integrated = new RectangleF(0, 452, width, 104); Card(integrated);
        MetricGlyphs.Draw(g, MetricGlyph.Integrated, new(14, 462, 20, 20), Ui.Cpu);
        DrawText(g, "iGPU", 13, Ui.Cpu, new(40, 461, width - 55, 24), bold: true);
        Value(g, extra.IntegratedUsage, "%", 14, 483, 82, 44, Ui.Text);
        Value(g, extra.IntegratedMemory, "GiB", width * .39f, 492, 84, 27, Ui.Text, "0.00");
        var intel = m?.Details?.Devices.FirstOrDefault(d => d.Category == "igpu");
        var clock = intel?.Metrics.FirstOrDefault(v => v.Unit == "MHz");
        var clockValue = clock?.Reading.IsFresh(now, 5) == true && clock.Availability == DetailAvailability.Available ? clock.Reading.Value : null;
        Value(g, clockValue, "MHz", width - 132, 492, 78, 27, Ui.Text);
        Hints.Add((integrated, (intel?.Name ?? "CPU核显") + "\n" + (intel?.Status ?? "尚无遥测") + "\n最忙引擎 / 共享内存已用 / 报告时钟；引擎利用率不相加。"));
        void Card(RectangleF bounds) { using var brush = new SolidBrush(Ui.Surface); g.FillRectangle(brush, bounds); }
        void MainRow(RectangleF r, bool gpu)
        {
            Card(r); var color = gpu ? Ui.Gpu : Ui.Cpu; var usage = gpu ? MetricId.GpuUsage : MetricId.CpuUsage; var temp = gpu ? MetricId.GpuTemperature : MetricId.CpuTemperature;
            MetricGlyphs.Draw(g, gpu ? MetricGlyph.Gpu : MetricGlyph.Cpu, new(14, r.Y + 10, 20, 20), color);
            DrawText(g, gpu ? "GPU" : "CPU", 14, color, new(40, r.Y + 8, 90, 24), bold: true);
            var peak = m?.History.Where(p => (now - p.Tick) / (double)Stopwatch.Frequency <= 120).Select(p => gpu ? p.GpuTemperature : p.CpuTemperature).Max();
            DrawText(g, "↑ " + DisplayRenderer.Format(peak) + "°C", 13, Ui.Muted, new(width - 122, r.Y + 8, 108, 24), true);
            var col = width * .32f;
            Value(g, m?.Fresh(usage, interval, now), "%", 14, r.Y + 29, Math.Min(118, col - 20), 58, Ui.Text);
            Value(g, m?.Fresh(temp, interval, now), "°C", col + 12, r.Y + 34, Math.Min(102, col - 20), 50, Ui.Text);
            MetricGlyphs.Draw(g, MetricGlyph.Bolt, new(width - 114, r.Y + 37, 15, 18), color);
            Value(g, gpu ? extra.GpuPower : extra.CpuPower, "W", width - 112, r.Y + 48, 66, 27, Ui.Text);
            var coreClock = gpu ? m?.Details?.Devices.FirstOrDefault(d => d.Id == "nvidia")?.Metrics.FirstOrDefault(v => v.Id == "nvidia/gpu-clock")
                : m?.Details?.Devices.Where(d => d.Category == "cpu").SelectMany(d => d.Metrics).Where(v => v.Unit == "MHz" && v.Availability == DetailAvailability.Available && v.Reading.IsFresh(now, 5)).OrderByDescending(v => v.Reading.Value).FirstOrDefault();
            var ghz = coreClock?.Reading.IsFresh(now, 5) == true ? coreClock.Reading.Value / 1000 : null;
            MetricGlyphs.Draw(g, MetricGlyph.Clock, new(width - 114, r.Y + 90, 13, 13), Ui.Muted);
            DrawText(g, DisplayRenderer.Format(ghz, "0.00") + " GHz", 14, Ui.Muted, new(width - 96, r.Y + 84, 82, 24), true);
            if (m is not null) DisplayRenderer.DrawTrend(g, new(16, r.Y + 104, width - 148, 12), m.History, p => gpu ? p.GpuUsage : p.CpuUsage, color, now, interval);
            MetricGlyphs.Bar(g, new(16, r.Y + 122, width - 32, 4), m?.Fresh(usage, interval, now), color);
            Hints.Add((r, (gpu ? "GPU" : "CPU") + " · " + Ui.Reading(m, usage, interval, "%") + "\n" + Ui.Reading(m, temp, interval, "°C") + "\n负载条0–100%；趋势120秒；↑为120秒温度采样峰值；功耗与报告时钟来自扩展采样。"));
        }
        void Capacity(RectangleF r, bool vram)
        {
            Card(r); var color = vram ? Ui.Gpu : Ui.Accent;
            MetricGlyphs.Draw(g, MetricGlyph.Memory, new(r.X + 14, r.Y + 10, 21, 20), color);
            DrawText(g, vram ? "VRAM" : "RAM", 14, color, new(r.X + 41, r.Y + 8, r.Width - 50, 24), bold: true);
            var percent = vram ? extra.VramPercent : m?.FreshMemoryPercent(interval, now);
            Value(g, percent, "%", r.X + 12, r.Y + 28, 100, 48, Ui.Text);
            var used = vram ? extra.VramUsed : m?.Fresh(MetricId.MemoryUsed, interval, now);
            var total = vram ? extra.VramTotal : m?.Fresh(MetricId.MemoryTotal, interval, now);
            DrawText(g, $"{DisplayRenderer.Format(used, "0.0")} / {DisplayRenderer.Format(total, "0.0")} GiB", 17, Ui.Muted, new(r.X + 16, r.Y + 90, r.Width - 24, 25));
            MetricGlyphs.Bar(g, new(r.X + 16, r.Y + 119, r.Width - 32, 4), percent, color);
            if (!vram)
            {
                MetricGlyphs.Draw(g, MetricGlyph.Thermometer, new(r.X + 14, r.Y + 131, 15, 19), color);
                DrawText(g, (extra.MemoryTemperatures.Length == 0 ? "— / —" : string.Join(" / ", extra.MemoryTemperatures.Select(v => DisplayRenderer.Format(v, "0.0")))) + " °C", 17, Ui.Text, new(r.X + 35, r.Y + 128, r.Width - 44, 26));
            }
            else DrawText(g, "R " + DisplayRenderer.Format(m?.Details?.Devices.FirstOrDefault(d => d.Id == "nvidia") is DetailDevice device ? HardwareSummary.Find(device, "nvidia/vram-reserved", now) : null, "0.00") + " GiB", 15, Ui.Muted, new(r.X + 16, r.Y + 129, r.Width - 30, 25));
            Hints.Add((r, vram ? "显存已用含系统保留；R为系统保留量。详细口径见硬件详情。" : HardwareSummary.Memory(m?.Details, now)));
        }
    }
}

internal sealed class CompactMetricGrid : TooltipCanvas
{
    private DetailMetric[] metrics = [];
    private int poll = 5;
    private int Columns => Math.Min(Math.Max(1, metrics.Length), Width / (DeviceDpi / 96f) >= 450 ? 3 : Width / (DeviceDpi / 96f) >= 300 ? 2 : 1);
    internal void UpdateMetrics(DetailMetric[] values, int seconds)
    {
        metrics = values; poll = seconds;
        AccessibleDescription = string.Join("；", values.Select(m => m.Label + " " + DetailFormatting.Value(m, Stopwatch.GetTimestamp(), seconds)));
        ResizeGrid(); Invalidate();
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); ResizeGrid(); }
    private void ResizeGrid()
    {
        var height = Ui.ScaleSize(this, 0, Math.Max(1, (metrics.Length + Columns - 1) / Columns) * 84).Height;
        if (Height != height) Height = height;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var scale = DeviceDpi / 96f; var g = e.Graphics; g.ScaleTransform(scale, scale); g.SmoothingMode = SmoothingMode.AntiAlias; Hints.Clear();
        var width = Width / scale; var columns = Columns; var cellWidth = width / columns; var now = Stopwatch.GetTimestamp();
        for (var i = 0; i < metrics.Length; i++)
        {
            var metric = metrics[i]; var r = new RectangleF(i % columns * cellWidth, i / columns * 84, cellWidth - 8, 78);
            using var brush = new SolidBrush(Ui.Raised); g.FillRectangle(brush, r);
            var fresh = metric.Availability == DetailAvailability.Available && (metric.IsStatic || metric.Reading.IsFresh(now, poll));
            var available = metric.Availability == DetailAvailability.Available && metric.Reading.Failure is null;
            var color = fresh ? metric.Unit == "%" ? Ui.Cpu : metric.Unit == "°C" ? Ui.Accent : Ui.Text : Ui.Warning;
            var glyph = !available ? MetricGlyph.Warning : !fresh ? MetricGlyph.Clock : metric.Unit == "°C" ? MetricGlyph.Thermometer : metric.Unit == "W" ? MetricGlyph.Bolt : metric.Unit == "GiB" ? MetricGlyph.Memory : MetricGlyph.Cpu;
            MetricGlyphs.Draw(g, glyph, new(r.X + 8, r.Y + 7, 14, 14), color);
            DrawText(g, Caption(metric.Label), 10.5f, Ui.Muted, new(r.X + 27, r.Y + 2, r.Width - 31, 27));
            var format = metric.Unit is "%" or "MHz" ? "0" : metric.Unit == "W" && metric.Reading.Value < 1 ? "0.000" : "0.0";
            var value = available ? metric.Reading.Value : null;
            Value(g, value, metric.Unit, r.X + 7, r.Y + 29, r.Width - 49, 28, color, format);
            if (!fresh && available) DrawText(g, "旧", 10, Ui.Warning, new(r.Right - 23, r.Y + 5, 18, 20));
            if (metric.Unit == "%") MetricGlyphs.Bar(g, new(r.X + 8, r.Bottom - 5, r.Width - 16, 3), fresh ? value : null, color);
            Hints.Add((r, metric.Label + "\n" + DetailFormatting.Value(metric, now, poll) + "\n" + metric.Reading.Source + "\n" + metric.Note));
        }
    }
    private static string Caption(string label) => label.Replace("CPU Core #", "C", StringComparison.Ordinal).Replace(" Thread #", "·T", StringComparison.Ordinal)
        .Replace("（含系统保留）", "", StringComparison.Ordinal).Replace("（1秒平均）", "", StringComparison.Ordinal).Replace("（D3D报告）", "", StringComparison.Ordinal);
}

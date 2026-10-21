using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SamaLite;

internal sealed class DisplayRenderer : IDisposable
{
    public Bitmap Canvas { get; } = new(480, 480, PixelFormat.Format32bppArgb);
    private readonly Graphics graphics;
    private readonly Font label = new("Segoe UI", 24, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font focus = new("Segoe UI", 42, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font temperature = new("Segoe UI", 74, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font small = new("Microsoft YaHei UI", 20, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font foot = new("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font memory = new("Segoe UI", 26, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font metric = new("Segoe UI", 30, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font unit = new("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font tiny = new("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly SolidBrush background = new(ColorTranslator.FromHtml("#101318"));
    private readonly SolidBrush primary = new(ColorTranslator.FromHtml("#F2F5F9"));
    private readonly SolidBrush muted = new(ColorTranslator.FromHtml("#B6C1D0"));
    private readonly SolidBrush cpu = new(ColorTranslator.FromHtml("#96BAFF"));
    private readonly SolidBrush gpu = new(ColorTranslator.FromHtml("#C7B6FF"));
    private readonly SolidBrush vram = new(Color.FromArgb(237, 159, 217));
    private readonly SolidBrush warning = new(ColorTranslator.FromHtml("#F4C17A"));
    private readonly StringFormat centered = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
    public static Rectangle[] DynamicRegions { get; } = Enumerable.Range(0, 6).Select(i => new Rectangle(24, 24 + i * 72, 432, 72)).ToArray();

    public DisplayRenderer()
    {
        graphics = Graphics.FromImage(Canvas);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }
    public void Draw(Metrics metrics, int interval = 1, string? state = null, DateTimeOffset? frozenAt = null, long? evaluationTick = null, SettingsSnapshot? configuration = null)
    {
        graphics.Clear(background.Color);
        var config = configuration ?? new Settings().Capture();
        var now = evaluationTick ?? Stopwatch.GetTimestamp();
        var summary = state ?? metrics.Summary(interval, now);
        var extra = CompactHardware.From(metrics, now);
        var stamp = (frozenAt ?? metrics.DisplaySampleTime(interval, now))?.ToString("HH:mm:ss") ?? "—";
        var normal = summary == "正常更新";
        var cpuUsage = metrics.Fresh(MetricId.CpuUsage, interval, now); var gpuUsage = metrics.Fresh(MetricId.GpuUsage, interval, now);
        var ram = metrics.FreshMemoryPercent(interval, now);
        Gauges(true, cpuUsage, ram, cpu.Color, Ui.Accent, config.Layout.Rings);
        Gauges(false, gpuUsage, extra.VramPercent, gpu.Color, vram.Color, config.Layout.Rings);
        MetricGlyphs.Draw(graphics, frozenAt.HasValue ? MetricGlyph.Pause : normal ? MetricGlyph.Check : MetricGlyph.Warning, new(190, 107, 13, 15), normal ? Ui.Accent : warning.Color);
        Text(stamp, tiny, normal ? muted : warning, new(208, 101, 89, 26));
        Row("CPU", 165, metrics.Fresh(MetricId.CpuTemperature, interval, now), extra.CpuPower, cpu);
        Row("GPU", 315, metrics.Fresh(MetricId.GpuTemperature, interval, now), extra.GpuPower, gpu);
        using var ramColor = new SolidBrush(Ui.Accent);
        Text("RAM", unit, ramColor, new(63, 235, 52, 19));
        Text("VRAM", unit, vram, new(365, 235, 52, 19));
        using (var separator = new Pen(Color.FromArgb(36, 182, 193, 208), 1))
        {
            graphics.DrawLine(separator, 191, 265, 191, 388); graphics.DrawLine(separator, 289, 265, 289, 388);
        }
        for (var i = 0; i < 12; i++)
        {
            var item = PanelMetrics.Get(config.Layout.Slots[i], metrics, config, now, extra);
            var x = i >= 9 ? 170 + i % 3 * 70 : 140 + i % 3 * 100; var y = 274 + i / 3 * 42;
            var center = i % 3 == 1 && i < 9;
            Text(item.Digits, center && item.Digits.Length <= 3 ? focus : i >= 9 ? memory : metric, primary, new(x - 42, y - 28, 84, 48));
            var suffixWidth = graphics.MeasureString(item.Suffix, unit, int.MaxValue, centered).Width;
            var start = x - (suffixWidth + 18) / 2;
            MetricGlyphs.Draw(graphics, item.Glyph, new(start, y + 12, 13, 13), item.Color);
            graphics.DrawString(item.Suffix, unit, muted, new PointF(start + 18 + suffixWidth / 2, y + 18), centered);
            if (item.Unit == "% iGPU" || item.Unit == "%") MetricGlyphs.Bar(graphics, new(x - 18, y + 28, 36, 3), item.Value, item.Color);
        }
        var busy = PanelMetrics.Get("disk-busy", metrics, config, now, extra);
        MetricGlyphs.Draw(graphics, MetricGlyph.Disk, new(188, 425, 17, 18), busy.Color);
        Text(busy.Digits + "%", foot, primary, new(211, 420, 90, 28));
        MetricGlyphs.Bar(graphics, new(199, 447, 82, 3), busy.Value, busy.Color);
    }
    public void DrawDynamic(Metrics metrics) => Draw(metrics);
    private void Gauges(bool left, double? outer, double? inner, Color outerColor, Color innerColor, bool bands)
    {
        if (!bands)
        {
            using var brush = new SolidBrush(outerColor);
            Text((left ? "CPU " : "GPU ") + Format(outer) + "%  " + (left ? "RAM " : "VRAM ") + Format(inner) + "%", unit, brush, new(left ? 100 : 240, 77, 140, 24));
            return;
        }
        UpperGauge.Draw(graphics, left, false, outer, outerColor); UpperGauge.Draw(graphics, left, true, inner, innerColor);
        var first = UpperGauge.Label(left, false, outer); var second = UpperGauge.Label(left, true, inner, first.Bounds);
        GaugeNumber(first, outer, outerColor); GaugeNumber(second, inner, innerColor);
    }
    private void GaugeNumber(GaugeLabel placement, double? value, Color color)
    {
        var b = placement.Bounds;
        using var leader = new Pen(color, 1); using var brush = new SolidBrush(color);
        graphics.DrawLine(leader, placement.Anchor, new(b.X + b.Width / 2, b.Y + b.Height / 2));
        graphics.FillRectangle(background, b); graphics.DrawRectangle(leader, b.X, b.Y, b.Width, b.Height);
        graphics.DrawString(Format(value) + "%", tiny, brush, b, centered);
    }
    private void Row(string name, int x, double? heat, double? power, SolidBrush color)
    {
        MetricGlyphs.Draw(graphics, name == "CPU" ? MetricGlyph.Cpu : MetricGlyph.Gpu, new(x - 35, 128, 17, 18), color.Color);
        Text(name, tiny, color, new(x - 12, 123, 43, 27));
        Text(Format(heat), temperature, primary, new(x - 68, 147, 136, 91));
        Text("°C", tiny, muted, new(x - 14, 224, 28, 21));
        MetricGlyphs.Draw(graphics, MetricGlyph.Bolt, new(x - 36, 244, 11, 15), color.Color);
        Text(Format(power) + "W", tiny, muted, new(x - 21, 238, 65, 24));
    }
    private void Text(string text, Font font, Brush brush, Rectangle rectangle) => graphics.DrawString(text, font, brush, new PointF(rectangle.X + rectangle.Width / 2f, rectangle.Y + rectangle.Height / 2f), centered);
    internal static string Format(double? value, string format = "0") => value?.ToString(format) ?? "—";
    internal static void DrawTrend(Graphics g, RectangleF bounds, HistoryPoint[] history, Func<HistoryPoint, double?> select,
        Color color, long now, int interval, double maximum = 100)
    {
        using var baseline = new Pen(Color.FromArgb(48, color), 1);
        g.DrawLine(baseline, bounds.Left, bounds.Bottom, bounds.Right, bounds.Bottom);
        using var pen = new Pen(color, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);
        PointF? previous = null;
        long previousTick = 0;
        var threshold = Math.Max(3 * interval, 5);
        var saved = g.Save();
        g.SetClip(bounds);
        foreach (var point in history)
        {
            var age = (now - point.Tick) / (double)Stopwatch.Frequency;
            var value = select(point);
            if (age is < 0 or > 120 || value is null) { previous = null; continue; }
            var next = new PointF(bounds.Right - (float)(age / 120 * bounds.Width), bounds.Bottom - (float)(Math.Clamp(value.Value / maximum, 0, 1) * bounds.Height));
            if (previous is PointF last && (point.Tick - previousTick) / (double)Stopwatch.Frequency <= threshold) g.DrawLine(pen, last, next);
            else g.FillEllipse(brush, next.X - 1.5f, next.Y - 1.5f, 3, 3);
            previous = next; previousTick = point.Tick;
        }
        g.Restore(saved);
    }
    public void DrawEnd(DateTimeOffset time)
    {
        graphics.Clear(background.Color);
        Text("SamaLite已退出", label, primary, new(70, 170, 340, 50));
        Text(time.ToString("MM-dd HH:mm"), small, muted, new(110, 235, 260, 40));
        Text("重新启动后更新", small, muted, new(100, 290, 280, 40));
    }
    public void DrawTest()
    {
        graphics.Clear(background.Color);
        graphics.FillRectangle(Brushes.Red, 90, 80, 150, 150);
        graphics.FillRectangle(Brushes.Green, 240, 80, 150, 150);
        graphics.FillRectangle(Brushes.Blue, 90, 230, 150, 150);
        graphics.FillRectangle(Brushes.White, 240, 230, 150, 150);
        Text("TOP", label, primary, new(180, 32, 120, 35));
    }
    public byte[] Pixels(Rectangle region)
    {
        var bytes = new byte[region.Width * region.Height * 4]; CopyPixels(region, bytes); return bytes;
    }
    public void CopyPixels(Rectangle region, byte[] bytes)
    {
        var data = Canvas.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { for (var row = 0; row < region.Height; row++) Marshal.Copy(data.Scan0 + row * data.Stride, bytes, row * region.Width * 4, region.Width * 4); }
        finally { Canvas.UnlockBits(data); }
    }
    public void Dispose()
    {
        graphics.Dispose(); Canvas.Dispose(); label.Dispose(); focus.Dispose(); temperature.Dispose(); small.Dispose(); foot.Dispose(); memory.Dispose(); metric.Dispose(); unit.Dispose(); tiny.Dispose();
        background.Dispose(); primary.Dispose(); muted.Dispose(); cpu.Dispose(); gpu.Dispose(); vram.Dispose(); warning.Dispose(); centered.Dispose();
    }
}

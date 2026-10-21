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
    private readonly Font load = new("Segoe UI", 60, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font temperature = new("Segoe UI", 38, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font small = new("Microsoft YaHei UI", 20, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font foot = new("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font memory = new("Segoe UI", 26, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font ratio = new("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font powerValue = new("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font tiny = new("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly SolidBrush background = new(ColorTranslator.FromHtml("#101318"));
    private readonly SolidBrush primary = new(ColorTranslator.FromHtml("#F2F5F9"));
    private readonly SolidBrush muted = new(ColorTranslator.FromHtml("#B6C1D0"));
    private readonly SolidBrush cpu = new(ColorTranslator.FromHtml("#96BAFF"));
    private readonly SolidBrush gpu = new(ColorTranslator.FromHtml("#C7B6FF"));
    private readonly SolidBrush warning = new(ColorTranslator.FromHtml("#F4C17A"));
    private readonly StringFormat centered = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
    private readonly StringFormat right = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
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
        if (config.Layout.Rings)
        {
            Arc(184, cpuUsage, cpu.Color); Arc(274, gpuUsage, gpu.Color);
            Arc(4, extra.VramPercent, gpu.Color); Arc(94, ram, Ui.Accent);
        }
        MetricGlyphs.Draw(graphics, frozenAt.HasValue ? MetricGlyph.Pause : normal ? MetricGlyph.Check : MetricGlyph.Warning, new(174, 39, 16, 18), normal ? Ui.Accent : warning.Color);
        Text(stamp, foot, normal ? muted : warning, new(192, 32, 108, 30));
        Row("CPU", 156, cpuUsage, metrics.Fresh(MetricId.CpuTemperature, interval, now), extra.CpuPower, cpu);
        Row("GPU", 324, gpuUsage, metrics.Fresh(MetricId.GpuTemperature, interval, now), extra.GpuPower, gpu);
        DrawTrend(graphics, new(94, 195, 126, 10), metrics.History, p => p.CpuUsage, cpu.Color, now, interval);
        DrawTrend(graphics, new(260, 195, 126, 10), metrics.History, p => p.GpuUsage, gpu.Color, now, interval);
        MemoryRow("RAM", 156, ram, metrics.Fresh(MetricId.MemoryUsed, interval, now), metrics.Fresh(MetricId.MemoryTotal, interval, now), Ui.Accent);
        MemoryRow("VRAM", 324, extra.VramPercent, extra.VramUsed, extra.VramTotal, gpu.Color);
        for (var i = 0; i < 12; i++)
        {
            var item = PanelMetrics.Get(config.Layout.Slots[i], metrics, config, now, extra);
            var x = i >= 9 ? 160 + i % 3 * 80 : 142 + i % 3 * 98; var y = 268 + i / 3 * 40;
            MetricGlyphs.Draw(graphics, item.Glyph, new(x - 43, y - 12, 19, 20), item.Color);
            Text(item.Digits, memory, primary, new(x - 24, y - 20, 70, 36));
            Text(item.Suffix, tiny, muted, new(x - 29, y + 9, 78, 21));
        }
        var busy = PanelMetrics.Get("disk-busy", metrics, config, now, extra);
        MetricGlyphs.Draw(graphics, MetricGlyph.Disk, new(188, 425, 17, 18), busy.Color);
        Text(busy.Digits + "%", foot, primary, new(211, 420, 90, 28));
        MetricGlyphs.Bar(graphics, new(199, 447, 82, 3), busy.Value, busy.Color);
    }
    public void DrawDynamic(Metrics metrics) => Draw(metrics);
    private void Arc(float start, double? value, Color color)
    {
        using var track = new Pen(Color.FromArgb(38, color), 5); using var fill = new Pen(color, 5);
        graphics.DrawArc(track, 29, 29, 422, 422, start, 82);
        if (value > 0) graphics.DrawArc(fill, 29, 29, 422, 422, start, (float)Math.Clamp(value.Value / 100, 0, 1) * 82);
    }
    private void Row(string name, int x, double? usage, double? heat, double? power, SolidBrush color)
    {
        MetricGlyphs.Draw(graphics, name == "CPU" ? MetricGlyph.Cpu : MetricGlyph.Gpu, new(x - 43, 72, 19, 19), color.Color);
        Text(name, foot, color, new(x - 20, 67, 60, 28));
        Number(usage, load, new(x - 64, 88, 112, 72));
        Text("%", foot, muted, new(x + 49, 122, 24, 27));
        Number(heat, temperature, new(x - 60, 152, 81, 46));
        Text("°C", foot, muted, new(x + 22, 166, 32, 27));
        MetricGlyphs.Draw(graphics, MetricGlyph.Bolt, new(x - 42, 208, 10, 14), color.Color);
        Text(Format(power) + "W", tiny, muted, new(x - 29, 201, 77, 27));
    }
    private void MemoryRow(string name, int x, double? percent, double? used, double? total, Color color)
    {
        using var brush = new SolidBrush(color);
        Text(name, tiny, brush, new(x - 63, 225, 50, 25));
        Text(Format(percent) + "%", memory, primary, new(x - 10, 220, 80, 34));
        MetricGlyphs.Bar(graphics, new(x - 61, 252, 123, 3), percent, color);
        // Exact used/total values remain available on the desktop hardware page.
    }
    private void Number(double? number, Font font, Rectangle rectangle) => graphics.DrawString(Format(number), font, primary, rectangle, right);
    private void Text(string text, Font font, Brush brush, Rectangle rectangle) => graphics.DrawString(text, font, brush, rectangle, centered);
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
        graphics.Dispose(); Canvas.Dispose(); label.Dispose(); load.Dispose(); temperature.Dispose(); small.Dispose(); foot.Dispose(); memory.Dispose(); ratio.Dispose(); powerValue.Dispose(); tiny.Dispose();
        background.Dispose(); primary.Dispose(); muted.Dispose(); cpu.Dispose(); gpu.Dispose(); warning.Dispose(); centered.Dispose(); right.Dispose();
    }
}

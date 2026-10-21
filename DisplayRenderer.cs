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
    private readonly Font focus = new("Segoe UI", 58, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font temperature = new("Segoe UI", 74, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font small = new("Microsoft YaHei UI", 20, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font foot = new("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font memory = new("Segoe UI", 26, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font metric = new("Segoe UI", 42, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font secondary = new("Segoe UI", 34, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font unit = new("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font footerUnit = new("Segoe UI", 16, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font diskUnit = new("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font clockFont = new("Segoe UI", 28, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font gameBig = new("Segoe UI", 86, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font gameMedium = new("Segoe UI", 48, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font tiny = new("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly SolidBrush background = new(ColorTranslator.FromHtml("#101318"));
    private readonly SolidBrush primary = new(ColorTranslator.FromHtml("#F2F5F9"));
    private readonly SolidBrush muted = new(ColorTranslator.FromHtml("#B6C1D0"));
    private readonly SolidBrush cpu = new(ColorTranslator.FromHtml("#96BAFF"));
    private readonly SolidBrush gpu = new(ColorTranslator.FromHtml("#C7B6FF"));
    private readonly SolidBrush vram = new(Color.FromArgb(237, 159, 217));
    private readonly SolidBrush warning = new(ColorTranslator.FromHtml("#F4C17A"));
    private readonly StringFormat centered = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
    public static Rectangle[] DynamicRegions { get; } = [new(24, 24, 432, 72), new(24, 96, 432, 72), new(24, 168, 432, 72),
        new(0, 240, 480, 60), new(0, 300, 480, 60), new(0, 360, 480, 60), new(0, 420, 480, 60)];
    internal static bool GameLayout(Metrics metrics, SettingsSnapshot config) => config.GameDisplay == "game" ||
        config.GameDisplay == "auto" && config.FramesEnabled && metrics.FrameActivity?.GameLayout == true;

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
        var game = GameLayout(metrics, config);
        var cpuUsage = metrics.Fresh(MetricId.CpuUsage, interval, now); var gpuUsage = metrics.Fresh(MetricId.GpuUsage, interval, now);
        var ram = metrics.FreshMemoryPercent(interval, now);
        Gauges(true, cpuUsage, ram, cpu.Color, Ui.Accent, config.Layout.Rings);
        Gauges(false, gpuUsage, extra.VramPercent, gpu.Color, vram.Color, config.Layout.Rings);
        MetricGlyphs.Draw(graphics, frozenAt.HasValue ? MetricGlyph.Pause : normal ? game ? MetricGlyph.Game : MetricGlyph.Check : MetricGlyph.Warning, new(146, 107, 20, 22), normal ? Ui.Accent : warning.Color);
        Text(stamp, clockFont, normal ? muted : warning, new(169, 96, 156, 40));
        Row("CPU", 165, metrics.Fresh(MetricId.CpuTemperature, interval, now), extra.CpuPower, cpu);
        Row("GPU", 315, metrics.Fresh(MetricId.GpuTemperature, interval, now), extra.GpuPower, gpu);
        using var ramColor = new SolidBrush(Ui.Accent);
        Text("CPU", tiny, cpu, new(24, 237, 32, 22)); Text("RAM", tiny, ramColor, new(61, 237, 36, 22));
        Text("VRAM", tiny, vram, new(376, 237, 46, 22)); Text("GPU", tiny, gpu, new(424, 237, 34, 22));
        if (game) DrawGame(metrics, config, now, extra);
        else DrawOverview(metrics, config, now, extra);
    }
    private void DrawOverview(Metrics metrics, SettingsSnapshot config, long now, CompactHardware extra)
    {
        using (var separator = new Pen(Color.FromArgb(36, 182, 193, 208), 1))
        {
            graphics.DrawLine(separator, 166, 262, 166, 366); graphics.DrawLine(separator, 314, 262, 314, 366);
        }
        for (var i = 0; i < 12; i++)
        {
            var item = PanelMetrics.Get(config.Layout.Slots[i], metrics, config, now, extra);
            if (i < 9)
            {
                var x = i < 6 ? 92 + i % 3 * 148 : 132 + i % 3 * 108;
                var y = i < 3 ? 272 : i < 6 ? 334 : 386;
                Text(item.Digits, i == 1 ? focus : i < 6 ? metric : secondary, primary, new(x - 68, y - 33, 136, 64));
                Caption(item, x, y + (i < 6 ? 33 : 26), unit, 20);
            }
            else
            {
                var x = 180 + i % 3 * 60;
                var caption = item.Suffix;
                var disk = config.Layout.Slots[i] is "disk-read" or "disk-write";
                if (disk) caption = caption[..^2];
                var value = item.Digits;
                if (disk && caption.StartsWith("k ", StringComparison.Ordinal)) { caption = caption[2..]; value += "k"; }
                if (config.Layout.Slots[i] == "igpu") caption = "iGPU %";
                Caption(item, x, 427, disk ? diskUnit : footerUnit, 16, !disk, caption);
                using var digits = new SolidBrush(disk ? item.Color : primary.Color);
                Text(value, memory, digits, new(x - 30, 430, 60, 38));
            }
        }
        var busy = PanelMetrics.Get("disk-busy", metrics, config, now, extra);
        MetricGlyphs.Bar(graphics, new(221, 472, 38, 4), busy.Value, busy.Color);
    }
    private void Caption(PanelValue item, float x, float y, Font font, float icon, bool compact = false, string? caption = null)
    {
        var text = caption ?? item.Suffix;
        if (item.Glyph is MetricGlyph.Download or MetricGlyph.Upload && (text.EndsWith(" ↓", StringComparison.Ordinal) || text.EndsWith(" ↑", StringComparison.Ordinal))) text = text[..^2];
        var width = graphics.MeasureString(text, font, int.MaxValue, centered).Width;
        if (compact && width + icon + 4 > 60) { graphics.DrawString(text, font, muted, new PointF(x, y), centered); return; }
        var start = x - (width + icon + 4) / 2;
        MetricGlyphs.Draw(graphics, item.Glyph, new(start, y - icon / 2, icon, icon), item.Color);
        using var captionBrush = new SolidBrush(item.Glyph is MetricGlyph.DiskRead or MetricGlyph.DiskWrite ? item.Color : muted.Color);
        graphics.DrawString(text, font, captionBrush, new PointF(start + icon + 4 + width / 2, y), centered);
    }
    private void DrawGame(Metrics metrics, SettingsSnapshot config, long now, CompactHardware extra)
    {
        var fps = PanelMetrics.Get("fps", metrics, config, now, extra);
        Text(fps.Digits, gameBig, primary, new(126, 230, 228, 90)); Caption(fps, 240, 315, unit, 22);
        var ms = PanelMetrics.Get("frametime", metrics, config, now, extra); var low = PanelMetrics.Get("low", metrics, config, now, extra);
        Text(ms.Digits, gameMedium, primary, new(65, 324, 162, 62)); Caption(ms, 146, 389, unit, 22);
        Text(low.Digits, gameMedium, primary, new(253, 324, 162, 62)); Caption(low, 334, 389, unit, 22);
        foreach (var (id, x) in new[] { ("fan", 186), ("pump", 294) })
        {
            var rpm = PanelMetrics.Get(id, metrics, config, now, extra);
            Text(rpm.Digits, secondary, primary, new(x - 50, 403, 100, 50)); Caption(rpm, x, 452, unit, 20);
        }
    }
    public void DrawDynamic(Metrics metrics) => Draw(metrics);
    private void Gauges(bool left, double? outer, double? inner, Color outerColor, Color innerColor, bool bands)
    {
        if (!bands)
        {
            using var brush = new SolidBrush(outerColor);
            Text((left ? "CPU " : "GPU ") + Format(outer) + "%  " + (left ? "RAM " : "VRAM ") + Format(inner) + "%", tiny, brush, new(left ? 100 : 240, 77, 140, 24));
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
        Text(Format(heat), temperature, primary, new(x - 68, 127, 136, 91));
        Text("°C", foot, muted, new(x - 57, 213, 32, 27));
        MetricGlyphs.Draw(graphics, MetricGlyph.Bolt, new(x - 13, 220, 13, 17), color.Color);
        Text(Format(power) + "W", foot, muted, new(x + 2, 213, 70, 27));
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
        graphics.Dispose(); Canvas.Dispose(); label.Dispose(); focus.Dispose(); temperature.Dispose(); small.Dispose(); foot.Dispose(); memory.Dispose(); metric.Dispose(); secondary.Dispose(); unit.Dispose(); footerUnit.Dispose(); diskUnit.Dispose(); clockFont.Dispose(); gameBig.Dispose(); gameMedium.Dispose(); tiny.Dispose();
        background.Dispose(); primary.Dispose(); muted.Dispose(); cpu.Dispose(); gpu.Dispose(); vram.Dispose(); warning.Dispose(); centered.Dispose();
    }
}

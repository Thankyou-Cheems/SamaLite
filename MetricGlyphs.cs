using System.Drawing.Drawing2D;

namespace SamaLite;

internal enum MetricGlyph { Cpu, Gpu, Memory, Thermometer, Bolt, Integrated, Clock, Pause, Warning, Check, Info, Fan, Pump, Game, Frame, Low, Nvme, Download, Upload, DiskRead, DiskWrite, Disk }
internal static class MetricGlyphs
{
    public static void Draw(Graphics graphics, MetricGlyph glyph, RectangleF bounds, Color color)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);
        switch (glyph)
        {
            case MetricGlyph.Fan:
                graphics.DrawEllipse(pen, 2, 2, 20, 20); graphics.FillEllipse(brush, 10, 10, 4, 4);
                graphics.FillEllipse(brush, 10, 3, 6, 7); graphics.FillEllipse(brush, 3, 11, 7, 6); graphics.FillEllipse(brush, 13, 13, 7, 6);
                break;
            case MetricGlyph.Pump:
                graphics.DrawEllipse(pen, 5, 5, 14, 14); graphics.DrawLine(pen, 2, 6, 6, 6); graphics.DrawLine(pen, 18, 18, 22, 18);
                graphics.DrawLines(pen, new PointF[] { new(9, 15), new(9, 9), new(15, 12), new(9, 15) }); break;
            case MetricGlyph.Game:
                graphics.DrawPolygon(pen, new PointF[] { new(6, 6), new(18, 6), new(22, 18), new(17, 18), new(14, 14), new(10, 14), new(7, 18), new(2, 18) });
                graphics.DrawLine(pen, 6, 10, 10, 10); graphics.DrawLine(pen, 8, 8, 8, 12); graphics.FillEllipse(brush, 15, 9, 2, 2); break;
            case MetricGlyph.Frame:
                graphics.DrawRectangle(pen, 2, 3, 20, 17); graphics.DrawLines(pen, new PointF[] { new(4, 15), new(8, 15), new(10, 7), new(13, 17), new(16, 12), new(20, 12) }); break;
            case MetricGlyph.Low:
                graphics.DrawLine(pen, 12, 3, 12, 16); graphics.DrawLines(pen, new PointF[] { new(7, 11), new(12, 16), new(17, 11) }); graphics.DrawLine(pen, 4, 21, 20, 21); break;
            case MetricGlyph.Download:
            case MetricGlyph.Upload:
                var down = glyph == MetricGlyph.Download;
                graphics.DrawLine(pen, 12, 3, 12, 20); graphics.DrawLines(pen, down ? new PointF[] { new(6, 14), new(12, 20), new(18, 14) } : new PointF[] { new(6, 9), new(12, 3), new(18, 9) }); break;
            case MetricGlyph.Nvme:
                graphics.DrawRectangle(pen, 7, 2, 10, 20); graphics.DrawRectangle(pen, 9, 5, 6, 6); graphics.DrawLine(pen, 10, 18, 14, 18); break;
            case MetricGlyph.DiskRead:
            case MetricGlyph.DiskWrite:
            case MetricGlyph.Disk:
                graphics.DrawRectangle(pen, 3, 3, 18, 18); graphics.DrawEllipse(pen, 6, 6, 8, 8); graphics.DrawLine(pen, 10, 10, 17, 17);
                if (glyph != MetricGlyph.Disk) { var y = glyph == MetricGlyph.DiskRead ? 20 : 15; graphics.DrawLine(pen, 16, 15, 16, 20); graphics.DrawLine(pen, 16, y, 19, y); } break;
            case MetricGlyph.Cpu:
                graphics.DrawRectangle(pen, 5, 5, 14, 14); graphics.DrawRectangle(pen, 9, 9, 6, 6);
                for (var i = 8; i <= 16; i += 4) { graphics.DrawLine(pen, i, 2, i, 5); graphics.DrawLine(pen, i, 19, i, 22); graphics.DrawLine(pen, 2, i, 5, i); graphics.DrawLine(pen, 19, i, 22, i); }
                break;
            case MetricGlyph.Integrated:
                graphics.DrawRectangle(pen, 4, 4, 16, 16); graphics.DrawLine(pen, 12, 11, 12, 16); graphics.FillEllipse(brush, 11, 7, 2, 2);
                for (var i = 8; i <= 16; i += 4) { graphics.DrawLine(pen, i, 1, i, 4); graphics.DrawLine(pen, i, 20, i, 23); } break;
            case MetricGlyph.Gpu:
                graphics.DrawRectangle(pen, 3, 5, 18, 13); graphics.DrawEllipse(pen, 7, 8, 7, 7); graphics.DrawLine(pen, 5, 21, 15, 21); graphics.DrawLine(pen, 17, 9, 19, 9); graphics.DrawLine(pen, 17, 13, 19, 13); break;
            case MetricGlyph.Memory:
                graphics.DrawRectangle(pen, 2, 6, 20, 11);
                for (var i = 5; i < 20; i += 5) { graphics.DrawRectangle(pen, i, 9, 3, 5); graphics.DrawLine(pen, i, 18, i, 21); } break;
            case MetricGlyph.Thermometer:
                graphics.DrawArc(pen, 9, 2, 6, 6, 180, 180); graphics.DrawLine(pen, 9, 5, 9, 14); graphics.DrawLine(pen, 15, 5, 15, 14);
                graphics.DrawArc(pen, 7, 12, 10, 10, -50, 280); graphics.DrawLine(pen, 12, 8, 12, 16); graphics.FillEllipse(brush, 10, 15, 4, 4); break;
            case MetricGlyph.Bolt: graphics.DrawLines(pen, new PointF[] { new(14, 2), new(5, 13), new(11, 13), new(9, 22), new(19, 10), new(13, 10), new(14, 2) }); break;
            case MetricGlyph.Clock:
                graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawLine(pen, 12, 6, 12, 12); graphics.DrawLine(pen, 12, 12, 16, 14); break;
            case MetricGlyph.Pause: graphics.FillRectangle(brush, 6, 4, 4, 16); graphics.FillRectangle(brush, 14, 4, 4, 16); break;
            case MetricGlyph.Warning:
                graphics.DrawPolygon(pen, new PointF[] { new(12, 2), new(23, 21), new(1, 21) }); graphics.DrawLine(pen, 12, 8, 12, 13); graphics.FillEllipse(brush, 11, 16, 2, 2); break;
            case MetricGlyph.Check: graphics.DrawLines(pen, new PointF[] { new(4, 12), new(9, 17), new(20, 6) }); break;
            case MetricGlyph.Info:
                graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawLine(pen, 12, 11, 12, 17); graphics.FillEllipse(brush, 11, 6, 2, 2); break;
        }
        graphics.Restore(state);
    }
    public static void Bar(Graphics g, RectangleF bounds, double? percent, Color color)
    {
        using var background = new SolidBrush(Color.FromArgb(48, color));
        using var foreground = new SolidBrush(color);
        g.FillRectangle(background, bounds);
        if (percent.HasValue) g.FillRectangle(foreground, bounds.X, bounds.Y, bounds.Width * (float)Math.Clamp(percent.Value / 100, 0, 1), bounds.Height);
    }
}

internal sealed record CompactHardware(double? CpuPower, double? GpuPower, double? VramUsed, double? VramTotal,
    double? VramPercent, double?[] MemoryTemperatures, double? IntegratedUsage, double? IntegratedMemory, DateTimeOffset? OldestExtra)
{
    public static CompactHardware From(Metrics metrics, long now)
    {
        var details = metrics.Details;
        var readings = new List<DetailMetric>();
        double? Take(DetailMetric? metric)
        {
            if (metric is null || metric.Availability != DetailAvailability.Available || !metric.Reading.IsFresh(now, details?.PollSeconds ?? 5)) return null;
            readings.Add(metric); return metric.Reading.Value;
        }
        var cpu = details?.Devices.Where(d => d.Category == "cpu").SelectMany(d => d.Metrics).FirstOrDefault(m => m.Label == "CPU Package" && m.Unit == "W");
        var gpu = details?.Devices.FirstOrDefault(d => d.Id == "nvidia");
        double? Gpu(string id) => Take(gpu?.Metrics.FirstOrDefault(m => m.Id == "nvidia/" + id));
        var temps = details?.Devices.Where(d => d.Category == "memory").SelectMany(d => d.Metrics).Where(m => m.Unit == "°C").Take(2).Select(Take).ToArray() ?? [];
        var integrated = details?.Devices.Where(d => d.Category == "igpu").SelectMany(d => d.Metrics).ToArray() ?? [];
        var engines = integrated.Where(m => m.Unit == "%").Select(Take).ToArray();
        var shared = Take(integrated.FirstOrDefault(m => m.Label == "共享内存已用"));
        var cpuPower = Take(cpu); var gpuPower = Gpu("gpu-power"); var vramUsed = Gpu("vram-used"); var vramTotal = Gpu("vram-total");
        var oldest = readings.Where(m => !m.IsStatic && !m.Id.EndsWith("vram-total", StringComparison.Ordinal)).Select(m => m.Reading.SampledAt).DefaultIfEmpty().Min();
        return new(cpuPower, gpuPower, vramUsed, vramTotal, DetailFormatting.Percent(vramUsed, vramTotal), temps, engines.Length == 0 || engines.Any(v => !v.HasValue) ? null : engines.Max(), shared, oldest);
    }
}

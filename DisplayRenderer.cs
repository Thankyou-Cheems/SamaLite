using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SamaLite;

internal sealed class DisplayRenderer : IDisposable
{
    public Bitmap Canvas { get; } = new(480, 480, PixelFormat.Format32bppArgb);
    private readonly Graphics graphics;
    private readonly Font title = new("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font value = new("Segoe UI", 36, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font small = new("Segoe UI", 15, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font memory = new("Segoe UI", 25, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly SolidBrush background = new(Color.FromArgb(10, 15, 23));
    private readonly SolidBrush primary = new(Color.FromArgb(234, 242, 250));
    private readonly SolidBrush muted = new(Color.FromArgb(130, 150, 169));
    private readonly SolidBrush cyan = new(Color.FromArgb(67, 212, 230));
    private readonly SolidBrush purple = new(Color.FromArgb(175, 151, 255));
    private readonly StringFormat centered = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    public static Rectangle[] DynamicRegions { get; } = [new(58, 119, 174, 112), new(248, 119, 174, 112), new(100, 277, 280, 62), new(124, 367, 232, 32)];

    public DisplayRenderer()
    {
        graphics = Graphics.FromImage(Canvas);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    public void Draw(Metrics metrics)
    {
        graphics.Clear(background.Color);
        using var ring = new Pen(Color.FromArgb(38, 55, 72), 2);
        graphics.DrawEllipse(ring, 10, 10, 459, 459);
        Text("SAMA LITE", title, muted, new(120, 47, 240, 27));
        Text("CPU", title, cyan, new(58, 91, 174, 28));
        Text("GPU", title, purple, new(248, 91, 174, 28));
        DrawCpu(metrics);
        DrawGpu(metrics);
        graphics.DrawLine(ring, 90, 250, 390, 250);
        Text("MEMORY", title, muted, new(120, 253, 240, 25));
        DrawMemory(metrics);
        Text("SYSTEM MONITOR", small, muted, new(120, 342, 240, 24));
        DrawClock(metrics);
    }

    public void DrawDynamic(Metrics metrics)
    {
        foreach (var region in DynamicRegions) graphics.FillRectangle(background, region);
        DrawCpu(metrics); DrawGpu(metrics); DrawMemory(metrics); DrawClock(metrics);
    }

    private void DrawCpu(Metrics metrics)
    {
        Text(Temperature(metrics.CpuTemperature), value, primary, new(58, 121, 174, 57));
        Text(Percent(metrics.CpuUsage) + " LOAD", small, cyan, new(58, 184, 174, 30));
    }
    private void DrawGpu(Metrics metrics)
    {
        Text(Temperature(metrics.GpuTemperature), value, primary, new(248, 121, 174, 57));
        Text(Percent(metrics.GpuUsage) + " LOAD", small, purple, new(248, 184, 174, 30));
    }
    private void DrawMemory(Metrics metrics) => Text($"{metrics.MemoryUsedGiB:0.0} / {metrics.MemoryTotalGiB:0.0} GiB", memory, primary, new(100, 277, 280, 62));
    private void DrawClock(Metrics metrics) => Text(metrics.Timestamp.ToString("HH:mm:ss"), title, muted, new(124, 367, 232, 32));
    private void Text(string text, Font font, Brush brush, Rectangle rectangle) => graphics.DrawString(text, font, brush, rectangle, centered);
    private static string Temperature(float? number) => number.HasValue ? $"{number:0}°C" : "--°C";
    private static string Percent(float? number) => number.HasValue ? $"{number:0}%" : "--%";

    public void DrawTest()
    {
        graphics.Clear(Color.FromArgb(10, 15, 23));
        using var red = new SolidBrush(Color.FromArgb(235, 55, 55));
        using var green = new SolidBrush(Color.FromArgb(40, 200, 80));
        using var blue = new SolidBrush(Color.FromArgb(50, 110, 240));
        graphics.FillRectangle(red, 90, 80, 150, 150);
        graphics.FillRectangle(green, 240, 80, 150, 150);
        graphics.FillRectangle(blue, 90, 230, 150, 150);
        graphics.FillRectangle(Brushes.White, 240, 230, 150, 150);
        using var labelBackground = new SolidBrush(Color.FromArgb(10, 15, 23));
        graphics.FillRectangle(labelBackground, 120, 195, 240, 90);
        Text("SAMA LITE", memory, primary, new(120, 197, 240, 40));
        Text("480 x 480  |  TEST", small, primary, new(120, 242, 240, 28));
        Text("TOP", title, cyan, new(180, 32, 120, 35));
    }

    public byte[] Pixels(Rectangle region)
    {
        var bytes = new byte[region.Width * region.Height * 4];
        CopyPixels(region, bytes);
        return bytes;
    }

    public void CopyPixels(Rectangle region, byte[] bytes)
    {
        var data = Canvas.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < region.Height; row++) Marshal.Copy(data.Scan0 + row * data.Stride, bytes, row * region.Width * 4, region.Width * 4);
        }
        finally { Canvas.UnlockBits(data); }
    }

    public void Dispose()
    {
        graphics.Dispose(); Canvas.Dispose(); title.Dispose(); value.Dispose(); small.Dispose(); memory.Dispose();
        background.Dispose(); primary.Dispose(); muted.Dispose(); cyan.Dispose(); purple.Dispose(); centered.Dispose();
    }
}

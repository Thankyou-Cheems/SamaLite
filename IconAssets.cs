using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace SamaLite;

internal enum TrayMark { Normal, Paused, Connecting, Disconnected, Attention }
internal static class IconAssets
{
    public static byte[] Export(TrayMark mark, bool light = false, bool application = false)
    {
        int[] sizes = application ? [16, 20, 24, 32, 48, 64, 128, 256] : [16, 20, 24, 32];
        var images = sizes.Select(size =>
        {
            using var bitmap = Draw(size, mark, light, application);
            using var bytes = new MemoryStream(); bitmap.Save(bytes, ImageFormat.Png); return bytes.ToArray();
        }).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        var offset = 6 + sizes.Length * 16;
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
        }
        foreach (var image in images) writer.Write(image);
        return stream.ToArray();
    }
    public static Icon Create(TrayMark mark, bool light = false)
    {
        using var stream = new MemoryStream(Export(mark, light)); using var icon = new Icon(stream); return (Icon)icon.Clone();
    }
    public static Bitmap Draw(int size, TrayMark mark, bool light, bool application = false)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 64f, size / 64f);
        var contrast = !application && SystemInformation.HighContrast;
        var foreground = contrast ? SystemColors.WindowText : Color.FromArgb(242, 245, 249);
        var face = contrast ? SystemColors.Window : Ui.Background;
        using var housing = new SolidBrush(contrast ? SystemColors.Window : Color.FromArgb(38, 47, 60));
        using var screen = new SolidBrush(face);
        using var rim = new Pen(contrast ? foreground : light ? Color.FromArgb(70, 87, 108) : Color.FromArgb(133, 151, 176), 2);
        g.FillEllipse(housing, 2, 2, 60, 60); g.DrawEllipse(rim, 3, 3, 58, 58);
        g.FillEllipse(screen, 7, 7, 50, 50);
        using var blue = new Pen(contrast ? foreground : Ui.Cpu, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var purple = new Pen(contrast ? foreground : Ui.Gpu, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(blue, 11, 11, 42, 42, 181, 80); g.DrawArc(purple, 11, 11, 42, 42, 279, 80);
        using var pen = new Pen(foreground, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(foreground);
        if (size <= 24)
        {
            // A thermometer survives tray scaling better than miniature text.
            g.DrawLine(pen, 31, 24, 31, 39); g.DrawEllipse(pen, 26, 37, 10, 10);
            g.DrawLine(pen, 38, 25, 41, 25); g.DrawLine(pen, 38, 31, 41, 31);
        }
        else
        {
            MetricGlyphs.Draw(g, MetricGlyph.Thermometer, new(21, 19, 23, 30), foreground);
            using var led = new SolidBrush(contrast ? foreground : Ui.Accent); g.FillEllipse(led, 29, 49, 6, 3);
        }
        if (application && mark == TrayMark.Normal) return bitmap;
        using var badge = new SolidBrush(contrast ? SystemColors.Window : Color.FromArgb(22, 28, 36));
        g.FillEllipse(badge, 41, 41, 22, 22);
        var x = 52f; var y = 52f; var radius = 2f;
        switch (mark)
        {
            case TrayMark.Normal:
                using (var normal = new SolidBrush(contrast ? foreground : Ui.Accent)) g.FillEllipse(normal, x - radius * 1.5f, y - radius * 1.5f, radius * 3, radius * 3); break;
            case TrayMark.Paused:
                g.DrawLine(pen, x - radius * 1.8f, y - radius * 2, x - radius * 1.8f, y + radius * 2);
                g.DrawLine(pen, x + radius * 1.8f, y - radius * 2, x + radius * 1.8f, y + radius * 2); break;
            case TrayMark.Connecting:
                for (var i = -1; i <= 1; i++) g.FillRectangle(brush, x + i * radius * 2.6f, y, radius, radius); break;
            case TrayMark.Disconnected: g.DrawLine(pen, x - radius * 2, y + radius * 2, x + radius * 2, y - radius * 2); break;
            case TrayMark.Attention:
                g.DrawLine(pen, x, y - radius * 3, x, y); g.FillRectangle(brush, x - radius / 2, y + radius * 2, radius, radius); break;
        }
        return bitmap;
    }
}

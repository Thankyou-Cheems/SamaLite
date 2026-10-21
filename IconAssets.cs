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
        g.Clear(application ? Ui.Background : Color.Transparent);
        g.SmoothingMode = size <= 24 ? SmoothingMode.None : SmoothingMode.AntiAlias;
        var color = application ? Ui.Accent : SystemInformation.HighContrast ? SystemColors.WindowText : light ? Color.FromArgb(30, 40, 48) : Color.FromArgb(242, 245, 249);
        using var pen = new Pen(color, Math.Max(1, MathF.Round(size / 16f))) { StartCap = LineCap.Square, EndCap = LineCap.Square };
        using var brush = new SolidBrush(color);
        // Pixel-snapped open display frame and L; the status occupies its open corner.
        var left = MathF.Round(size * .125f); var top = MathF.Round(size * .125f);
        var right = MathF.Round(size * .8125f); var bottom = MathF.Round(size * .8125f);
        g.DrawLines(pen, new PointF[] { new(right, size * .43f), new(right, top), new(left, top), new(left, bottom), new(size * .45f, bottom) });
        g.DrawLines(pen, new PointF[] { new(size * .34f, size * .32f), new(size * .34f, size * .62f), new(size * .58f, size * .62f) });
        var x = size * .7f; var y = size * .76f; var radius = Math.Max(1, size / 16f);
        switch (mark)
        {
            case TrayMark.Normal: g.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2); break;
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

namespace SamaLite;

internal readonly record struct GaugeLabel(PointF Anchor, RectangleF Bounds);

internal static class UpperGauge
{
    internal const float OuterRadius = 205, InnerRadius = 180, OuterWidth = 16, InnerWidth = 14;
    // Both quarters finish at twelve o'clock; right progresses counterclockwise.
    internal static float Angle(bool left, double percent) => left ? 180 + (float)Math.Clamp(percent, 0, 100) * .9f : -(float)Math.Clamp(percent, 0, 100) * .9f;
    internal static PointF Point(float radius, float angle)
    {
        var radians = angle * MathF.PI / 180;
        return new(240 + radius * MathF.Cos(radians), 240 + radius * MathF.Sin(radians));
    }
    internal static GaugeLabel Label(bool left, bool inner, double? percent, RectangleF? previous = null)
    {
        var angle = Angle(left, percent ?? 0); var radius = inner ? InnerRadius : OuterRadius;
        var anchor = Point(radius, angle); var point = anchor;
        const float width = 43, height = 20;
        // Keep horizontal labels within the safety circle, even at 0 and 100%.
        var radians = angle * MathF.PI / 180; var c = MathF.Abs(MathF.Cos(radians)); var s = MathF.Abs(MathF.Sin(radians));
        var projection = c * width / 2 + s * height / 2;
        var safeRadius = -projection + MathF.Sqrt(projection * projection + 213 * 213 - width * width / 4 - height * height / 4);
        if (radius > safeRadius) point = Point(safeRadius, angle);
        point.X = left ? Math.Min(point.X, 215) : Math.Max(point.X, 265);
        var main = left ? new RectangleF(99, 126, 133, 118) : new RectangleF(248, 126, 133, 118);
        // Search nearby horizontal positions: coincident percentages must not stack
        // labels, and a capacity label must never cover the large temperature.
        for (var step = 0; step <= 20; step++)
        for (var dy = -step; dy <= step; dy++)
        for (var dx = -step; dx <= step; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != step) continue;
            var x = point.X + (left ? dx : -dx) * 4;
            var bounds = new RectangleF(x - width / 2, point.Y + dy * 4 - height / 2, width, height);
            if (left ? bounds.Right > 237 : bounds.Left < 243) continue;
            if (RectangleF.Inflate(main, 2, 2).IntersectsWith(bounds) || (previous is RectangleF outer && RectangleF.Inflate(outer, 3, 3).IntersectsWith(bounds))) continue;
            if (new RectangleF(142, 96, 184, 40).IntersectsWith(bounds)) continue;
            var maxX = Math.Max(Math.Abs(bounds.Left - 240), Math.Abs(bounds.Right - 240));
            var maxY = Math.Max(Math.Abs(bounds.Top - 240), Math.Abs(bounds.Bottom - 240));
            if (maxX * maxX + maxY * maxY > 213 * 213) continue;
            return new(anchor, bounds);
        }
        return new(anchor, new(left ? 56 : 381, inner ? 267 : 242, width, height));
    }
    public static void Draw(Graphics g, bool left, bool inner, double? percent, Color color)
    {
        var radius = inner ? InnerRadius : OuterRadius; var width = inner ? InnerWidth : OuterWidth;
        var bounds = new RectangleF(240 - radius, 240 - radius, radius * 2, radius * 2);
        var start = left ? 180 : 0; var direction = left ? 1 : -1;
        using var track = new Pen(Color.FromArgb(44, color), width);
        using var fill = new Pen(color, width);
        g.DrawArc(track, bounds, start, direction * 90);
        if (percent > 0) g.DrawArc(fill, bounds, start, direction * 90 * (float)Math.Clamp(percent.Value / 100, 0, 1));
        using var tick = new Pen(Color.FromArgb(130, color), 1);
        for (var value = 0; value <= 100; value += 25)
        {
            var angle = Angle(left, value);
            var a = Point(radius - width / 2 - 2, angle); var b = Point(radius - width / 2 - 5, angle);
            g.DrawLine(tick, a, b);
        }
    }
}

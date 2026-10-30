using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AlToque.Controls;

public enum BurstKind
{
    Confetti,
    Stars,
    Puff,
    Sweat,
    Sparkles,
    Hearts,
}

/// <summary>Un estallido de partículas que nace en <see cref="Start"/> y se calcula de forma analítica en cada fotograma.</summary>
public readonly record struct Burst(BurstKind Kind, double Start, Point Origin, int Count, int Seed, double Size = 1);

/// <summary>Confeti, estrellas, humo, gotas de sudor y corazones: los efectos que acompañan a los momentos clave.</summary>
public static class Particles
{
    public static readonly Color[] Festive =
    [
        ColorUtil.Hex("#5B5BF0"), ColorUtil.Hex("#F7C266"), ColorUtil.Hex("#46D891"), ColorUtil.Hex("#FF7378"),
        ColorUtil.Hex("#A3A3FF"), ColorUtil.Hex("#FF8FB1"), ColorUtil.Hex("#4CC3FF"),
    ];

    public static double Lifetime(BurstKind kind) => kind switch
    {
        BurstKind.Confetti => 1.5,
        BurstKind.Stars => 0.95,
        BurstKind.Puff => 0.85,
        BurstKind.Sweat => 0.9,
        BurstKind.Sparkles => 0.8,
        BurstKind.Hearts => 1.3,
        _ => 1,
    };

    public static bool IsAlive(in Burst b, double now) => now - b.Start < Lifetime(b.Kind) && now >= b.Start;

    /// <summary>Número pseudoaleatorio estable en [0, 1).</summary>
    public static double Rand(int seed, int i, int k)
    {
        unchecked
        {
            var h = (uint)(seed * 73856093) ^ (uint)(i * 19349663) ^ (uint)(k * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    public static void Draw(DrawingContext ctx, in Burst b, double now)
    {
        var t = now - b.Start;
        var life = Lifetime(b.Kind);
        if (t < 0 || t >= life)
            return;
        var p = t / life;
        var s = b.Size;
        var seed = b.Seed;
        for (int i = 0; i < b.Count; i++)
        {
            double R(int k) => Rand(seed, i, k);
            switch (b.Kind)
            {
                case BurstKind.Confetti:
                {
                    var angle = (-170 + R(0) * 160) * Math.PI / 180;
                    var speed = (45 + R(1) * 55) * s;
                    var x = b.Origin.X + Math.Cos(angle) * speed * t;
                    var y = b.Origin.Y + Math.Sin(angle) * speed * t + 0.5 * 150 * s * t * t;
                    var spin = (R(2) * 2 - 1) * 900 * t;
                    var color = Festive[(int)(R(3) * Festive.Length) % Festive.Length];
                    var fade = p < 0.7 ? 1 : 1 - (p - 0.7) / 0.3;
                    // Se voltea en el aire: el ancho oscila.
                    var flipW = Math.Abs(Math.Cos(t * (6 + R(4) * 8)));
                    using (ctx.PushOpacity(fade))
                    using (ctx.PushTransform(Matrix.CreateScale(0.25 + flipW * 0.75, 1) * Matrix.CreateRotation(spin * Math.PI / 180) * Matrix.CreateTranslation(x, y)))
                    {
                        var brush = new ImmutableSolidColorBrush(color);
                        if (R(5) < 0.3)
                            ctx.DrawEllipse(brush, null, default, 1.6 * s, 1.6 * s);
                        else
                            ctx.FillRectangle(brush, new Rect(-2.2 * s, -1.2 * s, 4.4 * s, 2.4 * s));
                    }
                    break;
                }
                case BurstKind.Stars:
                {
                    var angle = (i / (double)b.Count) * Math.PI * 2 + R(0) * 0.5;
                    var dist = (14 + Ease.OutCubic(p) * (16 + R(1) * 10)) * s;
                    var pos = b.Origin + new Vector(Math.Cos(angle) * dist, Math.Sin(angle) * dist);
                    var size = 4.2 * s * Math.Sin(Math.Min(1, p * 1.25) * Math.PI) * (0.7 + R(2) * 0.6);
                    Star(ctx, pos, size, i % 3 == 0 ? Festive[1] : i % 3 == 1 ? Festive[4] : ColorUtil.Hex("#FFFFFF"), t * 4 + R(3) * 3);
                    break;
                }
                case BurstKind.Puff:
                {
                    var angle = (200 + R(0) * 70) * Math.PI / 180;
                    var dist = (4 + Ease.OutCubic(p) * (12 + R(1) * 8)) * s;
                    var pos = b.Origin + new Vector(Math.Cos(angle) * dist, Math.Sin(angle) * dist - p * 6 * s);
                    var r = (2 + p * (5 + R(2) * 3)) * s;
                    using (ctx.PushOpacity((1 - p) * 0.85))
                        ctx.DrawEllipse(new ImmutableSolidColorBrush(ColorUtil.Hex("#E4E4EE")), null, pos, r, r);
                    break;
                }
                case BurstKind.Sweat:
                {
                    var dir = R(0) < 0.5 ? -1 : 1;
                    var x = b.Origin.X + dir * (6 + p * 10) * s;
                    var y = b.Origin.Y - 6 * s + (-14 * p + 30 * p * p) * s;
                    using (ctx.PushOpacity(p < 0.75 ? 1 : (1 - p) / 0.25))
                        Drop(ctx, new Point(x, y), 3.2 * s, ColorUtil.Hex("#7CC8FF"));
                    break;
                }
                case BurstKind.Sparkles:
                {
                    var angle = R(0) * Math.PI * 2;
                    var dist = (10 + R(1) * 22) * s;
                    var pos = b.Origin + new Vector(Math.Cos(angle) * dist, Math.Sin(angle) * dist);
                    var local = Ease.Seg(p, R(2) * 0.4, 0.6);
                    var size = 4 * s * Math.Sin(local * Math.PI);
                    Star4(ctx, pos, size, i % 2 == 0 ? Festive[1] : ColorUtil.Hex("#FFFFFF"), local * 2);
                    break;
                }
                case BurstKind.Hearts:
                {
                    var local = Ease.Seg(p, R(0) * 0.35, 0.65);
                    if (local <= 0 || local >= 1)
                        break;
                    var pos = b.Origin + new Vector((R(1) * 2 - 1) * 14 * s + Math.Sin(local * 9) * 3 * s, -local * 30 * s);
                    using (ctx.PushOpacity(Math.Sin(local * Math.PI)))
                        Heart(ctx, pos, (3 + R(2) * 2) * s, ColorUtil.Hex("#FF5C7C"));
                    break;
                }
            }
        }
    }

    public static void Star(DrawingContext ctx, Point c, double r, Color color, double rotation)
    {
        if (r <= 0.2)
            return;
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            for (int i = 0; i < 10; i++)
            {
                var a = rotation + i * Math.PI / 5 - Math.PI / 2;
                var rr = i % 2 == 0 ? r : r * 0.45;
                var pt = new Point(c.X + Math.Cos(a) * rr, c.Y + Math.Sin(a) * rr);
                if (i == 0)
                    sg.BeginFigure(pt, true);
                else
                    sg.LineTo(pt);
            }
            sg.EndFigure(true);
        }
        ctx.DrawGeometry(new ImmutableSolidColorBrush(color), null, g);
    }

    public static void Star4(DrawingContext ctx, Point c, double r, Color color, double rotation)
    {
        if (r <= 0.2)
            return;
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            for (int i = 0; i < 8; i++)
            {
                var a = rotation + i * Math.PI / 4;
                var rr = i % 2 == 0 ? r : r * 0.3;
                var pt = new Point(c.X + Math.Cos(a) * rr, c.Y + Math.Sin(a) * rr);
                if (i == 0)
                    sg.BeginFigure(pt, true);
                else
                    sg.LineTo(pt);
            }
            sg.EndFigure(true);
        }
        ctx.DrawGeometry(new ImmutableSolidColorBrush(color), null, g);
    }

    public static void Heart(DrawingContext ctx, Point c, double r, Color color)
    {
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            sg.BeginFigure(new Point(c.X, c.Y + r * 0.9), true);
            sg.CubicBezierTo(new Point(c.X - r * 1.4, c.Y + r * 0.1), new Point(c.X - r * 1.1, c.Y - r * 1.1), new Point(c.X, c.Y - r * 0.45));
            sg.CubicBezierTo(new Point(c.X + r * 1.1, c.Y - r * 1.1), new Point(c.X + r * 1.4, c.Y + r * 0.1), new Point(c.X, c.Y + r * 0.9));
            sg.EndFigure(true);
        }
        ctx.DrawGeometry(new ImmutableSolidColorBrush(color), null, g);
    }

    public static void Drop(DrawingContext ctx, Point c, double r, Color color)
    {
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            sg.BeginFigure(new Point(c.X, c.Y - r * 1.6), true);
            sg.CubicBezierTo(new Point(c.X + r * 0.4, c.Y - r * 0.8), new Point(c.X + r, c.Y - r * 0.2), new Point(c.X + r, c.Y + r * 0.3));
            sg.ArcTo(new Point(c.X - r, c.Y + r * 0.3), new Size(r, r), 0, true, SweepDirection.Clockwise);
            sg.CubicBezierTo(new Point(c.X - r, c.Y - r * 0.2), new Point(c.X - r * 0.4, c.Y - r * 0.8), new Point(c.X, c.Y - r * 1.6));
            sg.EndFigure(true);
        }
        ctx.DrawGeometry(new ImmutableSolidColorBrush(color), null, g);
    }
}

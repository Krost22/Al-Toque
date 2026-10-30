using System.Globalization;
using AlToque.Controls;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AlToque.Chasqui;

public enum ChasquiEyes
{
    Normal,
    Happy,
    Wide,
    Focused,
    Worried,
    Cross,
    Dizzy,
    Sleepy,
    Meh,
    Wink,
}

public enum ChasquiMouth
{
    None,
    Smile,
    Open,
    O,
    Wobble,
    Flat,
}

/// <summary>Una "foto" de Chasqui: posición, giro, deformación y expresión, en la caja de 100 × 100.</summary>
public struct ChasquiPose
{
    public double X, Y, Rotation;
    public double Scale, Sx, Sy;
    public double Opacity;
    public bool Flip;

    /// <summary>0 = índigo, 1 = gris (rechazado), -1 = rojo (error); se mezcla de forma continua.</summary>
    public double Tint;

    public double Blink;
    public Vector Look;
    public ChasquiEyes Eyes;
    public ChasquiMouth Mouth;

    /// <summary>Aleteo del ala superior, -1..1.</summary>
    public double Flap;

    public static ChasquiPose Rest => new() { Scale = 1, Sx = 1, Sy = 1, Opacity = 1 };

    /// <summary>Mezcla dos poses (fundido cruzado entre estados); la expresión cambia a mitad.</summary>
    public static ChasquiPose Lerp(in ChasquiPose a, in ChasquiPose b, double t)
    {
        var r = t < 0.5 ? a : b;
        r.X = Ease.Lerp(a.X, b.X, t);
        r.Y = Ease.Lerp(a.Y, b.Y, t);
        r.Rotation = Ease.Lerp(a.Rotation, b.Rotation, t);
        r.Scale = Ease.Lerp(a.Scale, b.Scale, t);
        r.Sx = Ease.Lerp(a.Sx, b.Sx, t);
        r.Sy = Ease.Lerp(a.Sy, b.Sy, t);
        r.Opacity = Ease.Lerp(a.Opacity, b.Opacity, t);
        r.Tint = Ease.Lerp(a.Tint, b.Tint, t);
        r.Blink = Ease.Lerp(a.Blink, b.Blink, t);
        r.Look = a.Look + (b.Look - a.Look) * t;
        r.Flap = Ease.Lerp(a.Flap, b.Flap, t);
        return r;
    }
}

/// <summary>
/// Dibuja a Chasqui, el avión de papel con ojos, con volumen suave y caras expresivas. Lo usan la vista grande y la
/// pista de progreso de las tarjetas.
/// </summary>
public static class ChasquiPainter
{
    private static readonly Color[] Indigo = [ColorUtil.Hex("#9A9AFF"), ColorUtil.Hex("#5B5BF0"), ColorUtil.Hex("#3434B0")];
    private static readonly Color[] Gray = [ColorUtil.Hex("#D2D2DE"), ColorUtil.Hex("#A4A4B8"), ColorUtil.Hex("#7C7C92")];
    private static readonly Color[] Red = [ColorUtil.Hex("#FFA3A6"), ColorUtil.Hex("#E5484D"), ColorUtil.Hex("#A92F34")];
    private static readonly IBrush White = new ImmutableSolidColorBrush(Colors.White);
    private static readonly IBrush Pupil = new ImmutableSolidColorBrush(ColorUtil.Hex("#1B1B38"));
    private static readonly Point LeftEye = new(46, 41);
    private static readonly Point RightEye = new(56.5, 37.5);
    private static readonly Typeface GlyphFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Black, FontStretch.Normal);

    /// <summary>Dibuja a Chasqui en la caja de 100 (centro en 50, 50) con la pose dada.</summary>
    public static void Draw(DrawingContext ctx, in ChasquiPose pose, bool shadow = true)
    {
        if (pose.Opacity <= 0.001 || pose.Scale <= 0.001)
            return;
        var flip = pose.Flip ? -1 : 1;
        var m = Matrix.CreateTranslation(-50, -50)
                * Matrix.CreateScale(flip * pose.Scale * pose.Sx, pose.Scale * pose.Sy)
                * Matrix.CreateRotation(flip * pose.Rotation * Math.PI / 180)
                * Matrix.CreateTranslation(50 + pose.X, 50 + pose.Y);
        using var _ = ctx.PushTransform(m);
        using var __ = ctx.PushOpacity(pose.Opacity);

        var colors = Palette(pose.Tint);
        // El ala superior se flexiona en la cola: aletea.
        var tail = new Point(13, 46 - pose.Flap * 6);
        var upper = Polygon(new Point(86, 20), tail, new Point(46, 55));
        var lower = Polygon(new Point(86, 20), new Point(46, 55), new Point(44, 84));
        var fold = Polygon(new Point(46, 55), new Point(44, 84), new Point(55, 62));

        if (shadow)
        {
            // Sombra suave sin desenfoque (barata a 60 fps): dos copias desplazadas y translúcidas.
            for (int i = 1; i <= 2; i++)
            {
                var shade = new ImmutableSolidColorBrush(Color.FromArgb((byte)(i == 1 ? 34 : 22), 10, 10, 40));
                using (ctx.PushTransform(Matrix.CreateTranslation(0, 2.5 * i + 1)))
                {
                    ctx.DrawGeometry(shade, null, upper);
                    ctx.DrawGeometry(shade, null, lower);
                }
            }
        }

        ctx.DrawGeometry(Gradient(colors[0], ColorUtil.Lighten(colors[0], 0.06), new RelativePoint(0.9, 0, RelativeUnit.Relative), new RelativePoint(0.1, 1, RelativeUnit.Relative)), null, upper);
        ctx.DrawGeometry(Gradient(colors[1], ColorUtil.Darken(colors[1], 0.07), new RelativePoint(1, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative)), null, lower);
        ctx.DrawGeometry(new ImmutableSolidColorBrush(colors[2]), null, fold);
        // Pliegue: un filo de luz sobre la arista central.
        ctx.DrawLine(new Pen(new ImmutableSolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 0.9, lineCap: PenLineCap.Round), new Point(85, 21), new Point(47, 54));

        DrawFace(ctx, pose, colors[0]);
    }

    private static Color[] Palette(double tint)
    {
        if (Math.Abs(tint) < 0.001)
            return Indigo;
        var other = tint > 0 ? Gray : Red;
        var k = Math.Min(1, Math.Abs(tint));
        return [ColorUtil.Mix(Indigo[0], other[0], k), ColorUtil.Mix(Indigo[1], other[1], k), ColorUtil.Mix(Indigo[2], other[2], k)];
    }

    private static LinearGradientBrush Gradient(Color a, Color b, RelativePoint start, RelativePoint end) => new()
    {
        StartPoint = start,
        EndPoint = end,
        GradientStops = { new GradientStop(b, 0), new GradientStop(a, 1) },
    };

    private static void DrawFace(DrawingContext ctx, in ChasquiPose pose, Color lid)
    {
        var look = new Vector(Math.Clamp(pose.Look.X, -1, 1), Math.Clamp(pose.Look.Y, -1, 1));
        var arc = new Pen(White, 2.1, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var ink = new Pen(Pupil, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        for (int i = 0; i < 2; i++)
        {
            var c = i == 0 ? LeftEye : RightEye;
            var isRight = i == 1;
            var eyes = pose.Eyes == ChasquiEyes.Wink ? (isRight ? ChasquiEyes.Happy : ChasquiEyes.Normal) : pose.Eyes;
            switch (eyes)
            {
                case ChasquiEyes.Happy:
                    ctx.DrawGeometry(null, arc, Arc(c, 4, up: true));
                    continue;
                case ChasquiEyes.Cross:
                    ctx.DrawEllipse(White, null, c, 4.6, 4.6);
                    ctx.DrawLine(ink, c + new Vector(-2.2, -2.2), c + new Vector(2.2, 2.2));
                    ctx.DrawLine(ink, c + new Vector(-2.2, 2.2), c + new Vector(2.2, -2.2));
                    continue;
                case ChasquiEyes.Dizzy:
                    ctx.DrawEllipse(White, null, c, 4.6, 4.6);
                    ctx.DrawGeometry(null, new Pen(Pupil, 1.1, lineCap: PenLineCap.Round), Spiral(c, 3.4, AnimClock.Now * 9 + i));
                    continue;
            }

            var r = eyes == ChasquiEyes.Wide ? 5.4 : 4.6;
            var ry = r * (1 - pose.Blink);
            if (ry < 0.9)
            {
                ctx.DrawGeometry(null, arc, Arc(c, 3.8, up: false));
                continue;
            }
            ctx.DrawEllipse(White, new Pen(Pupil, 0.7), c, r, ry);
            var pr = eyes == ChasquiEyes.Wide ? 1.7 : 2.25;
            var pupilOffset = new Vector(look.X * 1.8, look.Y * 1.8);
            if (eyes is ChasquiEyes.Sleepy or ChasquiEyes.Meh)
                pupilOffset += new Vector(eyes == ChasquiEyes.Meh ? 1.2 : 0, 1.4);
            if (eyes == ChasquiEyes.Worried)
                pupilOffset += new Vector(0, -1.2);
            using (ctx.PushGeometryClip(new EllipseGeometry(new Rect(c.X - r, c.Y - ry, r * 2, ry * 2))))
            {
                ctx.DrawEllipse(Pupil, null, c + pupilOffset, pr, pr);
                ctx.DrawEllipse(White, null, c + pupilOffset + new Vector(0.8, -0.9), pr * 0.35, pr * 0.35);
                // Párpados: cubren parte del ojo según la expresión.
                var lidBrush = new ImmutableSolidColorBrush(lid);
                switch (eyes)
                {
                    case ChasquiEyes.Focused:
                        ctx.DrawGeometry(lidBrush, null, Polygon(c + new Vector(-6, -7), c + new Vector(6, -7), c + new Vector(6, isRight ? -0.6 : -2.8), c + new Vector(-6, isRight ? -2.8 : -0.6)));
                        break;
                    case ChasquiEyes.Sleepy:
                        ctx.FillRectangle(lidBrush, new Rect(c.X - 6, c.Y - 7, 12, 7.2));
                        break;
                    case ChasquiEyes.Meh:
                        ctx.FillRectangle(lidBrush, new Rect(c.X - 6, c.Y - 7, 12, 6));
                        break;
                }
            }
            if (eyes == ChasquiEyes.Worried)
            {
                var dir = isRight ? -1 : 1;
                ctx.DrawLine(new Pen(Pupil, 1.4, lineCap: PenLineCap.Round), c + new Vector(-3.4 * dir, -6.2), c + new Vector(2.6 * dir, -8.4));
            }
        }

        var mouth = new Point(53, 48.5);
        var pen = new Pen(Pupil, 1.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using var tilt = ctx.PushTransform(Matrix.CreateTranslation(-mouth.X, -mouth.Y) * Matrix.CreateRotation(-0.33) * Matrix.CreateTranslation(mouth.X, mouth.Y));
        switch (pose.Mouth)
        {
            case ChasquiMouth.Smile:
                ctx.DrawGeometry(null, pen, Quad(mouth + new Vector(-3, -0.5), mouth + new Vector(0, 2.6), mouth + new Vector(3, -0.5)));
                break;
            case ChasquiMouth.Open:
            {
                var g = new StreamGeometry();
                using (var sg = g.Open())
                {
                    sg.BeginFigure(mouth + new Vector(-3.6, -0.8), true);
                    sg.CubicBezierTo(mouth + new Vector(-3, 4), mouth + new Vector(3, 4), mouth + new Vector(3.6, -0.8));
                    sg.EndFigure(true);
                }
                ctx.DrawGeometry(Pupil, null, g);
                using (ctx.PushGeometryClip(g))
                    ctx.DrawEllipse(new ImmutableSolidColorBrush(ColorUtil.Hex("#FF8A92")), null, mouth + new Vector(0, 2.6), 2, 1.3);
                break;
            }
            case ChasquiMouth.O:
                ctx.DrawEllipse(Pupil, null, mouth + new Vector(0, 0.8), 1.5, 1.9);
                break;
            case ChasquiMouth.Wobble:
            {
                var g = new StreamGeometry();
                using (var sg = g.Open())
                {
                    sg.BeginFigure(mouth + new Vector(-3.4, 0.6), false);
                    sg.QuadraticBezierTo(mouth + new Vector(-1.7, -1.2), mouth + new Vector(0, 0.6));
                    sg.QuadraticBezierTo(mouth + new Vector(1.7, 2.2), mouth + new Vector(3.4, 0.6));
                    sg.EndFigure(false);
                }
                ctx.DrawGeometry(null, pen, g);
                break;
            }
            case ChasquiMouth.Flat:
                ctx.DrawLine(pen, mouth + new Vector(-2.2, 0.8), mouth + new Vector(2.2, 0.8));
                break;
        }
    }

    private static Geometry Arc(Point c, double r, bool up) => up
        ? Quad(new Point(c.X - r, c.Y + r * 0.4), new Point(c.X, c.Y - r * 1.1), new Point(c.X + r, c.Y + r * 0.4))
        : Quad(new Point(c.X - r, c.Y - r * 0.1), new Point(c.X, c.Y + r * 0.9), new Point(c.X + r, c.Y - r * 0.1));

    private static Geometry Quad(Point a, Point control, Point b)
    {
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            sg.BeginFigure(a, false);
            sg.QuadraticBezierTo(control, b);
            sg.EndFigure(false);
        }
        return g;
    }

    private static Geometry Spiral(Point c, double r, double phase)
    {
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            for (int i = 0; i <= 24; i++)
            {
                var t = i / 24.0;
                var a = phase + t * Math.PI * 4;
                var p = new Point(c.X + Math.Cos(a) * r * t, c.Y + Math.Sin(a) * r * t);
                if (i == 0)
                    sg.BeginFigure(p, false);
                else
                    sg.LineTo(p);
            }
            sg.EndFigure(false);
        }
        return g;
    }

    internal static Geometry Polygon(params Point[] points)
    {
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            sg.BeginFigure(points[0], true);
            foreach (var p in points.AsSpan(1))
                sg.LineTo(p);
            sg.EndFigure(true);
        }
        return g;
    }

    /// <summary>Signo ("!", "?", "z") con borde, para los bocadillos de Chasqui.</summary>
    public static void Glyph(DrawingContext ctx, string text, Point center, double size, Color color, double opacity = 1, double rotation = 0)
    {
        if (opacity <= 0.01 || size <= 0.5)
            return;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, GlyphFace, size, new ImmutableSolidColorBrush(color));
        using var o = ctx.PushOpacity(opacity);
        using var t = ctx.PushTransform(Matrix.CreateRotation(rotation * Math.PI / 180) * Matrix.CreateTranslation(center.X, center.Y));
        ctx.DrawText(ft, new Point(-ft.Width / 2, -ft.Height / 2));
    }
}

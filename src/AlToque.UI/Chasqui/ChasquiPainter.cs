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
/// Dibuja a Chasqui, la carpeta con cara, guantes y zapatillas, con volumen suave y caras expresivas. Lo usan la vista
/// grande y la pista de progreso de las tarjetas. El "aleteo" de la pose mueve brazos y piernas: saluda en reposo y
/// corre al enviar.
/// </summary>
public static class ChasquiPainter
{
    // Colores por tinte: frente claro, frente oscuro, tapa de atrás, lado, zapatilla.
    private static readonly Color[] Blue = [ColorUtil.Hex("#52B8FF"), ColorUtil.Hex("#6A3BF0"), ColorUtil.Hex("#2D5FEA"), ColorUtil.Hex("#3B2FC8"), ColorUtil.Hex("#5A4FF0")];
    private static readonly Color[] Gray = [ColorUtil.Hex("#DCDCE6"), ColorUtil.Hex("#9A9AB0"), ColorUtil.Hex("#A8A8BC"), ColorUtil.Hex("#7C7C92"), ColorUtil.Hex("#8E8EA4")];
    private static readonly Color[] Red = [ColorUtil.Hex("#FFA3A6"), ColorUtil.Hex("#D93A44"), ColorUtil.Hex("#E5484D"), ColorUtil.Hex("#A92F34"), ColorUtil.Hex("#C2363D")];
    private static readonly IBrush White = new ImmutableSolidColorBrush(Colors.White);
    private static readonly IBrush Pupil = new ImmutableSolidColorBrush(ColorUtil.Hex("#151A45"));
    private static readonly IBrush Tongue = new ImmutableSolidColorBrush(ColorUtil.Hex("#F06BC8"));
    private static readonly IBrush Paper = new ImmutableSolidColorBrush(ColorUtil.Hex("#F3F4FF"));
    private static readonly IPen PaperEdge = new ImmutablePen(new ImmutableSolidColorBrush(ColorUtil.Hex("#C9CCF5")), 0.6);
    private static readonly IPen Limb = new ImmutablePen(new ImmutableSolidColorBrush(ColorUtil.Hex("#1C1D4A")), 3.6, lineCap: PenLineCap.Round);
    private static readonly IPen GloveEdge = new ImmutablePen(new ImmutableSolidColorBrush(ColorUtil.Hex("#AEB2F0")), 0.8);
    private static readonly IPen Rim = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 0.8);
    private static readonly IBrush Shadow = new ImmutableSolidColorBrush(Color.FromArgb(40, 70, 70, 200));
    private static readonly Point LeftEye = new(38, 53.5);
    private static readonly Point RightEye = new(59, 53.5);
    private static readonly Typeface GlyphFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Black, FontStretch.Normal);

    private static readonly Geometry BackPlate = new RectangleGeometry(new Rect(29, 24, 47, 43), 6, 6);
    private static readonly Geometry BackTab = new RectangleGeometry(new Rect(31, 19.5, 20, 9), 3, 3);
    private static readonly Geometry Sheet = new RectangleGeometry(new Rect(42, 28, 27, 13), 2.5, 2.5);
    private static readonly Geometry Front = Geometry.Parse(
        "M23,40 Q23,33 30,33 L45,33 Q48,33 50,36 L51,37.5 Q52.5,39 55,39 L66,39 Q72,39 72,45 L72,66 Q72,73 65,73 L30,73 Q23,73 23,66 Z");

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
        if (shadow)
            ctx.DrawEllipse(Shadow, null, new Point(50, 93.5), 24, 3);

        // Un poco ladeado, como en el dibujo: con ganas de moverse.
        using var tilt = ctx.PushTransform(Matrix.CreateTranslation(-50, -55) * Matrix.CreateRotation(5 * Math.PI / 180) * Matrix.CreateTranslation(50, 55));
        var swing = Math.Clamp(pose.Flap, -1, 1);

        // Piernas y zapatillas (detrás del cuerpo): se cruzan al caminar.
        Leg(ctx, new Point(41, 70), 100 + swing * 16, colors[4], toeLeft: true);
        Leg(ctx, new Point(58, 71), 80 - swing * 16, colors[4], toeLeft: false);

        // Brazos: el izquierdo saluda en alto, el derecho abierto hacia afuera.
        Arm(ctx, new Point(26, 52), -128 + swing * 26, 16.5, bend: -1);
        Arm(ctx, new Point(71, 60), 8 - swing * 18, 13, bend: 1);

        // La carpeta: tapa de atrás con su pestaña, la hoja que asoma y el frente con la cara.
        var back = new ImmutableSolidColorBrush(colors[2]);
        ctx.DrawGeometry(back, null, BackTab);
        ctx.DrawGeometry(back, null, BackPlate);
        ctx.DrawRectangle(new ImmutableSolidColorBrush(colors[3]), null, new Rect(66, 34, 11, 34), 5, 5);
        ctx.DrawGeometry(Paper, PaperEdge, Sheet);
        var frontBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(23, 33, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(72, 73, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(colors[0], 0), new GradientStop(ColorUtil.Mix(colors[0], colors[1], 0.55), 0.55), new GradientStop(colors[1], 1) },
        };
        ctx.DrawGeometry(frontBrush, Rim, Front);

        DrawFace(ctx, pose, ColorUtil.Mix(colors[0], colors[1], 0.45));
    }

    private static Color[] Palette(double tint)
    {
        if (Math.Abs(tint) < 0.001)
            return Blue;
        var other = tint > 0 ? Gray : Red;
        var k = Math.Min(1, Math.Abs(tint));
        var mixed = new Color[Blue.Length];
        for (int i = 0; i < mixed.Length; i++)
            mixed[i] = ColorUtil.Mix(Blue[i], other[i], k);
        return mixed;
    }

    private static Point Toward(Point from, double degrees, double length) =>
        new(from.X + Math.Cos(degrees * Math.PI / 180) * length, from.Y + Math.Sin(degrees * Math.PI / 180) * length);

    /// <summary>Brazo azul marino un poco curvo y guante blanco de dibujo animado.</summary>
    private static void Arm(DrawingContext ctx, Point shoulder, double degrees, double length, int bend)
    {
        var hand = Toward(shoulder, degrees, length);
        var mid = Toward(shoulder, degrees, length / 2);
        var normal = new Vector(-Math.Sin(degrees * Math.PI / 180), Math.Cos(degrees * Math.PI / 180)) * (2.2 * bend);
        ctx.DrawGeometry(null, Limb, Quad(shoulder, mid + normal, hand));
        // Guante: puño, palma y dos dedos.
        var dir = new Vector(Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180));
        var palm = hand + dir * 4;
        ctx.DrawEllipse(White, GloveEdge, Toward(palm, degrees - 38, 4.6), 2.5, 2.5);
        ctx.DrawEllipse(White, GloveEdge, Toward(palm, degrees + 22, 4.9), 2.5, 2.5);
        ctx.DrawEllipse(White, GloveEdge, palm, 5.1, 5.1);
        ctx.DrawEllipse(White, GloveEdge, hand, 3.2, 3.2);
    }

    /// <summary>Pierna y zapatilla (capellada de color, suela y puntera blancas).</summary>
    private static void Leg(DrawingContext ctx, Point hip, double degrees, Color shoe, bool toeLeft)
    {
        // 90° es recto hacia abajo.
        var ankle = Toward(hip, degrees, 12);
        ctx.DrawLine(Limb, hip, ankle);
        var toe = toeLeft ? -1 : 1;
        var upper = new StreamGeometry();
        using (var sg = upper.Open())
        {
            sg.BeginFigure(new Point(ankle.X - 5.5 + toe * 1.5, ankle.Y + 2.4), true);
            sg.CubicBezierTo(new Point(ankle.X - 5.5 + toe * 1.5, ankle.Y - 3.8), new Point(ankle.X + 5.5 + toe * 1.5, ankle.Y - 3.8), new Point(ankle.X + 5.5 + toe * 1.5, ankle.Y + 2.4));
            sg.EndFigure(true);
        }
        ctx.DrawGeometry(new ImmutableSolidColorBrush(shoe), null, upper);
        ctx.DrawEllipse(White, GloveEdge, new Point(ankle.X + toe * 6.4, ankle.Y + 1.6), 3.6, 2.9);
        ctx.DrawRectangle(White, GloveEdge, new Rect(ankle.X - 7.4 + toe * 3, ankle.Y + 2.2, 14.8, 4.2), 2.1, 2.1);
        // Cuello blanco de la zapatilla.
        ctx.DrawEllipse(White, GloveEdge, new Point(ankle.X + toe * 0.4, ankle.Y - 1.4), 2.6, 1.3);
    }

    private static void DrawFace(DrawingContext ctx, in ChasquiPose pose, Color skin)
    {
        var look = new Vector(Math.Clamp(pose.Look.X, -1, 1), Math.Clamp(pose.Look.Y, -1, 1)) * 1.3;
        var arc = new Pen(Pupil, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var lid = new ImmutableSolidColorBrush(skin);
        for (int i = 0; i < 2; i++)
        {
            var c = (i == 0 ? LeftEye : RightEye) + look;
            var isRight = i == 1;
            var eyes = pose.Eyes == ChasquiEyes.Wink ? (isRight ? ChasquiEyes.Happy : ChasquiEyes.Normal) : pose.Eyes;
            switch (eyes)
            {
                case ChasquiEyes.Happy:
                    ctx.DrawGeometry(null, arc, Arc(c, 3.6, up: true));
                    continue;
                case ChasquiEyes.Cross:
                    ctx.DrawLine(arc, c + new Vector(-2.6, -2.6), c + new Vector(2.6, 2.6));
                    ctx.DrawLine(arc, c + new Vector(-2.6, 2.6), c + new Vector(2.6, -2.6));
                    continue;
                case ChasquiEyes.Dizzy:
                    ctx.DrawGeometry(null, new Pen(Pupil, 1.3, lineCap: PenLineCap.Round), Spiral(c, 3.8, AnimClock.Now * 9 + i));
                    continue;
            }

            var rx = eyes == ChasquiEyes.Wide ? 4.8 : 4.2;
            var ry = (eyes == ChasquiEyes.Wide ? 6 : 5.3) * (1 - pose.Blink);
            if (ry < 1)
            {
                ctx.DrawGeometry(null, arc, Arc(c, 3.3, up: false));
                continue;
            }
            ctx.DrawEllipse(Pupil, null, c, rx, ry);
            ctx.DrawEllipse(White, null, c + new Vector(1.1, -ry * 0.4), rx * 0.33, rx * 0.33);
            // Párpados del color de la carpeta: tapan parte del ojo según la expresión.
            using (ctx.PushGeometryClip(new EllipseGeometry(new Rect(c.X - rx, c.Y - ry, rx * 2, ry * 2))))
            {
                switch (eyes)
                {
                    case ChasquiEyes.Focused:
                        ctx.DrawGeometry(lid, null, Polygon(c + new Vector(-6, -7), c + new Vector(6, -7), c + new Vector(6, isRight ? -0.8 : -3), c + new Vector(-6, isRight ? -3 : -0.8)));
                        break;
                    case ChasquiEyes.Sleepy:
                        ctx.FillRectangle(lid, new Rect(c.X - 6, c.Y - 7, 12, 7.4));
                        break;
                    case ChasquiEyes.Meh:
                        ctx.FillRectangle(lid, new Rect(c.X - 6, c.Y - 7, 12, 5.6));
                        break;
                }
            }
            if (eyes == ChasquiEyes.Worried)
            {
                var dir = isRight ? -1 : 1;
                ctx.DrawLine(new Pen(Pupil, 1.4, lineCap: PenLineCap.Round), c + new Vector(-3.4 * dir, -7), c + new Vector(2.6 * dir, -9.2));
            }
        }

        var mouth = new Point(48.5, 59) + look * 0.6;
        var pen = new Pen(Pupil, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        switch (pose.Mouth)
        {
            case ChasquiMouth.None or ChasquiMouth.Smile:
                ctx.DrawGeometry(null, pen, Quad(mouth + new Vector(-3.4, -0.6), mouth + new Vector(0, 3), mouth + new Vector(3.4, -0.6)));
                break;
            case ChasquiMouth.Open:
            {
                var g = new StreamGeometry();
                using (var sg = g.Open())
                {
                    sg.BeginFigure(mouth + new Vector(-4, -1.6), true);
                    sg.QuadraticBezierTo(mouth + new Vector(0, -0.6), mouth + new Vector(4, -1.6));
                    sg.CubicBezierTo(mouth + new Vector(3.6, 4.6), mouth + new Vector(-3.6, 4.6), mouth + new Vector(-4, -1.6));
                    sg.EndFigure(true);
                }
                ctx.DrawGeometry(Pupil, null, g);
                using (ctx.PushGeometryClip(g))
                    ctx.DrawEllipse(Tongue, null, mouth + new Vector(0, 3.4), 2.6, 1.7);
                break;
            }
            case ChasquiMouth.O:
                ctx.DrawEllipse(Pupil, null, mouth + new Vector(0, 0.8), 1.8, 2.2);
                break;
            case ChasquiMouth.Wobble:
            {
                var g = new StreamGeometry();
                using (var sg = g.Open())
                {
                    sg.BeginFigure(mouth + new Vector(-3.6, 0.6), false);
                    sg.QuadraticBezierTo(mouth + new Vector(-1.8, -1.2), mouth + new Vector(0, 0.6));
                    sg.QuadraticBezierTo(mouth + new Vector(1.8, 2.4), mouth + new Vector(3.6, 0.6));
                    sg.EndFigure(false);
                }
                ctx.DrawGeometry(null, pen, g);
                break;
            }
            case ChasquiMouth.Flat:
                ctx.DrawLine(pen, mouth + new Vector(-2.4, 0.8), mouth + new Vector(2.4, 0.8));
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AlToque.Controls;

/// <summary>
/// El logo de Al Toque en vectores: dos carpetas (azul y violeta) que se pasan archivos, con las flechas del
/// intercambio, sobre un cuadrado azul noche. Mismo dibujo que los íconos (branding/altoque.svg, lienzo de 256).
/// </summary>
public sealed class BrandMark : Control
{
    private static readonly Geometry BlueBack = Geometry.Parse("M52.5,48.0 L82.7,48.0 Q87.6,48.0 89.9,52.3 L94.4,60.5 Q96.7,64.8 101.6,64.8 L142.6,64.8 Q150.7,64.8 149.9,72.9 L144.8,123.9 Q144.0,132.0 135.9,132.0 L44.1,132.0 Q36.0,132.0 36.8,123.9 L43.6,56.1 Q44.4,48.0 52.5,48.0 Z");
    private static readonly Geometry BlueFront = Geometry.Parse("M57.7,76.6 L143.1,76.6 Q152.8,76.6 150.4,86.0 L141.0,122.6 Q138.6,132.0 128.9,132.0 L43.6,132.0 Q33.8,132.0 36.2,122.6 L45.6,86.0 Q48.0,76.6 57.7,76.6 Z");
    private static readonly Geometry PurpleBack = Geometry.Parse("M143.1,132.0 L171.1,132.0 Q175.6,132.0 177.8,135.9 L181.9,143.3 Q184.1,147.2 188.6,147.2 L226.6,147.2 Q234.1,147.2 233.3,154.7 L228.7,200.5 Q228.0,208.0 220.5,208.0 L135.5,208.0 Q128.0,208.0 128.7,200.5 L134.9,139.5 Q135.6,132.0 143.1,132.0 Z");
    private static readonly Geometry PurpleFront = Geometry.Parse("M148.0,157.8 L227.0,157.8 Q236.0,157.8 233.8,166.6 L225.3,199.3 Q223.0,208.0 214.0,208.0 L135.0,208.0 Q126.0,208.0 128.3,199.3 L136.8,166.6 Q139.0,157.8 148.0,157.8 Z");
    private static readonly Geometry Arc1 = Geometry.Parse("M120.0,70.8 A44,44 0 0,1 200.0,96.0");
    private static readonly Geometry Arc2 = Geometry.Parse("M136.0,185.2 A44,44 0 0,1 56.0,160.0");
    private static readonly Geometry Head1 = Geometry.Parse("M197.1,121.9 L179.3,96.3 Q177.0,93.0 181.0,93.0 L219.0,93.0 Q223.0,93.0 220.7,96.3 L202.9,121.9 Q200.0,126.0 197.1,121.9 Z");
    private static readonly Geometry Head2 = Geometry.Parse("M58.9,134.1 L76.7,159.7 Q79.0,163.0 75.0,163.0 L37.0,163.0 Q33.0,163.0 35.3,159.7 L53.1,134.1 Q56.0,130.0 58.9,134.1 Z");

    private static readonly IBrush Tile = Linear(0, 0, 256, 256, ("#1F2D96", 0), ("#121757", 0.6), ("#1C1062", 1));
    private static readonly IBrush Glow = new RadialGradientBrush
    {
        Center = new RelativePoint(60, 40, RelativeUnit.Absolute),
        GradientOrigin = new RelativePoint(60, 40, RelativeUnit.Absolute),
        RadiusX = new RelativeScalar(170, RelativeUnit.Absolute),
        RadiusY = new RelativeScalar(170, RelativeUnit.Absolute),
        GradientStops = { new GradientStop(Color.Parse("#593B4CC0"), 0), new GradientStop(Color.Parse("#003B4CC0"), 1) },
    };
    private static readonly IPen Arc1Pen = new Pen(Linear(120, 70.8, 200, 96, ("#007F95FF", 0), ("#C8A9B6FF", 0.45), ("#EEF0FF", 1)), 20, lineCap: PenLineCap.Round);
    private static readonly IPen Arc2Pen = new Pen(Linear(136, 185.2, 56, 160, ("#3C7C3AED", 0), ("#9B6BFF", 0.5), ("#F1EAFF", 1)), 20, lineCap: PenLineCap.Round);
    private static readonly IBrush Head1Brush = new SolidColorBrush(Color.Parse("#EEF0FF"));
    private static readonly IBrush Head2Brush = new SolidColorBrush(Color.Parse("#F1EAFF"));
    private static readonly IBrush BlueBackBrush = Linear(36, 48, 150, 132, ("#38B6FF", 0), ("#2E6BF2", 1));
    private static readonly IBrush BlueFrontBrush = Linear(36, 73, 150, 132, ("#7CE0FF", 0), ("#3E6DFF", 1));
    private static readonly IBrush PurpleBackBrush = Linear(128, 132, 234, 208, ("#B48BFF", 0), ("#7A45EE", 1));
    private static readonly IBrush PurpleFrontBrush = Linear(128, 155, 234, 208, ("#DDB8FF", 0), ("#8645FF", 1));
    private static readonly IPen Edge = new Pen(new SolidColorBrush(Color.Parse("#46FFFFFF")), 1.6);

    static BrandMark()
    {
        WidthProperty.OverrideDefaultValue<BrandMark>(30);
        HeightProperty.OverrideDefaultValue<BrandMark>(30);
    }

    private static LinearGradientBrush Linear(double x0, double y0, double x1, double y1, params (string Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(x0, y0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(x1, y1, RelativeUnit.Absolute),
        };
        foreach (var (color, offset) in stops)
            brush.GradientStops.Add(new GradientStop(Color.Parse(color), offset));
        return brush;
    }

    public override void Render(DrawingContext ctx)
    {
        var s = Math.Min(Bounds.Width, Bounds.Height);
        if (s <= 0)
            return;
        using var _ = ctx.PushTransform(Matrix.CreateScale(s / 256, s / 256));
        var tile = new Rect(0, 0, 256, 256);
        ctx.DrawRectangle(Tile, null, tile, 66, 66);
        ctx.DrawRectangle(Glow, null, tile, 66, 66);
        // Las flechas van debajo: su cola se esconde detrás de las carpetas.
        ctx.DrawGeometry(null, Arc1Pen, Arc1);
        ctx.DrawGeometry(Head1Brush, null, Head1);
        ctx.DrawGeometry(null, Arc2Pen, Arc2);
        ctx.DrawGeometry(Head2Brush, null, Head2);
        ctx.DrawGeometry(BlueBackBrush, null, BlueBack);
        ctx.DrawGeometry(BlueFrontBrush, Edge, BlueFront);
        ctx.DrawGeometry(PurpleBackBrush, null, PurpleBack);
        ctx.DrawGeometry(PurpleFrontBrush, Edge, PurpleFront);
    }
}

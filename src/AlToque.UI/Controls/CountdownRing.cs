using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AlToque.Controls;

/// <summary>Anillo que se vacía con el tiempo que queda (emparejar, ofertas que vencen).</summary>
public sealed class CountdownRing : Control
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<CountdownRing, double>(nameof(Fraction), 1);

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<CountdownRing, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<CountdownRing, IBrush?>(nameof(Track));

    static CountdownRing()
    {
        AffectsRender<CountdownRing>(FractionProperty, StrokeProperty, TrackProperty);
        WidthProperty.OverrideDefaultValue<CountdownRing>(20);
        HeightProperty.OverrideDefaultValue<CountdownRing>(20);
    }

    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var thickness = Math.Max(2, size * 0.14);
        var r = (size - thickness) / 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (Track is { } track)
            ctx.DrawEllipse(null, new Pen(track, thickness), c, r, r);
        var f = Math.Clamp(Fraction, 0, 1);
        if (f <= 0 || Stroke is not { } stroke)
            return;
        var pen = new Pen(stroke, thickness, lineCap: PenLineCap.Round);
        if (f >= 0.999)
        {
            ctx.DrawEllipse(null, pen, c, r, r);
            return;
        }
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            var start = -Math.PI / 2;
            var end = start + f * Math.PI * 2;
            sg.BeginFigure(new Point(c.X + Math.Cos(start) * r, c.Y + Math.Sin(start) * r), false);
            sg.ArcTo(new Point(c.X + Math.Cos(end) * r, c.Y + Math.Sin(end) * r), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise);
            sg.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AlToque.Controls;

/// <summary>
/// Borde discontinuo con esquinas redondeadas (zonas para soltar, destinos posibles). Con <see cref="Marching"/>
/// los trazos avanzan como hormigas para invitar a soltar.
/// </summary>
public sealed class DashedBorder : Decorator
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<DashedBorder, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<DashedBorder, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<DashedBorder, double>(nameof(StrokeThickness), 1.25);

    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<DashedBorder, double>(nameof(Radius), 8);

    public static readonly StyledProperty<bool> MarchingProperty =
        AvaloniaProperty.Register<DashedBorder, bool>(nameof(Marching));

    public static readonly StyledProperty<bool> SolidProperty =
        AvaloniaProperty.Register<DashedBorder, bool>(nameof(Solid));

    static DashedBorder()
    {
        AffectsRender<DashedBorder>(StrokeProperty, FillProperty, StrokeThicknessProperty, RadiusProperty, SolidProperty);
        MarchingProperty.Changed.AddClassHandler<DashedBorder>((d, _) => d.RequestFrame());
        IsVisibleProperty.Changed.AddClassHandler<DashedBorder>((d, _) => Avalonia.Threading.Dispatcher.UIThread.Post(d.RequestFrame));
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public bool Marching
    {
        get => GetValue(MarchingProperty);
        set => SetValue(MarchingProperty, value);
    }

    public bool Solid
    {
        get => GetValue(SolidProperty);
        set => SetValue(SolidProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestFrame();
    }

    private bool _running;

    /// <summary>Las hormigas solo marchan mientras se ven: oculto no gasta fotogramas.</summary>
    private void RequestFrame()
    {
        if (_running || !Marching || MotionPrefs.Reduced || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _running = true;
        top.RequestAnimationFrame(_ =>
        {
            _running = false;
            InvalidateVisual();
            if (top.IsVisible)
                RequestFrame();
        });
    }

    public override void Render(DrawingContext context)
    {
        var t = StrokeThickness;
        var rect = new Rect(Bounds.Size).Deflate(t / 2);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        Pen? pen = null;
        if (Stroke is { } stroke)
        {
            var offset = Marching && !MotionPrefs.Reduced ? -(AnimClock.Now * 6 % 7) : 0;
            pen = new Pen(stroke, t, Solid ? null : new DashStyle([4 / t, 3 / t], offset / t), PenLineCap.Round);
        }
        context.DrawRectangle(Fill, pen, new RoundedRect(rect, Radius));
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Metadata;

namespace AlToque.Controls;

/// <summary>
/// Control segmentado: columnas iguales con <see cref="RadioButton"/> (clase <c>segment</c>) y un "pulgar" que se
/// desliza hasta la opción marcada en 167 ms con la curva Entrar.
/// </summary>
public sealed class Segmented : Control
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<Segmented>();

    public static readonly StyledProperty<IBrush?> ThumbBrushProperty =
        AvaloniaProperty.Register<Segmented, IBrush?>(nameof(ThumbBrush));

    public static readonly StyledProperty<IBrush?> ThumbBorderBrushProperty =
        AvaloniaProperty.Register<Segmented, IBrush?>(nameof(ThumbBorderBrush));

    private const double Inset = 4;
    private readonly UniformRow _row = new();
    private Rect _from, _to, _current;
    private double _animStart = -1;
    private bool _hasCurrent;

    static Segmented()
    {
        AffectsRender<Segmented>(BackgroundProperty, ThumbBrushProperty, ThumbBorderBrushProperty);
    }

    public Segmented()
    {
        VisualChildren.Add(_row);
        LogicalChildren.Add(_row);
        AddHandler(ToggleButton.IsCheckedChangedEvent, (_, _) => MoveThumb(animate: true), RoutingStrategies.Bubble);
    }

    [Content]
    public Avalonia.Controls.Controls Items => _row.Children;

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? ThumbBrush
    {
        get => GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }

    public IBrush? ThumbBorderBrush
    {
        get => GetValue(ThumbBorderBrushProperty);
        set => SetValue(ThumbBorderBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _row.Measure(availableSize.Deflate(new Thickness(Inset)));
        return _row.DesiredSize.Inflate(new Thickness(Inset));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _row.Arrange(new Rect(finalSize).Deflate(Inset));
        MoveThumb(animate: false);
        return finalSize;
    }

    private Rect TargetRect()
    {
        foreach (var child in _row.Children)
        {
            if (child is ToggleButton { IsChecked: true } t)
                return t.Bounds.Translate(new Vector(Inset, Inset));
        }
        return default;
    }

    private void MoveThumb(bool animate)
    {
        var target = TargetRect();
        if (target == _to && _hasCurrent)
            return;
        _from = _hasCurrent ? _current : target;
        _to = target;
        if (!animate || !_hasCurrent || MotionPrefs.Reduced || _from.Width <= 0)
        {
            _current = target;
            _hasCurrent = target.Width > 0;
            _animStart = -1;
            InvalidateVisual();
            return;
        }
        _animStart = AnimClock.Now;
        RequestFrame();
    }

    private void RequestFrame() => TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => Tick());

    private void Tick()
    {
        if (_animStart < 0)
            return;
        var t = (AnimClock.Now - _animStart) / 0.167;
        var e = Ease.Enter(t);
        _current = new Rect(
            Ease.Lerp(_from.X, _to.X, e), Ease.Lerp(_from.Y, _to.Y, e),
            Ease.Lerp(_from.Width, _to.Width, e), Ease.Lerp(_from.Height, _to.Height, e));
        InvalidateVisual();
        if (t >= 1)
        {
            _current = _to;
            _animStart = -1;
            return;
        }
        RequestFrame();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Background is { } bg)
            context.DrawRectangle(bg, null, new RoundedRect(bounds, 6));
        if (_hasCurrent && _current.Width > 0)
        {
            var pen = ThumbBorderBrush is { } b ? new Pen(b, 1) : null;
            context.DrawRectangle(ThumbBrush, pen, new RoundedRect(_current.Deflate(0.5), 4));
        }
    }
}

/// <summary>Columnas de igual ancho.</summary>
internal sealed class UniformRow : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var n = Math.Max(1, Children.Count);
        var w = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width / n;
        double maxW = 0, maxH = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(w, availableSize.Height));
            maxW = Math.Max(maxW, child.DesiredSize.Width);
            maxH = Math.Max(maxH, child.DesiredSize.Height);
        }
        return new Size(maxW * n, maxH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var n = Math.Max(1, Children.Count);
        var w = finalSize.Width / n;
        for (int i = 0; i < Children.Count; i++)
            Children[i].Arrange(new Rect(i * w, 0, w, finalSize.Height));
        return finalSize;
    }
}

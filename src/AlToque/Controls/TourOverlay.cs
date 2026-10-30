using AlToque.Chasqui;
using AlToque.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AlToque.Controls;

/// <summary>Un paso del tutorial: qué se ilumina (el primer control visible de la lista), qué se explica y la cara de Chasqui.</summary>
public sealed record TourStep(string[] Targets, string Title, string Body, ChasquiState Mood);

/// <summary>
/// Tutorial guiado sobre una ventana: oscurece todo menos lo que se explica, el foco viaja de un paso a otro con la
/// curva Entrar y Chasqui lo cuenta desde un globo. Lo que está iluminado se puede usar; el resto queda bloqueado.
/// </summary>
public sealed class TourOverlay : Control
{
    private const double BubbleMaxWidth = 360;
    private const double Gap = 12;
    private readonly Border _bubble;
    private readonly ChasquiView _chasqui;
    private readonly TextBlock _title = new() { Classes = { "strong" }, FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _body = new() { Classes = { "body2" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _counter = new() { Classes = { "meta", "num" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _next = new() { Classes = { "accent" }, MinWidth = 110 };
    private readonly Button _skip = new() { Classes = { "ghost" }, Content = S.TourSkip, Margin = new Thickness(0, 0, 8, 0) };
    private IReadOnlyList<TourStep> _steps = [];
    private Func<string, Control?> _resolve = _ => null;
    private int _index;
    private Rect? _from, _to, _hole;
    private double _moveStart = -10;
    private bool _framePending;

    public TourOverlay()
    {
        IsVisible = false;
        _chasqui = new ChasquiView { Width = 60, Height = 60, ShowRadar = false, VerticalAlignment = VerticalAlignment.Top };
        _next.Click += (_, _) => Next();
        _skip.Click += (_, _) => Finish();
        var text = new StackPanel { Spacing = 4, Margin = new Thickness(12, 2, 0, 0), Children = { _title, _body } };
        Grid.SetColumn(text, 1);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { _chasqui, text } };
        Grid.SetColumn(_skip, 2);
        Grid.SetColumn(_next, 3);
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Children = { _counter, _skip, _next } };
        _bubble = new Border
        {
            Classes = { "panel", "floatcard" },
            Padding = new Thickness(16, 14),
            BorderThickness = new Thickness(1.5),
            Child = new StackPanel { Spacing = 14, Children = { head, actions } },
        };
        // Borde de acento: el globo se distingue del panel oscurecido también en tema oscuro.
        _bubble[!Border.BorderBrushProperty] = _bubble.GetResourceObservable("AccentBrush").ToBinding();
        VisualChildren.Add(_bubble);
        LogicalChildren.Add(_bubble);
    }

    public bool IsRunning { get; private set; }

    /// <summary>Terminó (completo o saltado).</summary>
    public event Action? Finished;

    public void Start(IReadOnlyList<TourStep> steps, Func<string, Control?> resolve)
    {
        _steps = steps;
        _resolve = resolve;
        _index = 0;
        _hole = _from = _to = null;
        IsRunning = true;
        IsVisible = true;
        ShowStep();
        _ = Fx.Enter(this, dy: 0, ms: 250);
        RequestFrame();
    }

    public void Next()
    {
        if (!IsRunning)
            return;
        if (_index >= _steps.Count - 1)
        {
            Finish();
            return;
        }
        _index++;
        ShowStep();
    }

    public void Finish()
    {
        if (!IsRunning)
            return;
        IsRunning = false;
        IsVisible = false;
        Finished?.Invoke();
    }

    private void ShowStep()
    {
        var step = _steps[_index];
        var last = _index == _steps.Count - 1;
        _title.Text = step.Title;
        _body.Text = step.Body;
        _counter.Text = S.TourStepOf(_index + 1, _steps.Count);
        _next.Content = last ? S.TourDone : S.TourNext;
        _skip.IsVisible = !last;
        _chasqui.State = step.Mood;
        Retarget(TargetRect());
        InvalidateMeasure();
        // El globo salta un poco en cada paso: se nota que cambió.
        _ = Fx.Pop(_bubble, from: 0.88, ms: 380);
        _next.Focus();
    }

    private Rect? TargetRect()
    {
        if (_index >= _steps.Count)
            return null;
        foreach (var name in _steps[_index].Targets)
        {
            if (_resolve(name) is not { IsEffectivelyVisible: true } c || c.Bounds.Width < 1)
                continue;
            if (c.TranslatePoint(default, this) is { } p)
                return new Rect(p, c.Bounds.Size).Inflate(6);
        }
        return null;
    }

    private void Retarget(Rect? target)
    {
        _from = _hole ?? (target is { } t ? t.Inflate(40) : null);
        _to = target;
        _moveStart = AnimClock.Now;
    }

    private void RequestFrame()
    {
        if (_framePending || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _framePending = true;
        top.RequestAnimationFrame(_ =>
        {
            _framePending = false;
            if (!IsRunning)
                return;
            // Si el contenido se movió (llegó un compañero, cambió el panel), el foco lo sigue.
            var target = TargetRect();
            if (!Same(target, _to))
                Retarget(target);
            var t = MotionPrefs.Reduced ? 1 : Ease.Enter((AnimClock.Now - _moveStart) / 0.35);
            _hole = _to is not { } to ? null
                : _from is not { } from ? to
                : new Rect(Ease.Lerp(from.X, to.X, t), Ease.Lerp(from.Y, to.Y, t), Ease.Lerp(from.Width, to.Width, t), Ease.Lerp(from.Height, to.Height, t));
            InvalidateArrange();
            InvalidateVisual();
            RequestFrame();
        });
    }

    private static bool Same(Rect? a, Rect? b) =>
        a is null ? b is null : b is { } r && Math.Abs(a.Value.X - r.X) < 0.5 && Math.Abs(a.Value.Y - r.Y) < 0.5 &&
                                Math.Abs(a.Value.Width - r.Width) < 0.5 && Math.Abs(a.Value.Height - r.Height) < 0.5;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? BubbleMaxWidth : Math.Min(BubbleMaxWidth, availableSize.Width - Gap * 2);
        _bubble.Width = Math.Max(200, width);
        _bubble.Measure(new Size(_bubble.Width, double.PositiveInfinity));
        // No ocupa sitio propio: la ventana mide solo su contenido.
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = _bubble.DesiredSize;
        double x, y;
        if (_hole is not { } hole)
        {
            x = (finalSize.Width - size.Width) / 2;
            y = (finalSize.Height - size.Height) / 2;
        }
        else
        {
            x = Math.Clamp(hole.Center.X - size.Width / 2, Gap, Math.Max(Gap, finalSize.Width - size.Width - Gap));
            if (hole.Bottom + Gap + size.Height <= finalSize.Height - Gap)
                y = hole.Bottom + Gap;
            else if (hole.Top - Gap - size.Height >= Gap)
                y = hole.Top - Gap - size.Height;
            else
                y = finalSize.Height - size.Height - Gap;
        }
        _bubble.Arrange(new Rect(new Point(x, Math.Max(Gap, y)), size));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        var full = new Rect(Bounds.Size);
        var scrim = new ImmutableSolidColorBrush(Color.FromArgb(185, 4, 4, 10));
        if (_hole is not { } hole)
        {
            context.DrawRectangle(scrim, null, full);
            return;
        }
        var cut = new GeometryGroup { FillRule = FillRule.EvenOdd };
        cut.Children.Add(new RectangleGeometry(full));
        cut.Children.Add(new RectangleGeometry(hole, 10, 10));
        context.DrawGeometry(scrim, null, cut);
        // Anillo que late alrededor de lo que se explica.
        var accent = ColorUtil.Hex("#7C7CFF");
        var pulse = MotionPrefs.Reduced ? 0.5 : 0.5 + 0.5 * Math.Sin(AnimClock.Now * 4);
        context.DrawRectangle(null, new Pen(new ImmutableSolidColorBrush(ColorUtil.WithAlpha(accent, 0.25 * pulse)), 8),
            new RoundedRect(hole.Inflate(3), 12));
        context.DrawRectangle(null, new Pen(new ImmutableSolidColorBrush(accent), 2), new RoundedRect(hole, 10));
    }
}

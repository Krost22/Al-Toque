using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;

namespace AlToque.Chasqui;

/// <summary>
/// Chasqui, el avión de papel con ojos. Se dibuja con vectores y se anima por fotogramas solo mientras es visible.
/// Cada estado tiene su clip (sección 11.3); con "reducir movimiento" se usan poses fijas y fundidos.
/// Los cambios de estado son interrumpibles: el clip nuevo arranca de inmediato con un fundido corto.
/// </summary>
public sealed class ChasquiView : Control
{
    public static readonly StyledProperty<ChasquiState> StateProperty =
        AvaloniaProperty.Register<ChasquiView, ChasquiState>(nameof(State));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ChasquiView, double>(nameof(Progress));

    public static readonly StyledProperty<bool> ReduceMotionProperty =
        AvaloniaProperty.Register<ChasquiView, bool>(nameof(ReduceMotion));

    private const double CrossfadeSeconds = 0.12;

    private static readonly Palette Normal = new("#8484FF", "#5B5BF0", "#3B3BBD");
    private static readonly Palette Danger = new("#F2898C", "#E5484D", "#B5353A");
    private static readonly Palette Muted = new("#C2C2D2", "#A0A0B6", "#7E7E95");
    private static readonly IBrush White = new ImmutableSolidColorBrush(Colors.White);
    private static readonly IBrush Pupil = new ImmutableSolidColorBrush(Color.Parse("#1E1E3C"));
    private static readonly IBrush SuccessBrush = new ImmutableSolidColorBrush(Color.Parse("#1F9D55"));
    private static readonly IBrush AccentBrush = new ImmutableSolidColorBrush(Color.Parse("#5B5BF0"));
    private static readonly Color[] ConfettiColors =
        [Color.Parse("#5B5BF0"), Color.Parse("#F5A524"), Color.Parse("#1F9D55"), Color.Parse("#E5484D"), Color.Parse("#3B82F6")];

    // Polígonos del avión en un espacio de 100 × 100, con el pivote en (50, 50).
    private static readonly Geometry UpperWing = Polygon((86, 20), (13, 46), (46, 55));
    private static readonly Geometry LowerWing = Polygon((86, 20), (46, 55), (44, 84));
    private static readonly Geometry InnerFold = Polygon((46, 55), (44, 84), (55, 62));
    private static readonly Point LeftEye = new(46, 41);
    private static readonly Point RightEye = new(56, 37.5);

    private readonly Particle[] _confetti = new Particle[10];
    private long _stateStart = Stopwatch.GetTimestamp();
    private bool _attached;
    private bool _frameRequested;
    private double _shownProgress;
    private Point? _pointer;
    private long _pointerSeen;
    private Vector _look;
    private double _nextBlink;
    private TopLevel? _topLevel;

    static ChasquiView()
    {
        AffectsRender<ChasquiView>(StateProperty, ProgressProperty, ReduceMotionProperty);
        FocusableProperty.OverrideDefaultValue<ChasquiView>(false);
    }

    public ChasquiView()
    {
        AutomationProperties.SetName(this, ChasquiStates.Describe(ChasquiState.Idle));
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    public ChasquiState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool ReduceMotion
    {
        get => GetValue(ReduceMotionProperty);
        set => SetValue(ReduceMotionProperty, value);
    }

    private readonly record struct Palette(IBrush Upper, IBrush Lower, IBrush Inner)
    {
        public Palette(string upper, string lower, string inner)
            : this(new ImmutableSolidColorBrush(Color.Parse(upper)), new ImmutableSolidColorBrush(Color.Parse(lower)), new ImmutableSolidColorBrush(Color.Parse(inner)))
        {
        }
    }

    private struct Particle
    {
        public double X, Y, Vx, Vy, Spin;
        public Color Color;
    }

    private struct Pose
    {
        public double Dx, Dy, Rotation, Scale, ScaleX, EyeScaleY, Opacity;
        public bool Flip, CrossEyes;
        public Palette Palette;

        public static Pose Default => new() { Scale = 1, ScaleX = 1, EyeScaleY = 1, Opacity = 1, Palette = Normal };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            _stateStart = Stopwatch.GetTimestamp();
            if (State is ChasquiState.Success or ChasquiState.Received)
                SeedConfetti();
            if (State is not (ChasquiState.Sending or ChasquiState.Receiving))
                _shownProgress = 0;
            AutomationProperties.SetName(this, ChasquiStates.Describe(State));
            RequestFrame();
        }
        else if (change.Property == ProgressProperty || change.Property == ReduceMotionProperty)
        {
            RequestFrame();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.AddHandler(PointerMovedEvent, OnPointerMovedAnywhere, RoutingStrategies.Tunnel, handledEventsToo: true);
            _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
        }
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(PointerMovedEvent, OnPointerMovedAnywhere);
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
            _topLevel = null;
        }
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == Window.WindowStateProperty)
            RequestFrame();
    }

    private void OnPointerMovedAnywhere(object? sender, PointerEventArgs e)
    {
        _pointer = e.GetPosition(this);
        _pointerSeen = Stopwatch.GetTimestamp();
        if (!ReduceMotion)
            RequestFrame();
    }

    private double Seconds(long since) => Stopwatch.GetElapsedTime(since).TotalSeconds;

    private static double GlobalSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>Se anima solo si hay algo que mover, la ventana es visible y no está minimizada.</summary>
    private bool NeedsFrames
    {
        get
        {
            if (!_attached || _topLevel is null || !_topLevel.IsVisible || !IsEffectivelyVisible)
                return false;
            if (_topLevel is Window { WindowState: WindowState.Minimized })
                return false;
            var t = Seconds(_stateStart);
            if (t < CrossfadeSeconds + 0.05)
                return true;
            if (ReduceMotion)
                return State == ChasquiState.Searching || Math.Abs(_shownProgress - Progress) > 0.001;
            if (ChasquiStates.IsTransient(State))
                return t < ChasquiStates.ClipDuration(State).TotalSeconds + 0.1 || State == ChasquiState.Incoming;
            return true;
        }
    }

    private void RequestFrame()
    {
        if (_frameRequested || !_attached || _topLevel is null)
            return;
        _frameRequested = true;
        _topLevel.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            InvalidateVisual();
            if (NeedsFrames)
                RequestFrame();
        });
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 1)
            return;
        var t = Seconds(_stateStart);
        var g = GlobalSeconds;
        var rm = ReduceMotion;

        // El progreso se acerca suavemente al real: la estela no da saltos.
        _shownProgress += (Math.Clamp(Progress, 0, 1) - _shownProgress) * (rm ? 1 : 0.18);

        var offset = new Point((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using var _ = ctx.PushTransform(Matrix.CreateScale(size / 100, size / 100) * Matrix.CreateTranslation(offset.X, offset.Y));
        var fade = t < CrossfadeSeconds ? 0.35 + 0.65 * (t / CrossfadeSeconds) : 1;
        using var __ = ctx.PushOpacity(fade);

        var pose = Pose.Default;
        UpdateLook(rm);
        switch (State)
        {
            case ChasquiState.Idle:
                Idle(ref pose, g, rm);
                break;
            case ChasquiState.Attentive:
                pose.Rotation = rm ? 0 : -8 + _look.X * 4;
                pose.Scale = rm ? 1 : 1.06;
                break;
            case ChasquiState.Preparing:
                Idle(ref pose, g, true);
                pose.ScaleX = rm ? 1 : 0.86 + 0.14 * (0.5 + 0.5 * Math.Cos(g * Math.Tau / 0.6));
                break;
            case ChasquiState.Searching:
                DrawRadar(ctx, g, rm);
                Idle(ref pose, g, rm);
                pose.Scale = 0.78;
                break;
            case ChasquiState.Waiting:
                pose.Rotation = 6 + (rm ? 0 : Tap(g));
                pose.Scale = 0.86;
                pose.Dy = -4;
                DrawDots(ctx, g, rm, 50, 88);
                _look = new Vector(1, 0);
                break;
            case ChasquiState.Sending:
            case ChasquiState.Receiving:
                Flying(ctx, ref pose, g, rm, State == ChasquiState.Receiving);
                break;
            case ChasquiState.Reconnecting:
                if (!rm)
                {
                    var a = g * Math.Tau / 1.8;
                    pose.Dx = 13 * Math.Cos(a);
                    pose.Dy = 6 + 7 * Math.Sin(a);
                    pose.Rotation = Math.Cos(a) * 14;
                }
                pose.Scale = 0.6;
                DrawCloud(ctx, g, rm);
                break;
            case ChasquiState.Success:
                Success(ctx, ref pose, t, rm);
                break;
            case ChasquiState.Received:
                Received(ctx, ref pose, t, rm);
                return;
            case ChasquiState.Rejected:
                pose.Palette = Muted;
                pose.Flip = true;
                pose.Scale = 0.8;
                if (!rm)
                {
                    pose.Dx = -10 * EaseOut(Math.Min(1, t / 0.5));
                    pose.Dy = -3 * Bump(t, 0.55) - 3 * Bump(t, 0.75);
                }
                else
                    pose.Dx = -10;
                break;
            case ChasquiState.Error:
                pose.Palette = Danger;
                pose.CrossEyes = true;
                var k = rm ? 1 : EaseOut(Math.Min(1, t / 0.6));
                pose.Scale = 1 - 0.22 * k;
                pose.Rotation = 18 * k;
                pose.Dy = 8 * k;
                if (!rm && t < 0.3)
                    pose.Dx = Math.Sin(t * Math.Tau * 10) * 4 * (1 - t / 0.3);
                pose.ScaleX = 1 - 0.12 * k;
                break;
            case ChasquiState.Incoming:
                pose.Flip = true;
                pose.Scale = 0.86;
                if (!rm)
                {
                    pose.Dx = 60 * (1 - EaseOutBack(Math.Min(1, t / 0.7)));
                    if (t > 0.7)
                        Idle(ref pose, g, false);
                }
                _look = new Vector(-1, 0);
                break;
        }

        DrawPlane(ctx, pose, g);
    }

    private void Idle(ref Pose pose, double g, bool still)
    {
        if (!still)
        {
            pose.Dy = Math.Sin(g * Math.Tau / 3.2) * 2.2;
            pose.Rotation = Math.Sin(g * Math.Tau / 3.2 + 1) * 2.5;
        }
        // Parpadeo aleatorio cada 4 a 8 s.
        if (_nextBlink <= 0 || g > _nextBlink + 0.13)
            _nextBlink = g + 4 + Random.Shared.NextDouble() * 4;
        else if (g >= _nextBlink)
            pose.EyeScaleY = 0.15;
    }

    private void Flying(DrawingContext ctx, ref Pose pose, double g, bool rm, bool receiving)
    {
        var p = _shownProgress;
        pose.Scale = 0.62;
        pose.Flip = receiving;
        var trailY = 70.0;
        if (rm)
        {
            DrawTrail(ctx, receiving ? 90 : 10, receiving ? 90 - 80 * p : 10 + 80 * p, 88, solid: true);
        }
        else
        {
            var centerX = receiving ? 70 - 42 * p : 30 + 42 * p;
            pose.Dx = centerX - 50;
            pose.Dy = -6 + Math.Sin(g * Math.Tau / 0.9) * 1.6;
            pose.Rotation = (receiving ? 4 : -4) + Math.Sin(g * Math.Tau / 0.9 + 0.6) * 2;
            var tailX = receiving ? centerX + 20 : centerX - 20;
            trailY = 58 + pose.Dy;
            DrawTrail(ctx, receiving ? 96 : 4, tailX, trailY, solid: false);
            // Líneas de velocidad.
            var pen = new Pen(AccentBrush, 1.2, lineCap: PenLineCap.Round);
            using (ctx.PushOpacity(0.35))
            {
                for (int i = 0; i < 3; i++)
                {
                    var phase = (g * 1.6 + i / 3.0) % 1;
                    var x = receiving ? centerX + 22 + phase * 14 : centerX - 22 - phase * 14;
                    var y = 44 + i * 7 + pose.Dy;
                    ctx.DrawLine(pen, new Point(x, y), new Point(receiving ? x + 6 : x - 6, y));
                }
            }
        }
        if (receiving)
            DrawDocument(ctx, new Point(50 + pose.Dx, 66 + pose.Dy), 0.55);
    }

    private void Success(DrawingContext ctx, ref Pose pose, double t, bool rm)
    {
        var land = rm ? 1 : EaseOut(Math.Min(1, t / 0.4));
        pose.Dy = -6 + 10 * land;
        pose.Scale = 0.8 - 0.1 * land;
        pose.Dx = -6;
        if (!rm)
            DrawConfetti(ctx, t);
        var checkT = rm ? 1 : Math.Clamp((t - 0.3) / 0.5, 0, 1);
        DrawCheck(ctx, new Point(74, 72), 12, checkT);
    }

    private void Received(DrawingContext ctx, ref Pose pose, double t, bool rm)
    {
        var grow = rm ? 1 : EaseOutBack(Math.Min(1, t / 0.6));
        DrawDocument(ctx, new Point(48, 50), 1.25 * grow);
        if (!rm)
            DrawConfetti(ctx, t);
        var checkT = rm ? 1 : Math.Clamp((t - 0.5) / 0.5, 0, 1);
        DrawCheck(ctx, new Point(70, 70), 12, checkT);
    }

    private void UpdateLook(bool rm)
    {
        var target = new Vector(0.6, -0.2);
        if (!rm && _pointer is { } p && Stopwatch.GetElapsedTime(_pointerSeen).TotalSeconds < 4)
        {
            var size = Math.Min(Bounds.Width, Bounds.Height);
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2 - size * 0.1);
            var v = p - center;
            var len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            target = len < 1 ? default : new Vector(v.X / Math.Max(len, size * 0.6), v.Y / Math.Max(len, size * 0.6));
        }
        // Resorte simple: los ojos siguen al puntero con algo de inercia.
        _look += (target - _look) * (rm ? 1 : 0.2);
    }

    private void DrawPlane(DrawingContext ctx, Pose pose, double g)
    {
        var m = Matrix.CreateTranslation(-50, -50)
                * Matrix.CreateScale((pose.Flip ? -1 : 1) * pose.Scale * pose.ScaleX, pose.Scale)
                * Matrix.CreateRotation((pose.Flip ? -pose.Rotation : pose.Rotation) * Math.PI / 180)
                * Matrix.CreateTranslation(50 + pose.Dx, 50 + pose.Dy);
        using var _ = ctx.PushTransform(m);
        using var __ = ctx.PushOpacity(pose.Opacity);
        ctx.DrawGeometry(pose.Palette.Upper, null, UpperWing);
        ctx.DrawGeometry(pose.Palette.Lower, null, LowerWing);
        ctx.DrawGeometry(pose.Palette.Inner, null, InnerFold);

        var look = pose.Flip ? new Vector(-_look.X, _look.Y) : _look;
        foreach (var eye in new[] { LeftEye, RightEye })
        {
            if (pose.CrossEyes)
            {
                var pen = new Pen(Pupil, 1.4, lineCap: PenLineCap.Round);
                ctx.DrawEllipse(White, null, eye, 4.4, 4.4);
                ctx.DrawLine(pen, eye + new Vector(-2, -2), eye + new Vector(2, 2));
                ctx.DrawLine(pen, eye + new Vector(-2, 2), eye + new Vector(2, -2));
                continue;
            }
            var ry = 4.6 * pose.EyeScaleY;
            ctx.DrawEllipse(White, new Pen(Pupil, 0.9), eye, 4.6, ry);
            if (pose.EyeScaleY > 0.5)
                ctx.DrawEllipse(Pupil, null, eye + new Vector(look.X * 1.7, look.Y * 1.7), 2.2, 2.2 * pose.EyeScaleY);
        }
    }

    private static void DrawRadar(DrawingContext ctx, double g, bool rm)
    {
        if (rm)
        {
            // Puntos que se iluminan en secuencia, sin movimiento.
            var lit = (int)(g * 2.5) % 3;
            for (int i = 0; i < 3; i++)
                ctx.DrawEllipse(AccentBrush, null, new Point(38 + i * 12, 92), 2.6, 2.6);
            using (ctx.PushOpacity(0.5))
                ctx.DrawEllipse(White, null, new Point(38 + lit * 12, 92), 1.6, 1.6);
            return;
        }
        for (int k = 0; k < 3; k++)
        {
            var phase = (g / 1.2 + k / 3.0) % 1;
            using (ctx.PushOpacity((1 - phase) * 0.45))
                ctx.DrawEllipse(null, new Pen(AccentBrush, 1.6), new Point(50, 52), 10 + phase * 38, 10 + phase * 38);
        }
    }

    private static void DrawDots(DrawingContext ctx, double g, bool rm, double cx, double cy)
    {
        for (int i = 0; i < 3; i++)
        {
            var o = rm ? 0.7 : 0.25 + 0.75 * Math.Max(0, Math.Sin((g * 2.2 - i * 0.33) * Math.PI));
            using (ctx.PushOpacity(o))
                ctx.DrawEllipse(AccentBrush, null, new Point(cx - 8 + i * 8, cy), 2.4, 2.4);
        }
    }

    private static void DrawCloud(DrawingContext ctx, double g, bool rm)
    {
        var cloud = new RoundedRect(new Rect(36, 6, 28, 13), 6.5);
        ctx.DrawRectangle(new ImmutableSolidColorBrush(Color.Parse("#E4E4F4")), null, cloud);
        DrawDots(ctx, g, rm, 50, 12.5);
    }

    private static void DrawTrail(DrawingContext ctx, double fromX, double toX, double y, bool solid)
    {
        if (Math.Abs(toX - fromX) < 0.5)
            return;
        var pen = solid
            ? new Pen(AccentBrush, 3, lineCap: PenLineCap.Round)
            : new Pen(AccentBrush, 2.4, new ImmutableDashStyle([2.2, 1.6], 0), PenLineCap.Round);
        using (ctx.PushOpacity(solid ? 1 : 0.65))
            ctx.DrawLine(pen, new Point(fromX, y), new Point(toX, y));
    }

    private static void DrawDocument(DrawingContext ctx, Point center, double scale)
    {
        if (scale <= 0.01)
            return;
        using var _ = ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(center.X, center.Y));
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(new Point(-10, -13), true);
            g.LineTo(new Point(4, -13));
            g.LineTo(new Point(10, -7));
            g.LineTo(new Point(10, 13));
            g.LineTo(new Point(-10, 13));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(White, new Pen(AccentBrush, 1.6, lineJoin: PenLineJoin.Round), body);
        var linePen = new Pen(new ImmutableSolidColorBrush(Color.Parse("#B8B8F8")), 1.6, lineCap: PenLineCap.Round);
        for (int i = 0; i < 3; i++)
            ctx.DrawLine(linePen, new Point(-6, -2 + i * 5), new Point(6, -2 + i * 5));
    }

    private static void DrawCheck(DrawingContext ctx, Point center, double radius, double t)
    {
        if (t <= 0)
            return;
        using (ctx.PushOpacity(Math.Min(1, t * 2)))
            ctx.DrawEllipse(SuccessBrush, null, center, radius * (0.7 + 0.3 * Math.Min(1, t * 2)), radius * (0.7 + 0.3 * Math.Min(1, t * 2)));
        // El ✓ se dibuja como un trazo que avanza.
        var a = center + new Vector(-5.5, 0.5);
        var b = center + new Vector(-1.5, 4.5);
        var c = center + new Vector(6, -4.5);
        var pen = new Pen(White, 2.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var first = Math.Clamp(t / 0.4, 0, 1);
        ctx.DrawLine(pen, a, a + (b - a) * first);
        if (t > 0.4)
        {
            var second = Math.Clamp((t - 0.4) / 0.6, 0, 1);
            ctx.DrawLine(pen, b, b + (c - b) * second);
        }
    }

    private void SeedConfetti()
    {
        for (int i = 0; i < _confetti.Length; i++)
        {
            var angle = (-160 + Random.Shared.NextDouble() * 140) * Math.PI / 180;
            var speed = 38 + Random.Shared.NextDouble() * 26;
            _confetti[i] = new Particle
            {
                X = 50,
                Y = 46,
                Vx = Math.Cos(angle) * speed,
                Vy = Math.Sin(angle) * speed,
                Spin = Random.Shared.NextDouble() * 720 - 360,
                Color = ConfettiColors[i % ConfettiColors.Length],
            };
        }
    }

    private void DrawConfetti(DrawingContext ctx, double t)
    {
        if (t > 1.2)
            return;
        foreach (var p in _confetti)
        {
            var x = p.X + p.Vx * t;
            var y = p.Y + p.Vy * t + 0.5 * 90 * t * t;
            var m = Matrix.CreateRotation(p.Spin * t * Math.PI / 180) * Matrix.CreateTranslation(x, y);
            using (ctx.PushOpacity(1 - t / 1.2))
            using (ctx.PushTransform(m))
                ctx.FillRectangle(new ImmutableSolidColorBrush(p.Color), new Rect(-1.8, -1, 3.6, 2));
        }
    }

    private static double Tap(double g)
    {
        var phase = g % 0.9;
        return phase < 0.15 ? Math.Sin(phase / 0.15 * Math.PI) * 5 : 0;
    }

    private static double Bump(double t, double at) => t > at && t < at + 0.15 ? Math.Sin((t - at) / 0.15 * Math.PI) : 0;

    /// <summary>"Rápido al salir, lento al llegar", como cubic-bezier(0, 0, 0, 1).</summary>
    private static double EaseOut(double x) => 1 - Math.Pow(1 - x, 3);

    private static double EaseOutBack(double x)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
    }

    private static Geometry Polygon(params (double X, double Y)[] points)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(points[0].X, points[0].Y), true);
        foreach (var (x, y) in points.Skip(1))
            ctx.LineTo(new Point(x, y));
        ctx.EndFigure(true);
        return geometry;
    }
}

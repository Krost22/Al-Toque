using AlToque.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AlToque.Chasqui;

/// <summary>
/// Chasqui, la mascota: un avión de papel con ojos que reacciona a todo. Tiene anticipación, aplastar y estirar,
/// caras para cada momento, pequeñas acciones cuando está aburrido y partículas en los logros. Con "reducir
/// movimiento" usa poses fijas y fundidos.
/// </summary>
public sealed class ChasquiView : Control
{
    public static readonly StyledProperty<ChasquiState> StateProperty =
        AvaloniaProperty.Register<ChasquiView, ChasquiState>(nameof(State));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ChasquiView, double>(nameof(Progress));

    public static readonly StyledProperty<bool> ReduceMotionProperty =
        AvaloniaProperty.Register<ChasquiView, bool>(nameof(ReduceMotion));

    public static readonly StyledProperty<bool> ShowRadarProperty =
        AvaloniaProperty.Register<ChasquiView, bool>(nameof(ShowRadar), true);

    private const double Crossfade = 0.12;
    private static readonly Random Rng = new();

    private ChasquiState _prev;
    private double _start = -10, _prevStart = -10;
    private readonly List<Burst> _bursts = [];
    private int _seed = Rng.Next();
    private double _blinkStart = -10, _nextBlink;
    private double _actionStart = -10, _nextAction;
    private int _action;
    private Point? _pointer;
    private double _pointerSeen = -100;
    private double _lastActivity;
    private bool _frameRequested;
    private bool _skipFrame;
    private TopLevel? _top;
    private Avalonia.Threading.DispatcherTimer? _wake;

    static ChasquiView()
    {
        AffectsRender<ChasquiView>(StateProperty, ProgressProperty, ReduceMotionProperty, ShowRadarProperty);
        FocusableProperty.OverrideDefaultValue<ChasquiView>(false);
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

    /// <summary>Anillos de radar detrás de Chasqui mientras busca.</summary>
    public bool ShowRadar
    {
        get => GetValue(ShowRadarProperty);
        set => SetValue(ShowRadarProperty, value);
    }

    private bool Reduced => ReduceMotion || MotionPrefs.Reduced;

    /// <summary>Reacción rápida a algo que hizo el usuario (por ejemplo, soltar archivos): salta y celebra.</summary>
    public void Cheer()
    {
        var now = AnimClock.Now;
        _action = 4;
        _actionStart = now;
        Emit(BurstKind.Sparkles, now, new Point(50, 40), 7, 1);
        RequestFrame();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            var now = AnimClock.Now;
            _prev = change.GetOldValue<ChasquiState>();
            _prevStart = _start;
            _start = now;
            _lastActivity = now;
            _seed = Rng.Next();
            OnEnter(State, now);
            AutomationProperties.SetName(this, ChasquiStates.Describe(State));
            RequestFrame();
        }
        else if (change.Property == ReduceMotionProperty)
        {
            RequestFrame();
        }
    }

    private void OnEnter(ChasquiState s, double now)
    {
        if (Reduced)
            return;
        switch (s)
        {
            case ChasquiState.Success:
                Emit(BurstKind.Confetti, now + 0.85, new Point(50, 44), 16, 1.1);
                Emit(BurstKind.Stars, now + 0.85, new Point(50, 46), 6, 1);
                break;
            case ChasquiState.Received:
                Emit(BurstKind.Stars, now + 0.12, new Point(50, 46), 6, 1);
                Emit(BurstKind.Confetti, now + 0.55, new Point(50, 48), 10, 0.9);
                break;
            case ChasquiState.Rejected:
                Emit(BurstKind.Puff, now + 0.35, new Point(80, 28), 4, 0.8);
                break;
            case ChasquiState.Error:
                Emit(BurstKind.Puff, now + 0.05, new Point(22, 48), 5, 1);
                Emit(BurstKind.Sweat, now + 0.4, new Point(62, 30), 2, 1);
                break;
            case ChasquiState.Sending:
                Emit(BurstKind.Puff, now + 0.22, new Point(18, 52), 6, 1);
                break;
            case ChasquiState.Attentive or ChasquiState.Incoming:
                Emit(BurstKind.Sparkles, now, new Point(50, 42), 5, 0.8);
                break;
        }
    }

    private void Emit(BurstKind kind, double at, Point origin, int count, double size)
    {
        _bursts.RemoveAll(b => AnimClock.Now - b.Start > Particles.Lifetime(b.Kind));
        if (_bursts.Count < 12)
            _bursts.Add(new Burst(kind, at, origin, count, _seed + _bursts.Count * 31, size));
    }

    // ===== Ciclo de vida y fotogramas =====

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        var now = AnimClock.Now;
        _nextBlink = now + 1.5;
        _nextAction = now + 5 + Rng.NextDouble() * 5;
        _lastActivity = now;
        if (_top is not null)
        {
            _top.AddHandler(PointerMovedEvent, OnPointerMovedAnywhere, RoutingStrategies.Tunnel, handledEventsToo: true);
            _top.PropertyChanged += OnTopLevelPropertyChanged;
        }
        AutomationProperties.SetName(this, ChasquiStates.Describe(State));
        // Despertador: en reposo no se dibuja nada; cada medio segundo se decide si toca parpadear o hacer algo.
        _wake = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(500), Avalonia.Threading.DispatcherPriority.Background, (_, _) =>
        {
            if (!CanAnimate || Reduced)
                return;
            if (Schedule(AnimClock.Now))
                RequestFrame();
        });
        _wake.Start();
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _wake?.Stop();
        _wake = null;
        if (_top is not null)
        {
            _top.RemoveHandler(PointerMovedEvent, OnPointerMovedAnywhere);
            _top.PropertyChanged -= OnTopLevelPropertyChanged;
            _top = null;
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
        var now = AnimClock.Now;
        _pointerSeen = now;
        _lastActivity = now;
        RequestFrame();
    }

    private bool CanAnimate =>
        _top is not null && _top.IsVisible && IsEffectivelyVisible && _top is not Window { WindowState: WindowState.Minimized } &&
        AnimClock.Frozen is null;

    private void RequestFrame()
    {
        if (_frameRequested || _top is null)
        {
            if (_top is null)
                InvalidateVisual();
            return;
        }
        _frameRequested = true;
        _top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            // Los bucles tranquilos (reposo, buscar, esperar) van a 30 fps; las acciones, a 60.
            _skipFrame = !_skipFrame;
            if (!_skipFrame || !IsCalmLoop(AnimClock.Now))
                InvalidateVisual();
            if (Busy(AnimClock.Now))
                RequestFrame();
        });
    }

    // ===== Dibujo =====

    public override void Render(DrawingContext ctx)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 2)
            return;
        var now = AnimClock.Now;
        var k = size / 100;
        using var box = ctx.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2));

        var state = State;
        var t = now - _start;
        var pose = PoseFor(state, t, now);
        if (t < Crossfade && _prevStart > 0)
        {
            var prev = PoseFor(_prev, now - _prevStart, now);
            pose = ChasquiPose.Lerp(prev, pose, Ease.Enter(t / Crossfade));
        }

        DrawBehind(ctx, state, t, now);
        ChasquiPainter.Draw(ctx, pose, shadow: size >= 32);
        DrawFront(ctx, state, t, now, pose);
        if (!Reduced)
        {
            foreach (var b in _bursts)
                Particles.Draw(ctx, b, now);
        }
    }

    /// <summary>Parpadeos y acciones espontáneas cuando está en reposo. Devuelve si empezó algo.</summary>
    private bool Schedule(double now)
    {
        if (Reduced || AnimClock.Frozen is not null)
            return false;
        var started = false;
        if (now >= _nextBlink)
        {
            _blinkStart = now;
            _nextBlink = now + (Rng.NextDouble() < 0.25 ? 0.3 : 2 + Rng.NextDouble() * 4);
            started = true;
        }
        if (State == ChasquiState.Idle && now >= _nextAction && !IsSleepy(now))
        {
            _action = Rng.Next(5);
            _actionStart = now;
            _nextAction = now + 7 + Rng.NextDouble() * 7;
            if (_action == 4)
                Emit(BurstKind.Sparkles, now + 0.3, new Point(58, 30), 5, 0.7);
            started = true;
        }
        return started;
    }

    /// <summary>¿Hace falta seguir dibujando? En reposo, tras unos segundos sin actividad, Chasqui se queda quieto.</summary>
    private bool Busy(double now)
    {
        if (!CanAnimate)
            return false;
        var t = now - _start;
        if (Reduced)
            return t < Crossfade + 0.05;
        if (t < 2 || now - _blinkStart < 0.2 || now - _actionStart < 2)
            return true;
        if (_bursts.Exists(b => now - b.Start < Particles.Lifetime(b.Kind)))
            return true;
        return State switch
        {
            ChasquiState.Idle => Liveliness(now) > 0,
            ChasquiState.Success or ChasquiState.Received or ChasquiState.Rejected or ChasquiState.Error => false,
            _ => true,
        };
    }

    /// <summary>1 con actividad reciente; baja a 0 entre los 6 y 8 s de calma (el balanceo se apaga sin saltos).</summary>
    private double Liveliness(double now) => 1 - Ease.Seg(now - _lastActivity, 6, 2);

    private bool IsCalmLoop(double now) =>
        State is ChasquiState.Idle or ChasquiState.Searching or ChasquiState.Waiting or ChasquiState.Reconnecting &&
        now - _start > Crossfade && now - _actionStart > 1.8 && now - _blinkStart > 0.2;

    private bool IsSleepy(double now) => State == ChasquiState.Idle && now - _lastActivity > 45 && now - _start > 45;

    private double Blink(double now)
    {
        var t = now - _blinkStart;
        return t is >= 0 and < 0.15 ? Math.Sin(t / 0.15 * Math.PI) : 0;
    }

    /// <summary>Hacia dónde mira: al puntero si se movió hace poco.</summary>
    private Vector PointerLook(double now)
    {
        if (_pointer is not { } p || now - _pointerSeen > 4)
            return default;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2 - size * 0.1);
        var d = p - center;
        var len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 1)
            return default;
        var reach = Math.Min(1, len / (size * 1.5));
        return new Vector(d.X / len * reach, d.Y / len * reach);
    }

    private ChasquiPose PoseFor(ChasquiState s, double t, double g)
    {
        var p = ChasquiPose.Rest;
        var rm = Reduced;
        p.Blink = Blink(g);
        p.Look = PointerLook(g);
        switch (s)
        {
            case ChasquiState.Idle:
            {
                var sleepy = IsSleepy(g);
                if (!rm)
                {
                    var live = Liveliness(g);
                    p.Y = Math.Sin(g * 1.7) * 3 * live;
                    p.Rotation = Math.Sin(g * 0.9) * 3 * live;
                    p.Flap = Math.Sin(g * 2.4) * 0.35 * live;
                }
                if (sleepy)
                {
                    p.Eyes = ChasquiEyes.Sleepy;
                    p.Mouth = ChasquiMouth.O;
                    p.Look = default;
                }
                else if (!rm)
                    IdleAction(ref p, g);
                break;
            }
            case ChasquiState.Attentive:
            {
                p.Scale = 1 + 0.14 * (rm ? 1 : Ease.Bounce(Ease.Seg(t, 0, 0.4)));
                if (!rm)
                {
                    p.Y = -Math.Abs(Math.Sin(g * 8)) * 4;
                    p.Rotation = Math.Sin(g * 8) * 5;
                    p.Flap = Math.Sin(g * 14) * 0.8;
                    var bounce = Math.Abs(Math.Cos(g * 8));
                    p.Sy = 1 - 0.05 * bounce;
                    p.Sx = 1 + 0.04 * bounce;
                }
                p.Eyes = ChasquiEyes.Wide;
                p.Mouth = ChasquiMouth.Open;
                break;
            }
            case ChasquiState.Preparing:
                p.Sx = 1.08;
                p.Sy = 0.9;
                p.X = -4 + (rm ? 0 : Math.Sin(g * 50) * 0.7);
                p.Eyes = ChasquiEyes.Focused;
                p.Mouth = ChasquiMouth.Flat;
                break;
            case ChasquiState.Searching:
                if (!rm)
                {
                    var a = g * 1.6;
                    p.X = Math.Cos(a) * 9;
                    p.Y = Math.Sin(a * 2) * 4;
                    p.Rotation = -Math.Sin(a) * 14;
                    p.Flap = Math.Sin(g * 6) * 0.6;
                    p.Look = new Vector(Math.Cos(g * 0.8), Math.Sin(g * 1.3) * 0.5);
                }
                break;
            case ChasquiState.Waiting:
            {
                var ph = g % 0.9;
                var tap = rm ? 0 : ph < 0.18 ? Math.Sin(ph / 0.18 * Math.PI) : 0;
                p.Rotation = 10 * tap;
                p.Y = 2.5 * tap;
                p.Sy = 1 - 0.07 * tap;
                p.Sx = 1 + 0.05 * tap;
                p.Flap = rm ? 0 : Math.Sin(g * 2.4) * 0.3;
                p.Look = new Vector(0.9, 0.3);
                break;
            }
            case ChasquiState.Sending:
            {
                if (!rm)
                {
                    // Despegue: se echa atrás (anticipación) y sale disparado estirándose.
                    var back = Ease.OutCubic(Ease.Seg(t, 0, 0.22));
                    var shoot = Ease.Seg(t, 0.22, 0.35);
                    var overshoot = t < 0.22 ? 0 : Math.Sin(shoot * Math.PI) * 6 * (1 - shoot);
                    p.X = -6 * back * (1 - Ease.Bounce(shoot)) + overshoot;
                    p.Sx = t < 0.22 ? 1 - 0.1 * back : 1 + 0.16 * Math.Sin(shoot * Math.PI);
                    p.Sy = t < 0.22 ? 1 + 0.08 * back : 1 - 0.1 * Math.Sin(shoot * Math.PI);
                    var cruise = Ease.Seg(t, 0.4, 0.5);
                    p.Y = Math.Sin(g * 3) * 3 * cruise;
                    p.Rotation = (-6 + Math.Sin(g * 3 + 1) * 4) * cruise;
                    p.Flap = Math.Sin(g * 12) * 0.9;
                }
                p.Eyes = ChasquiEyes.Focused;
                p.Look = new Vector(1, 0);
                break;
            }
            case ChasquiState.Receiving:
                p.Flip = true;
                if (!rm)
                {
                    p.Y = Math.Sin(g * 2.2) * 3;
                    p.Rotation = Math.Sin(g * 1.1) * 4;
                    p.Flap = Math.Sin(g * 5) * 0.5;
                }
                p.Mouth = ChasquiMouth.Smile;
                p.Look = new Vector(0.8, 0.2);
                break;
            case ChasquiState.Reconnecting:
                if (!rm)
                {
                    var a = g * 1.1;
                    p.X = Math.Cos(a) * 12;
                    p.Y = Math.Sin(a) * 7;
                    p.Rotation = -Math.Cos(a) * 18;
                    p.Flap = Math.Sin(g * 3) * 0.4;
                }
                p.Eyes = ChasquiEyes.Worried;
                p.Mouth = ChasquiMouth.Wobble;
                break;
            case ChasquiState.Success:
                if (rm)
                {
                    p.Eyes = ChasquiEyes.Happy;
                    p.Mouth = ChasquiMouth.Open;
                    break;
                }
                Loop(ref p, t, 0.2, 0.65, 14);
                Land(ref p, t, 0.85);
                p.Eyes = t < 0.2 ? ChasquiEyes.Wide : ChasquiEyes.Happy;
                p.Mouth = t < 0.2 ? ChasquiMouth.O : ChasquiMouth.Open;
                if (t > 1.4)
                {
                    p.Y += Math.Sin(g * 1.7) * 2;
                    p.Flap = Math.Sin(g * 2.4) * 0.3;
                }
                break;
            case ChasquiState.Received:
                if (!rm)
                {
                    Hop(ref p, t, 0, 0.55, 14);
                    Land(ref p, t, 0.55);
                }
                p.Eyes = ChasquiEyes.Happy;
                p.Mouth = ChasquiMouth.Open;
                break;
            case ChasquiState.Rejected:
            {
                var tilt = rm ? 1 : Ease.OutCubic(Ease.Seg(t, 0, 0.3));
                p.Rotation = -16 * tilt;
                p.Y = 4 * tilt;
                if (!rm)
                {
                    // Se encoge de hombros: baja, sube un poco y vuelve.
                    var shrug = Ease.Seg(t, 0.3, 0.45);
                    p.Sy = 1 + 0.1 * Math.Sin(shrug * Math.PI) - 0.04 * Math.Sin(shrug * Math.PI * 2);
                    p.Y -= 3 * Math.Sin(shrug * Math.PI);
                }
                p.Tint = tilt;
                p.Eyes = ChasquiEyes.Meh;
                p.Mouth = ChasquiMouth.Flat;
                break;
            }
            case ChasquiState.Error:
            {
                if (!rm)
                {
                    var decay = 1 - Ease.Seg(t, 0, 0.7);
                    p.X = Math.Sin(t * 42) * 5 * decay;
                    p.Rotation = Math.Sin(t * 42 + 1) * 6 * decay;
                    p.Sy = 1 - 0.05 * Math.Abs(Math.Sin(t * 42)) * decay;
                }
                p.Tint = -Math.Min(1, 0.6 + 0.4 * Ease.Seg(t, 0, 0.2));
                p.Eyes = t < 0.5 || rm ? ChasquiEyes.Cross : ChasquiEyes.Dizzy;
                p.Mouth = ChasquiMouth.Wobble;
                break;
            }
            case ChasquiState.Incoming:
                if (!rm)
                {
                    Hop(ref p, t, 0, 0.45, 12);
                    Land(ref p, t, 0.45);
                    p.Flap = Math.Sin(g * 7) * 0.5;
                }
                p.Eyes = ChasquiEyes.Wide;
                p.Mouth = ChasquiMouth.O;
                p.Look = new Vector(0, 0.6);
                break;
        }
        return p;
    }

    /// <summary>Una vuelta completa en el aire (looping).</summary>
    private static void Loop(ref ChasquiPose p, double t, double start, double length, double radius)
    {
        if (t < start)
        {
            var a = Ease.OutCubic(t / start);
            p.Sy = 1 - 0.14 * a;
            p.Sx = 1 + 0.1 * a;
            p.Y = 3 * a;
            return;
        }
        var u = Ease.Seg(t, start, length);
        if (u >= 1)
            return;
        var angle = Ease.InOutSine(u) * Math.PI * 2;
        p.X = Math.Sin(angle) * radius;
        p.Y = -(1 - Math.Cos(angle)) * radius * 0.9;
        p.Rotation = -angle * 180 / Math.PI;
        p.Sx = 1 + 0.12 * Math.Sin(u * Math.PI);
        p.Flap = Math.Sin(t * 30) * 0.9;
    }

    private static void Hop(ref ChasquiPose p, double t, double start, double length, double height)
    {
        var u = Ease.Seg(t, start, length);
        if (u <= 0 || u >= 1)
            return;
        p.Y -= height * 4 * u * (1 - u);
        var stretch = Math.Cos(u * Math.PI) * 0.1;
        p.Sy *= 1 + stretch;
        p.Sx *= 1 - stretch * 0.8;
    }

    /// <summary>Aterrizaje: se aplasta y rebota hasta quedarse quieto.</summary>
    private static void Land(ref ChasquiPose p, double t, double at)
    {
        var u = t - at;
        if (u < 0 || u > 0.6)
            return;
        var squash = Math.Exp(-u * 7) * Math.Cos(u * 22) * 0.18;
        p.Sy *= 1 - squash;
        p.Sx *= 1 + squash * 0.9;
    }

    private void IdleAction(ref ChasquiPose p, double g)
    {
        var t = g - _actionStart;
        switch (_action)
        {
            case 0 when t < 1.3:
                Loop(ref p, t, 0.15, 0.85, 12);
                p.Eyes = ChasquiEyes.Happy;
                p.Mouth = ChasquiMouth.Open;
                break;
            case 1 when t < 1.7:
            {
                // Bostezo con estiramiento.
                var a = Math.Sin(Ease.Seg(t, 0, 1.7) * Math.PI);
                p.Sy *= 1 + 0.12 * a;
                p.Sx *= 1 - 0.08 * a;
                p.Eyes = a > 0.3 ? ChasquiEyes.Sleepy : p.Eyes;
                p.Mouth = a > 0.3 ? ChasquiMouth.O : p.Mouth;
                break;
            }
            case 2 when t < 2.2:
                p.Look = new Vector(Math.Sin(t * 2.9) * 1, -0.2);
                break;
            case 3 when t < 0.9:
            {
                // Tonel: gira sobre su eje.
                var u = Ease.InOutSine(t / 0.9);
                p.Sx *= Math.Cos(u * Math.PI * 2);
                p.Y -= Math.Sin(u * Math.PI) * 6;
                break;
            }
            case 4 when t < 0.9:
                Hop(ref p, t, 0, 0.5, 9);
                Land(ref p, t, 0.5);
                p.Eyes = ChasquiEyes.Wink;
                p.Mouth = ChasquiMouth.Smile;
                break;
        }
    }

    private void DrawBehind(DrawingContext ctx, ChasquiState s, double t, double g)
    {
        if (s == ChasquiState.Searching && ShowRadar)
        {
            var ring = new ImmutableSolidColorBrush(Color.FromArgb(60, 0x8E, 0x8E, 0xA8));
            ctx.DrawEllipse(null, new Pen(ring, 0.8), new Point(50, 50), 30, 30);
            ctx.DrawEllipse(null, new Pen(ring, 0.8), new Point(50, 50), 46, 46);
            if (!Reduced)
            {
                for (int i = 0; i < 2; i++)
                {
                    var u = (g * 0.55 + i * 0.5) % 1;
                    var r = 18 + u * 34;
                    ctx.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Hex("#7C7CFF"), (1 - u) * 0.45)), 1.4),
                        new Point(50, 50), r, r);
                }
            }
        }
        if (s == ChasquiState.Sending && !Reduced && t > 0.3)
        {
            // Líneas de velocidad que corren hacia atrás.
            for (int i = 0; i < 3; i++)
            {
                var u = (g * 1.8 + i * 0.33) % 1;
                var y = 44 + i * 9 + Math.Sin(g * 3) * 3;
                var x = 26 - u * 26;
                var pen = new Pen(new ImmutableSolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Hex("#A3A3FF"), (1 - u) * 0.8)), 2.2, lineCap: PenLineCap.Round);
                ctx.DrawLine(pen, new Point(x, y), new Point(x + 10, y));
            }
        }
    }

    private void DrawFront(DrawingContext ctx, ChasquiState s, double t, double g, in ChasquiPose pose)
    {
        var rm = Reduced;
        var accent = ColorUtil.Hex("#A3A3FF");
        switch (s)
        {
            case ChasquiState.Attentive when t < 1.2:
            {
                var pop = rm ? 1 : Ease.Bounce(Ease.Seg(t, 0, 0.35));
                ChasquiPainter.Glyph(ctx, "!", new Point(78, 14 + pose.Y * 0.5), 22 * pop, ColorUtil.Hex("#F7C266"), 1 - Ease.Seg(t, 0.9, 0.3), 12);
                break;
            }
            case ChasquiState.Incoming:
            {
                var pop = rm ? 1 : Ease.Bounce(Ease.Seg(t, 0, 0.35));
                var pulse = rm ? 1 : 1 + Math.Sin(g * 6) * 0.06;
                ChasquiPainter.Glyph(ctx, "!", new Point(76, 12 + pose.Y * 0.4), 24 * pop * pulse, ColorUtil.Hex("#F7C266"), 1, 10);
                break;
            }
            case ChasquiState.Searching when !rm:
            {
                var u = g % 3.4;
                if (u < 1.3)
                {
                    var pop = Ease.Bounce(Ease.Seg(u, 0, 0.35));
                    ChasquiPainter.Glyph(ctx, "?", new Point(72 + pose.X * 0.6, 16 + pose.Y * 0.6), 18 * pop, accent, 1 - Ease.Seg(u, 1.0, 0.3), -8);
                }
                break;
            }
            case ChasquiState.Waiting when !rm:
            {
                // "…": tres puntos que laten como al escribir.
                for (int i = 0; i < 3; i++)
                {
                    var a = 0.35 + 0.65 * Math.Max(0, Math.Sin(g * 5 - i * 0.7));
                    ctx.DrawEllipse(new ImmutableSolidColorBrush(ColorUtil.WithAlpha(accent, a)), null, new Point(70 + i * 7, 16 - a * 2), 2.4, 2.4);
                }
                break;
            }
            case ChasquiState.Idle when IsSleepy(g) && !rm:
                for (int i = 0; i < 2; i++)
                {
                    var u = 0.3 + i * 0.45;
                    ChasquiPainter.Glyph(ctx, "z", new Point(70 + u * 14, 26 - u * 20), 9 + u * 7, accent, Math.Sin(u * Math.PI), -12);
                }
                break;
            case ChasquiState.Reconnecting when !rm:
            {
                var u = g % 1.6;
                if (u < 0.9)
                    Particles.Drop(ctx, new Point(64 + pose.X, 26 + pose.Y + u * 10), 2.8, ColorUtil.WithAlpha(ColorUtil.Hex("#7CC8FF"), 1 - u / 0.9));
                break;
            }
        }
    }
}

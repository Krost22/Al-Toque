using AlToque.Chasqui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AlToque.Controls;

public enum TrackMode
{
    Hidden,

    /// <summary>Oferta enviada: Chasqui da golpecitos al principio de la pista.</summary>
    Waiting,

    /// <summary>La estela es la barra de progreso; las rayas corren hacia atrás.</summary>
    Active,

    /// <summary>Corte de red: la barra se congela (nunca retrocede) y Chasqui planea en círculos.</summary>
    Frozen,

    /// <summary>Aterriza, se dibuja el ✓ y salta el confeti.</summary>
    Done,
}

/// <summary>Pista de progreso de las tarjetas de envío, con Chasqui volando en la punta de la estela.</summary>
public sealed class TransferTrack : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<TransferTrack, double>(nameof(Progress));

    public static readonly StyledProperty<TrackMode> ModeProperty =
        AvaloniaProperty.Register<TransferTrack, TrackMode>(nameof(Mode));

    public static readonly StyledProperty<string?> EndColorProperty =
        AvaloniaProperty.Register<TransferTrack, string?>(nameof(EndColor));

    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<TransferTrack, IBrush?>(nameof(DotBrush), new ImmutableSolidColorBrush(ColorUtil.Hex("#8E8E9F")));

    public static readonly StyledProperty<IBrush?> SuccessBrushProperty =
        AvaloniaProperty.Register<TransferTrack, IBrush?>(nameof(SuccessBrush), new ImmutableSolidColorBrush(ColorUtil.Hex("#46D891")));

    private static readonly Color Accent = ColorUtil.Hex("#5B5BF0");
    private double _shown;
    private double _lastFrame = -1;
    private double _modeStart = -10;
    private Burst? _confetti;
    private bool _frameRequested;

    static TransferTrack()
    {
        AffectsRender<TransferTrack>(ProgressProperty, ModeProperty, EndColorProperty, DotBrushProperty, SuccessBrushProperty);
        HeightProperty.OverrideDefaultValue<TransferTrack>(40);
        ModeProperty.Changed.AddClassHandler<TransferTrack>((t, e) => t.OnModeChanged((TrackMode)e.NewValue!));
        ProgressProperty.Changed.AddClassHandler<TransferTrack>((t, _) => t.RequestFrame());
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public TrackMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>Color de identidad del compañero, para el punto final.</summary>
    public string? EndColor
    {
        get => GetValue(EndColorProperty);
        set => SetValue(EndColorProperty, value);
    }

    public IBrush? DotBrush
    {
        get => GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    public IBrush? SuccessBrush
    {
        get => GetValue(SuccessBrushProperty);
        set => SetValue(SuccessBrushProperty, value);
    }

    private void OnModeChanged(TrackMode mode)
    {
        _modeStart = AnimClock.Now;
        if (mode == TrackMode.Done)
            _shown = 1;
        RequestFrame();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _shown = Mode == TrackMode.Done ? 1 : Math.Clamp(Progress, 0, 1);
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (_frameRequested)
            return;
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            InvalidateVisual();
            return;
        }
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            InvalidateVisual();
            if (NeedsFrames(top))
                RequestFrame();
        });
    }

    private bool NeedsFrames(TopLevel top)
    {
        if (!top.IsVisible || !IsEffectivelyVisible || top is Window { WindowState: WindowState.Minimized } || AnimClock.Frozen is not null)
            return false;
        if (MotionPrefs.Reduced)
            return Math.Abs(_shown - Target) > 0.001;
        return Mode switch
        {
            TrackMode.Hidden => false,
            TrackMode.Done => AnimClock.Now - _modeStart < 2,
            _ => true,
        };
    }

    private double Target => Mode == TrackMode.Done ? 1 : Math.Clamp(Progress, 0, 1);

    public override void Render(DrawingContext ctx)
    {
        var mode = Mode;
        if (mode == TrackMode.Hidden || Bounds.Width < 40)
            return;
        var now = AnimClock.Now;
        var reduced = MotionPrefs.Reduced;

        // Progreso suavizado: nunca retrocede, alcanza el valor real con una curva que acelera al final.
        var target = Target;
        if (AnimClock.Frozen is not null || _lastFrame < 0 || reduced)
            _shown = Math.Max(_shown, target);
        else
        {
            var dt = Math.Clamp(now - _lastFrame, 0, 0.1);
            var gap = target - _shown;
            if (gap > 0)
                _shown += gap * (1 - Math.Exp(-dt * (5 + 10 * target)));
        }
        _lastFrame = now;
        if (mode == TrackMode.Done)
            _shown = Math.Min(1, _shown);

        var w = Bounds.Width;
        var y = Bounds.Height - 15;
        const double endR = 10;
        var left = 3.0;
        var right = w - endR * 2 - 6;
        var endCenter = new Point(w - endR - 1, y);

        // Pista punteada hasta el destino.
        var dot = DotBrush ?? Brushes.Gray;
        for (var x = left; x <= right; x += 4.5)
            ctx.DrawEllipse(dot, null, new Point(x, y), 0.8, 0.8);

        var t = now - _modeStart;
        switch (mode)
        {
            case TrackMode.Waiting:
            {
                var breathe = reduced ? 1 : 1 + Math.Sin(now * 3) * 0.07;
                ctx.DrawEllipse(new ImmutableSolidColorBrush(ColorUtil.Hex(EndColor ?? "#2AA05A")), null, endCenter, endR * breathe, endR * breathe);
                var tap = reduced ? 0 : (now % 0.9) < 0.18 ? Math.Sin((now % 0.9) / 0.18 * Math.PI) : 0;
                var pose = ChasquiPose.Rest;
                pose.Rotation = 8 + 12 * tap;
                pose.Y = 4 * tap;
                pose.Sy = 1 - 0.08 * tap;
                pose.Look = new Vector(1, 0.2);
                pose.Flap = reduced ? 0 : Math.Sin(now * 2.4) * 0.3;
                DrawChasqui(ctx, new Point(left + 12, y - 11), 30, pose);
                // Puntos que "escriben" bajo el destino.
                for (int i = 0; i < 3; i++)
                {
                    var a = reduced ? 0.7 : 0.35 + 0.65 * Math.Max(0, Math.Sin(now * 5 - i * 0.7));
                    ctx.DrawEllipse(new ImmutableSolidColorBrush(ColorUtil.WithAlpha(Color.FromRgb(0x8E, 0x8E, 0x9F), a)), null,
                        new Point(endCenter.X - 9 + i * 9, y + 11 + (reduced ? 0 : -a * 1.5)), 2, 2);
                }
                break;
            }
            case TrackMode.Active or TrackMode.Frozen:
            {
                var head = left + (right - left) * _shown;
                var frozen = mode == TrackMode.Frozen;
                var thick = 5.0;
                if (head > left + 0.5)
                {
                    var soft = new Pen(new ImmutableSolidColorBrush(ColorUtil.WithAlpha(Accent, frozen ? 0.25 : 0.35)), thick, lineCap: PenLineCap.Round);
                    ctx.DrawLine(soft, new Point(left, y), new Point(head, y));
                    // Las rayas corren hacia atrás: se siente más rápido.
                    var offset = reduced || frozen ? 0 : now * 14 % 9;
                    var dashes = new Pen(new ImmutableSolidColorBrush(frozen ? ColorUtil.WithAlpha(Accent, 0.55) : Accent), thick,
                        reduced ? null : new DashStyle([1.2, 0.6], offset / thick), PenLineCap.Flat);
                    using (ctx.PushClip(new RoundedRect(new Rect(left - thick / 2, y - thick / 2, head - left + thick, thick), thick / 2)))
                        ctx.DrawLine(dashes, new Point(left - thick, y), new Point(head + thick, y));
                }
                ctx.DrawEllipse(new ImmutableSolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Hex(EndColor ?? "#2AA05A"), 0.9)), null, endCenter, endR * 0.45, endR * 0.45);
                var pose = ChasquiPose.Rest;
                var center = new Point(head, y - 13);
                if (frozen)
                {
                    // Planea en círculos sobre la punta, preocupado.
                    var a = reduced ? 0 : now * 2.2;
                    center += new Vector(Math.Cos(a) * 6, Math.Sin(a) * 3);
                    pose.Rotation = reduced ? 0 : -Math.Cos(a) * 16;
                    pose.Eyes = ChasquiEyes.Worried;
                    pose.Flap = reduced ? 0 : Math.Sin(now * 3) * 0.4;
                }
                else
                {
                    pose.Rotation = reduced ? -4 : -6 + Math.Sin(now * 3) * 4;
                    center += new Vector(0, reduced ? 0 : Math.Sin(now * 3) * 1.6);
                    pose.Flap = reduced ? 0 : Math.Sin(now * 14) * 0.9;
                    pose.Eyes = ChasquiEyes.Focused;
                    pose.Look = new Vector(1, 0);
                    if (!reduced)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            var u = (now * 2.4 + i * 0.5) % 1;
                            var lx = center.X - 14 - u * 12;
                            var ly = center.Y + 1 + i * 4;
                            ctx.DrawLine(new Pen(new ImmutableSolidColorBrush(ColorUtil.WithAlpha(ColorUtil.Hex("#A3A3FF"), (1 - u) * 0.7)), 1.4, lineCap: PenLineCap.Round),
                                new Point(lx, ly), new Point(lx + 5, ly));
                        }
                    }
                }
                DrawChasqui(ctx, center, 30, pose);
                break;
            }
            case TrackMode.Done:
            {
                var success = SuccessBrush ?? Brushes.Green;
                ctx.DrawLine(new Pen(success, 5, lineCap: PenLineCap.Round), new Point(left, y), new Point(right + 2, y));
                // Chasqui aterriza junto al ✓ (400 ms) y se aplasta al tocar.
                var land = reduced ? 1 : Ease.Seg(t, 0, 0.4);
                var pose = ChasquiPose.Rest;
                pose.Eyes = ChasquiEyes.Happy;
                pose.Mouth = ChasquiMouth.Open;
                var squash = reduced ? 0 : Math.Exp(-Math.Max(0, t - 0.4) * 7) * Math.Cos(Math.Max(0, t - 0.4) * 22) * 0.2 * (t > 0.4 ? 1 : 0);
                pose.Sy = 1 - squash;
                pose.Sx = 1 + squash;
                pose.Rotation = -4 + 10 * (1 - land);
                var cx = right - 26 + (1 - Ease.Enter(land)) * -30;
                DrawChasqui(ctx, new Point(cx, y - 13 - (1 - land) * 8), 30, pose);
                // ✓ dibujado como un trazo (450 ms).
                var check = reduced ? 1 : Ease.Seg(t, 0.25, 0.45);
                var r = endR * (reduced ? 1 : 0.6 + 0.4 * Ease.Bounce(Ease.Seg(t, 0.2, 0.3)));
                ctx.DrawEllipse(success, null, endCenter, r, r);
                DrawCheck(ctx, endCenter, check);
                if (!reduced && t < 2)
                {
                    _confetti ??= new Burst(BurstKind.Confetti, _modeStart + 0.4, endCenter, 10, Random.Shared.Next(), 0.55);
                    Particles.Draw(ctx, _confetti.Value, now);
                }
                break;
            }
        }
    }

    private static void DrawChasqui(DrawingContext ctx, Point center, double size, in ChasquiPose pose)
    {
        var k = size / 100;
        using (ctx.PushTransform(Matrix.CreateTranslation(-50, -50) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(center.X, center.Y)))
            ChasquiPainter.Draw(ctx, pose, shadow: false);
    }

    private static void DrawCheck(DrawingContext ctx, Point c, double t)
    {
        if (t <= 0)
            return;
        var a = c + new Vector(-4.2, 0.2);
        var b = c + new Vector(-1.2, 3.4);
        var d = c + new Vector(4.6, -3.2);
        var pen = new Pen(Brushes.White, 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var first = Math.Clamp(t / 0.4, 0, 1);
        ctx.DrawLine(pen, a, a + (b - a) * first);
        if (t > 0.4)
            ctx.DrawLine(pen, b, b + (d - b) * Math.Clamp((t - 0.4) / 0.6, 0, 1));
    }
}

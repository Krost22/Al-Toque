using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AlToque.Controls.Avatars;

public enum AvatarMood
{
    Idle,
    Happy,
    Excited,
    Surprised,
    Sleepy,
    Sad,
    Love,
    Dizzy,
    Wink,
    Focused,
}

/// <summary>Estado de identidad alrededor del avatar. Nunca depende solo del color: escudo, anillo y texto.</summary>
public enum AvatarRing
{
    None,

    /// <summary>Emparejado y comprobado: insignia con escudo.</summary>
    Verified,

    /// <summary>No emparejado: anillo discontinuo ámbar.</summary>
    Unverified,

    /// <summary>Conocido por caché, aún sin comprobar: anillo punteado.</summary>
    Checking,

    /// <summary>Destino al arrastrar: anillo de acento que late.</summary>
    Drop,

    /// <summary>Elegido en el selector de avatares.</summary>
    Selected,
}

/// <summary>
/// Avatar de personaje: ficha redondeada con el color de la identidad y un personaje que parpadea, mira, salta al
/// pasar el ratón, celebra, se encoge de hombros… Todo se dibuja a mano y solo se anima cuando hace falta.
/// </summary>
public sealed class AvatarView : Control
{
    public static readonly StyledProperty<string?> AvatarIdProperty =
        AvaloniaProperty.Register<AvatarView, string?>(nameof(AvatarId));

    public static readonly StyledProperty<string?> ColorHexProperty =
        AvaloniaProperty.Register<AvatarView, string?>(nameof(ColorHex));

    public static readonly StyledProperty<string?> InitialProperty =
        AvaloniaProperty.Register<AvatarView, string?>(nameof(Initial));

    public static readonly StyledProperty<AvatarMood> MoodProperty =
        AvaloniaProperty.Register<AvatarView, AvatarMood>(nameof(Mood));

    public static readonly StyledProperty<AvatarRing> RingProperty =
        AvaloniaProperty.Register<AvatarView, AvatarRing>(nameof(Ring));

    public static readonly StyledProperty<bool> DimmedProperty =
        AvaloniaProperty.Register<AvatarView, bool>(nameof(Dimmed));

    public static readonly StyledProperty<bool> ReactProperty =
        AvaloniaProperty.Register<AvatarView, bool>(nameof(React), true);

    public static readonly StyledProperty<bool> LiveProperty =
        AvaloniaProperty.Register<AvatarView, bool>(nameof(Live), true);

    public static readonly StyledProperty<bool> PopOnLoadProperty =
        AvaloniaProperty.Register<AvatarView, bool>(nameof(PopOnLoad));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<AvatarView, IBrush?>(nameof(AccentBrush), new SolidColorBrush(Color.FromRgb(0x5B, 0x5B, 0xF0)));

    public static readonly StyledProperty<IBrush?> SuccessBrushProperty =
        AvaloniaProperty.Register<AvatarView, IBrush?>(nameof(SuccessBrush), new SolidColorBrush(Color.FromRgb(0x46, 0xD8, 0x91)));

    public static readonly StyledProperty<IBrush?> WarningBrushProperty =
        AvaloniaProperty.Register<AvatarView, IBrush?>(nameof(WarningBrush), new SolidColorBrush(Color.FromRgb(0xF7, 0xC2, 0x66)));

    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<AvatarView, IBrush?>(nameof(MutedBrush), new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x9F)));

    public static readonly StyledProperty<IBrush?> BadgeBackgroundProperty =
        AvaloniaProperty.Register<AvatarView, IBrush?>(nameof(BadgeBackground), new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x28)));

    /// <summary>Radio de las esquinas de la ficha en la caja de 100.</summary>
    private const double TileRadius = 27;

    private static readonly Random Rng = new();

    private double _popStart = -10, _hopStart = -10, _shakeStart = -10, _celebrateStart = -10, _wiggleStart = -10;
    private double _blinkStart = -10, _lookStart = -10, _nodStart = -10;
    private Vector _lookFrom, _lookTo;
    private double _nextBlink, _nextIdle;
    private bool _hover, _frameRequested;
    private RenderTargetBitmap? _body;
    private (string? Id, string? Color, int Pixels) _bodyKey;
    private FormattedText? _letter;
    private (string Text, double Size) _letterKey;

    static AvatarView()
    {
        AffectsRender<AvatarView>(AvatarIdProperty, ColorHexProperty, InitialProperty, MoodProperty, RingProperty, DimmedProperty,
            AccentBrushProperty, SuccessBrushProperty, WarningBrushProperty, MutedBrushProperty, BadgeBackgroundProperty);
        WidthProperty.OverrideDefaultValue<AvatarView>(56);
        HeightProperty.OverrideDefaultValue<AvatarView>(56);
        MoodProperty.Changed.AddClassHandler<AvatarView>((v, e) => v.OnMoodChanged((AvatarMood)e.NewValue!));
        RingProperty.Changed.AddClassHandler<AvatarView>((v, _) => v.RequestFrame());
        AvatarIdProperty.Changed.AddClassHandler<AvatarView>((v, e) =>
        {
            if (e.OldValue is not null)
                v.Pop();
        });
    }

    public string? AvatarId
    {
        get => GetValue(AvatarIdProperty);
        set => SetValue(AvatarIdProperty, value);
    }

    /// <summary>Color de la identidad (<c>#RRGGBB</c>), fondo de la ficha.</summary>
    public string? ColorHex
    {
        get => GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    /// <summary>Letra para el avatar de inicial.</summary>
    public string? Initial
    {
        get => GetValue(InitialProperty);
        set => SetValue(InitialProperty, value);
    }

    public AvatarMood Mood
    {
        get => GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public AvatarRing Ring
    {
        get => GetValue(RingProperty);
        set => SetValue(RingProperty, value);
    }

    /// <summary>Sin conexión: atenuado y dormido.</summary>
    public bool Dimmed
    {
        get => GetValue(DimmedProperty);
        set => SetValue(DimmedProperty, value);
    }

    /// <summary>Salta y sonríe al pasar el ratón.</summary>
    public bool React
    {
        get => GetValue(ReactProperty);
        set => SetValue(ReactProperty, value);
    }

    /// <summary>Parpadea y hace pequeñas acciones de vez en cuando.</summary>
    public bool Live
    {
        get => GetValue(LiveProperty);
        set => SetValue(LiveProperty, value);
    }

    public bool PopOnLoad
    {
        get => GetValue(PopOnLoadProperty);
        set => SetValue(PopOnLoadProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? SuccessBrush
    {
        get => GetValue(SuccessBrushProperty);
        set => SetValue(SuccessBrushProperty, value);
    }

    public IBrush? WarningBrush
    {
        get => GetValue(WarningBrushProperty);
        set => SetValue(WarningBrushProperty, value);
    }

    public IBrush? MutedBrush
    {
        get => GetValue(MutedBrushProperty);
        set => SetValue(MutedBrushProperty, value);
    }

    public IBrush? BadgeBackground
    {
        get => GetValue(BadgeBackgroundProperty);
        set => SetValue(BadgeBackgroundProperty, value);
    }

    // ===== Acciones =====

    /// <summary>Aparece creciendo con rebote.</summary>
    public void Pop() => Start(ref _popStart);

    /// <summary>Saltito con anticipación, estiramiento y aterrizaje aplastado.</summary>
    public void Hop() => Start(ref _hopStart);

    /// <summary>Dos saltos con chispas: emparejado, recibido, enviado.</summary>
    public void Celebrate() => Start(ref _celebrateStart);

    /// <summary>"No" con la cabeza.</summary>
    public void Shake() => Start(ref _shakeStart);

    /// <summary>Mueve orejas, antenas o tentáculos.</summary>
    public void Wiggle() => Start(ref _wiggleStart);

    /// <summary>Asiente: "sí, ya voy".</summary>
    public void Nod() => Start(ref _nodStart);

    private void Start(ref double field)
    {
        if (MotionPrefs.Reduced)
        {
            InvalidateVisual();
            return;
        }
        field = AnimClock.Now;
        RequestFrame();
    }

    private void OnMoodChanged(AvatarMood mood)
    {
        switch (mood)
        {
            case AvatarMood.Love or AvatarMood.Excited:
                Celebrate();
                break;
            case AvatarMood.Happy:
                Hop();
                break;
            case AvatarMood.Surprised:
                Nod();
                break;
            case AvatarMood.Sad or AvatarMood.Dizzy:
                Shake();
                break;
            default:
                Wiggle();
                break;
        }
    }

    // ===== Vida propia: parpadeos y pequeñas acciones =====

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var now = AnimClock.Now;
        _nextBlink = now + 1 + Rng.NextDouble() * 4;
        _nextIdle = now + 6 + Rng.NextDouble() * 10;
        Director.Add(this);
        if (PopOnLoad)
            Pop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Director.Remove(this);
        _body?.Dispose();
        _body = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!React)
            return;
        _hover = true;
        Hop();
        Wiggle();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_hover)
            return;
        _hover = false;
        InvalidateVisual();
    }

    private void DirectorTick(double now)
    {
        if (!Live || MotionPrefs.Reduced || !IsEffectivelyVisible || !CanAnimateHere())
            return;
        if (now >= _nextBlink)
        {
            _blinkStart = now;
            // A veces, doble parpadeo.
            _nextBlink = now + (Rng.NextDouble() < 0.2 ? 0.35 : 2.2 + Rng.NextDouble() * 4.5);
            RequestFrame();
        }
        if (now >= _nextIdle)
        {
            _nextIdle = now + 7 + Rng.NextDouble() * 12;
            switch (Rng.Next(4))
            {
                case 0 or 1:
                    _lookFrom = LookAt(now);
                    _lookTo = new Vector(Rng.NextDouble() * 2 - 1, Rng.NextDouble() * 0.8 - 0.4);
                    _lookStart = now;
                    break;
                case 2:
                    _wiggleStart = now;
                    break;
                default:
                    _hopStart = now;
                    break;
            }
            RequestFrame();
        }
    }

    private bool CanAnimateHere() =>
        TopLevel.GetTopLevel(this) is not Window w || (w.IsVisible && w.WindowState != WindowState.Minimized);

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
            if (IsAnimating(AnimClock.Now))
                RequestFrame();
        });
    }

    private bool IsAnimating(double now)
    {
        if (AnimClock.Frozen is not null)
            return false;
        return now - _popStart < 0.6 || now - _hopStart < 0.6 || now - _shakeStart < 0.7 || now - _celebrateStart < 1.3 ||
               now - _wiggleStart < 1.0 || now - _blinkStart < 0.16 || now - _lookStart < 2.4 || now - _nodStart < 0.6 ||
               (Ring == AvatarRing.Drop && !MotionPrefs.Reduced);
    }

    private Vector LookAt(double now)
    {
        var t = now - _lookStart;
        if (t < 0 || t > 2.4)
            return default;
        // Mira, se queda y vuelve al centro.
        var go = Ease.OutCubic(Ease.Seg(t, 0, 0.25));
        var back = Ease.InOutSine(Ease.Seg(t, 1.9, 0.5));
        var v = _lookFrom + (_lookTo - _lookFrom) * go;
        return v * (1 - back);
    }

    // ===== Dibujo =====

    public override void Render(DrawingContext context)
    {
        var now = AnimClock.Now;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 4)
            return;
        var gap = Math.Max(3, size * 0.065);
        var tile = size - gap * 2;
        var origin = new Point((Bounds.Width - size) / 2 + gap, (Bounds.Height - size) / 2 + gap);
        var s = tile / 100.0;
        var center = origin + new Vector(tile / 2, tile / 2);

        var popT = (now - _popStart) / 0.5;
        var pop = popT is >= 0 and < 1 ? Math.Max(0.01, Ease.Bounce(popT)) : 1;
        var dropPulse = Ring == AvatarRing.Drop && !MotionPrefs.Reduced ? 1.05 + Math.Sin(now * 7) * 0.025 : 1;
        var scale = pop * dropPulse;

        using var dim = context.PushOpacity(Dimmed ? 0.55 : 1);
        using var tileScale = context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateScale(scale, scale) *
                                                    Matrix.CreateTranslation(center.X, center.Y));
        DrawRing(context, origin, tile, gap, now);

        using (context.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            var bg = ColorUtil.Hex(ColorHex ?? "#2AA05A");
            DrawTile(context, bg);
            using (context.PushClip(new RoundedRect(new Rect(0, 0, 100, 100), TileRadius)))
            {
                var character = AvatarCatalog.Find(AvatarId);
                using (context.PushTransform(CharacterMotion(now)))
                {
                    if (character is null)
                        DrawLetter(context);
                    else
                    {
                        var energy = Energy(now);
                        if (energy > 0 || !DrawCachedBody(context, character, bg, tile))
                        {
                            var painter = new Painter(context, bg, now, energy);
                            foreach (var layer in character.Layers)
                                painter.Draw(layer);
                        }
                        DrawFace(context, character.Face, EffectiveMood(), Blink(now), LookAt(now), now);
                    }
                }
            }
            DrawGloss(context);
        }

        DrawBadge(context, origin, tile, size);
        DrawParticles(context, center, tile, now);
    }

    /// <summary>
    /// Con el personaje quieto, el cuerpo (con sus sombras difusas) se dibuja una vez en un mapa de bits y se reutiliza:
    /// parpadear o mirar solo vuelve a pintar la cara.
    /// </summary>
    private bool DrawCachedBody(DrawingContext context, Character character, Color bg, double tile)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var pixels = (int)Math.Ceiling(tile * scaling);
        if (pixels is < 8 or > 1024)
            return false;
        var key = (character.Id, ColorHex, pixels);
        if (_body is null || _bodyKey != key)
        {
            _body?.Dispose();
            _body = null;
            try
            {
                var bitmap = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96 * scaling, 96 * scaling));
                using (var bctx = bitmap.CreateDrawingContext())
                using (bctx.PushTransform(Matrix.CreateScale(tile / 100, tile / 100)))
                using (bctx.PushClip(new RoundedRect(new Rect(0, 0, 100, 100), TileRadius)))
                {
                    var painter = new Painter(bctx, bg, 0, 0);
                    foreach (var layer in character.Layers)
                        painter.Draw(layer);
                }
                _body = bitmap;
                _bodyKey = key;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                return false;
            }
        }
        context.DrawImage(_body, new Rect(0, 0, 100, 100));
        return true;
    }

    private AvatarMood EffectiveMood()
    {
        if (Dimmed && Mood == AvatarMood.Idle)
            return AvatarMood.Sleepy;
        if (_hover && React && Mood is AvatarMood.Idle or AvatarMood.Focused)
            return AvatarMood.Happy;
        if (Ring == AvatarRing.Drop && Mood == AvatarMood.Idle)
            return AvatarMood.Excited;
        return Mood;
    }

    private double Energy(double now)
    {
        var e = 0.0;
        e = Math.Max(e, Window(now - _wiggleStart, 1.0));
        e = Math.Max(e, Window(now - _hopStart, 0.6) * 0.8);
        e = Math.Max(e, Window(now - _celebrateStart, 1.3));
        e = Math.Max(e, Window(now - _popStart, 0.6) * 0.6);
        if (Ring == AvatarRing.Drop)
            e = Math.Max(e, 0.55);
        return MotionPrefs.Reduced ? 0 : e;

        static double Window(double t, double len) => t >= 0 && t < len ? Math.Sin(Math.PI * Math.Min(1, t / len * 1.4 + 0.15)) : 0;
    }

    private double Blink(double now)
    {
        var t = now - _blinkStart;
        if (t is < 0 or > 0.16)
            return 0;
        return Math.Sin(t / 0.16 * Math.PI);
    }

    /// <summary>Saltos, sacudidas y asentimientos del personaje dentro de la ficha (pivote abajo al centro).</summary>
    private Matrix CharacterMotion(double now)
    {
        if (MotionPrefs.Reduced)
            return Matrix.Identity;
        double dy = 0, sx = 1, sy = 1, rot = 0, dx = 0;

        void Jump(double t, double height)
        {
            if (t is < 0 or >= 1)
                return;
            if (t < 0.16)
            {
                // Anticipación: se agacha.
                var a = Math.Sin(t / 0.16 * Math.PI / 2);
                sy *= 1 - 0.1 * a;
                sx *= 1 + 0.08 * a;
            }
            else if (t < 0.74)
            {
                var u = (t - 0.16) / 0.58;
                dy -= height * 4 * u * (1 - u);
                var stretch = Math.Cos(u * Math.PI) * 0.09;
                sy *= 1 + stretch;
                sx *= 1 - stretch * 0.8;
            }
            else
            {
                // Aterriza aplastado y rebota hasta quedarse quieto.
                var u = (t - 0.74) / 0.26;
                var squash = Math.Sin(u * Math.PI) * 0.11 * (1 - u * 0.3);
                sy *= 1 - squash;
                sx *= 1 + squash;
            }
        }

        Jump((now - _hopStart) / 0.6, 13);
        var c = (now - _celebrateStart) / 1.3;
        if (c is >= 0 and < 1)
        {
            Jump(c / 0.5, 16);
            Jump((c - 0.5) / 0.5, 11);
            rot += Math.Sin(c * Math.PI * 4) * 6 * (1 - c);
        }
        var sh = (now - _shakeStart) / 0.7;
        if (sh is >= 0 and < 1)
        {
            rot += Math.Sin(sh * Math.PI * 5) * 11 * (1 - sh);
            dx += Math.Sin(sh * Math.PI * 5) * 2.5 * (1 - sh);
        }
        var nod = (now - _nodStart) / 0.6;
        if (nod is >= 0 and < 1)
        {
            var k = Math.Sin(nod * Math.PI * 2) * (1 - nod);
            dy += k * 4;
            sy *= 1 - k * 0.05;
        }
        var w = (now - _wiggleStart) / 1.0;
        if (w is >= 0 and < 1)
            rot += Math.Sin(w * Math.PI * 4) * 4 * (1 - w);
        if (_hover && React)
            dy -= 1.5;

        return Matrix.CreateTranslation(-50, -104) * Matrix.CreateScale(sx, sy) * Matrix.CreateRotation(rot * Math.PI / 180) *
               Matrix.CreateTranslation(50 + dx, 104 + dy);
    }

    private static void DrawTile(DrawingContext ctx, Color bg)
    {
        var rect = new RoundedRect(new Rect(0, 0, 100, 100), TileRadius);
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.2, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.8, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ColorUtil.Lighten(bg, 0.07), 0),
                new GradientStop(bg, 0.55),
                new GradientStop(ColorUtil.Darken(bg, 0.06), 1),
            },
        };
        ctx.DrawRectangle(fill, null, rect,
            new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 8, Color = ColorUtil.WithAlpha(Color.FromRgb(0, 0, 0), 0.22) }));
        // Brillo superior suave.
        var glow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.3, 0.05, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.3, 0.05, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(46, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
            },
        };
        ctx.DrawRectangle(glow, null, rect);
    }

    private static void DrawGloss(DrawingContext ctx)
    {
        // Filo de luz arriba y sombra interior abajo: la ficha parece un botón blando.
        var edge = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(90, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.35),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.7),
                new GradientStop(Color.FromArgb(50, 0, 0, 0), 1),
            },
        };
        ctx.DrawRectangle(null, new Pen(edge, 1.6), new RoundedRect(new Rect(0.8, 0.8, 98.4, 98.4), TileRadius - 0.8));
    }

    private void DrawLetter(DrawingContext ctx)
    {
        var text = string.IsNullOrEmpty(Initial) ? "?" : Initial!;
        if (_letter is null || _letterKey != (text, 46))
        {
            var typeface = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI, fonts:Inter#Inter"), FontStyle.Normal,
                FontWeight.SemiBold, FontStretch.Normal);
            _letter = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 46, Brushes.White);
            _letterKey = (text, 46);
        }
        var origin = new Point(50 - _letter.Width / 2, 50 - _letter.Height / 2);
        using (ctx.PushOpacity(0.25))
        using (ctx.PushTransform(Matrix.CreateTranslation(0, 2)))
        {
            var shadow = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI, fonts:Inter#Inter"), FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal),
                46, Brushes.Black);
            ctx.DrawText(shadow, origin);
        }
        ctx.DrawText(_letter, origin);
    }

    private void DrawRing(DrawingContext ctx, Point origin, double tile, double gap, double now)
    {
        var ring = Ring;
        if (ring is AvatarRing.None or AvatarRing.Verified)
            return;
        var s = tile / 100.0;
        var rect = new Rect(origin.X, origin.Y, tile, tile).Inflate(gap * 0.62);
        var radius = TileRadius * s + gap * 0.62;
        var thickness = Math.Max(1.5, tile * 0.028);
        switch (ring)
        {
            case AvatarRing.Unverified when WarningBrush is { } warn:
                ctx.DrawRectangle(null, new Pen(warn, thickness, new DashStyle([2.2, 1.6], 0), PenLineCap.Round), new RoundedRect(rect, radius));
                break;
            case AvatarRing.Checking when MutedBrush is { } muted:
                ctx.DrawRectangle(null, new Pen(muted, thickness, new DashStyle([0.01, 2.4], -now * 1.2 % 2.41), PenLineCap.Round),
                    new RoundedRect(rect, radius));
                break;
            case AvatarRing.Drop or AvatarRing.Selected when AccentBrush is { } accent:
                if (ring == AvatarRing.Drop && accent is ISolidColorBrush sc && !MotionPrefs.Reduced)
                {
                    var glow = 0.25 + 0.2 * Math.Sin(now * 7);
                    ctx.DrawRectangle(null, new Pen(new SolidColorBrush(ColorUtil.WithAlpha(sc.Color, glow)), thickness * 3.2),
                        new RoundedRect(rect.Inflate(thickness), radius + thickness));
                }
                ctx.DrawRectangle(null, new Pen(accent, thickness * 1.3), new RoundedRect(rect, radius));
                break;
        }
    }

    private void DrawBadge(DrawingContext ctx, Point origin, double tile, double size)
    {
        if (Ring != AvatarRing.Verified || size < 28)
            return;
        var r = Math.Max(8, size * 0.17);
        var c = new Point(origin.X + tile - r * 0.45, origin.Y + tile - r * 0.45);
        ctx.DrawEllipse(BadgeBackground, null, c, r, r);
        if (Icons.Get(Ic.ShieldCheck) is { } shield && SuccessBrush is { } success)
        {
            var iconSize = r * 1.3;
            var k = iconSize / 24;
            using (ctx.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(c.X - iconSize / 2, c.Y - iconSize / 2)))
                ctx.DrawGeometry(null, new Pen(success, 2 / Math.Max(0.4, k * 1.15), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), shield);
        }
    }

    private void DrawParticles(DrawingContext ctx, Point center, double tile, double now)
    {
        if (MotionPrefs.Reduced)
            return;
        var c = (now - _celebrateStart) / 1.3;
        if (c is >= 0 and < 1)
        {
            Color[] colors = [Color.FromRgb(0xFF, 0xD1, 0x66), Color.FromRgb(0xA3, 0xA3, 0xFF), Color.FromRgb(0xFF, 0x8F, 0xB1), Color.FromRgb(0x7B, 0xE0, 0xB0)];
            for (int i = 0; i < 10; i++)
            {
                var a = i / 10.0 * Math.PI * 2 + 0.3;
                var start = i % 2 * 0.12;
                var p = Ease.Seg(c, start, 0.75);
                if (p <= 0 || p >= 1)
                    continue;
                var dist = tile * (0.42 + Ease.OutCubic(p) * 0.38);
                var pos = center + new Vector(Math.Cos(a) * dist, Math.Sin(a) * dist);
                var sz = tile * 0.075 * Math.Sin(p * Math.PI);
                Sparkle(ctx, pos, sz, colors[i % colors.Length], p * 3);
            }
        }
        var mood = EffectiveMood();
        if (mood == AvatarMood.Love && tile > 30)
        {
            for (int i = 0; i < 2; i++)
            {
                var t = (now * 0.6 + i * 0.5) % 1;
                var pos = center + new Vector(tile * (0.36 + i * 0.1), -tile * (0.25 + t * 0.35));
                using (ctx.PushOpacity(Math.Sin(t * Math.PI)))
                    Heart(ctx, pos, tile * 0.07, Color.FromRgb(0xFF, 0x5C, 0x7C));
            }
            RequestFrame();
        }
        if (mood == AvatarMood.Sleepy && tile > 30 && MutedBrush is { } muted)
        {
            var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold, FontStretch.Normal);
            for (int i = 0; i < 2; i++)
            {
                var z = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, tile * (0.13 + i * 0.05), muted);
                ctx.DrawText(z, center + new Vector(tile * (0.3 + i * 0.12), -tile * (0.42 + i * 0.14)));
            }
        }
    }

    internal static void Sparkle(DrawingContext ctx, Point c, double r, Color color, double rotation)
    {
        if (r <= 0.2)
            return;
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            for (int i = 0; i < 8; i++)
            {
                var a = rotation + i * Math.PI / 4;
                var rr = i % 2 == 0 ? r : r * 0.32;
                var p = new Point(c.X + Math.Cos(a) * rr, c.Y + Math.Sin(a) * rr);
                if (i == 0)
                    sg.BeginFigure(p, true);
                else
                    sg.LineTo(p);
            }
            sg.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(color), null, g);
    }

    internal static void Heart(DrawingContext ctx, Point c, double r, Color color)
    {
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            sg.BeginFigure(new Point(c.X, c.Y + r * 0.9), true);
            sg.CubicBezierTo(new Point(c.X - r * 1.4, c.Y + r * 0.1), new Point(c.X - r * 1.1, c.Y - r * 1.1), new Point(c.X, c.Y - r * 0.45));
            sg.CubicBezierTo(new Point(c.X + r * 1.1, c.Y - r * 1.1), new Point(c.X + r * 1.4, c.Y + r * 0.1), new Point(c.X, c.Y + r * 0.9));
            sg.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(color), null, g);
    }

    // ===== Cara =====

    private static void DrawFace(DrawingContext ctx, FaceSpec f, AvatarMood mood, double blink, Vector look, double now)
    {
        var r = f.EyeSize;
        var eyeOffset = new Vector(look.X * r * 0.5, look.Y * r * 0.4);

        // Mejillas.
        var blushAlpha = mood is AvatarMood.Happy or AvatarMood.Love or AvatarMood.Excited ? Math.Min(0.75, f.BlushAlpha * 1.5) : f.BlushAlpha;
        var blush = new SolidColorBrush(ColorUtil.WithAlpha(f.Blush, blushAlpha));
        var dx = (f.RightEye.X - f.LeftEye.X) * 0.14;
        ctx.DrawEllipse(blush, null, new Point(f.LeftEye.X - dx, f.LeftEye.Y + r * 2.0), r * 1.15, r * 0.62);
        ctx.DrawEllipse(blush, null, new Point(f.RightEye.X + dx, f.RightEye.Y + r * 2.0), r * 1.15, r * 0.62);

        DrawEye(ctx, f, f.LeftEye + eyeOffset, mood, blink, isLeft: true, now);
        DrawEye(ctx, f, f.RightEye + eyeOffset, mood, blink, isLeft: false, now);
        DrawMouth(ctx, f, mood, now);
    }

    private static void DrawEye(DrawingContext ctx, FaceSpec f, Point c, AvatarMood mood, double blink, bool isLeft, double now)
    {
        var r = f.EyeSize;
        var ink = new SolidColorBrush(f.EyeInk);
        var line = new Pen(ink, r * 0.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        if (f.EyeWhite is { } white && mood is not (AvatarMood.Happy or AvatarMood.Excited or AvatarMood.Sleepy or AvatarMood.Love or AvatarMood.Dizzy))
        {
            var wr = f.EyeWhiteSize * (mood == AvatarMood.Surprised ? 1.08 : 1);
            var open = 1 - blink;
            ctx.DrawEllipse(new SolidColorBrush(white), null, c, wr, Math.Max(0.6, wr * open));
            if (open < 0.25)
                return;
        }

        switch (mood)
        {
            case AvatarMood.Happy or AvatarMood.Excited:
                ctx.DrawGeometry(null, line, Arc(c, r, up: true));
                if (mood == AvatarMood.Excited)
                    Sparkle(ctx, new Point(c.X + r * 0.9, c.Y - r * 1.5), r * 0.65, Color.FromRgb(0xFF, 0xD1, 0x66), now * 4);
                return;
            case AvatarMood.Wink when !isLeft:
                ctx.DrawGeometry(null, line, Arc(c, r, up: true));
                return;
            case AvatarMood.Sleepy:
                ctx.DrawGeometry(null, line, Arc(c, r, up: false));
                return;
            case AvatarMood.Love:
                var beat = 1 + Math.Sin(now * 9) * 0.1;
                Heart(ctx, c, r * 1.15 * beat, Color.FromRgb(0xFF, 0x4F, 0x74));
                return;
            case AvatarMood.Dizzy:
                var k = r * 0.8;
                ctx.DrawLine(line, new Point(c.X - k, c.Y - k), new Point(c.X + k, c.Y + k));
                ctx.DrawLine(line, new Point(c.X - k, c.Y + k), new Point(c.X + k, c.Y - k));
                return;
        }

        var size = mood == AvatarMood.Surprised ? 1.25 : mood == AvatarMood.Focused ? 0.9 : 1;
        var rx = r * 0.86 * size;
        var ry = r * f.EyeTall * size * (1 - blink);
        if (mood == AvatarMood.Focused)
            ry *= 0.75;
        if (ry < r * 0.18)
        {
            // Ojo cerrado al parpadear: una línea curva.
            ctx.DrawGeometry(null, line, Arc(c, r * 0.95, up: false));
            return;
        }
        ctx.DrawEllipse(ink, null, c, rx, ry);
        if (mood == AvatarMood.Sad)
        {
            var brow = new Pen(ink, r * 0.42, lineCap: PenLineCap.Round);
            var dir = isLeft ? 1 : -1;
            ctx.DrawLine(brow, new Point(c.X - dir * r * 1.1, c.Y - r * 1.7), new Point(c.X + dir * r * 0.7, c.Y - r * 2.3));
        }
        // Brillo: le da vida a la mirada.
        IBrush shine = IsDark(f.EyeInk) ? Brushes.White : new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        ctx.DrawEllipse(shine, null, new Point(c.X + rx * 0.32, c.Y - ry * 0.38), r * 0.3 * size, r * 0.3 * size * Math.Min(1, ry / (r * 0.9)));
        if (mood == AvatarMood.Surprised)
            ctx.DrawEllipse(shine, null, new Point(c.X - rx * 0.3, c.Y + ry * 0.35), r * 0.14, r * 0.14);
    }

    private static bool IsDark(Color c) => c.R * 0.3 + c.G * 0.59 + c.B * 0.11 < 128;

    private static Geometry Arc(Point c, double r, bool up)
    {
        var g = new StreamGeometry();
        using (var sg = g.Open())
        {
            if (up)
            {
                sg.BeginFigure(new Point(c.X - r, c.Y + r * 0.35), false);
                sg.QuadraticBezierTo(new Point(c.X, c.Y - r * 1.15), new Point(c.X + r, c.Y + r * 0.35));
            }
            else
            {
                sg.BeginFigure(new Point(c.X - r, c.Y - r * 0.1), false);
                sg.QuadraticBezierTo(new Point(c.X, c.Y + r * 0.95), new Point(c.X + r, c.Y - r * 0.1));
            }
            sg.EndFigure(false);
        }
        return g;
    }

    private static void DrawMouth(DrawingContext ctx, FaceSpec f, AvatarMood mood, double now)
    {
        if (f.Mouth2 == MouthStyle.None)
            return;
        var m = f.Mouth;
        var w = f.MouthWidth;
        var ink = new SolidColorBrush(f.MouthInk);
        var pen = new Pen(ink, Math.Max(1.6, f.EyeSize * 0.42), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var g = new StreamGeometry();
        switch (mood)
        {
            case AvatarMood.Happy or AvatarMood.Excited or AvatarMood.Love:
            {
                var open = mood == AvatarMood.Excited ? 1.15 : 1;
                using (var sg = g.Open())
                {
                    sg.BeginFigure(new Point(m.X - w * 0.55, m.Y - w * 0.12), true);
                    sg.CubicBezierTo(new Point(m.X - w * 0.45, m.Y + w * 0.75 * open), new Point(m.X + w * 0.45, m.Y + w * 0.75 * open),
                        new Point(m.X + w * 0.55, m.Y - w * 0.12));
                    sg.EndFigure(true);
                }
                ctx.DrawGeometry(ink, null, g);
                // Lengüita.
                using (ctx.PushGeometryClip(g))
                    ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0x86, 0x8C)), null, new Point(m.X, m.Y + w * 0.5 * open), w * 0.3, w * 0.2);
                return;
            }
            case AvatarMood.Surprised:
                ctx.DrawEllipse(ink, null, new Point(m.X, m.Y + w * 0.12), w * 0.2, w * 0.27);
                return;
            case AvatarMood.Sleepy:
            {
                var breath = 1 + Math.Sin(now * 2) * 0.15;
                ctx.DrawEllipse(ink, null, new Point(m.X, m.Y + w * 0.1), w * 0.13 * breath, w * 0.11 * breath);
                return;
            }
            case AvatarMood.Sad:
                using (var sg = g.Open())
                {
                    sg.BeginFigure(new Point(m.X - w * 0.42, m.Y + w * 0.28), false);
                    sg.QuadraticBezierTo(new Point(m.X, m.Y - w * 0.22), new Point(m.X + w * 0.42, m.Y + w * 0.28));
                    sg.EndFigure(false);
                }
                break;
            case AvatarMood.Dizzy:
                using (var sg = g.Open())
                {
                    sg.BeginFigure(new Point(m.X - w * 0.5, m.Y + w * 0.1), false);
                    sg.QuadraticBezierTo(new Point(m.X - w * 0.25, m.Y - w * 0.2), new Point(m.X, m.Y + w * 0.1));
                    sg.QuadraticBezierTo(new Point(m.X + w * 0.25, m.Y + w * 0.4), new Point(m.X + w * 0.5, m.Y + w * 0.1));
                    sg.EndFigure(false);
                }
                break;
            case AvatarMood.Focused:
                ctx.DrawLine(pen, new Point(m.X - w * 0.25, m.Y + w * 0.1), new Point(m.X + w * 0.25, m.Y + w * 0.1));
                return;
            default:
                using (var sg = g.Open())
                {
                    if (f.Mouth2 == MouthStyle.Cat)
                    {
                        sg.BeginFigure(new Point(m.X - w * 0.5, m.Y - w * 0.05), false);
                        sg.QuadraticBezierTo(new Point(m.X - w * 0.25, m.Y + w * 0.38), new Point(m.X, m.Y - w * 0.02));
                        sg.QuadraticBezierTo(new Point(m.X + w * 0.25, m.Y + w * 0.38), new Point(m.X + w * 0.5, m.Y - w * 0.05));
                    }
                    else
                    {
                        sg.BeginFigure(new Point(m.X - w * 0.5, m.Y), false);
                        sg.QuadraticBezierTo(new Point(m.X, m.Y + w * 0.5), new Point(m.X + w * 0.5, m.Y));
                    }
                    sg.EndFigure(false);
                }
                break;
        }
        ctx.DrawGeometry(null, pen, g);
    }

    // ===== Director: un solo temporizador para todos los avatares vivos =====

    private static class Director
    {
        private static readonly List<WeakReference<AvatarView>> Views = [];
        private static DispatcherTimer? _timer;

        public static void Add(AvatarView v)
        {
            Views.Add(new WeakReference<AvatarView>(v));
            if (_timer is null)
            {
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => Tick());
                _timer.Start();
            }
        }

        public static void Remove(AvatarView v)
        {
            Views.RemoveAll(w => !w.TryGetTarget(out var t) || ReferenceEquals(t, v));
            if (Views.Count == 0)
            {
                _timer?.Stop();
                _timer = null;
            }
        }

        private static void Tick()
        {
            var now = AnimClock.Now;
            foreach (var w in Views.ToArray())
            {
                if (w.TryGetTarget(out var v))
                    v.DirectorTick(now);
            }
        }
    }
}

/// <summary>Pinta capas con el sombreado neo-skeuomórfico de los personajes.</summary>
internal readonly struct Painter(DrawingContext ctx, Color background, double time, double energy)
{
    private static readonly Color White = Color.FromRgb(255, 255, 255);

    public void Draw(Layer layer)
    {
        var m = Matrix.Identity;
        var angle = layer.Rotate;
        if (layer.Wiggle != 0 && energy > 0)
            angle += Math.Sin((time * 1.7 + layer.Phase) * Math.PI * 2) * layer.Wiggle * energy;
        if (angle != 0)
            m = Matrix.CreateTranslation(-layer.Pivot.X, -layer.Pivot.Y) * Matrix.CreateRotation(angle * Math.PI / 180) *
                Matrix.CreateTranslation(layer.Pivot.X, layer.Pivot.Y);
        if (layer.Bob != 0 && energy > 0)
            m *= Matrix.CreateTranslation(0, Math.Sin((time * 1.3 + layer.Phase) * Math.PI * 2) * layer.Bob * energy);

        using var _ = ctx.PushTransform(m);
        switch (layer.Paint)
        {
            case Paint.Body:
                Shadow(layer.Geo);
                Volume(layer.Geo, layer.Color);
                break;
            case Paint.Soft:
                Volume(layer.Geo, layer.Color, subtle: true);
                break;
            case Paint.Flat:
                ctx.DrawGeometry(new SolidColorBrush(layer.Color), null, layer.Geo);
                break;
            case Paint.Line:
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(layer.Color), layer.Width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), layer.Geo);
                break;
        }
    }

    /// <summary>Sombra difusa proyectada hacia abajo sobre la ficha o las capas de detrás.</summary>
    private void Shadow(Geometry g)
    {
        var color = ColorUtil.ShadowOf(background, 0.45);
        var bounds = g.Bounds.Inflate(14);
        using (ctx.PushTransform(Matrix.CreateTranslation(0, 3.5)))
        using (ctx.PushEffect(new BlurEffect { Radius = 7 }, bounds))
            ctx.DrawGeometry(new SolidColorBrush(color), null, g);
    }

    private void Volume(Geometry g, Color c, bool subtle = false)
    {
        var top = ColorUtil.Lighten(c, subtle ? 0.03 : 0.05);
        var bottom = ColorUtil.Darken(c, subtle ? 0.05 : 0.09);
        ctx.DrawGeometry(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(top, 0), new GradientStop(c, 0.45), new GradientStop(bottom, 1) },
        }, null, g);
        // Luz cenital, arriba a la izquierda.
        ctx.DrawGeometry(new RadialGradientBrush
        {
            Center = new RelativePoint(0.3, 0.12, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.3, 0.12, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ColorUtil.WithAlpha(White, subtle ? 0.18 : 0.34), 0),
                new GradientStop(ColorUtil.WithAlpha(White, 0), 1),
            },
        }, null, g);
        // Borde que gira hacia la sombra.
        ctx.DrawGeometry(new RadialGradientBrush
        {
            Center = new RelativePoint(0.42, 0.34, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.42, 0.34, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.78, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ColorUtil.WithAlpha(ColorUtil.Darken(c, 0.35), 0), 0.62),
                new GradientStop(ColorUtil.WithAlpha(ColorUtil.Darken(c, 0.35), subtle ? 0.18 : 0.32), 1),
            },
        }, null, g);
    }
}

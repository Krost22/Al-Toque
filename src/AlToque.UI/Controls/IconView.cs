using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace AlToque.Controls;

/// <summary>Íconos de trazo (estilo Lucide, licencia ISC) dibujados con 1,75 px a cualquier tamaño.</summary>
public enum Ic
{
    None,
    Settings,
    Close,
    Plus,
    Minus,
    Check,
    ChevronDown,
    ChevronRight,
    Folder,
    ShieldCheck,
    Shield,
    ShieldAlert,
    EyeOff,
    Users,
    UserPlus,
    Retry,
    Alert,
    Qr,
    Hash,
    External,
    Image,
    File,
    FileText,
    Video,
    Program,
    Music,
    Archive,
    Message,
    Clock,
    History,
    Power,
    Pointer,
    Volume,
    Sparkles,
    Languages,
    Info,
    Pause,
    Play,
    Copy,
    Help,
    Send,
    Star,
    Trash,
    Pencil,
    Wifi,
    Globe,
    Zap,
    Download,
    Monitor,
    Pin,
    PinOff,
}

public static class Icons
{
    private static readonly Dictionary<Ic, Geometry> Cache = [];

    private static string C(double cx, double cy, double r) => string.Create(CultureInfo.InvariantCulture,
        $"M{cx - r} {cy}A{r} {r} 0 1 0 {cx + r} {cy}A{r} {r} 0 1 0 {cx - r} {cy}Z");

    private static string R(double x, double y, double w, double h, double r) => string.Create(CultureInfo.InvariantCulture,
        $"M{x + r} {y}H{x + w - r}A{r} {r} 0 0 1 {x + w} {y + r}V{y + h - r}A{r} {r} 0 0 1 {x + w - r} {y + h}H{x + r}A{r} {r} 0 0 1 {x} {y + h - r}V{y + r}A{r} {r} 0 0 1 {x + r} {y}Z");

    private const string ShieldPath =
        "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z";

    private const string FilePath = "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z M14 2v4a2 2 0 0 0 2 2h4";

    /// <summary>Trazos en una caja de 24 × 24.</summary>
    public static string Data(Ic icon) => icon switch
    {
        Ic.Settings => "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z " + C(12, 12, 3),
        Ic.Close => "M18 6L6 18 M6 6L18 18",
        Ic.Plus => "M5 12h14 M12 5v14",
        Ic.Minus => "M5 12h14",
        Ic.Check => "M20 6L9 17l-5-5",
        Ic.ChevronDown => "M6 9l6 6l6-6",
        Ic.ChevronRight => "M9 18l6-6l-6-6",
        Ic.Folder => "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z",
        Ic.ShieldCheck => ShieldPath + " M9 12l2 2l4-4",
        Ic.Shield => ShieldPath,
        Ic.ShieldAlert => ShieldPath + " M12 8v4 M12 16h.01",
        Ic.EyeOff => "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49 M14.084 14.158a3 3 0 0 1-4.242-4.242 M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143 M2 2l20 20",
        Ic.Users => "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 " + C(9, 7, 4) + " M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75",
        Ic.UserPlus => "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 " + C(9, 7, 4) + " M19 8v6 M22 11h-6",
        Ic.Retry => "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5",
        Ic.Alert => "M21.73 18l-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3 M12 9v4 M12 17h.01",
        Ic.Qr => R(3, 3, 5, 5, 1) + " " + R(16, 3, 5, 5, 1) + " " + R(3, 16, 5, 5, 1) + " M21 16h-3a2 2 0 0 0-2 2v3 M21 21v.01 M12 7v3a2 2 0 0 1-2 2H7 M3 12h.01 M12 3h.01 M12 16v.01 M16 12h1 M21 12v.01 M12 21v-1",
        Ic.Hash => "M4 9h16 M4 15h16 M10 3L8 21 M16 3l-2 18",
        Ic.External => "M15 3h6v6 M10 14L21 3 M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",
        Ic.Image => R(3, 3, 18, 18, 2) + " " + C(9, 9, 2) + " M21 15l-3.086-3.086a2 2 0 0 0-2.828 0L6 21",
        Ic.File => FilePath,
        Ic.FileText => FilePath + " M10 9H8 M16 13H8 M16 17H8",
        Ic.Video => "M16 13l5.223 3.482a.5.5 0 0 0 .777-.416V7.87a.5.5 0 0 0-.752-.432L16 10.5 " + R(2, 6, 14, 12, 2),
        Ic.Program => R(2, 4, 20, 16, 2) + " M10 4v4 M2 8h20 M6 4v4",
        Ic.Music => "M9 18V5l12-2v13 " + C(6, 18, 3) + " " + C(18, 16, 3),
        Ic.Archive => R(2, 3, 20, 5, 1) + " M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8 M10 12h4",
        Ic.Message => "M7.9 20A9 9 0 1 0 4 16.1L2 22Z M8 12h.01 M12 12h.01 M16 12h.01",
        Ic.Clock => C(12, 12, 10) + " M12 6v6l4 2",
        Ic.History => "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5 M12 7v5l4 2",
        Ic.Power => "M12 2v10 M18.4 6.6a9 9 0 1 1-12.77.04",
        Ic.Pointer => "M9 9l5 12l1.8-5.2L21 14Z M7.2 2.2L8 5.1 M5.1 8l-2.9-.8 M14 4.1L12 6 M6 12l-1.9 2",
        Ic.Volume => "M11 4.702a.705.705 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.705.705 0 0 0 11 19.298z M16 9a5 5 0 0 1 0 6 M19.364 18.364a9 9 0 0 0 0-12.728",
        Ic.Sparkles => "M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z M20 3v4 M22 5h-4 M4 17v2 M5 18H3",
        Ic.Languages => "M5 8l6 6 M4 14l6-6l2-3 M2 5h12 M7 2h1 M22 22l-5-10l-5 10 M14 18h6",
        Ic.Info => C(12, 12, 10) + " M12 16v-4 M12 8h.01",
        Ic.Pause => R(14, 4, 4, 16, 1) + " " + R(6, 4, 4, 16, 1),
        Ic.Play => "M6 3L20 12L6 21Z",
        Ic.Copy => R(8, 8, 14, 14, 2) + " M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2",
        Ic.Help => C(12, 12, 10) + " M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3 M12 17h.01",
        Ic.Send => "M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z M21.854 2.147l-10.94 10.939",
        Ic.Star => "M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z",
        Ic.Trash => "M3 6h18 M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6 M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2 M10 11v6 M14 11v6",
        Ic.Pin => "M12 17v5 M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z",
        Ic.PinOff => "M12 17v5 M15 9.34V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H7.89 M2 2l20 20 M9 9v1.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h11",
        Ic.Pencil => "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z M15 5l4 4",
        Ic.Wifi => "M12 20h.01 M2 8.82a15 15 0 0 1 20 0 M5 12.859a10 10 0 0 1 14 0 M8.5 16.429a5 5 0 0 1 7 0",
        Ic.Globe => C(12, 12, 10) + " M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20 M2 12h20",
        Ic.Zap => "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
        Ic.Download => "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5l5-5 M12 15V3",
        Ic.Monitor => R(2, 3, 20, 14, 2) + " M8 21h8 M12 17v4",
        _ => "",
    };

    public static Geometry? Get(Ic icon)
    {
        if (icon == Ic.None)
            return null;
        lock (Cache)
        {
            if (!Cache.TryGetValue(icon, out var g))
                Cache[icon] = g = Geometry.Parse(Data(icon));
            return g;
        }
    }
}

/// <summary>Dibuja un ícono de <see cref="Icons"/> con el color de texto heredado (o <see cref="Brush"/>).</summary>
public sealed class IconView : Control
{
    public static readonly StyledProperty<Ic> KindProperty = AvaloniaProperty.Register<IconView, Ic>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<IconView>();

    public static readonly StyledProperty<double> StrokeProperty =
        AvaloniaProperty.Register<IconView, double>(nameof(Stroke), 1.75);

    static IconView()
    {
        AffectsRender<IconView>(KindProperty, ForegroundProperty, StrokeProperty);
        WidthProperty.OverrideDefaultValue<IconView>(16);
        HeightProperty.OverrideDefaultValue<IconView>(16);
    }

    public Ic Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Icons.Get(Kind) is not { } geometry || Foreground is not { } brush)
            return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;
        var scale = size / 24;
        var dx = (Bounds.Width - size) / 2;
        var dy = (Bounds.Height - size) / 2;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
            context.DrawGeometry(null, new Pen(brush, Stroke / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }
}

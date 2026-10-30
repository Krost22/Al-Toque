using AlToque.Controls;
using AlToque.Chasqui;
using AlToque.Controls.Avatars;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

// Uso: AlToque.Gallery <carpeta-salida> [escena…]
// Dibuja escenas sin ventana real (Avalonia.Headless + Skia) y las guarda como PNG para revisar el diseño.
AppBuilder.Configure<AlToque.App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia()
    .SetupWithoutStarting();

var outDir = args.Length > 0 ? args[0] : "gallery-out";
Directory.CreateDirectory(outDir);
var wanted = args.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);
AnimClock.Frozen = 1000;

var scenes = new Dictionary<string, Func<(Control Content, double W, double H)>>(StringComparer.OrdinalIgnoreCase)
{
    ["avatares"] = AvatarSheet,
    ["animos"] = MoodSheet,
    ["chasqui"] = ChasquiSheet,
    ["pista"] = TrackSheet,
    ["chasqui-tira"] = ChasquiStrip,
};

foreach (var (name, build) in scenes)
{
    if (wanted.Count > 0 && !wanted.Contains(name))
        continue;
    foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
    {
        var (content, w, h) = build();
        var win = new Window
        {
            Width = w,
            Height = h,
            Content = content,
            RequestedThemeVariant = theme,
        };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = win.CaptureRenderedFrame();
        var file = Path.Combine(outDir, $"{name}-{(theme == ThemeVariant.Dark ? "oscuro" : "claro")}.png");
        frame?.Save(file);
        Console.WriteLine(file);
        win.Close();
    }
}

static string[] Palette() =>
[
    "#2AA05A", "#C8285E", "#C0682A", "#2A8AB0", "#9A3AB8", "#8A8A22", "#B83A3A", "#2E9E8E", "#6F8F2A", "#B05A9A",
    "#3A7FC0", "#C09A2A",
];

static (Control, double, double) AvatarSheet()
{
    var wrap = new WrapPanel { Margin = new Thickness(24), ItemWidth = 132, ItemHeight = 150 };
    var colors = Palette();
    var i = 0;
    foreach (var id in AvatarCatalog.Ids)
    {
        var cell = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        cell.Children.Add(new AvatarView { AvatarId = id, ColorHex = colors[i % colors.Length], Initial = "E", Width = 104, Height = 104, Live = false });
        cell.Children.Add(new TextBlock { Text = AvatarCatalog.NameOf(id), HorizontalAlignment = HorizontalAlignment.Center });
        wrap.Children.Add(cell);
        i++;
    }
    return (new Border { Child = wrap }, 7 * 132 + 48, 4 * 150 + 48);
}

static (Control, double, double) MoodSheet()
{
    var grid = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
    var moods = Enum.GetValues<AvatarMood>();
    string[] ids = ["gato", "rana", "pinguino", "robot", "fantasma"];
    var colors = Palette();
    var row0 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
    foreach (var m in moods)
        row0.Children.Add(new TextBlock { Text = m.ToString(), Width = 88, TextAlignment = TextAlignment.Center });
    grid.Children.Add(row0);
    var k = 0;
    foreach (var id in ids)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var m in moods)
            row.Children.Add(new AvatarView { AvatarId = id, ColorHex = colors[k % colors.Length], Mood = m, Width = 88, Height = 88, Live = false });
        grid.Children.Add(row);
        k += 3;
    }
    var rings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
    foreach (var ring in Enum.GetValues<AvatarRing>())
    {
        var cell = new StackPanel { Spacing = 4 };
        cell.Children.Add(new AvatarView { AvatarId = "oso", ColorHex = "#2A8AB0", Ring = ring, Width = 88, Height = 88, Live = false });
        cell.Children.Add(new TextBlock { Text = ring.ToString(), HorizontalAlignment = HorizontalAlignment.Center });
        rings.Children.Add(cell);
    }
    rings.Children.Add(new AvatarView { AvatarId = "perro", ColorHex = "#C0682A", Dimmed = true, Width = 88, Height = 88, Live = false });
    var small = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
    foreach (var sz in new[] { 24, 32, 40, 56 })
        small.Children.Add(new AvatarView { AvatarId = "zorro", ColorHex = "#2AA05A", Ring = AvatarRing.Verified, Width = sz, Height = sz, Live = false, VerticalAlignment = VerticalAlignment.Center });
    rings.Children.Add(small);
    grid.Children.Add(rings);
    return (new Border { Child = grid }, moods.Length * 96 + 48, 6 * 104 + 180);
}

// Fotogramas de cada estado de Chasqui: cada fila es un estado y cada columna un instante después de entrar.
static (Control, double, double) ChasquiSheet()
{
    var states = Enum.GetValues<ChasquiState>();
    double[] times = [0.05, 0.2, 0.4, 0.6, 0.85, 1.0, 1.3, 2.0];
    var grid = new Grid { Margin = new Thickness(16) };
    grid.ColumnDefinitions.Add(new ColumnDefinition(110, GridUnitType.Pixel));
    foreach (var _ in times)
        grid.ColumnDefinitions.Add(new ColumnDefinition(96, GridUnitType.Pixel));
    for (int r = 0; r < states.Length; r++)
    {
        grid.RowDefinitions.Add(new RowDefinition(96, GridUnitType.Pixel));
        var label = new TextBlock { Text = states[r].ToString(), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(label, r);
        grid.Children.Add(label);
        for (int c = 0; c < times.Length; c++)
        {
            var view = new FrozenChasqui(states[r], times[c]) { Width = 80, Height = 80 };
            Grid.SetRow(view, r);
            Grid.SetColumn(view, c + 1);
            grid.Children.Add(view);
        }
    }
    return (grid, 110 + times.Length * 96 + 32, states.Length * 96 + 32);
}

// Una fila con los momentos más expresivos de Chasqui, para la documentación.
static (Control, double, double) ChasquiStrip()
{
    (ChasquiState State, double At, string Label)[] moments =
    [
        (ChasquiState.Idle, 0.6, "Reposo"), (ChasquiState.Attentive, 0.45, "Atento"), (ChasquiState.Searching, 0.9, "Buscando"),
        (ChasquiState.Sending, 1.2, "Enviando"), (ChasquiState.Success, 0.98, "Éxito"), (ChasquiState.Rejected, 0.8, "Rechazado"),
        (ChasquiState.Error, 0.35, "Error"),
    ];
    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16) };
    foreach (var (state, at, label) in moments)
    {
        var cell = new StackPanel { Spacing = 2 };
        cell.Children.Add(new FrozenChasqui(state, at) { Width = 120, Height = 120 });
        cell.Children.Add(new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "meta" } });
        row.Children.Add(cell);
    }
    return (row, moments.Length * 128 + 32, 180);
}

static (Control, double, double) TrackSheet()
{
    var panel = new StackPanel { Margin = new Thickness(24), Spacing = 18, Width = 460 };
    foreach (var (mode, progress, at) in new[] { (TrackMode.Waiting, 0.0, 0.3), (TrackMode.Active, 0.63, 0.5), (TrackMode.Frozen, 0.63, 0.5), (TrackMode.Done, 1.0, 0.15), (TrackMode.Done, 1.0, 0.5), (TrackMode.Done, 1.0, 1.0) })
    {
        AnimClock.Frozen = 1000;
        var track = new TransferTrack { Mode = mode, Progress = progress, EndColor = "#2AA05A" };
        AnimClock.Frozen = 1000 + at;
        panel.Children.Add(new TextBlock { Text = $"{mode} · {progress:P0} · t={at}s" });
        panel.Children.Add(new Border { Classes = { "panel" }, Padding = new Thickness(16, 8), Child = track });
    }
    return (panel, 520, 640);
}

/// <summary>Chasqui con su propio reloj: entra en el estado en 1000 s y se dibuja en 1000 + t.</summary>
sealed class FrozenChasqui : Control
{
    private readonly ChasquiView _view;
    private readonly double _t;

    public FrozenChasqui(ChasquiState state, double t)
    {
        _t = t;
        AnimClock.Frozen = 1000;
        _view = new ChasquiView { State = state };
        VisualChildren.Add(_view);
        LogicalChildren.Add(_view);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _view.Arrange(new Rect(finalSize));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        AnimClock.Frozen = 1000 + _t;
        base.Render(context);
    }
}

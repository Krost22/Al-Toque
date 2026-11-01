using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;

namespace AlToque.Views;

/// <summary>
/// Un chat fijado en la esquina de la pantalla: siempre a la vista, encima de las demás ventanas, hasta que se minimiza
/// (queda en la barra de tareas) o se devuelve al panel. Cerrarlo con Alt+F4 también lo minimiza.
/// </summary>
public partial class ChatWindow : Window
{
    private readonly ChatView _chat;
    private bool _moving;

    public ChatWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FloatingWindow.ApplyChrome(this, this.FindControl<Border>("Root")!);
        _chat = this.FindControl<ChatView>("Chat")!;
        _chat.HeaderPressed += e =>
        {
            _moving = true;
            BeginMoveDrag(e);
        };
        _chat.FilesDropped += paths => FilesDropped?.Invoke(paths);
        _chat.ImagePasted += bitmap => ImagePasted?.Invoke(bitmap);
        Grip("GripTop", WindowEdge.North);
        Grip("GripLeft", WindowEdge.West);
        Grip("GripCorner", WindowEdge.NorthWest);
        PointerReleased += (_, _) => _moving = false;
        PositionChanged += (_, _) =>
        {
            if (!_moving)
                return;
            UserPlaced = true;
            PlacementChanged?.Invoke();
        };
        SizeChanged += (_, e) =>
        {
            if (_moving)
                PlacementChanged?.Invoke();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                MinimizeRequested?.Invoke();
        };
    }

    public ChatViewModel? Vm => DataContext as ChatViewModel;

    /// <summary>Lo movieron a mano: ya no se recoloca en la esquina.</summary>
    public bool UserPlaced { get; set; }

    public bool IsMinimized => WindowState == WindowState.Minimized;

    /// <summary>Se cierra de verdad (se devolvió al panel o la app termina); si no, cerrar la minimiza.</summary>
    public bool AllowClose { get; set; }

    public event Action<IReadOnlyList<string>>? FilesDropped;
    public event Action<Bitmap>? ImagePasted;
    public event Action? MinimizeRequested;

    /// <summary>La movieron o le cambiaron el tamaño: hay que recordarlo.</summary>
    public event Action? PlacementChanged;

    public void FocusComposer() => _chat.FocusComposer();

    private void Grip(string name, WindowEdge edge) =>
        this.FindControl<Border>(name)!.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            _moving = true;
            BeginResizeDrag(edge, e);
            e.Handled = true;
        };

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose && !AppController.IsQuitting)
        {
            e.Cancel = true;
            MinimizeRequested?.Invoke();
        }
        base.OnClosing(e);
    }
}

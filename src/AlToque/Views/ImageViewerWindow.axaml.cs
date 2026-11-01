using AlToque.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AlToque.Views;

/// <summary>Las fotos del chat en grande, sobre toda el área de trabajo de la pantalla. Esc o clic en el fondo cierran.</summary>
public partial class ImageViewerWindow : Window
{
    public ImageViewerWindow()
    {
        AvaloniaXamlLoader.Load(this);
        var backdrop = this.FindControl<Grid>("Backdrop")!;
        var picture = this.FindControl<Image>("Picture")!;
        backdrop.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is null && !IsOnPicture(picture, e))
                Close();
        };
        // Antes que los botones: las flechas pasan de foto en vez de mover el foco.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    break;
                case Key.Left:
                    (DataContext as ImageViewerViewModel)?.Go(-1);
                    break;
                case Key.Right or Key.Space:
                    (DataContext as ImageViewerViewModel)?.Go(+1);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>El clic cayó sobre la foto dibujada (no sobre el margen vacío del control).</summary>
    private static bool IsOnPicture(Image picture, PointerPressedEventArgs e)
    {
        if (picture.Source is not { } source)
            return false;
        var bounds = picture.Bounds.Size;
        var size = source.Size;
        var scale = Math.Min(1, Math.Min(bounds.Width / size.Width, bounds.Height / size.Height));
        var drawn = new Size(size.Width * scale, size.Height * scale);
        var origin = new Point((bounds.Width - drawn.Width) / 2, (bounds.Height - drawn.Height) / 2);
        return new Rect(origin, drawn).Contains(e.GetPosition(picture));
    }

    /// <summary>Cubre el área de trabajo de la pantalla donde está <paramref name="near"/> (sin tapar la barra de tareas).</summary>
    public void CoverScreenOf(Window? near)
    {
        var screen = (near is not null ? near.Screens.ScreenFromWindow(near) : null) ?? Screens.Primary;
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        Position = area.Position;
        Width = area.Width / screen.Scaling;
        Height = area.Height / screen.Scaling;
    }

    /// <summary>Lado mayor que vale la pena decodificar: el de la pantalla, en píxeles reales.</summary>
    public static int MaxSideFor(Window? near)
    {
        var screen = near?.Screens.ScreenFromWindow(near) ?? near?.Screens.Primary;
        return screen is null ? 2560 : Math.Clamp(Math.Max(screen.Bounds.Width, screen.Bounds.Height), 1024, 4096);
    }
}

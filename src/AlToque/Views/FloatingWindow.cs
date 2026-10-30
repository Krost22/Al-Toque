using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AlToque.Views;

/// <summary>Ventanas flotantes sin bordes (panel, Chasqui, solicitudes): sombra suave y posición junto a la bandeja.</summary>
public static class FloatingWindow
{
    public const double ShadowMargin = 12;

    /// <param name="shadowOnRoot">Falso cuando el contenido son varias tarjetas con su propia sombra.</param>
    public static void ApplyChrome(Window window, Border root, bool shadowOnRoot = true)
    {
        if (OperatingSystem.IsWindows())
        {
            // Fondo transparente para dibujar esquinas redondeadas y una sombra propia.
            window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            window.Background = Brushes.Transparent;
            root.Margin = new Thickness(ShadowMargin);
            if (shadowOnRoot)
                root.BoxShadow = BoxShadows.Parse("0 8 28 0 #38000000");
        }
        else
        {
            // Sin compositor garantizado (X11/Wayland): fondo sólido.
            window.Bind(Window.BackgroundProperty, window.GetResourceObservable("SurfaceBrush"));
            root.CornerRadius = new CornerRadius(0);
            root.BorderThickness = new Thickness(1);
        }
    }

    /// <summary>Esquina inferior derecha del área de trabajo (encima de la barra de tareas), desplazada hacia arriba; en macOS, la superior.</summary>
    public static void PlaceBottomRight(Window window, double offsetAboveDip, double offsetLeftDip = 0)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null)
            return;
        var size = window.Bounds.Size;
        if (size.Width < 1 || size.Height < 1)
        {
            window.Measure(new Size(double.IsNaN(window.Width) ? double.PositiveInfinity : window.Width, double.PositiveInfinity));
            size = window.DesiredSize;
        }
        var scale = screen.Scaling;
        var area = screen.WorkingArea;
        var gap = OperatingSystem.IsWindows() ? 0 : (int)(8 * scale);
        var x = area.Right - (int)Math.Ceiling(size.Width * scale) - gap - (int)(offsetLeftDip * scale);
        // En macOS el ícono vive en la barra de menús, arriba: las ventanas bajan desde la esquina superior derecha.
        var y = OperatingSystem.IsMacOS()
            ? area.Y + gap + (int)(offsetAboveDip * scale)
            : area.Bottom - (int)Math.Ceiling(size.Height * scale) - gap - (int)(offsetAboveDip * scale);
        window.Position = new PixelPoint(Math.Max(area.X, x), Math.Max(area.Y, y));
    }

    public static double HeightOf(Window window) => window.IsVisible ? window.Bounds.Height : 0;
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AlToque.Views;

/// <summary>
/// Bordes y esquinas para cambiar el tamaño de una ventana sin marco del sistema (<c>WindowDecorations="None"</c>).
/// Cerca del borde de la tarjeta el cursor cambia y arrastrar estira o encoge la ventana, como en cualquier otra.
/// </summary>
public static class ResizeGrips
{
    private const double Inside = 7;
    private const double Corner = 16;

    /// <param name="root">La tarjeta visible (en Windows tiene alrededor un margen transparente para la sombra).</param>
    /// <param name="started">Se llama al empezar a arrastrar un borde.</param>
    public static void Attach(Window window, Control root, Action? started = null)
    {
        Cursor? current = null;
        window.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
                return;
            var edge = EdgeAt(window, root, e.GetPosition(window));
            var cursor = edge is { } ed ? CursorFor(ed) : null;
            if (cursor?.ToString() == current?.ToString())
                return;
            current = cursor;
            window.Cursor = cursor;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        window.AddHandler(InputElement.PointerExitedEvent, (_, _) =>
        {
            current = null;
            window.Cursor = null;
        }, RoutingStrategies.Direct);

        window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!window.CanResize || !e.GetCurrentPoint(window).Properties.IsLeftButtonPressed
                || EdgeAt(window, root, e.GetPosition(window)) is not { } edge)
                return;
            started?.Invoke();
            window.BeginResizeDrag(edge, e);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    private static WindowEdge? EdgeAt(Window window, Control root, Point p)
    {
        if (!window.CanResize || window.WindowState != WindowState.Normal)
            return null;
        // Los bordes de la tarjeta, dentro de la ventana (más el margen de la sombra, si lo hay).
        var box = root.Bounds;
        double left = box.X, top = box.Y, right = box.Right, bottom = box.Bottom;
        if (p.X < left - FloatingWindow.ShadowMargin || p.Y < top - FloatingWindow.ShadowMargin
            || p.X > right + FloatingWindow.ShadowMargin || p.Y > bottom + FloatingWindow.ShadowMargin)
            return null;
        bool w = p.X < left + Inside, e = p.X > right - Inside, n = p.Y < top + Inside, s = p.Y > bottom - Inside;
        bool wc = p.X < left + Corner, ec = p.X > right - Corner, nc = p.Y < top + Corner, sc = p.Y > bottom - Corner;
        if ((n && wc) || (w && nc))
            return WindowEdge.NorthWest;
        if ((n && ec) || (e && nc))
            return WindowEdge.NorthEast;
        if ((s && wc) || (w && sc))
            return WindowEdge.SouthWest;
        if ((s && ec) || (e && sc))
            return WindowEdge.SouthEast;
        if (n)
            return WindowEdge.North;
        if (s)
            return WindowEdge.South;
        if (w)
            return WindowEdge.West;
        if (e)
            return WindowEdge.East;
        return null;
    }

    private static Cursor CursorFor(WindowEdge edge) => new(edge switch
    {
        WindowEdge.North or WindowEdge.South => StandardCursorType.SizeNorthSouth,
        WindowEdge.West or WindowEdge.East => StandardCursorType.SizeWestEast,
        WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
        WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
        WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
        _ => StandardCursorType.BottomRightCorner,
    });
}

/// <summary>Que ninguna ventana quede más grande que la pantalla en la que está (portátil después de un monitor grande).</summary>
public static class ScreenFit
{
    /// <summary>Lo que cabe en la pantalla de la ventana, en DIP (menos un respiro).</summary>
    public static Size Available(Window window, double gap = 12)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null)
            return new Size(double.PositiveInfinity, double.PositiveInfinity);
        var area = screen.WorkingArea;
        return new Size(Math.Max(200, area.Width / screen.Scaling - gap), Math.Max(200, area.Height / screen.Scaling - gap));
    }

    // Los máximos que puso el diseño (por ejemplo MaxWidth del panel), para no pisarlos con los de la pantalla.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, Tuple<double, double>> Design = new();

    /// <summary>Al abrir (y al pasar a otra pantalla): si no cabe, se achica y vuelve a quedar dentro.</summary>
    public static void Attach(Window window)
    {
        Design.AddOrUpdate(window, Tuple.Create(window.MaxWidth, window.MaxHeight));
        window.Opened += (_, _) => Fit(window);
        window.ScalingChanged += (_, _) => Fit(window);
    }

    public static void Fit(Window window)
    {
        var max = Available(window, window.WindowDecorations == WindowDecorations.None ? 12 : 48);
        if (Design.TryGetValue(window, out var design))
            max = new Size(Math.Min(max.Width, design.Item1), Math.Min(max.Height, design.Item2));
        window.MaxHeight = max.Height;
        window.MaxWidth = max.Width;
        var resized = false;
        if (window.Bounds.Height > max.Height + 1 || (!double.IsNaN(window.Height) && window.Height > max.Height))
        {
            window.SizeToContent = window.SizeToContent == SizeToContent.WidthAndHeight ? SizeToContent.Width : SizeToContent.Manual;
            window.Height = max.Height;
            resized = true;
        }
        if (window.Bounds.Width > max.Width + 1 || (!double.IsNaN(window.Width) && window.Width > max.Width))
        {
            window.Width = max.Width;
            resized = true;
        }
        if (resized || !IsInside(window))
            KeepInside(window);
    }

    private static bool IsInside(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null)
            return true;
        var area = screen.WorkingArea;
        var w = (int)(window.Bounds.Width * screen.Scaling);
        var h = (int)(window.Bounds.Height * screen.Scaling);
        return window.Position.X >= area.X && window.Position.Y >= area.Y && window.Position.X + w <= area.Right && window.Position.Y + h <= area.Bottom;
    }

    /// <summary>Corre la ventana para que se vea entera en su pantalla.</summary>
    public static void KeepInside(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        var w = (int)Math.Ceiling((double.IsNaN(window.Width) ? window.Bounds.Width : window.Width) * screen.Scaling);
        var h = (int)Math.Ceiling((double.IsNaN(window.Height) ? window.Bounds.Height : window.Height) * screen.Scaling);
        var x = Math.Clamp(window.Position.X, area.X, Math.Max(area.X, area.Right - w));
        var y = Math.Clamp(window.Position.Y, area.Y, Math.Max(area.Y, area.Bottom - h));
        if (x != window.Position.X || y != window.Position.Y)
            window.Position = new PixelPoint(x, y);
    }
}

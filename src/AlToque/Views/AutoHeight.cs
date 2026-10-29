using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AlToque.Views;

/// <summary>
/// Mantiene una ventana ajustada al alto de su contenido. <see cref="SizeToContent"/> deja de aplicarse en algunas
/// plataformas (X11) después del primer evento de configuración; aquí se mide fuera del ciclo de layout y se fija
/// el alto cada vez que cambia el modelo de vista o cuando se pide con <see cref="Refit"/>.
/// </summary>
public static class AutoHeight
{
    private sealed class State
    {
        public bool Pending;
    }

    private static readonly ConditionalWeakTable<Window, State> States = new();

    public static void Attach(Window window)
    {
        INotifyPropertyChanged? current = null;
        void OnChanged(object? sender, PropertyChangedEventArgs e) => Refit(window);

        window.DataContextChanged += (_, _) =>
        {
            if (current is not null)
                current.PropertyChanged -= OnChanged;
            current = window.DataContext as INotifyPropertyChanged;
            if (current is not null)
                current.PropertyChanged += OnChanged;
        };
        window.Opened += (_, _) => Refit(window);
    }

    /// <summary>Vuelve a medir en cuanto el hilo de la interfaz quede libre (agrupa varias peticiones).</summary>
    public static void Refit(Window window)
    {
        var state = States.GetOrCreateValue(window);
        if (state.Pending)
            return;
        state.Pending = true;
        Dispatcher.UIThread.Post(() =>
        {
            state.Pending = false;
            if (!window.IsVisible || window.Content is not Control content || window.ClientSize.Width <= 0)
                return;
            content.Measure(new Size(window.ClientSize.Width, double.PositiveInfinity));
            var desired = Math.Ceiling(content.DesiredSize.Height);
            if (desired > 0 && Math.Abs(desired - window.ClientSize.Height) > 1)
            {
                window.SizeToContent = SizeToContent.Manual;
                window.Height = desired;
            }
        }, DispatcherPriority.Background);
    }
}

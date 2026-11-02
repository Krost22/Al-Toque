using AlToque.Controls;
using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

public partial class TransfersWindow : Window
{
    public TransfersWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FloatingWindow.ApplyChrome(this, this.FindControl<Border>("Root")!, shadowOnRoot: false);
        AutoHeight.Attach(this);
        SizeChanged += (_, _) => LayoutChanged?.Invoke();
        // Cada tarjeta nueva entra desde abajo (16 px + opacidad, 250 ms).
        this.FindControl<ItemsControl>("Cards")!.ContainerPrepared += (_, e) => _ = Fx.Enter(e.Container, dy: 16);
        this.FindControl<ItemsControl>("GroupCards")!.ContainerPrepared += (_, e) => _ = Fx.Enter(e.Container, dy: 16);
    }

    /// <summary>Cambió el tamaño: hay que recolocar las ventanas apiladas.</summary>
    public event Action? LayoutChanged;

    /// <summary>La tarjeta sale (167 ms, curva Salir) antes de quitarla de la lista.</summary>
    public Task AnimateOutAsync(object vm)
    {
        var cards = this.FindControl<ItemsControl>(vm is GroupSendViewModel ? "GroupCards" : "Cards")!;
        var container = cards.ContainerFromItem(vm);
        return container is null || !IsVisible ? Task.CompletedTask : Fx.Exit(container);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AppController.IsQuitting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}

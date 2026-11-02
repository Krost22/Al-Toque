using AlToque.Controls;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

/// <summary>Aviso flotante de una solicitud de conexión de un equipo cercano.</summary>
public partial class PairRequestWindow : Window
{
    public PairRequestWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FloatingWindow.ApplyChrome(this, this.FindControl<Border>("Root")!);
        AutoHeight.Attach(this);
        SizeChanged += (_, _) => LayoutChanged?.Invoke();
    }

    public event Action? LayoutChanged;

    /// <summary>Con el ratón encima no se retira sola.</summary>
    public bool IsHovered => this.FindControl<Border>("Root")?.IsPointerOver == true;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (this.FindControl<Border>("Root") is { } root)
            _ = Fx.Pop(root, from: 0.85, ms: 400);
    }
}

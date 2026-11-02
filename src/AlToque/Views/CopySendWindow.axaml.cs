using AlToque.Controls;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

/// <summary>Tarjeta flotante de «Enviar lo copiado»; no roba el foco a la app donde se copió.</summary>
public partial class CopySendWindow : Window
{
    public CopySendWindow()
    {
        AvaloniaXamlLoader.Load(this);
        var root = this.FindControl<Border>("Root")!;
        FloatingWindow.ApplyChrome(this, root);
        AutoHeight.Attach(this);
        SizeChanged += (_, _) => LayoutChanged?.Invoke();
    }

    public event Action? LayoutChanged;

    /// <summary>Con el ratón encima no se retira sola.</summary>
    public bool IsHovered => this.FindControl<Border>("Root")?.IsPointerOver == true;

    public void Pop()
    {
        if (this.FindControl<Border>("Root") is { } root)
            _ = Fx.Pop(root, from: 0.9, ms: 300);
    }
}

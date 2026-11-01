using AlToque.Controls;
using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AlToque.Views;

/// <summary>Tarjeta flotante de un mensaje nuevo: clic o «Responder» abren el chat.</summary>
public partial class MessageToastWindow : Window
{
    public MessageToastWindow()
    {
        AvaloniaXamlLoader.Load(this);
        var root = this.FindControl<Border>("Root")!;
        FloatingWindow.ApplyChrome(this, root);
        AutoHeight.Attach(this);
        SizeChanged += (_, _) => LayoutChanged?.Invoke();
        root.PointerPressed += (_, e) =>
        {
            if (e.Source is Control c && c.FindAncestorOfType<Button>(includeSelf: true) is null && DataContext is MessageToastViewModel vm)
                vm.Reply.Execute(null);
        };
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

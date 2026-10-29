using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

public partial class TransfersWindow : Window
{
    public TransfersWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FloatingWindow.ApplyChrome(this, this.FindControl<Border>("Root")!);
        AutoHeight.Attach(this);
        SizeChanged += (_, _) => LayoutChanged?.Invoke();
    }

    /// <summary>Cambió el tamaño: hay que recolocar las ventanas apiladas.</summary>
    public event Action? LayoutChanged;

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

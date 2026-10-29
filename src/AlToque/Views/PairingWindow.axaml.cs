using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

public partial class PairingWindow : Window
{
    public PairingWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AutoHeight.Attach(this);
    }
}

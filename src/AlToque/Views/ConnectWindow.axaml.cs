using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

public partial class ConnectWindow : Window
{
    public ConnectWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AutoHeight.Attach(this);
        Opened += (_, _) => this.FindControl<TextBox>("InputBox")?.Focus();
    }
}

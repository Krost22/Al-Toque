using AlToque.Chasqui;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

/// <summary>Primera ejecución: Chasqui se presenta y pide el nombre con el que te verán.</summary>
public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            this.FindControl<ChasquiView>("Chasqui")!.State = ChasquiState.Incoming;
            var box = this.FindControl<TextBox>("AliasBox")!;
            box.Focus();
            box.SelectAll();
        };
    }

    public string Alias
    {
        get => this.FindControl<TextBox>("AliasBox")!.Text ?? "";
        set => this.FindControl<TextBox>("AliasBox")!.Text = value;
    }

    public bool ReduceMotion
    {
        set => this.FindControl<ChasquiView>("Chasqui")!.ReduceMotion = value;
    }

    public event Action<string>? Started;

    private void OnStart(object? sender, RoutedEventArgs e)
    {
        Started?.Invoke(Alias);
        Close();
    }
}

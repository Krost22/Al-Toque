using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

/// <summary>Confirmación sí/no (abrir un programa recibido, restablecer la identidad).</summary>
public partial class ConfirmWindow : Window
{
    private readonly TaskCompletionSource<bool> _result = new();

    public ConfirmWindow() => AvaloniaXamlLoader.Load(this);

    public static Task<bool> AskAsync(string message)
    {
        var w = new ConfirmWindow();
        w.FindControl<TextBlock>("MessageText")!.Text = message;
        w.Closed += (_, _) => w._result.TrySetResult(false);
        w.Show();
        w.Activate();
        return w._result.Task;
    }

    private void OnYes(object? sender, RoutedEventArgs e)
    {
        _result.TrySetResult(true);
        Close();
    }

    private void OnNo(object? sender, RoutedEventArgs e)
    {
        _result.TrySetResult(false);
        Close();
    }
}

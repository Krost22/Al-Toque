using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

/// <summary>Pide un texto corto (el nombre de un grupo). Devuelve null si se cancela.</summary>
public partial class AskTextWindow : Window
{
    private readonly TaskCompletionSource<string?> _result = new();

    public AskTextWindow() => AvaloniaXamlLoader.Load(this);

    public static Task<string?> AskAsync(string title, string okLabel, string? initial = null, string? placeholder = null)
    {
        var w = new AskTextWindow();
        w.FindControl<TextBlock>("TitleText")!.Text = title;
        w.FindControl<Button>("OkButton")!.Content = okLabel;
        var input = w.FindControl<TextBox>("Input")!;
        input.Text = initial ?? "";
        input.PlaceholderText = placeholder;
        w.Closed += (_, _) => w._result.TrySetResult(null);
        w.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        w.Show();
        w.Activate();
        return w._result.Task;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var text = this.FindControl<TextBox>("Input")!.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return;
        _result.TrySetResult(text);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result.TrySetResult(null);
        Close();
    }
}

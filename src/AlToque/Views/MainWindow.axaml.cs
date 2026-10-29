using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace AlToque.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    private void OnAliasLostFocus(object? sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.CommitAlias();

    private async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
            vm.Folder = path;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        (DataContext as MainViewModel)?.CommitAlias();
        base.OnClosing(e);
    }
}

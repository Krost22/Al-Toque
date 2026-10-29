using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace AlToque.Views;

public partial class OfferWindow : Window
{
    public OfferWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FloatingWindow.ApplyChrome(this, this.FindControl<Border>("Root")!);
        AutoHeight.Attach(this);
        SizeChanged += (_, _) => LayoutChanged?.Invoke();
    }

    public event Action? LayoutChanged;

    private async void OnChangeFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OfferViewModel vm)
            return;
        var start = await StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(Directory.Exists(vm.Folder) ? vm.Folder : Path.GetDirectoryName(vm.Folder) ?? vm.Folder)));
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, SuggestedStartLocation = start });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
            vm.Folder = path;
    }
}

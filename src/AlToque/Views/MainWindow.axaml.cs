using System.ComponentModel;
using AlToque.Controls;
using AlToque.Controls.Avatars;
using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace AlToque.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as MainViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnVmChanged;
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Tab):
                // Cambio de página: la nueva sube 16 px con un fundido.
                var page = _vm?.Tab switch { 0 => "PeersPage", 1 => "HistoryPage", 2 => "SettingsPage", _ => "AboutPage" };
                if (this.FindControl<Control>(page) is { } target)
                    _ = Fx.Enter(target, dy: 16);
                this.FindControl<ScrollViewer>("Pages")?.ScrollToHome();
                break;
            case nameof(MainViewModel.MyAvatarId):
                this.FindControl<AvatarView>("MyAvatar")?.Celebrate();
                break;
            case nameof(MainViewModel.ShowAvatarPicker) when _vm?.ShowAvatarPicker == true:
                if (this.FindControl<Control>("AvatarPicker") is { } picker)
                    _ = Fx.Enter(picker, dy: 10);
                break;
        }
    }

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

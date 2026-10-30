using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AlToque.Mobile.Views;

public partial class MobileView : UserControl
{
    private TopLevel? _top;
    private IInsetsManager? _insets;

    public MobileView()
    {
        InitializeComponent();
        Scrim.PointerPressed += (_, e) =>
        {
            if (DataContext is MobileViewModel { HasOffer: false } vm)
            {
                vm.CloseSheet.Execute(null);
                e.Handled = true;
            }
        };
    }

    private MobileViewModel? ViewModel => DataContext as MobileViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        if (_top is null)
            return;
        _top.BackRequested += OnBackRequested;
        _insets = _top.InsetsManager;
        if (_insets is not null)
        {
            _insets.SafeAreaChanged += OnSafeAreaChanged;
            ApplySafeArea();
        }
        if (ViewModel is { } vm)
            vm.PickFiles = PickFilesAsync;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_top is not null)
            _top.BackRequested -= OnBackRequested;
        if (_insets is not null)
            _insets.SafeAreaChanged -= OnSafeAreaChanged;
        if (ViewModel is { } vm && vm.PickFiles == PickFilesAsync)
            vm.PickFiles = null;
        _top = null;
        _insets = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => ApplySafeArea();

    /// <summary>Nada queda debajo de la barra de estado, la de navegación ni el teclado.</summary>
    private void ApplySafeArea() => SafeArea.Margin = _insets?.SafeAreaPadding ?? default;

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Back() == true)
            e.Handled = true;
    }

    private async Task<IReadOnlyList<IStorageFile>> PickFilesAsync()
    {
        if (_top?.StorageProvider is not { CanOpen: true } storage)
            return [];
        return await storage.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = "Al Toque" });
    }
}

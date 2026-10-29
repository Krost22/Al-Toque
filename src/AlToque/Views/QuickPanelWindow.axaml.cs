using AlToque.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace AlToque.Views;

public partial class QuickPanelWindow : Window
{
    public QuickPanelWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FloatingWindow.ApplyChrome(this, this.FindControl<Border>("Root")!);
        AutoHeight.Attach(this);
        var root = this.FindControl<Border>("Root")!;
        root.AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        root.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        root.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        root.AddHandler(DragDrop.DropEvent, OnDrop);
        // El panel no se oculta al perder el foco: hay que poder hacer clic en el Explorador y arrastrar
        // archivos hasta aquí. Se cierra con la X, con Esc o con el ícono de la bandeja.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                HideRequested?.Invoke();
        };
    }

    /// <summary>El usuario pidió cerrar el panel (Esc o Alt+F4): el controlador lo oculta y limpia lo pendiente.</summary>
    public event Action? HideRequested;

    private QuickPanelViewModel? Vm => DataContext as QuickPanelViewModel;

    public void PlaceNearTray() => FloatingWindow.PlaceBottomRight(this, 0);

    private static PeerViewModel? PeerUnder(DragEventArgs e) =>
        (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.DataContext is PeerViewModel)?.DataContext as PeerViewModel;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (Vm is not { } vm)
            return;
        vm.DragOver = hasFiles;
        var over = PeerUnder(e);
        foreach (var p in vm.Peers)
            p.IsDropTarget = p == over;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        vm.DragOver = false;
        foreach (var p in vm.Peers)
            p.IsDropTarget = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is not { Count: > 0 } || Vm is not { } vm)
            return;
        // Soltar sobre un compañero envía; soltar en el panel deja los archivos esperando destino.
        var peer = PeerUnder(e);
        if (peer is null)
            vm.SetPending(paths);
        else
            (Application.Current as App)?.Controller?.OnPeerClicked(peer, paths, string.IsNullOrWhiteSpace(vm.Message) ? null : vm.Message);
        e.Handled = true;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = "Al Toque" });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PlaceNearTray();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Se oculta en lugar de cerrarse: la UI se crea una vez y se reutiliza.
        if (!AppController.IsQuitting)
        {
            e.Cancel = true;
            HideRequested?.Invoke();
        }
        base.OnClosing(e);
    }
}

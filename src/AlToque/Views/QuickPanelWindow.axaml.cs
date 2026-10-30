using AlToque.Chasqui;
using AlToque.Controls;
using AlToque.Controls.Avatars;
using AlToque.Localization;
using AlToque.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace AlToque.Views;

public partial class QuickPanelWindow : Window
{
    private int _dragToken;

    public QuickPanelWindow()
    {
        AvaloniaXamlLoader.Load(this);
        var root = this.FindControl<Border>("Root")!;
        FloatingWindow.ApplyChrome(this, root);
        AutoHeight.Attach(this);
        root.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        root.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        root.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        root.AddHandler(DragDrop.DropEvent, OnDrop);
        // El panel no se oculta al perder el foco: hay que poder hacer clic en el Explorador y arrastrar
        // archivos hasta aquí. Se cierra con la X, con Esc o con el ícono de la bandeja.
        KeyDown += (_, e) =>
        {
            if (Tour.IsRunning)
            {
                // Durante el tutorial: Esc lo salta, Entrar o → avanza.
                if (e.Key == Key.Escape)
                    Tour.Finish();
                else if (e.Key is Key.Enter or Key.Right)
                    Tour.Next();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
                HideRequested?.Invoke();
        };
        // Los compañeros que aparecen entran con un saltito.
        this.FindControl<ItemsControl>("PeerList")!.ContainerPrepared += (_, e) =>
        {
            if (IsVisible)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => AvatarIn(e.Container, 60 * e.Index));
        };
    }

    /// <summary>El usuario pidió cerrar el panel (Esc o Alt+F4): el controlador lo oculta y limpia lo pendiente.</summary>
    public event Action? HideRequested;

    private QuickPanelViewModel? Vm => DataContext as QuickPanelViewModel;

    private TourOverlay Tour => this.FindControl<TourOverlay>("TourLayer")!;

    public bool IsTourRunning => Tour.IsRunning;

    /// <summary>Tutorial guiado: el panel paso a paso. <paramref name="done"/> se llama al terminar o saltarlo.</summary>
    public void StartTour(Action done)
    {
        if (Tour.IsRunning)
            return;
        var hasPeers = Vm?.HasPeers == true;
        TourStep[] steps =
        [
            new([], S.TourHelloTitle, S.TourHelloBody, ChasquiState.Incoming),
            new(["PendingCard", "ReadyCard", "NobodyView"], S.TourSendTitle, S.TourSendBody, ChasquiState.Attentive),
            new(["PeerList", "NobodyView"], S.TourPeersTitle, hasPeers ? S.TourPeersBody : S.TourNobodyBody, ChasquiState.Searching),
            new(["PairLink", "NobodyPair"], S.TourPairTitle, S.TourPairBody, ChasquiState.Waiting),
            new(["ModeSection"], S.TourModeTitle, S.TourModeBody, ChasquiState.Idle),
            new(["SettingsButton"], S.TourSettingsTitle, S.TourSettingsBody, ChasquiState.Received),
            new([], S.TourEndTitle, S.TourEndBody, ChasquiState.Success),
        ];
        void OnFinished()
        {
            Tour.Finished -= OnFinished;
            done();
        }
        Tour.Finished += OnFinished;
        Tour.Start(steps, name => this.FindControl<Control>(name));
    }

    public void StopTour() => Tour.Finish();

    public void PlaceNearTray() => FloatingWindow.PlaceBottomRight(this, 0);

    /// <summary>Entrada del panel: las secciones suben en cascada y los avatares saltan uno tras otro.</summary>
    public void PlayEntrance()
    {
        if (this.FindControl<StackPanel>("Sections") is not { } sections)
            return;
        Fx.Stagger(sections.Children.Where(c => c.IsVisible), stepMs: 35, dy: 14);
        var i = 0;
        foreach (var avatar in this.GetVisualDescendants().OfType<AvatarView>())
        {
            var delay = 120 + i++ * 70;
            Avalonia.Threading.DispatcherTimer.RunOnce(avatar.Pop, TimeSpan.FromMilliseconds(delay));
        }
    }

    private static void AvatarIn(Control container, int delayMs)
    {
        foreach (var avatar in container.GetVisualDescendants().OfType<AvatarView>())
            Avalonia.Threading.DispatcherTimer.RunOnce(avatar.Pop, TimeSpan.FromMilliseconds(delayMs));
    }

    private static PeerViewModel? PeerUnder(DragEventArgs e) =>
        (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.DataContext is PeerViewModel)?.DataContext as PeerViewModel;

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        OnDragOver(sender, e);
        if (Vm is not { } vm || e.DataTransfer.TryGetFiles() is not { } items)
            return;
        var paths = items.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        vm.SetDragInfo(paths.Count, null);
        // El tamaño se suma en segundo plano (solo archivos sueltos: las carpetas se miden al enviar).
        var token = ++_dragToken;
        _ = Task.Run(() => paths.Where(File.Exists).Sum(p => new FileInfo(p).Length)).ContinueWith(t =>
        {
            if (token == _dragToken && t.IsCompletedSuccessfully && paths.All(File.Exists))
                vm.SetDragInfo(paths.Count, t.Result);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (Vm is not { } vm)
            return;
        vm.DragOver = hasFiles;
        var over = PeerUnder(e);
        foreach (var p in vm.Peers)
            p.IsDropTarget = p == over && p.IsOnline;
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
        var peer = PeerUnder(e);
        OnDragLeave(sender, e);
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is not { Count: > 0 } || Vm is not { } vm)
            return;
        // Soltar sobre un compañero envía; soltar en el panel deja los archivos esperando destino.
        if (peer is null)
            vm.SetPending(paths);
        else
        {
            CelebrateDrop(peer);
            (Application.Current as App)?.Controller?.OnPeerClicked(peer, paths, string.IsNullOrWhiteSpace(vm.Message) ? null : vm.Message);
        }
        e.Handled = true;
    }

    /// <summary>El avatar que recibió los archivos salta de alegría.</summary>
    private void CelebrateDrop(PeerViewModel peer)
    {
        foreach (var avatar in this.GetVisualDescendants().OfType<AvatarView>())
        {
            if (avatar.DataContext == peer)
                avatar.Celebrate();
        }
        this.FindControl<ChasquiView>("Mascot")?.Cheer();
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

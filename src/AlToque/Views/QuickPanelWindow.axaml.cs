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
        ScreenFit.Attach(this);
        // Bordes y esquinas para agrandarla o achicarla; desde ese momento se queda del tamaño elegido.
        ResizeGrips.Attach(this, root, () =>
        {
            _sizing = true;
            AutoHeight.SetManual(this, true);
            SizeToContent = SizeToContent.Manual;
        });
        SizeChanged += (_, _) =>
        {
            FitChatPane();
            if (_sizing)
                SizeChosen?.Invoke(Width, Height);
        };
        root.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        root.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        root.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        root.AddHandler(DragDrop.DropEvent, OnDrop);
        // Archivos soltados (o pegados) sobre un chat abierto: van a ese compañero.
        var chatPane = this.FindControl<ChatView>("ChatPane")!;
        chatPane.FilesDropped += paths =>
        {
            if (Vm?.Chat is { } chat)
                ChatFilesDropped?.Invoke(chat.PeerId, paths);
        };
        chatPane.ImagePasted += bitmap =>
        {
            if (Vm?.Chat is { } chat)
                ChatImagePasted?.Invoke(chat.PeerId, bitmap);
        };
        // Se mueve arrastrando el encabezado; desde ese momento ya no se recoloca junto a la bandeja.
        var header = this.FindControl<Grid>("Header")!;
        header.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
                return;
            // Doble clic: vuelve al tamaño automático (el segundo clic no llega si ya empezó a moverse).
            if (e.ClickCount == 2)
            {
                ResetSize();
                SizeChosen?.Invoke(double.NaN, double.NaN);
                e.Handled = true;
                return;
            }
            // El arrastre empieza al mover el ratón, no al presionar: así el doble clic sigue llegando.
            _press = e;
            _pressAt = e.GetPosition(this);
        };
        header.PointerMoved += (_, e) =>
        {
            if (_press is not { } press || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            var d = e.GetPosition(this) - _pressAt;
            if (Math.Abs(d.X) + Math.Abs(d.Y) < 4)
                return;
            _press = null;
            _moving = true;
            BeginMoveDrag(press);
        };
        header.PointerReleased += (_, _) =>
        {
            _moving = false;
            _press = null;
        };
        PositionChanged += (_, _) =>
        {
            if (_moving)
                UserPlaced = true;
        };
        // La ventana no se oculta al perder el foco: hay que poder hacer clic en el Explorador y arrastrar
        // archivos hasta aquí. Esc y «—» la minimizan; sigue en la barra de tareas.
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
                MinimizeRequested?.Invoke();
        };
        InitGroups();
        // Los compañeros que aparecen entran con un saltito.
        this.FindControl<ItemsControl>("PeerList")!.ContainerPrepared += (_, e) =>
        {
            if (IsVisible)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => AvatarIn(e.Container, 60 * e.Index));
        };
    }

    private bool _moving;
    private bool _sizing;
    private PointerPressedEventArgs? _press;
    private Point _pressAt;

    /// <summary>El usuario le cambió el tamaño (NaN, NaN: volvió al automático con doble clic en el encabezado).</summary>
    public event Action<double, double>? SizeChosen;

    /// <summary>Tamaño elegido en otra sesión; si no cabe en esta pantalla, se achica.</summary>
    public void ApplySavedSize(double width, double height)
    {
        var max = ScreenFit.Available(this);
        _sizing = false;
        AutoHeight.SetManual(this, true);
        SizeToContent = SizeToContent.Manual;
        Width = Math.Clamp(width, MinWidth, Math.Min(MaxWidth, max.Width));
        Height = Math.Clamp(height, MinHeight, max.Height);
        _sizing = true;
    }

    /// <summary>Vuelve al ancho de siempre y al alto de su contenido.</summary>
    public void ResetSize()
    {
        _sizing = false;
        Width = 440;
        AutoHeight.SetManual(this, false);
        FitChatPane();
    }

    /// <summary>
    /// El chat abierto ocupa lo que queda bajo el encabezado si la ventana tiene un tamaño elegido; si no, un alto
    /// cómodo que quepa en la pantalla (portátil de 768 px incluido).
    /// </summary>
    private void FitChatPane()
    {
        if (Vm?.IsChatOpen != true || this.FindControl<ChatView>("ChatPane") is not { } pane)
            return;
        if (AutoHeight.IsManual(this) && this.FindControl<ScrollViewer>("Scroller") is { Bounds.Height: > 0 } scroller)
            pane.Height = Math.Max(260, scroller.Bounds.Height - 2);
        else if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
            pane.Height = Math.Clamp(screen.WorkingArea.Height / screen.Scaling - 190, 300, 600);
    }
    /// <summary>Soltaron archivos sobre el chat abierto con ese compañero.</summary>
    public event Action<string, IReadOnlyList<string>>? ChatFilesDropped;

    /// <summary>Pegaron una imagen en el chat abierto con ese compañero.</summary>
    public event Action<string, Avalonia.Media.Imaging.Bitmap>? ChatImagePasted;

    /// <summary>El usuario pidió cerrar la ventana (Esc, Alt+F4 o «Cerrar ventana» en la barra de tareas): se minimiza.</summary>
    public event Action? MinimizeRequested;

    /// <summary>El usuario la movió: se respeta su posición en lugar de pegarla a la bandeja.</summary>
    public bool UserPlaced { get; private set; }

    public bool IsMinimized => WindowState == WindowState.Minimized;

    private QuickPanelViewModel? Vm => DataContext as QuickPanelViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is { } vm)
            vm.ChatOpened += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (this.FindControl<ChatView>("ChatPane") is not { } pane)
                    return;
                FitChatPane();
                pane.FocusComposer();
            });
    }

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
            new(["PeerList", "NobodyView"], S.Tour1Title, hasPeers ? S.Tour1Body : S.Tour1Nobody, ChasquiState.Attentive),
            new(["NearbyArea", "PairLink", "NobodyPair"], S.Tour2Title, S.Tour2Body, ChasquiState.Waiting),
            new(["MinimizeButton"], S.Tour3Title, S.Tour3Body, ChasquiState.Success),
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

    /// <summary>Lo que ocupan los chats fijados en la esquina: el panel se pone a su izquierda.</summary>
    public double TrayOffset { get; set; }

    public void PlaceNearTray()
    {
        if (!UserPlaced)
            FloatingWindow.PlaceBottomRight(this, 0, TrayOffset);
    }

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
        // Un compañero arrastrado hacia la ficha de un grupo.
        if (e.DataTransfer.Contains(PeerFormat))
        {
            var target = ChipUnder(e);
            e.DragEffects = target is { IsGroup: true } ? DragDropEffects.Link : DragDropEffects.None;
            HighlightChip(target);
            e.Handled = true;
            return;
        }
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (Vm is not { } vm)
            return;
        vm.DragOver = hasFiles;
        var over = PeerUnder(e);
        foreach (var p in vm.Peers)
            p.IsDropTarget = p == over && p.IsOnline;
        // Archivos sobre un grupo: se envían a todos sus miembros.
        HighlightChip(hasFiles ? ChipUnder(e) : null);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        vm.DragOver = false;
        foreach (var p in vm.Peers)
            p.IsDropTarget = false;
        HighlightChip(null);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var peer = PeerUnder(e);
        var chip = ChipUnder(e);
        OnDragLeave(sender, e);
        if (e.DataTransfer.Contains(PeerFormat))
        {
            if (chip is { IsGroup: true, Id: { } groupId } && e.DataTransfer.TryGetValue(PeerFormat) is { } peerId)
                Controller?.AddToGroup(groupId, peerId);
            e.Handled = true;
            return;
        }
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is not { Count: > 0 } || Vm is not { } vm)
            return;
        // Soltar sobre un compañero envía; sobre un grupo, a todos sus miembros; en el panel, los deja esperando destino.
        if (chip is { IsGroup: true, Id: { } id })
            _ = Controller?.SendToGroupAsync(id, paths);
        else if (peer is null)
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
        // Cerrar la ventana la minimiza: Al Toque sigue en la barra de tareas y recibiendo. Para salir está
        // «Salir de Al Toque» (clic derecho en su botón de la barra de tareas o en el ícono de la bandeja).
        if (!AppController.IsQuitting)
        {
            e.Cancel = true;
            MinimizeRequested?.Invoke();
        }
        base.OnClosing(e);
    }
}

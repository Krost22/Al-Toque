using System.Diagnostics;
using AlToque.Chasqui;
using AlToque.Controls;
using AlToque.Core;
using AlToque.Core.Agent;
using AlToque.Core.Pairing;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;
using AlToque.Localization;
using AlToque.Platform;
using AlToque.ViewModels;
using AlToque.Views;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AlToque;

/// <summary>
/// Conecta el agente con la interfaz: bandeja, panel rápido, Chasqui flotante, solicitudes y ventanas.
/// Las ventanas se crean la primera vez que hacen falta y después solo se ocultan.
/// </summary>
public sealed class AppController
{
    private static readonly TimeSpan SendSuccessLinger = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReceiveSuccessLinger = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ErrorLinger = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan NeutralLinger = TimeSpan.FromSeconds(8);

    private readonly App _app;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly DispatcherTimer _fastTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _slowTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<(OfferWindow Window, OfferViewModel Vm)> _offers = [];
    private TrayIcon? _tray;
    private NativeMenuItem? _modeNobody, _modeTrusted, _modeEveryone;
    private QuickPanelWindow? _panelWindow;
    private TransfersWindow? _transfersWindow;
    private MainWindow? _mainWindow;
    private PairingWindow? _pairingWindow;
    private ConnectWindow? _connectWindow;
    private string? _trayIconName;
    private string? _integrationSignature;
    private bool _systemReduceMotion;
    private Updates? _updates;
    private NativeMenuItem? _updateItem;
    private bool _restartingForUpdate;

    public AppController(App app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _app = app;
        _desktop = desktop;
    }

    public static bool IsQuitting { get; private set; }

    public AgentHost Agent { get; private set; } = null!;
    public QuickPanelViewModel Panel { get; private set; } = null!;
    public TransfersViewModel Transfers { get; } = new();
    public MainViewModel? Main { get; private set; }
    public PairingViewModel? Pairing { get; private set; }
    public ConnectViewModel? Connect { get; private set; }
    public UpdateViewModel Update { get; private set; } = null!;

    /// <summary>Movimiento reducido: por ajuste ("Animaciones") o, en "Como en Windows", por el sistema.</summary>
    public bool ReduceMotion => Agent.Settings.Current.AnimationMode switch
    {
        "full" => false,
        "reduced" => true,
        _ => Agent.Settings.Current.ReduceMotion || _systemReduceMotion,
    };

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    // ── Arranque ─────────────────────────────────────────────────────────────────

    public void Start()
    {
        try
        {
            Agent = AgentHost.Create(new AgentOptions { IsPublicNetwork = NetworkCategory.IsOnPublicNetwork, AllowPairRequests = true });
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudo iniciar el agente: " + ex);
            _desktop.Shutdown(1);
            return;
        }
        S.Initialize(Agent.Settings.Current.Language);
        _systemReduceMotion = SystemPrefs.PrefersReducedMotion();
        MotionPrefs.Reduced = ReduceMotion;
        Panel = new QuickPanelViewModel(this) { ReduceMotion = ReduceMotion };
        Transfers.ReduceMotion = ReduceMotion;

        WireAgent();
        Agent.Start();
        BuildTray();
        ApplyIntegration();
        RefreshPeers();
        RefreshRecent();
        RefreshMode();

        _fastTimer.Tick += (_, _) => FastTick();
        _slowTimer.Tick += (_, _) => SlowTick();
        _slowTimer.Start();
        _desktop.ShutdownRequested += (_, _) => Cleanup();
        _updates = new Updates(() => Agent.Settings.Current.AutoUpdate);
        Update = new UpdateViewModel(_updates, () => Agent.Settings.Current.AutoUpdate,
            v => Agent.Settings.Update(s => s.AutoUpdate = v), InstallUpdate);
        Update.PropertyChanged += (_, _) => RefreshUpdateItem();
        Panel.Update = Update;
        RefreshUpdateItem();
        CelebrateUpdate();

        if (!Agent.Settings.Current.WelcomeDone)
            ShowWelcome();
        else if (!Program.StartedInBackground)
            ShowPanel();
        else
            ShowPanelMinimized();
        RestorePinned();
        CleanPastedImages();
    }

    private void WireAgent()
    {
        Agent.OfferReceived += o => Post(() => ShowOffer(o));
        Agent.TransferStarted += i => Post(() => AddTransfer(i));
        Agent.TransferFinished += i => Post(() => OnTransferFinished(i));
        Agent.ReceiveModeChanged += () => Post(RefreshMode);
        Agent.NetworkStateChanged += () => Post(() =>
        {
            RefreshMode();
            RefreshNetworkName();
        });
        RefreshNetworkName();
        Agent.PickRequested += p => Post(() => ShowPanel(p));
        Agent.CodeRequested += p => Post(() => ShowConnect(p, showMyCode: false));
        Agent.ShowRequested += () => Post(() => ShowPanel());
        Agent.QuitRequested += () => Post(Quit);
        Agent.Peers.Changed += () => Post(RefreshPeers);
        Agent.Links.PresenceChanged += _ => Post(RefreshPeers);
        Agent.Chats.Changed += (peerId, message) => Post(() => OnChatChanged(peerId, message));
        Agent.Pairing.PromptRequested += p => Post(() => OnPairingPrompt(p));
        Agent.Pairing.Finished += (id, outcome) => Post(() => OnPairingFinished(id, outcome));
        Agent.Trust.Changed += () => Post(() =>
        {
            ApplyIntegration();
            Main?.ReloadPeers();
            RefreshPeers();
            RefreshMode();
        });
        Agent.History.Changed += () => Post(() =>
        {
            RefreshRecent();
            Main?.ReloadHistory();
        });
        Agent.Settings.Changed += _ => Post(() =>
        {
            MotionPrefs.Reduced = ReduceMotion;
            Panel.ReduceMotion = ReduceMotion;
            Transfers.ReduceMotion = ReduceMotion;
            ApplyIntegration();
            RefreshMode();
        });
    }

    // ── Bandeja ──────────────────────────────────────────────────────────────────

    private void BuildTray()
    {
        var menu = new NativeMenu();
        _updateItem = Item(S.UpdateNow, InstallUpdate);
        _updateItem.IsVisible = false;
        menu.Add(_updateItem);
        menu.Add(Item(S.TrayOpen, () => ShowPanel()));
        menu.Add(new NativeMenuItemSeparator());
        var modes = new NativeMenu();
        _modeNobody = RadioItem(S.ModeNobody, () => Agent.SetReceiveMode(ReceiveMode.Nobody));
        _modeTrusted = RadioItem(S.ModeTrusted, () => Agent.SetReceiveMode(ReceiveMode.TrustedOnly));
        _modeEveryone = RadioItem(S.ModeEveryone, () => Agent.SetReceiveMode(ReceiveMode.Everyone));
        modes.Add(_modeNobody);
        modes.Add(_modeTrusted);
        modes.Add(_modeEveryone);
        menu.Add(new NativeMenuItem(S.TrayReceiveFrom) { Menu = modes });
        menu.Add(Item(S.TrayPair, ShowPairing));
        menu.Add(Item(S.TrayConnect, () => ShowConnect(null, showMyCode: false)));
        menu.Add(Item(S.TrayHistory, () => ShowMain(1)));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(S.Quit, Quit));

        _tray = new TrayIcon { ToolTipText = S.TrayTooltipIdle, Menu = menu, IsVisible = true };
        _tray.Clicked += (_, _) => TogglePanel();
        TrayIcon.SetIcons(_app, [_tray]);
        UpdateTray();

        static NativeMenuItem Item(string header, Action action)
        {
            var item = new NativeMenuItem(header);
            item.Click += (_, _) => action();
            return item;
        }

        static NativeMenuItem RadioItem(string header, Action action)
        {
            var item = new NativeMenuItem(header) { ToggleType = MenuItemToggleType.Radio };
            item.Click += (_, _) => action();
            return item;
        }
    }

    private void UpdateTray()
    {
        if (_tray is null)
            return;
        var mode = Agent.EffectiveMode;
        var paused = Agent.IsReceivingPausedByNetwork || (mode == ReceiveMode.Nobody && !Agent.Pairing.IsWindowOpen);
        var receiving = Agent.ActiveTransfers.Any(t => t.Direction == TransferDirection.Receive);
        var icon = paused ? "tray-paused" : receiving ? "tray-receiving" : mode == ReceiveMode.Everyone ? "tray-everyone" : "tray";
        if (icon != _trayIconName)
        {
            _trayIconName = icon;
            using var stream = AssetLoader.Open(new Uri($"avares://AlToque/Assets/{icon}.ico"));
            _tray.Icon = new WindowIcon(stream);
        }
        var unread = Agent.Chats.TotalUnread();
        _tray.ToolTipText = unread > 0 ? "Al Toque · " + S.ChatUnread(unread)
            : paused ? S.TrayTooltipPaused : mode == ReceiveMode.Everyone && Agent.EveryoneUntil is { } u
            ? "Al Toque · " + S.EveryoneRemaining(u - DateTimeOffset.UtcNow)
            : S.TrayTooltipIdle;
        if (_modeNobody is not null)
        {
            _modeNobody.IsChecked = mode == ReceiveMode.Nobody;
            _modeTrusted!.IsChecked = mode == ReceiveMode.TrustedOnly;
            _modeEveryone!.IsChecked = mode == ReceiveMode.Everyone;
        }
    }

    // ── Integración con el sistema ───────────────────────────────────────────────

    private void ApplyIntegration()
    {
        var settings = Agent.Settings.Current;
        var exe = Environment.ProcessPath ?? "";
        var peers = Agent.Trust.All
            .OrderByDescending(p => p.Favorite)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new MenuPeer(p.DeviceId, p.DisplayName))
            .ToList();
        var signature = $"{settings.ShellIntegration}|{settings.StartWithSystem}|{exe}|{S.Spanish}|" + string.Join(";", peers.Select(p => p.DeviceId + "=" + p.Name));
        if (signature == _integrationSignature)
            return;
        _integrationSignature = signature;
        // Registro y COM fuera del hilo de la interfaz.
        _ = Task.Run(() =>
        {
            if (OperatingSystem.IsWindows())
            {
                if (settings.ShellIntegration && exe.Length > 0)
                    WindowsShell.Install(exe, peers);
                else
                    WindowsShell.Uninstall();
                // Clic derecho en el botón de la barra de tareas › «Salir de Al Toque».
                if (exe.Length > 0)
                    JumpList.SetQuitTask(exe, S.QuitApp);
            }
            StartupRegistration.Set(settings.StartWithSystem);
        });
    }

    /// <summary>
    /// Recibir en la red pública actual: lo pide el usuario desde el aviso. En Windows además se pide (una vez)
    /// permiso de administrador para que el firewall deje pasar las conexiones en redes públicas.
    /// </summary>
    public async void AllowPublicNetwork()
    {
        Agent.AllowPublicNetworkForSession();
        RefreshMode();
        if (!OperatingSystem.IsWindows() || Agent.Settings.Current.PublicFirewallRuleAdded)
            return;
        var port = Agent.Server.IsListening ? Agent.Server.Port : Agent.Settings.Current.TcpPort;
        if (await Task.Run(() => Firewall.RequestRule(port, includePublic: true)))
            Agent.Settings.Update(s => s.PublicFirewallRuleAdded = true);
    }

    // ── Panel rápido ─────────────────────────────────────────────────────────────

    /// <summary>Clic en el ícono de la bandeja: trae la ventana al frente o, si ya está delante, la minimiza.</summary>
    public void TogglePanel()
    {
        if (_panelWindow is { IsVisible: true, IsMinimized: false, IsActive: true })
            MinimizePanel();
        else
            ShowPanel();
    }

    private QuickPanelWindow EnsurePanelWindow()
    {
        if (_panelWindow is null)
        {
            _panelWindow = new QuickPanelWindow { DataContext = Panel };
            _panelWindow.MinimizeRequested += MinimizePanel;
            _panelWindow.TrayOffset = PinnedWidth();
            _panelWindow.SizeChanged += (_, _) => LayoutCorner();
            _panelWindow.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.WindowStateProperty)
                {
                    LayoutToasts();
                    UpdateChatVisibility();
                }
            };
            _panelWindow.Activated += (_, _) => UpdateChatVisibility();
            _panelWindow.ChatFilesDropped += (peerId, paths) => _ = SendToPeerAsync(peerId, NameOf(peerId), paths, null);
            _panelWindow.ChatImagePasted += (peerId, bitmap) => _ = SendPastedImageAsync(peerId, bitmap);
            _panelWindow.Deactivated += (_, _) => UpdateChatVisibility();
        }
        return _panelWindow;
    }

    /// <summary>Al iniciar con la sesión: la ventana espera minimizada en la barra de tareas, a un clic.</summary>
    private void ShowPanelMinimized()
    {
        var window = EnsurePanelWindow();
        window.WindowState = WindowState.Minimized;
        window.Show();
    }

    public void ShowPanel(IReadOnlyList<string>? pendingPaths = null)
    {
        EnsurePanelWindow();
        if (pendingPaths is not null)
            Panel.SetPending(pendingPaths);
        Panel.BeginSearch();
        Agent.Discovery?.Query();
        DispatcherTimer.RunOnce(() => Panel.EndSearch(), TimeSpan.FromSeconds(2.5));
        var wasVisible = _panelWindow!.IsVisible && !_panelWindow.IsMinimized;
        if (_panelWindow.IsMinimized)
            _panelWindow.WindowState = WindowState.Normal;
        _panelWindow.Show();
        _panelWindow.TrayOffset = PinnedWidth();
        _panelWindow.PlaceNearTray();
        _panelWindow.Activate();
        if (!wasVisible)
            _panelWindow.PlayEntrance();
        LayoutToasts();
        // Primera vez en esta PC: tutorial guiado (cuando el panel ya terminó de entrar).
        if (!Agent.Settings.Current.TutorialDone && !_panelWindow.IsTourRunning)
            DispatcherTimer.RunOnce(StartTour, TimeSpan.FromMilliseconds(700));
    }

    private void StartTour()
    {
        if (_panelWindow is not { IsVisible: true } panel)
            return;
        panel.StartTour(() =>
        {
            if (!Agent.Settings.Current.TutorialDone)
                Agent.Settings.Update(s => s.TutorialDone = true);
        });
    }

    /// <summary>"Ver el tutorial otra vez" desde Acerca de.</summary>
    public void ReplayTour()
    {
        ShowPanel();
        DispatcherTimer.RunOnce(StartTour, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>«—», Esc o cerrar la ventana: se minimiza a la barra de tareas y Al Toque sigue recibiendo.</summary>
    public void MinimizePanel()
    {
        if (_panelWindow?.IsTourRunning == true)
            _panelWindow.StopTour();
        if (_panelWindow is { IsVisible: true } window)
            window.WindowState = WindowState.Minimized;
        Panel.SetPending(null);
        LayoutToasts();
    }

    /// <summary>× en la tarjeta: un compañero de confianza deja de serlo; a otro equipo se lo oculta en esta sesión.</summary>
    public async void RemovePeer(PeerViewModel peer)
    {
        if (peer.IsTrusted)
        {
            if (!await ConfirmWindow.AskAsync(S.RemovePeerConfirm(peer.Name)))
                return;
            Agent.Trust.Remove(peer.DeviceId);
            if (_pinned.ContainsKey(peer.DeviceId))
            {
                ClosePinned(peer.DeviceId);
                SavePinned();
            }
        }
        else
        {
            if (!await ConfirmWindow.AskAsync(S.HidePeerConfirm(peer.Name)))
                return;
            Agent.Block(peer.DeviceId);
        }
        RefreshPeers();
    }

    /// <summary>Clic (o archivos soltados) en un compañero: envía, pide archivos o, si todavía no es compañero, pide conectarse.</summary>
    public async void OnPeerClicked(PeerViewModel peer, IReadOnlyList<string>? paths, string? message)
    {
        // Un equipo cercano que todavía no es compañero: primero se conectan; lo que se soltó sale después.
        if (!peer.IsTrusted && peer.CanConnect)
        {
            if (Panel.HasPending)
                Panel.SetPending(null);
            RequestConnection(peer, paths, message);
            return;
        }
        if (paths is not { Count: > 0 })
        {
            if (peer.IsPairing && !peer.IsTrusted)
            {
                ShowPairing();
                Pairing!.PairWith.Execute(peer);
                return;
            }
            if (_panelWindow is null)
                return;
            paths = await _panelWindow.PickFilesAsync();
            if (paths.Count == 0)
                return;
        }
        var fromContextMenu = Panel.HasPending;
        Panel.SetPending(null);
        await SendToPeerAsync(peer.DeviceId, peer.Name, paths, message);
        if (fromContextMenu)
            MinimizePanel();
    }

    private async Task SendToPeerAsync(string deviceId, string name, IReadOnlyList<string> paths, string? message)
    {
        var target = await Agent.LocateAsync(deviceId, TimeSpan.FromSeconds(1.5));
        if (target is null)
        {
            Panel.Flash(ChasquiState.Error, S.ErrorText("E_UNREACHABLE", name, null));
            ShowPanel();
            return;
        }
        Agent.BeginSend(target, paths, message);
    }

    /// <summary>El nombre de la red tarda unos milisegundos (COM): se pide fuera del hilo de la interfaz.</summary>
    private void RefreshNetworkName() =>
        _ = Task.Run(NetworkCategory.CurrentNetworkName).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                Panel.SetNetworkName(t.Result);
        }, TaskScheduler.FromCurrentSynchronizationContext());

    public async Task<IReadOnlyList<string>> PickFilesAsync(Window? owner = null)
    {
        owner ??= (Window?)_panelWindow ?? _mainWindow;
        if (owner is null)
            return [];
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = "Al Toque" });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    /// <summary>"Reintentar" en recientes: mismo destino y mismos archivos (los que sigan existiendo).</summary>
    public async void Retry(HistoryEntry entry)
    {
        var paths = (entry.Paths ?? []).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (paths.Count == 0)
        {
            Panel.Flash(ChasquiState.Error, S.ErrorText("E_IO", entry.PeerName, null));
            return;
        }
        if (string.IsNullOrEmpty(entry.PeerId) || Agent.Peers.Find(entry.PeerId) is null)
        {
            // Sin compañero conocido: los archivos quedan esperando destino en el panel.
            ShowPanel(paths);
            return;
        }
        await SendToPeerAsync(entry.PeerId, entry.PeerName, paths, null);
    }

    public void OpenHistoryItem(HistoryEntry entry)
    {
        if ((entry.FirstFile ?? entry.Folder) is { } path && (File.Exists(path) || Directory.Exists(path)))
            Opener.ShowInFolder(path);
    }

    /// <summary>Envío desde "Conectar por IP o código".</summary>
    public async Task SendToTargetAsync(PeerTarget target, IReadOnlyList<string>? paths)
    {
        if (paths is not { Count: > 0 })
        {
            var owner = (Window?)_connectWindow ?? _panelWindow;
            if (owner is null)
                return;
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = "Al Toque" });
            paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (paths.Count == 0)
                return;
        }
        Agent.BeginSend(target, paths);
        _connectWindow?.Close();
    }

    private void RefreshPeers()
    {
        var snapshot = Agent.Peers.Snapshot().Where(p => !Agent.IsBlocked(p.DeviceId)).ToList();
        Panel.SyncPeers(snapshot);
        Panel.SyncChats(Agent.Chats.Summaries(), Agent.DeviceId);
        Pairing?.SyncCandidates(snapshot);
    }

    private void RefreshRecent() => Panel.SyncRecent(Agent.History.Recent(4));

    private void RefreshMode()
    {
        Panel.SyncMode(Agent.EffectiveMode, Agent.EveryoneUntil, Agent.Trust.All.Count, Agent.IsReceivingPausedByNetwork, Agent.IsVisibleNearby);
        Main?.RefreshMode();
        Pairing?.SyncNetwork(Agent.IsReceivingPausedByNetwork);
        UpdateTray();
    }

    // ── Transferencias y Chasqui flotante ────────────────────────────────────────

    private void AddTransfer(TransferInfo info)
    {
        if (Transfers.Items.Any(t => t.Info == info))
            return;
        var vm = new TransferViewModel(info, DismissTransfer, Agent.CancelTransfer, RetryTransfer, () => ShowConnect(info.SourcePaths, showMyCode: false),
            Opener.ShowInFolder);
        vm.OpenFileRequested += t => _ = OpenReceivedFileAsync(t);
        Transfers.Items.Insert(0, vm);
        ShowTransfersWindow();
        _fastTimer.Start();
        UpdateTray();
    }

    private void OnTransferFinished(TransferInfo info)
    {
        var vm = Transfers.Items.FirstOrDefault(t => t.Info == info);
        if (vm is null)
            return; // Ofertas rechazadas o vencidas: nunca llegaron a mostrarse como transferencia.
        vm.Refresh();
        if (_transfersWindow is not null)
            AutoHeight.Refit(_transfersWindow);
        var state = ChasquiStates.For(info);
        Panel.Flash(state, vm.Title);
        if (Agent.Settings.Current.Sounds)
        {
            if (info.IsSuccess)
                SystemPrefs.Play(SystemPrefs.Sound.Success);
            else if (vm.IsError)
                SystemPrefs.Play(SystemPrefs.Sound.Error);
        }
        UpdateTray();
    }

    private async void RetryTransfer(TransferViewModel vm)
    {
        var info = vm.Info;
        DismissTransfer(vm);
        if (info.PeerId is { } id)
            await SendToPeerAsync(id, info.PeerName, info.SourcePaths, info.OutgoingMessage);
    }

    private async void DismissTransfer(TransferViewModel vm)
    {
        if (!Transfers.Items.Contains(vm))
            return;
        if (_transfersWindow is not null)
            await _transfersWindow.AnimateOutAsync(vm);
        Transfers.Items.Remove(vm);
        if (Transfers.Items.Count == 0)
            _transfersWindow?.Hide();
        else if (_transfersWindow is not null)
            AutoHeight.Refit(_transfersWindow);
        LayoutToasts();
    }

    private void ShowTransfersWindow()
    {
        if (_transfersWindow is null)
        {
            _transfersWindow = new TransfersWindow { DataContext = Transfers };
            _transfersWindow.LayoutChanged += LayoutToasts;
        }
        if (!_transfersWindow.IsVisible)
            _transfersWindow.Show();
        AutoHeight.Refit(_transfersWindow);
        LayoutToasts();
    }

    private async Task OpenReceivedFileAsync(TransferViewModel vm)
    {
        if (vm.Info.CompletedPaths is not [var path])
            return;
        if (FileKinds.IsDangerous(path) && !await ConfirmWindow.AskAsync(S.OpenExecutableConfirm(Path.GetFileName(path))))
            return;
        Opener.OpenFile(path);
    }

    private void FastTick()
    {
        foreach (var t in Transfers.Items)
        {
            if (t.IsActive || t.FinishedAt is null)
                t.Refresh();
        }
        if (_transfersWindow is { IsVisible: true } tw)
            AutoHeight.Refit(tw);
        Panel.SyncActivity(Transfers.Items);
        if (!Transfers.Items.Any(t => t.IsActive))
            _fastTimer.Stop();
    }

    private void SlowTick()
    {
        // Los avisos de mensajes se retiran solos a los 8 s (salvo con el ratón encima).
        foreach (var (window, vm) in _messageToasts.Where(t => DateTimeOffset.Now - t.Vm.ShownAt > TimeSpan.FromSeconds(8) && !t.Window.IsHovered).ToList())
            CloseMessageToast(vm);
        // Una solicitud ya resuelta se retira sola a los pocos segundos.
        foreach (var (_, vm) in _pairRequests.Where(r => r.Vm.FinishedAt is { } f && DateTimeOffset.Now - f > TimeSpan.FromSeconds(r.Vm.IsDone ? 10 : 5) && !r.Window.IsHovered).ToList())
            ClosePairRequest(vm);
        Panel.Tick();
        foreach (var (_, vm) in _offers)
            vm.Tick();
        Pairing?.Tick();
        if (Agent.EffectiveMode == ReceiveMode.Everyone)
            RefreshMode();
        Panel.UpdateChasqui();

        // Los terminados se retiran solos: éxito al envío en 3 s; recepciones con botones, más tarde.
        var now = DateTimeOffset.Now;
        foreach (var t in Transfers.Items.Where(t => !t.IsActive && t.FinishedAt is { } f && now - f > Linger(t)).ToList())
            DismissTransfer(t);
    }

    private static TimeSpan Linger(TransferViewModel t) =>
        t.IsError ? ErrorLinger : t.CanOpen ? ReceiveSuccessLinger : t.IsSuccess ? SendSuccessLinger : NeutralLinger;

    // ── Solicitudes entrantes ────────────────────────────────────────────────────

    private void ShowOffer(IncomingOffer offer)
    {
        if (offer.Withdrawn.IsCancellationRequested)
            return;
        var vm = new OfferViewModel(offer, AlwaysAcceptFrom, PairInsteadOf);
        var window = new OfferWindow { DataContext = vm };
        window.LayoutChanged += LayoutToasts;
        vm.Answered += () => CloseOffer(window);
        offer.Withdrawn.Register(() => Post(() => CloseOffer(window)));
        _offers.Add((window, vm));
        window.Show();
        LayoutToasts();
        Panel.Flash(ChasquiState.Incoming, offer.PeerName + " " + S.WantsToSend(offer.FileCount));
        if (Agent.Settings.Current.Sounds)
            SystemPrefs.Play(SystemPrefs.Sound.Incoming);
    }

    private void AlwaysAcceptFrom(IncomingOffer offer)
    {
        if (offer.Trusted)
            Agent.Trust.Update(offer.PeerId, p => p.AutoAccept = true);
    }

    /// <summary>"Emparejar en su lugar": se rechaza el envío y se abre el emparejamiento con ese equipo.</summary>
    private void PairInsteadOf(IncomingOffer offer)
    {
        ShowPairing();
        if (Agent.Peers.Find(offer.PeerId) is { } info)
        {
            var peer = new PeerViewModel(info.DeviceId);
            peer.Update(info);
            Pairing!.PairWith.Execute(peer);
        }
    }

    private void CloseOffer(OfferWindow window)
    {
        var index = _offers.FindIndex(o => o.Window == window);
        if (index < 0)
            return;
        _offers.RemoveAt(index);
        window.Close();
        LayoutToasts();
    }

    /// <summary>
    /// Apila en la esquina inferior derecha: Chasqui flotante abajo y las solicitudes encima. Si el panel está
    /// abierto, todo se corre a su izquierda para no taparlo.
    /// </summary>
    private void LayoutToasts()
    {
        // Las tarjetas van a la izquierda de los chats fijados y de la ventana principal si está junto a la bandeja.
        var left = PinnedWidth() + (_panelWindow is { IsVisible: true, IsMinimized: false, UserPlaced: false } p ? p.Bounds.Width : 0);
        var offset = 0.0;
        var overlap = OperatingSystem.IsWindows() ? FloatingWindow.ShadowMargin : -8;
        if (_transfersWindow is { IsVisible: true } tw)
        {
            FloatingWindow.PlaceBottomRight(tw, 0, left);
            offset += tw.Bounds.Height - overlap;
        }
        foreach (var (window, _) in _offers)
        {
            FloatingWindow.PlaceBottomRight(window, Math.Max(0, offset), left);
            offset += window.Bounds.Height - overlap;
        }
        foreach (var (window, _) in _messageToasts)
        {
            FloatingWindow.PlaceBottomRight(window, Math.Max(0, offset), left);
            offset += window.Bounds.Height - overlap;
        }
        foreach (var (window, _) in _pairRequests)
        {
            FloatingWindow.PlaceBottomRight(window, Math.Max(0, offset), left);
            offset += window.Bounds.Height - overlap;
        }
    }

    // ── Chats ────────────────────────────────────────────────────────────────────

    private readonly List<(MessageToastWindow Window, MessageToastViewModel Vm)> _messageToasts = [];
    private readonly Dictionary<string, ChatWindow> _pinned = new(StringComparer.Ordinal);
    private ImageViewerWindow? _viewer;
    private DispatcherTimer? _pinSaveTimer;

    /// <summary>El chat dentro del panel; «volver» regresa a la lista de compañeros.</summary>
    public ChatViewModel CreateChat(string peerId, Action back) =>
        new(Agent, peerId, ChatActionsFor(peerId, () => _panelWindow, back, pinned: false))
        {
            IsVisible = _panelWindow is { IsActive: true, IsMinimized: false },
        };

    private ChatActions ChatActionsFor(string peerId, Func<Window?> owner, Action? back, bool pinned) => new()
    {
        Attach = () => PickAndSendAsync(peerId, owner()),
        OpenPath = Opener.OpenFile,
        ShowInFolder = Opener.ShowInFolder,
        Back = back,
        Confirm = ConfirmWindow.AskAsync,
        ViewPhotos = (paths, index) => ViewPhotos(paths, index, owner()),
        TogglePin = pinned ? () => UnpinChat(peerId) : () => PinChat(peerId),
        Minimize = pinned ? () => MinimizePinned(peerId) : null,
    };

    private string NameOf(string peerId) => Agent.Peers.Find(peerId)?.DisplayName ?? Agent.Trust.Find(peerId)?.DisplayName ?? "";

    private async Task PickAndSendAsync(string peerId, Window? owner = null)
    {
        var paths = await PickFilesAsync(owner);
        if (paths.Count > 0)
            await SendToPeerAsync(peerId, NameOf(peerId), paths, null);
    }

    /// <summary>
    /// Una imagen pegada en el chat (una captura): se guarda como PNG en la carpeta de la app y se envía como
    /// cualquier archivo; así queda en la conversación de los dos.
    /// </summary>
    private async Task SendPastedImageAsync(string peerId, Avalonia.Media.Imaging.Bitmap bitmap)
    {
        string path;
        try
        {
            var dir = Agent.Paths.PastedDir;
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, S.PastedImageName(DateTime.Now));
            for (var i = 2; File.Exists(path); i++)
                path = Path.Combine(dir, Path.GetFileNameWithoutExtension(S.PastedImageName(DateTime.Now)) + $" ({i}).png");
            var target = path;
            await Task.Run(() => bitmap.Save(target, new Avalonia.Media.Imaging.PngBitmapEncoderOptions()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            AgentLog.Write("No se pudo guardar la imagen pegada: " + ex.Message);
            Panel.Flash(ChasquiState.Error, S.ErrorText("E_IO", NameOf(peerId), null));
            return;
        }
        await SendToPeerAsync(peerId, NameOf(peerId), [path], null);
    }

    /// <summary>Las imágenes pegadas se guardan dos meses; después solo queda su registro en el chat.</summary>
    private void CleanPastedImages() => _ = Task.Run(() =>
    {
        try
        {
            var dir = new DirectoryInfo(Agent.Paths.PastedDir);
            if (!dir.Exists)
                return;
            foreach (var file in dir.EnumerateFiles("*.png").Where(f => DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromDays(60)))
                file.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Write("Limpieza de imágenes pegadas: " + ex.Message);
        }
    });

    private void ViewPhotos(IReadOnlyList<string> paths, int index, Window? near)
    {
        if (paths.Count == 0)
            return;
        _viewer?.Close();
        var viewer = new ImageViewerWindow();
        viewer.DataContext = new ImageViewerViewModel(paths, index, ImageViewerWindow.MaxSideFor(near ?? _panelWindow), Opener.OpenFile, Opener.ShowInFolder,
            viewer.Close);
        viewer.CoverScreenOf(near ?? _panelWindow);
        viewer.Closed += (_, _) =>
        {
            if (_viewer == viewer)
                _viewer = null;
        };
        _viewer = viewer;
        viewer.Show();
        viewer.Activate();
    }

    /// <summary>El chat a la vista: lo que llega cuenta como leído (el del panel, con la ventana activa; el fijado, sin minimizar).</summary>
    private void UpdateChatVisibility()
    {
        if (Panel.Chat is { } chat)
            chat.IsVisible = _panelWindow is { IsActive: true, IsMinimized: false, IsVisible: true };
        foreach (var window in _pinned.Values)
        {
            if (window.Vm is { } vm)
                vm.IsVisible = window is { IsVisible: true, IsMinimized: false };
        }
    }

    private bool IsBeingRead(string peerId) =>
        Panel.Chat is { IsVisible: true } chat && chat.PeerId == peerId
        || _pinned.TryGetValue(peerId, out var pinned) && pinned.Vm is { IsVisible: true };

    private void OnChatChanged(string peerId, Core.Chat.ChatMessage? message)
    {
        Panel.SyncChats(Agent.Chats.Summaries(), Agent.DeviceId);
        var unread = Agent.Chats.TotalUnread();
        if (_panelWindow is not null)
            _panelWindow.Title = unread > 0 ? $"({unread}) Al Toque" : "Al Toque";
        UpdateTray();
        var fresh = message is { } m && m.Author == peerId && DateTimeOffset.UtcNow - m.ReceivedAt < TimeSpan.FromSeconds(15);
        if (fresh && message is { IsFiles: false } text)
        {
            if (!IsBeingRead(peerId))
                ShowMessageToast(peerId, text.Text ?? "");
            // El chat fijado ya lo muestra; si no se está usando esa ventana, un sonido avisa.
            else if (_pinned.TryGetValue(peerId, out var pinned) && !pinned.IsActive && Agent.Settings.Current.Sounds)
                SystemPrefs.Play(SystemPrefs.Sound.Incoming);
        }
        // Lo que ya se leyó deja de avisar.
        foreach (var (_, vm) in _messageToasts.Where(t => Agent.Chats.Unread(t.Vm.PeerId) == 0).ToList())
            CloseMessageToast(vm);
    }

    private void ShowMessageToast(string peerId, string text)
    {
        var existing = _messageToasts.FirstOrDefault(t => t.Vm.PeerId == peerId);
        if (existing.Vm is not null)
        {
            existing.Vm.Add(text);
            return;
        }
        var info = Agent.Peers.Find(peerId);
        var name = info?.DisplayName ?? Agent.Trust.Find(peerId)?.DisplayName ?? "";
        var vm = new MessageToastViewModel(peerId, name, info?.Avatar, text, ReplyFromToast, CloseMessageToast);
        var window = new MessageToastWindow { DataContext = vm };
        window.LayoutChanged += LayoutToasts;
        _messageToasts.Add((window, vm));
        window.Show();
        LayoutToasts();
        if (Agent.Settings.Current.Sounds)
            SystemPrefs.Play(SystemPrefs.Sound.Incoming);
    }

    private void ReplyFromToast(MessageToastViewModel vm)
    {
        CloseMessageToast(vm);
        OpenChat(vm.PeerId);
    }

    /// <summary>Abre la conversación: la fijada en la esquina si existe; si no, dentro del panel.</summary>
    public void OpenChat(string peerId)
    {
        if (FocusPinned(peerId))
            return;
        ShowPanel();
        Panel.OpenChat(peerId);
        UpdateChatVisibility();
    }

    private void CloseMessageToast(MessageToastViewModel vm)
    {
        var toast = _messageToasts.FirstOrDefault(t => t.Vm == vm);
        if (toast.Window is null)
            return;
        _messageToasts.Remove(toast);
        toast.Window.Close();
        LayoutToasts();
    }

    // ── Chats fijados en la esquina ──────────────────────────────────────────────

    /// <summary>Si ese chat está fijado, lo trae a la vista (y devuelve true).</summary>
    public bool FocusPinned(string peerId)
    {
        if (!_pinned.TryGetValue(peerId, out var window))
            return false;
        if (window.IsMinimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        window.FocusComposer();
        return true;
    }

    /// <summary>«Fijar en la esquina»: el chat sale del panel a su propia ventana, siempre encima.</summary>
    public void PinChat(string peerId)
    {
        if (Agent.Trust.Find(peerId) is null)
            return;
        if (Panel.Chat?.PeerId == peerId)
            Panel.CloseChat();
        if (!_pinned.ContainsKey(peerId) && _pinned.Count >= PinnedChat.Max)
            ClosePinned(_pinned.Keys.First());
        OpenPinned(peerId, state: null);
        FocusPinned(peerId);
        SavePinned();
    }

    /// <summary>«Devolver al panel»: se cierra la ventana fijada y la conversación sigue en el panel.</summary>
    private void UnpinChat(string peerId)
    {
        ClosePinned(peerId);
        SavePinned();
        ShowPanel();
        Panel.OpenChat(peerId);
        UpdateChatVisibility();
    }

    private void MinimizePinned(string peerId)
    {
        if (_pinned.TryGetValue(peerId, out var window))
            window.WindowState = WindowState.Minimized;
    }

    private void ClosePinned(string peerId)
    {
        if (!_pinned.Remove(peerId, out var window))
            return;
        window.AllowClose = true;
        window.Close();
        window.Vm?.Dispose();
        LayoutCorner();
    }

    private void OpenPinned(string peerId, PinnedChat? state)
    {
        if (_pinned.ContainsKey(peerId))
            return;
        ChatWindow? window = null;
        var vm = new ChatViewModel(Agent, peerId, ChatActionsFor(peerId, () => window, back: null, pinned: true), pinned: true);
        window = new ChatWindow { DataContext = vm };
        if (state is { Width: { } width, Height: { } height })
        {
            window.Width = Math.Clamp(width, window.MinWidth, 900);
            window.Height = Math.Clamp(height, window.MinHeight, 1200);
        }
        window.MinimizeRequested += () => MinimizePinned(peerId);
        window.PlacementChanged += () =>
        {
            LayoutCorner();
            SavePinnedSoon();
        };
        window.FilesDropped += paths => _ = SendToPeerAsync(peerId, NameOf(peerId), paths, null);
        window.ImagePasted += bitmap => _ = SendPastedImageAsync(peerId, bitmap);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property != Window.WindowStateProperty)
                return;
            UpdateChatVisibility();
            LayoutCorner();
            SavePinnedSoon();
        };
        window.Opened += (_, _) =>
        {
            LayoutCorner();
            UpdateChatVisibility();
        };
        _pinned[peerId] = window;
        if (state is { X: { } x, Y: { } y } && IsOnAScreen(window, new Avalonia.PixelPoint(x, y)))
        {
            window.UserPlaced = true;
            window.Position = new Avalonia.PixelPoint(x, y);
        }
        else
        {
            FloatingWindow.PlaceBottomRight(window, 0, PinnedWidth());
        }
        if (state is { Minimized: true })
            window.WindowState = WindowState.Minimized;
        window.Show();
    }

    /// <summary>Que la ventana no quede fuera de la pantalla (se desconectó el monitor donde estaba).</summary>
    private static bool IsOnAScreen(Window window, Avalonia.PixelPoint point) =>
        window.Screens.All.Any(s => s.WorkingArea.Contains(new Avalonia.PixelPoint(point.X + 40, point.Y + 20)));

    /// <summary>Al iniciar: los chats que estaban fijados vuelven a su sitio.</summary>
    private void RestorePinned()
    {
        foreach (var state in Agent.Settings.Current.PinnedChats ?? [])
        {
            if (Agent.Trust.Find(state.PeerId) is not null)
                OpenPinned(state.PeerId, state);
        }
    }

    /// <summary>Lo que ocupan, desde la esquina, los chats fijados que están en su sitio (no movidos a mano).</summary>
    private double PinnedWidth()
    {
        var gap = OperatingSystem.IsWindows() ? FloatingWindow.ShadowMargin : -8;
        return _pinned.Values.Where(w => w is { IsVisible: true, IsMinimized: false, UserPlaced: false }).Sum(w => w.Bounds.Width - gap);
    }

    /// <summary>De la esquina hacia la izquierda: chats fijados, el panel (si está junto a la bandeja) y las tarjetas.</summary>
    private void LayoutCorner()
    {
        var gap = OperatingSystem.IsWindows() ? FloatingWindow.ShadowMargin : -8;
        var offset = 0.0;
        foreach (var window in _pinned.Values.Where(w => w is { IsVisible: true, IsMinimized: false, UserPlaced: false }))
        {
            FloatingWindow.PlaceBottomRight(window, 0, offset);
            offset += window.Bounds.Width - gap;
        }
        if (_panelWindow is { } panel)
        {
            panel.TrayOffset = offset;
            if (panel is { IsVisible: true, IsMinimized: false })
                panel.PlaceNearTray();
        }
        LayoutToasts();
    }

    private void SavePinnedSoon()
    {
        if (_pinSaveTimer is null)
        {
            _pinSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _pinSaveTimer.Tick += (_, _) =>
            {
                _pinSaveTimer.Stop();
                SavePinned();
            };
        }
        _pinSaveTimer.Stop();
        _pinSaveTimer.Start();
    }

    private void SavePinned()
    {
        var list = _pinned.Select(kv => new PinnedChat
        {
            PeerId = kv.Key,
            X = kv.Value.UserPlaced ? kv.Value.Position.X : null,
            Y = kv.Value.UserPlaced ? kv.Value.Position.Y : null,
            Width = kv.Value.ClientSize.Width >= kv.Value.MinWidth ? kv.Value.ClientSize.Width : null,
            Height = kv.Value.ClientSize.Height >= kv.Value.MinHeight ? kv.Value.ClientSize.Height : null,
            Minimized = kv.Value.IsMinimized,
        }).ToList();
        Agent.Settings.Update(s => s.PinnedChats = list.Count > 0 ? list : null);
    }

    // ── Emparejamiento ───────────────────────────────────────────────────────────

    public void ShowPairing()
    {
        if (Pairing is null)
        {
            Pairing = new PairingViewModel(this);
            Pairing.FinishRequested += () => _pairingWindow?.Close();
        }
        if (_pairingWindow is null)
        {
            _pairingWindow = new PairingWindow { DataContext = Pairing };
            _pairingWindow.Closed += (_, _) =>
            {
                _pairingWindow = null;
                // Si quedaba un código sin responder, se rechaza ya: el otro equipo no se queda esperando.
                if (Pairing.Prompt is { Outcome: null } pending)
                    pending.Confirm(false);
                Agent.Pairing.CloseWindow();
            };
            Pairing.Reset();
        }
        Pairing.SyncNetwork(Agent.IsReceivingPausedByNetwork);
        Agent.Pairing.OpenWindow();
        Agent.Discovery?.Query();
        RefreshPeers();
        Pairing.Tick();
        _pairingWindow.Show();
        _pairingWindow.Activate();
    }

    /// <summary>
    /// Clic en un equipo de «Cerca de ti»: se le pide conectarse sin que tenga que abrir «Emparejar». La ventana de
    /// emparejamiento muestra el código; lo que estaba esperando destino (clic derecho) sale al quedar conectados.
    /// </summary>
    public void RequestConnection(PeerViewModel peer, IReadOnlyList<string>? pending, string? message)
    {
        _afterPairing = pending is { Count: > 0 } ? (peer.DeviceId, pending, message) : null;
        ShowPairing();
        Pairing!.PairWith.Execute(peer);
    }

    private (string PeerId, IReadOnlyList<string> Paths, string? Message)? _afterPairing;
    private readonly List<(PairRequestWindow Window, PairRequestViewModel Vm)> _pairRequests = [];

    /// <summary>Alguien cercano pidió conectarse: aviso flotante con el código, «Conectar» y «Ahora no».</summary>
    private void ShowPairRequest(PairingPrompt prompt)
    {
        var vm = new PairRequestViewModel(prompt, OpenChat, ClosePairRequest);
        var window = new PairRequestWindow { DataContext = vm };
        window.LayoutChanged += LayoutToasts;
        prompt.Aborted.Register(() => Post(() =>
        {
            // El otro se retiró antes de que se respondiera: el aviso se va.
            if (vm.IsAsking)
                ClosePairRequest(vm);
        }));
        _pairRequests.Add((window, vm));
        window.Show();
        LayoutToasts();
        if (Agent.Settings.Current.Sounds)
            SystemPrefs.Play(SystemPrefs.Sound.Incoming);
    }

    private void ClosePairRequest(PairRequestViewModel vm)
    {
        var entry = _pairRequests.FirstOrDefault(r => r.Vm == vm);
        if (entry.Window is null)
            return;
        _pairRequests.Remove(entry);
        entry.Window.Close();
        LayoutToasts();
    }

    private void OnPairingPrompt(PairingPrompt prompt)
    {
        if (prompt.IsRequest)
        {
            ShowPairRequest(prompt);
            return;
        }
        if (_pairingWindow is null)
            ShowPairing();
        Pairing!.ShowPrompt(prompt);
        _pairingWindow!.Activate();
        if (Agent.Settings.Current.Sounds)
            SystemPrefs.Play(SystemPrefs.Sound.Incoming);
    }

    private void OnPairingFinished(string deviceId, PairingOutcome outcome)
    {
        // Lo que esperaba destino sale en cuanto quedan conectados.
        if (_afterPairing is { } after && after.PeerId == deviceId)
        {
            _afterPairing = null;
            if (outcome == PairingOutcome.Paired)
                _ = SendToPeerAsync(deviceId, NameOf(deviceId), after.Paths, after.Message);
        }
        if (Pairing is null || _pairingWindow is null)
            return;
        var name = Pairing.Prompt?.PeerName ?? Agent.Trust.Find(deviceId)?.DisplayName ?? "";
        Pairing.ShowOutcome(outcome, name);
    }

    /// <summary>Se cambió «Visible para equipos cercanos»: el anuncio sale ya y los textos del panel cambian.</summary>
    public void OnVisibilityChanged()
    {
        Agent.Discovery?.AnnounceSoon();
        RefreshMode();
    }

    // ── Otras ventanas ───────────────────────────────────────────────────────────

    public void ShowConnect(IReadOnlyList<string>? paths, bool showMyCode)
    {
        Connect ??= new ConnectViewModel(this);
        Connect.Prepare(paths, showMyCode);
        if (_connectWindow is null)
        {
            _connectWindow = new ConnectWindow { DataContext = Connect };
            _connectWindow.Closed += (_, _) => _connectWindow = null;
        }
        _connectWindow.Show();
        _connectWindow.Activate();
    }

    public void ShowMain(int tab)
    {
        if (_mainWindow is null)
        {
            Main = new MainViewModel(this);
            _mainWindow = new MainWindow { DataContext = Main };
            _mainWindow.Closed += (_, _) =>
            {
                _mainWindow = null;
                Main = null;
            };
        }
        Main!.Tab = tab;
        // Ajustes abre su propia ventana: la principal sigue donde estaba.
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ShowWelcome()
    {
        var window = new WelcomeWindow
        {
            DataContext = new WelcomeViewModel(Agent.DeviceId, Agent.Settings.Current.Alias, Agent.Settings.Current.Avatar),
            ReduceMotion = ReduceMotion,
        };
        window.Started += (alias, avatar) => Agent.Settings.Update(s =>
        {
            s.Alias = TextLimits.CleanAlias(alias) ?? s.Alias;
            s.Avatar = avatar;
            s.WelcomeDone = true;
        });
        window.Closed += (_, _) =>
        {
            if (!Agent.Settings.Current.WelcomeDone)
                Agent.Settings.Update(s => s.WelcomeDone = true);
            ShowPanel();
        };
        window.Show();
    }

    // ── Actualizaciones ──────────────────────────────────────────────────────────

    private void RefreshUpdateItem()
    {
        if (_updateItem is null || Update is null)
            return;
        _updateItem.IsVisible = Update.HasNews && _updates?.CanInstall == true;
        _updateItem.Header = S.TrayUpdate(_updates?.AvailableVersion ?? "");
    }

    /// <summary>"Actualizar": descarga si hace falta y reinicia en la versión nueva.</summary>
    public async void InstallUpdate()
    {
        if (_updates is null)
            return;
        if (_updates.Status == UpdateStatus.Available)
        {
            if (!_updates.CanInstall)
            {
                Opener.OpenUrl(Updates.ReleasesUrl);
                return;
            }
            await _updates.DownloadAsync();
        }
        if (_updates.Status != UpdateStatus.Ready)
            return;
        if (Transfers.Items.Any(t => t.IsActive) && !await ConfirmWindow.AskAsync(S.UpdateCutsTransfers))
            return;
        AgentLog.Write("Reiniciando para actualizar a " + _updates.AvailableVersion);
        _restartingForUpdate = true;
        IsQuitting = true;
        Cleanup();
        _updates.ApplyAndRestart(Program.StartedInBackground ? ["--background"] : []);
        _desktop.Shutdown();
    }

    /// <summary>Primera ejecución tras actualizar: Chasqui lo celebra en el panel.</summary>
    private void CelebrateUpdate()
    {
        var last = Agent.Settings.Current.LastVersion;
        if (last == AppInfo.Version)
            return;
        Agent.Settings.Update(s => s.LastVersion = AppInfo.Version);
        if (last is not null && Updates.IsNewer(AppInfo.Version, last))
            Panel.Flash(ChasquiState.Success, S.UpdatedTo(AppInfo.Version));
    }

    public async void ConfirmResetIdentity()
    {
        if (!await ConfirmWindow.AskAsync(S.ResetIdentityConfirm))
            return;
        Agent.ResetIdentity();
        Restart();
    }

    private void Restart()
    {
        if (Environment.ProcessPath is { } exe)
        {
            // La nueva instancia espera a que esta libere el candado de instancia única.
            Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = false })?.Dispose();
        }
        Quit();
    }

    public void Quit()
    {
        if (IsQuitting)
            return;
        // Antes de que se cierren las ventanas: los chats fijados vuelven así la próxima vez.
        if (_pinned.Count > 0)
            SavePinned();
        IsQuitting = true;
        Cleanup();
        _desktop.Shutdown();
    }

    private bool _cleanedUp;

    private void Cleanup()
    {
        if (_cleanedUp)
            return;
        _cleanedUp = true;
        _fastTimer.Stop();
        _slowTimer.Stop();
        _tray?.Dispose();
        // Con una versión ya descargada, se instala al terminar este proceso (sin volver a abrirse).
        if (!_restartingForUpdate)
            _updates?.ApplyOnExit();
        _updates?.Dispose();
        try
        {
            Agent.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }
}

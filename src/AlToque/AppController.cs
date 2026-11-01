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
            Agent = AgentHost.Create(new AgentOptions { IsPublicNetwork = NetworkCategory.IsOnPublicNetwork });
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
        _tray.ToolTipText = paused ? S.TrayTooltipPaused : mode == ReceiveMode.Everyone && Agent.EveryoneUntil is { } u
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
            _panelWindow.SizeChanged += (_, _) =>
            {
                _panelWindow.PlaceNearTray();
                LayoutToasts();
            };
            _panelWindow.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.WindowStateProperty)
                    LayoutToasts();
            };
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
        }
        else
        {
            if (!await ConfirmWindow.AskAsync(S.HidePeerConfirm(peer.Name)))
                return;
            Agent.Block(peer.DeviceId);
        }
        RefreshPeers();
    }

    /// <summary>Clic en un compañero: envía lo pendiente, pide archivos o, si quiere emparejar, empareja.</summary>
    public async void OnPeerClicked(PeerViewModel peer, IReadOnlyList<string>? paths, string? message)
    {
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

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var owner = (Window?)_panelWindow ?? _mainWindow;
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
        Pairing?.SyncCandidates(snapshot);
    }

    private void RefreshRecent() => Panel.SyncRecent(Agent.History.Recent(4));

    private void RefreshMode()
    {
        Panel.SyncMode(Agent.EffectiveMode, Agent.EveryoneUntil, Agent.Trust.All.Count, Agent.IsReceivingPausedByNetwork);
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
        // Las tarjetas se ponen al lado de la ventana principal si está en su sitio junto a la bandeja.
        var left = _panelWindow is { IsVisible: true, IsMinimized: false, UserPlaced: false } p ? p.Bounds.Width : 0;
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

    private void OnPairingPrompt(PairingPrompt prompt)
    {
        if (_pairingWindow is null)
            ShowPairing();
        Pairing!.ShowPrompt(prompt);
        _pairingWindow!.Activate();
        if (Agent.Settings.Current.Sounds)
            SystemPrefs.Play(SystemPrefs.Sound.Incoming);
    }

    private void OnPairingFinished(string deviceId, PairingOutcome outcome)
    {
        if (Pairing is null || _pairingWindow is null)
            return;
        var name = Pairing.Prompt?.PeerName ?? Agent.Trust.Find(deviceId)?.DisplayName ?? "";
        Pairing.ShowOutcome(outcome, name);
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

using System.Diagnostics;
using AlToque.Chasqui;
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

    public bool ReduceMotion => Agent.Settings.Current.ReduceMotion || _systemReduceMotion;

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
        _updates = new Updates();

        if (!Agent.Settings.Current.WelcomeDone)
            ShowWelcome();
        else if (!Program.StartedInBackground)
            ShowPanel();
    }

    private void WireAgent()
    {
        Agent.OfferReceived += o => Post(() => ShowOffer(o));
        Agent.TransferStarted += i => Post(() => AddTransfer(i));
        Agent.TransferFinished += i => Post(() => OnTransferFinished(i));
        Agent.ReceiveModeChanged += () => Post(RefreshMode);
        Agent.NetworkStateChanged += () => Post(RefreshMode);
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

    public void TogglePanel()
    {
        if (_panelWindow is { IsVisible: true })
            HidePanel();
        else if (_panelWindow is null || Environment.TickCount64 - _panelWindow.LastAutoHide > 400)
            ShowPanel(); // Si se acaba de ocultar por el mismo clic en la bandeja, el clic era para cerrarlo.
    }

    public void ShowPanel(IReadOnlyList<string>? pendingPaths = null)
    {
        if (_panelWindow is null)
        {
            _panelWindow = new QuickPanelWindow { DataContext = Panel };
            _panelWindow.SizeChanged += (_, _) =>
            {
                _panelWindow.PlaceNearTray();
                LayoutToasts();
            };
        }
        if (pendingPaths is not null)
            Panel.SetPending(pendingPaths);
        Panel.BeginSearch();
        Agent.Discovery?.Query();
        DispatcherTimer.RunOnce(() => Panel.EndSearch(), TimeSpan.FromSeconds(2.5));
        _panelWindow.Show();
        _panelWindow.PlaceNearTray();
        _panelWindow.Activate();
        LayoutToasts();
    }

    public void HidePanel()
    {
        _panelWindow?.Hide();
        Panel.SetPending(null);
        LayoutToasts();
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
            HidePanel();
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
        var snapshot = Agent.Peers.Snapshot();
        Panel.SyncPeers(snapshot);
        Pairing?.SyncCandidates(snapshot);
    }

    private void RefreshRecent() => Panel.SyncRecent(Agent.History.Recent(4));

    private void RefreshMode()
    {
        Panel.SyncMode(Agent.EffectiveMode, Agent.EveryoneUntil, Agent.Trust.All.Count == 0, Agent.IsReceivingPausedByNetwork);
        Pairing?.SyncNetwork(Agent.IsReceivingPausedByNetwork);
        UpdateTray();
    }

    // ── Transferencias y Chasqui flotante ────────────────────────────────────────

    private void AddTransfer(TransferInfo info)
    {
        if (Transfers.Items.Any(t => t.Info == info))
            return;
        var vm = new TransferViewModel(info, DismissTransfer, Agent.CancelTransfer);
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

    private void DismissTransfer(TransferViewModel vm)
    {
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
        var vm = new OfferViewModel(offer);
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
        var left = _panelWindow is { IsVisible: true } p ? p.Bounds.Width : 0;
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
        Pairing ??= new PairingViewModel(this);
        if (_pairingWindow is null)
        {
            _pairingWindow = new PairingWindow { DataContext = Pairing };
            _pairingWindow.Closed += (_, _) =>
            {
                _pairingWindow = null;
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
        _mainWindow.Show();
        _mainWindow.Activate();
        HidePanel();
    }

    private void ShowWelcome()
    {
        var window = new WelcomeWindow { Alias = Agent.Settings.Current.Alias, ReduceMotion = ReduceMotion };
        window.Started += alias => Agent.Settings.Update(s =>
        {
            s.Alias = TextLimits.CleanAlias(alias) ?? s.Alias;
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
        try
        {
            Agent.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }
}

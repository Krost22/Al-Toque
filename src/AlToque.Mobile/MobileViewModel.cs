using System.Collections.ObjectModel;
using AlToque.Chasqui;
using AlToque.Controls.Avatars;
using AlToque.Core;
using AlToque.Core.Agent;
using AlToque.Core.Discovery;
using AlToque.Core.Pairing;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;
using AlToque.Localization;
using AlToque.ViewModels;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AlToque.Mobile;

/// <summary>Hojas que se abren desde abajo sobre la pantalla principal.</summary>
public enum MobileSheet
{
    None,
    Welcome,
    Settings,
    Pair,
    Connect,
    MyCode,
}

/// <summary>
/// La pantalla del teléfono: compañeros, lo pendiente de enviar, transferencias, recientes y quién puede enviarte. Las
/// solicitudes, el emparejamiento, el código y los ajustes se abren como hojas.
/// </summary>
public sealed class MobileViewModel : ViewModelBase
{
    private static readonly TimeSpan SendSuccessLinger = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan OtherLinger = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FlashDuration = TimeSpan.FromSeconds(4);

    private readonly AgentHost _agent;
    private readonly IMobilePlatform _platform;
    private readonly DispatcherTimer _slow = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _fast = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly List<OfferViewModel> _offers = [];
    private readonly Dictionary<Guid, string> _outgoingCopies = [];
    private IReadOnlyList<string> _pending = [];
    private string? _pendingCopyDir;
    private bool _isPreparing;
    private MobileSheet _sheet;
    private string _editName = "";
    private string _myAvatarId;
    private string? _flash;
    private DateTimeOffset _flashUntil;
    private ChasquiState _chasqui = ChasquiState.Idle;
    private string? _updateVersion;
    private string? _updateUrl;
    private bool _updateDismissed;
    private string _updateStatus = "";
    private bool _busy;

    public MobileViewModel(AgentHost agent, IMobilePlatform platform)
    {
        _agent = agent;
        _platform = platform;
        MyColorHex = IdentityVisuals.ColorHex(agent.DeviceId);
        _myAvatarId = AvatarCatalog.Resolve(agent.Settings.Current.Avatar, agent.DeviceId);
        AvatarChoices = AvatarCatalog.Ids.Select(id => new AvatarChoiceViewModel(id, MyColorHex, MyInitial, PickAvatar) { IsSelected = id == _myAvatarId }).ToList();

        SendFiles = new RelayCommand(() => _ = PickPendingAsync());
        PeerTapped = new RelayCommand(p => _ = OnPeerTappedAsync(p as PeerViewModel));
        ClearPending = new RelayCommand(() => SetPending([], null));
        CloseSheet = new RelayCommand(Close);
        OpenSettings = new RelayCommand(() => Open(MobileSheet.Settings));
        OpenPair = new RelayCommand(() => Open(MobileSheet.Pair));
        OpenConnect = new RelayCommand(() => Open(MobileSheet.Connect));
        OpenMyCode = new RelayCommand(() => Open(MobileSheet.MyCode));
        SaveWelcome = new RelayCommand(() =>
        {
            ApplyName();
            _agent.Settings.Update(s => s.WelcomeDone = true);
            Open(MobileSheet.None);
        });
        PairWith = new RelayCommand(p => _ = PairWithAsync(p as PeerViewModel));
        PairMatches = new RelayCommand(() => AnswerPair(true));
        PairNoMatch = new RelayCommand(() => AnswerPair(false));
        ConnectFind = new RelayCommand(() => _ = FindAsync(), () => !ConnectBusy);
        ConnectSend = new RelayCommand(() => _ = SendToFoundAsync());
        OpenDownloads = new RelayCommand(() => _platform.OpenReceived(null));
        DownloadUpdate = new RelayCommand(() => _platform.OpenUrl(_updateUrl ?? ReleaseCheck.ReleasesUrl));
        DismissUpdate = new RelayCommand(() =>
        {
            _updateDismissed = true;
            Raise(nameof(ShowUpdate));
        });
        CheckUpdates = new RelayCommand(() => _ = CheckUpdatesAsync(manual: true));

        _agent.OfferReceived += o => Post(() => AddOffer(o));
        _agent.TransferStarted += i => Post(() => AddTransfer(i));
        _agent.TransferFinished += i => Post(() => OnTransferFinished(i));
        _agent.ReceiveModeChanged += () => Post(RefreshMode);
        _agent.Peers.Changed += () => Post(RefreshPeers);
        _agent.Trust.Changed += () => Post(() =>
        {
            RefreshPeers();
            RefreshMode();
        });
        _agent.History.Changed += () => Post(RefreshRecent);
        _agent.Pairing.PromptRequested += p => Post(() => ShowPairPrompt(p));
        _agent.Pairing.Finished += (id, outcome) => Post(() => ShowPairOutcome(id, outcome));

        _slow.Tick += (_, _) => SlowTick();
        _fast.Tick += (_, _) => FastTick();
        _slow.Start();
        RefreshPeers();
        RefreshRecent();
        RefreshMode();
        if (!_agent.Settings.Current.WelcomeDone)
            Open(MobileSheet.Welcome);
        DispatcherTimer.RunOnce(() => _ = CheckUpdatesAsync(manual: false), TimeSpan.FromSeconds(5));
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    /// <summary>La vista entrega el selector de archivos del sistema.</summary>
    public Func<Task<IReadOnlyList<IStorageFile>>>? PickFiles { get; set; }

    // ── Yo ───────────────────────────────────────────────────────────────────────

    public string MyName => _agent.Settings.Current.Alias;
    public string MyColorHex { get; }
    public string MyInitial => IdentityVisuals.Initial(MyName);
    public string MyAvatarId { get => _myAvatarId; private set => Set(ref _myAvatarId, value); }
    public string MySymbols => IdentityVisuals.SymbolSequence(_agent.DeviceId);
    public string MyFingerprint => S.YourFingerprint(IdentityVisuals.ShortCode(_agent.DeviceId));
    public IReadOnlyList<AvatarChoiceViewModel> AvatarChoices { get; }

    public string EditName
    {
        get => _editName;
        set
        {
            if (!Set(ref _editName, value))
                return;
            foreach (var c in AvatarChoices)
                c.Initial = IdentityVisuals.Initial(value);
        }
    }

    private void PickAvatar(AvatarChoiceViewModel choice)
    {
        foreach (var c in AvatarChoices)
            c.IsSelected = c == choice;
        MyAvatarId = choice.Id;
        _agent.Settings.Update(s => s.Avatar = choice.Id);
    }

    private void ApplyName()
    {
        var name = TextLimits.CleanAlias(EditName);
        if (name is not null && name != _agent.Settings.Current.Alias)
            _agent.Settings.Update(s => s.Alias = name);
        Raise(nameof(MyName));
        Raise(nameof(MyInitial));
    }

    // ── Chasqui y aviso breve ────────────────────────────────────────────────────

    public ChasquiState Chasqui { get => _chasqui; private set => Set(ref _chasqui, value); }

    public string HeroTitle => _agent.EffectiveMode == ReceiveMode.Nobody ? S.MobileReceivingPaused : S.MobileReadyTitle;
    public string HeroBody => S.MobileReadyBody;

    public string? Flash
    {
        get => _flash;
        private set
        {
            if (Set(ref _flash, value))
                Raise(nameof(HasFlash));
        }
    }

    public bool HasFlash => Flash is not null;

    private void ShowFlash(string text)
    {
        Flash = text;
        _flashUntil = DateTimeOffset.Now + FlashDuration;
    }

    // ── Compañeros ───────────────────────────────────────────────────────────────

    public ObservableCollection<PeerViewModel> Peers { get; } = [];
    public bool HasPeers => Peers.Count > 0;
    public bool ShowNobody => Peers.Count == 0;
    public string PeersCountText => S.InThisNetwork(Peers.Count(p => p.IsOnline));
    public RelayCommand PeerTapped { get; }

    private void RefreshPeers()
    {
        var snapshot = _agent.Peers.Snapshot()
            .OrderByDescending(p => p.Presence == PeerPresence.Online)
            .ThenByDescending(p => p.IsFavorite)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        SyncPeers(Peers, snapshot);
        SyncPeers(PairCandidates, snapshot.Where(p => p.Pairing && p.Presence == PeerPresence.Online && p.EndPoint is not null).ToList());
        Raise(nameof(HasPeers));
        Raise(nameof(ShowNobody));
        Raise(nameof(PeersCountText));
        Raise(nameof(HasPairCandidates));
        Raise(nameof(NoPairCandidates));
        UpdateChasqui();
    }

    private static void SyncPeers(ObservableCollection<PeerViewModel> list, IReadOnlyList<PeerInfo> snapshot)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (snapshot.All(p => p.DeviceId != list[i].DeviceId))
                list.RemoveAt(i);
        }
        for (int i = 0; i < snapshot.Count; i++)
        {
            var info = snapshot[i];
            var index = IndexOf(list, info.DeviceId);
            var vm = index >= 0 ? list[index] : new PeerViewModel(info.DeviceId);
            vm.Update(info);
            if (index < 0)
                list.Insert(i, vm);
            else if (index != i)
                list.Move(index, i);
        }
    }

    private static int IndexOf(ObservableCollection<PeerViewModel> list, string deviceId)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].DeviceId == deviceId)
                return i;
        }
        return -1;
    }

    private async Task OnPeerTappedAsync(PeerViewModel? peer)
    {
        if (peer is null)
            return;
        if (peer.IsPairing && !peer.IsTrusted)
        {
            Open(MobileSheet.Pair);
            await PairWithAsync(peer);
            return;
        }
        if (!HasPending)
            await PickPendingAsync();
        if (!HasPending)
            return;
        var paths = _pending;
        var copies = _pendingCopyDir;
        _pendingCopyDir = null;
        SetPending([], null);
        await SendAsync(peer.DeviceId, peer.Name, paths, copies);
    }

    private async Task SendAsync(string deviceId, string name, IReadOnlyList<string> paths, string? copies)
    {
        var target = await _agent.LocateAsync(deviceId, TimeSpan.FromSeconds(1.5));
        if (target is null)
        {
            ShowFlash(S.MobileUnreachable(name));
            SetPending(paths, copies);
            return;
        }
        var info = _agent.BeginSend(target, paths);
        if (copies is not null)
            _outgoingCopies[info.Id] = copies;
    }

    // ── Pendiente de enviar ──────────────────────────────────────────────────────

    public bool HasPending => _pending.Count > 0;
    public bool ShowReadyCard => !HasPending && !IsPreparing;

    public string PendingTitle
    {
        get
        {
            long size = 0;
            foreach (var p in _pending)
            {
                try
                {
                    size += File.Exists(p) ? new FileInfo(p).Length : 0;
                }
                catch (IOException)
                {
                }
            }
            return S.MobilePendingTitle(_pending.Count, Format.Bytes(size));
        }
    }

    public bool IsPreparing
    {
        get => _isPreparing;
        private set
        {
            if (Set(ref _isPreparing, value))
                Raise(nameof(ShowReadyCard));
        }
    }

    public RelayCommand SendFiles { get; }
    public RelayCommand ClearPending { get; }

    /// <summary>Archivos que llegan desde "Compartir" (ya copiados en la carpeta temporal).</summary>
    public void Share(IReadOnlyList<string> paths, string? copiesDir)
    {
        if (paths.Count == 0)
            return;
        Close();
        SetPending(paths, copiesDir);
    }

    private void SetPending(IReadOnlyList<string> paths, string? copiesDir)
    {
        if (_pendingCopyDir is not null && _pendingCopyDir != copiesDir)
            OutgoingFiles.Delete(_pendingCopyDir);
        _pending = paths;
        _pendingCopyDir = copiesDir;
        Raise(nameof(HasPending));
        Raise(nameof(ShowReadyCard));
        Raise(nameof(PendingTitle));
        Raise(nameof(PendingHint));
        UpdateChasqui();
    }

    public string PendingHint => S.MobilePendingHint;

    private async Task PickPendingAsync()
    {
        if (PickFiles is null)
            return;
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await PickFiles();
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudo abrir el selector de archivos: " + ex.Message);
            return;
        }
        if (files.Count == 0)
            return;
        IsPreparing = true;
        try
        {
            var (paths, dir) = await OutgoingFiles.MaterializeAsync(files, _platform.CacheDirectory);
            SetPending(paths, dir);
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudieron preparar los archivos: " + ex.Message);
            ShowFlash(S.ErrorText("E_IO", "", ex.Message));
        }
        finally
        {
            IsPreparing = false;
            foreach (var f in files)
                f.Dispose();
        }
    }

    // ── Transferencias y recientes ───────────────────────────────────────────────

    public ObservableCollection<TransferViewModel> Transfers { get; } = [];
    public bool HasTransfers => Transfers.Count > 0;
    public ObservableCollection<HistoryItemViewModel> Recent { get; } = [];
    public bool HasRecent => Recent.Count > 0;

    private void AddTransfer(TransferInfo info)
    {
        if (Transfers.Any(t => t.Info == info))
            return;
        var vm = new TransferViewModel(info, Dismiss, _agent.CancelTransfer, Retry, () => Open(MobileSheet.Connect), _platform.OpenReceived);
        vm.OpenFileRequested += t => _platform.OpenReceived(t.Info.CompletedPaths.FirstOrDefault());
        Transfers.Insert(0, vm);
        Raise(nameof(HasTransfers));
        _fast.Start();
        SetBusy(true);
        UpdateChasqui();
    }

    private void OnTransferFinished(TransferInfo info)
    {
        if (info.Direction == TransferDirection.Receive && info.CompletedPaths.Count > 0)
            _platform.PublishReceived(info.CompletedPaths);
        if (info.IsSuccess && _outgoingCopies.Remove(info.Id, out var copies))
            OutgoingFiles.Delete(copies);
        Transfers.FirstOrDefault(t => t.Info == info)?.Refresh();
        Chasqui = ChasquiStates.For(info);
        SetBusy(Transfers.Any(t => t.IsActive));
    }

    private void Dismiss(TransferViewModel vm)
    {
        if (Transfers.Remove(vm))
            Raise(nameof(HasTransfers));
        if (!vm.Info.IsSuccess && _outgoingCopies.Remove(vm.Info.Id, out var copies))
            OutgoingFiles.Delete(copies);
    }

    private void Retry(TransferViewModel vm)
    {
        var info = vm.Info;
        _outgoingCopies.Remove(info.Id, out var copies);
        Transfers.Remove(vm);
        Raise(nameof(HasTransfers));
        if (info.PeerId is { } id)
            _ = SendAsync(id, info.PeerName, info.SourcePaths, copies);
    }

    private void RefreshRecent()
    {
        Recent.Clear();
        foreach (var e in _agent.History.Recent(5))
            Recent.Add(new HistoryItemViewModel(e, open: h => _platform.OpenReceived(h.FirstFile ?? h.Folder)));
        Raise(nameof(HasRecent));
    }

    private void SetBusy(bool busy)
    {
        if (busy == _busy)
            return;
        _busy = busy;
        _platform.SetBusy(busy);
    }

    private void FastTick()
    {
        foreach (var t in Transfers)
        {
            if (t.IsActive || t.FinishedAt is null)
                t.Refresh();
        }
        if (!Transfers.Any(t => t.IsActive))
            _fast.Stop();
        UpdateChasqui();
    }

    private void SlowTick()
    {
        var now = DateTimeOffset.Now;
        foreach (var t in Transfers.Where(t => !t.IsActive && t.FinishedAt is { } f && now - f > (t.IsSuccess && t.Info.Direction == TransferDirection.Send ? SendSuccessLinger : OtherLinger)).ToList())
            Dismiss(t);
        foreach (var o in _offers)
            o.Tick();
        if (Flash is not null && now > _flashUntil)
            Flash = null;
        if (_agent.EffectiveMode == ReceiveMode.Everyone)
            RefreshMode();
        if (Sheet == MobileSheet.Pair)
            Raise(nameof(PairWindowText));
        UpdateChasqui();
    }

    private void UpdateChasqui()
    {
        if (ChasquiStates.IsTransient(Chasqui))
        {
            // Los momentos (éxito, rechazo…) se ven unos segundos y vuelven al estado de fondo.
            if (!_transientSince.HasValue)
                _transientSince = DateTimeOffset.Now;
            if (DateTimeOffset.Now - _transientSince < TimeSpan.FromSeconds(3))
                return;
        }
        _transientSince = null;
        var active = Transfers.FirstOrDefault(t => t.IsActive);
        Chasqui = active is not null ? ChasquiStates.For(active.Info)
            : HasPending ? ChasquiState.Attentive
            : Peers.Count == 0 ? ChasquiState.Searching
            : ChasquiState.Idle;
    }

    private DateTimeOffset? _transientSince;

    // ── Solicitudes entrantes ────────────────────────────────────────────────────

    public OfferViewModel? Offer => _offers.FirstOrDefault();
    public bool HasOffer => _offers.Count > 0;

    private void AddOffer(IncomingOffer offer)
    {
        if (offer.Withdrawn.IsCancellationRequested)
            return;
        var vm = new OfferViewModel(offer, o =>
        {
            if (o.Trusted)
                _agent.Trust.Update(o.PeerId, p => p.AutoAccept = true);
        }, _ => Open(MobileSheet.Pair));
        vm.Answered += () => RemoveOffer(vm);
        offer.Withdrawn.Register(() => Post(() => RemoveOffer(vm)));
        _offers.Add(vm);
        Chasqui = ChasquiState.Incoming;
        RaiseOffer();
    }

    private void RemoveOffer(OfferViewModel vm)
    {
        if (_offers.Remove(vm))
            RaiseOffer();
    }

    private void RaiseOffer()
    {
        Raise(nameof(Offer));
        Raise(nameof(HasOffer));
        Raise(nameof(IsOverlayOpen));
    }

    // ── Hojas ────────────────────────────────────────────────────────────────────

    public MobileSheet Sheet
    {
        get => _sheet;
        private set
        {
            if (!Set(ref _sheet, value))
                return;
            Raise(nameof(IsWelcome));
            Raise(nameof(IsSettings));
            Raise(nameof(IsProfile));
            Raise(nameof(IsPair));
            Raise(nameof(IsConnect));
            Raise(nameof(IsMyCode));
            Raise(nameof(IsSheetOpen));
            Raise(nameof(IsOverlayOpen));
        }
    }

    public bool IsWelcome => Sheet == MobileSheet.Welcome;
    public bool IsSettings => Sheet == MobileSheet.Settings;
    public bool IsProfile => Sheet is MobileSheet.Welcome or MobileSheet.Settings;
    public bool IsPair => Sheet == MobileSheet.Pair;
    public bool IsConnect => Sheet == MobileSheet.Connect;
    public bool IsMyCode => Sheet == MobileSheet.MyCode;
    public bool IsSheetOpen => Sheet != MobileSheet.None;
    public bool IsOverlayOpen => IsSheetOpen || HasOffer;

    public RelayCommand CloseSheet { get; }
    public RelayCommand OpenSettings { get; }
    public RelayCommand OpenPair { get; }
    public RelayCommand OpenConnect { get; }
    public RelayCommand OpenMyCode { get; }
    public RelayCommand SaveWelcome { get; }

    private void Open(MobileSheet sheet)
    {
        if (Sheet == MobileSheet.Settings)
            ApplyName();
        if (sheet is MobileSheet.Welcome or MobileSheet.Settings)
            EditName = _agent.Settings.Current.Alias;
        if (sheet == MobileSheet.Pair)
        {
            ResetPair();
            _agent.Pairing.OpenWindow();
        }
        else if (Sheet == MobileSheet.Pair)
        {
            _agent.Pairing.CloseWindow();
        }
        if (sheet == MobileSheet.Connect)
            ResetConnect();
        if (sheet == MobileSheet.MyCode)
        {
            Raise(nameof(MyCode));
            Raise(nameof(MyAddresses));
        }
        Sheet = sheet;
    }

    private void Close()
    {
        if (Sheet == MobileSheet.Welcome)
            return; // La bienvenida se cierra con "Empezar".
        Open(MobileSheet.None);
    }

    /// <summary>Botón "atrás" del teléfono: cierra la hoja abierta. Devuelve false si no había nada que cerrar.</summary>
    public bool Back()
    {
        if (HasOffer)
            return true; // Una solicitud se responde, no se descarta con "atrás".
        if (IsSheetOpen)
        {
            Close();
            return true;
        }
        if (HasPending)
        {
            SetPending([], null);
            return true;
        }
        return false;
    }

    // ── Quién puede enviarte ─────────────────────────────────────────────────────

    public bool ModeNobody
    {
        get => _agent.EffectiveMode == ReceiveMode.Nobody;
        set
        {
            if (value)
                _agent.SetReceiveMode(ReceiveMode.Nobody);
        }
    }

    public bool ModeTrusted
    {
        get => _agent.EffectiveMode == ReceiveMode.TrustedOnly;
        set
        {
            if (value)
                _agent.SetReceiveMode(ReceiveMode.TrustedOnly);
        }
    }

    public bool ModeEveryone
    {
        get => _agent.EffectiveMode == ReceiveMode.Everyone;
        set
        {
            if (value)
                _agent.SetReceiveMode(ReceiveMode.Everyone);
        }
    }

    public string ModeHint => _agent.EffectiveMode switch
    {
        ReceiveMode.Nobody => S.ModeNobodyHint,
        ReceiveMode.Everyone => S.ModeHintEveryone(_agent.EveryoneUntil is { } u ? u - DateTimeOffset.UtcNow : null),
        _ => S.ModeHintTrusted(_agent.Trust.All.Count, _agent.IsVisibleNearby),
    };

    private void RefreshMode()
    {
        Raise(nameof(ModeNobody));
        Raise(nameof(ModeTrusted));
        Raise(nameof(ModeEveryone));
        Raise(nameof(ModeHint));
        Raise(nameof(HeroTitle));
    }

    // ── Emparejar ────────────────────────────────────────────────────────────────

    private PairingPrompt? _pairPrompt;
    private string? _pairStatus;
    private string? _pairResult;
    private bool _pairResultOk;
    private bool _pairConfirmed;
    private bool _pairConnecting;

    public ObservableCollection<PeerViewModel> PairCandidates { get; } = [];
    public bool HasPairCandidates => PairCandidates.Count > 0;
    public bool NoPairCandidates => PairCandidates.Count == 0;
    public RelayCommand PairWith { get; }
    public RelayCommand PairMatches { get; }
    public RelayCommand PairNoMatch { get; }

    public PairingPrompt? PairPrompt
    {
        get => _pairPrompt;
        private set
        {
            if (Set(ref _pairPrompt, value))
                RaisePair();
        }
    }

    public string PairCode => PairPrompt?.Code is { Length: 6 } c ? c[..3] + " " + c[3..] : PairPrompt?.Code ?? "";
    public string PairPeerName => PairPrompt?.PeerName ?? "";
    public string PairPeerColorHex => PairPrompt?.PeerColor ?? MyColorHex;
    public string PairPeerAvatarId => PairPrompt is { } p ? AvatarCatalog.Resolve(p.PeerAvatar, p.PeerId) : MyAvatarId;
    public string PairPeerInitial => IdentityVisuals.Initial(PairPeerName);
    public string PairQuestion => S.SeesThisCode(PairPeerName);
    public string? PairStatus { get => _pairStatus; private set => Set(ref _pairStatus, value); }
    public string? PairResult { get => _pairResult; private set => Set(ref _pairResult, value); }
    public bool PairResultOk { get => _pairResultOk; private set => Set(ref _pairResultOk, value); }
    public bool ShowPairChooser => PairPrompt is null && PairResult is null && !_pairConnecting;
    public bool ShowPairCode => PairPrompt is not null && PairResult is null;
    public bool ShowPairConfirm => ShowPairCode && !_pairConfirmed;
    public bool ShowPairResult => PairResult is not null;
    public bool ShowPairStatus => !ShowPairChooser && PairResult is null && PairStatus is not null;

    public string PairWindowText => _agent.Pairing.WindowClosesAt is { } c && c > DateTimeOffset.UtcNow ? S.MobilePairOpenFor(c - DateTimeOffset.UtcNow) : "";

    private void RaisePair()
    {
        Raise(nameof(PairCode));
        Raise(nameof(PairPeerName));
        Raise(nameof(PairPeerColorHex));
        Raise(nameof(PairPeerAvatarId));
        Raise(nameof(PairPeerInitial));
        Raise(nameof(PairQuestion));
        Raise(nameof(ShowPairChooser));
        Raise(nameof(ShowPairCode));
        Raise(nameof(ShowPairConfirm));
        Raise(nameof(ShowPairResult));
        Raise(nameof(ShowPairStatus));
    }

    private void ResetPair()
    {
        _pairConfirmed = false;
        _pairConnecting = false;
        PairPrompt = null;
        PairResult = null;
        PairStatus = null;
        RaisePair();
    }

    private async Task PairWithAsync(PeerViewModel? peer)
    {
        if (peer is null || _agent.Peers.ResolveEndPoint(peer.DeviceId) is not { } target)
            return;
        _pairConnecting = true;
        PairStatus = S.PairConnecting(peer.Name);
        RaisePair();
        var outcome = await _agent.Pairing.PairWithAsync(target, peer.DeviceId, CancellationToken.None);
        ShowPairOutcome(peer.DeviceId, outcome, peer.Name);
    }

    private void ShowPairPrompt(PairingPrompt prompt)
    {
        if (Sheet != MobileSheet.Pair)
        {
            Open(MobileSheet.Pair);
        }
        _pairConfirmed = false;
        _pairConnecting = false;
        PairResult = null;
        PairStatus = null;
        PairPrompt = prompt;
        RaisePair();
    }

    private void AnswerPair(bool matches)
    {
        if (PairPrompt is null)
            return;
        PairPrompt.Confirm(matches);
        _pairConfirmed = true;
        PairStatus = matches ? S.PairWaitingOther : S.PairOutcome("Rejected");
        RaisePair();
    }

    private void ShowPairOutcome(string deviceId, PairingOutcome outcome, string? name = null)
    {
        if (Sheet != MobileSheet.Pair)
            return;
        name ??= PairPrompt?.PeerName ?? _agent.Trust.Find(deviceId)?.DisplayName ?? "";
        _pairConnecting = false;
        PairResultOk = outcome == PairingOutcome.Paired;
        PairResult = PairResultOk ? S.PairDone(name) : S.PairOutcome(outcome.ToString());
        if (PairResultOk)
            Chasqui = ChasquiState.Success;
        RaisePair();
    }

    // ── Conectar por código ──────────────────────────────────────────────────────

    private string _connectInput = "";
    private string? _connectStatus;
    private bool _connectIsError;
    private bool _connectBusy;
    private PeerTarget? _found;
    private string _foundName = "";
    private string _foundAvatarId = "";
    private string _foundColorHex = "#5B5BF0";

    public string ConnectInput { get => _connectInput; set => Set(ref _connectInput, value); }
    public string? ConnectStatus { get => _connectStatus; private set => Set(ref _connectStatus, value); }
    public bool ConnectIsError { get => _connectIsError; private set => Set(ref _connectIsError, value); }
    public RelayCommand ConnectFind { get; }
    public RelayCommand ConnectSend { get; }

    public bool ConnectBusy
    {
        get => _connectBusy;
        private set
        {
            if (Set(ref _connectBusy, value))
                ConnectFind.NotifyCanExecuteChanged();
        }
    }

    public bool HasFound => _found is not null;
    public string FoundName { get => _foundName; private set => Set(ref _foundName, value); }
    public string FoundAvatarId { get => _foundAvatarId; private set => Set(ref _foundAvatarId, value); }
    public string FoundColorHex { get => _foundColorHex; private set => Set(ref _foundColorHex, value); }
    public string FoundInitial => IdentityVisuals.Initial(FoundName);

    public string MyCode => _agent.MyConnectionCode ?? "—";

    public string MyAddresses => string.Join("  ·  ", _agent.MyAddresses.Select(a =>
        _agent.Server.Port == AppInfo.DefaultTcpPort ? a.ToString() : $"{a}:{_agent.Server.Port}"));

    private void ResetConnect()
    {
        _found = null;
        ConnectStatus = null;
        ConnectIsError = false;
        Raise(nameof(HasFound));
    }

    private async Task FindAsync()
    {
        var text = ConnectInput.Trim();
        if (text.Length == 0)
            return;
        ConnectBusy = true;
        ResetConnect();
        ConnectStatus = S.ConnectProbe + "…";
        try
        {
            var target = AgentHost.TargetForCode(text) ?? await AgentHost.TargetForAddressAsync(text);
            if (target is null)
            {
                FailConnect(S.ConnectInvalid);
                return;
            }
            var (id, key, alias, _) = await _agent.Client.ProbeAsync(target, CancellationToken.None);
            var trusted = _agent.Trust.IsTrustedKey(key);
            var name = _agent.Trust.Find(id)?.DisplayName ?? (alias.Length > 0 ? alias : IdentityVisuals.ShortCode(id));
            FoundName = name;
            FoundColorHex = IdentityVisuals.ColorHex(id);
            FoundAvatarId = AvatarCatalog.Resolve(_agent.Peers.Find(id)?.Avatar, id);
            Raise(nameof(FoundInitial));
            // La clave queda fijada: si cambia antes de enviar, no se envía nada.
            _found = target with { DisplayName = name, ExpectedDeviceId = id, PinnedKey = trusted ? key : null, Code = null };
            Raise(nameof(HasFound));
            ConnectStatus = S.ConnectFound;
        }
        catch (PeerConnectException ex)
        {
            FailConnect(ex.Failure switch
            {
                ConnectFailure.IdentityMismatch => S.ConnectWrongCode,
                ConnectFailure.Rejected => S.ConnectRejected,
                _ => S.ConnectNotFound,
            });
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ProtocolException or System.Net.Sockets.SocketException
                                       or System.Security.Authentication.AuthenticationException)
        {
            FailConnect(S.ConnectNotFound);
        }
        finally
        {
            ConnectBusy = false;
        }
    }

    private void FailConnect(string message)
    {
        ConnectIsError = true;
        ConnectStatus = message;
    }

    private async Task SendToFoundAsync()
    {
        if (_found is not { } target)
            return;
        if (!HasPending)
            await PickPendingAsync();
        if (!HasPending)
            return;
        var info = _agent.BeginSend(target, _pending);
        if (_pendingCopyDir is not null)
            _outgoingCopies[info.Id] = _pendingCopyDir;
        _pendingCopyDir = null;
        SetPending([], null);
        Open(MobileSheet.None);
    }

    // ── Ajustes, versión nueva ───────────────────────────────────────────────────

    public string VersionText => S.Version(AppInfo.Version);
    public string Credits => S.Credits;
    public RelayCommand OpenDownloads { get; }
    public RelayCommand DownloadUpdate { get; }
    public RelayCommand DismissUpdate { get; }
    public RelayCommand CheckUpdates { get; }
    public bool ShowUpdate => _updateVersion is not null && !_updateDismissed;
    public string UpdateTitle => S.MobileUpdateTitle(_updateVersion ?? "");
    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }

    private async Task CheckUpdatesAsync(bool manual)
    {
        if (manual)
            UpdateStatus = S.UpdateChecking;
        ReleaseCheck.Release? latest;
        try
        {
            latest = await ReleaseCheck.LatestAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            latest = null;
        }
        if (latest is null)
        {
            if (manual)
                UpdateStatus = S.MobileUpdateFailed;
            return;
        }
        if (ReleaseCheck.IsNewer(latest.Version, AppInfo.Version))
        {
            _updateVersion = latest.Version;
            _updateUrl = latest.ApkUrl;
            _updateDismissed = false;
            UpdateStatus = UpdateTitle;
            Raise(nameof(UpdateTitle));
            Raise(nameof(ShowUpdate));
        }
        else if (manual)
        {
            UpdateStatus = S.MobileUpToDate;
        }
    }
}

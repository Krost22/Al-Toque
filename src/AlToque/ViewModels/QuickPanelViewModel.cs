using System.Collections.ObjectModel;
using AlToque.Chasqui;
using AlToque.Core.Discovery;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;
using AlToque.Localization;

namespace AlToque.ViewModels;

/// <summary>Panel rápido de la bandeja: radar de compañeros, zona para soltar, recientes y modo de recepción.</summary>
public sealed class QuickPanelViewModel : ViewModelBase
{
    private readonly AppController _app;
    private ChasquiState _chasqui;
    private double _chasquiProgress;
    private string _status = S.Searching;
    private int _modeIndex = 1;
    private string? _modeHint;
    private bool _networkPublic;
    private bool _searching = true;
    private IReadOnlyList<string>? _pending;
    private string _pendingTitle = "";
    private string _message = "";
    private bool _dragOver;
    private bool _reduceMotion;
    private bool _syncingMode;
    private string _dragSummary = "";
    private string? _networkName;
    private DateTimeOffset _searchStarted = DateTimeOffset.Now;
    private int _trustedCount;
    private ReceiveMode _mode = ReceiveMode.TrustedOnly;

    public QuickPanelViewModel(AppController app)
    {
        _app = app;
        OpenSettings = new RelayCommand(() => _app.ShowMain(2));
        OpenHistory = new RelayCommand(() => _app.ShowMain(1));
        Close = new RelayCommand(_app.HidePanel);
        Pair = new RelayCommand(_app.ShowPairing);
        Connect = new RelayCommand(() => _app.ShowConnect(PendingPaths, showMyCode: false));
        ShowMyCode = new RelayCommand(() => _app.ShowConnect(null, showMyCode: true));
        ClearPending = new RelayCommand(() => SetPending(null));
        AllowPublicNetwork = new RelayCommand(_app.AllowPublicNetwork);
        PeerClicked = new RelayCommand(p =>
        {
            if (p is PeerViewModel peer)
                _app.OnPeerClicked(peer, PendingPaths, MessageOrNull);
        });
        ChooseFiles = new RelayCommand(async () =>
        {
            var paths = await _app.PickFilesAsync();
            if (paths.Count > 0)
                SetPending(paths);
        });
    }

    public ObservableCollection<PeerViewModel> Peers { get; } = [];
    public ObservableCollection<HistoryItemViewModel> Recent { get; } = [];

    public RelayCommand OpenSettings { get; }
    public RelayCommand OpenHistory { get; }
    public RelayCommand Close { get; }
    public RelayCommand Pair { get; }
    public RelayCommand Connect { get; }
    public RelayCommand ShowMyCode { get; }
    public RelayCommand ClearPending { get; }
    public RelayCommand AllowPublicNetwork { get; }
    public RelayCommand PeerClicked { get; }
    public RelayCommand ChooseFiles { get; }

    public ChasquiState Chasqui { get => _chasqui; set => Set(ref _chasqui, value); }
    public double ChasquiProgress { get => _chasquiProgress; set => Set(ref _chasquiProgress, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool ReduceMotion { get => _reduceMotion; set => Set(ref _reduceMotion, value); }

    public bool HasPeers => Peers.Count > 0;
    public bool ShowNobody => Peers.Count == 0 && !_searching;
    public bool ShowSearching => Peers.Count == 0 && _searching;
    public bool HasRecent => Recent.Count > 0;

    // ── Estados visuales del panel ───────────────────────────────────────────────

    /// <summary>"No veo a nadie": vista grande con Chasqui buscando y consejos.</summary>
    public bool ShowNobodyView => Peers.Count == 0 && !DragOver && !HasPending;

    public bool ShowReadyCard => !DragOver && !HasPending && Peers.Count > 0;
    public bool ShowPeersArea => Peers.Count > 0;
    public bool ShowRecentArea => HasRecent && !DragOver && !HasPending && Peers.Count > 0;
    public bool ShowDropForLater => DragOver && !HasPending;
    public string PeersCountText => S.InThisNetwork(Peers.Count(p => p.IsOnline));
    public string DragSummary { get => _dragSummary; private set => Set(ref _dragSummary, value); }

    public string SearchText => S.StillSearching(_networkName, (int)Math.Max(0, (DateTimeOffset.Now - _searchStarted).TotalSeconds));
    public string VisibilityText => _mode switch
    {
        ReceiveMode.Nobody => S.NobodyInvisible,
        ReceiveMode.Everyone => S.NobodyVisibleEveryone,
        _ => S.NobodyVisibleTrusted,
    };
    public bool IsVisibleOnNetwork => _mode != ReceiveMode.Nobody && !NetworkPublic;

    public bool ModeNobody
    {
        get => ModeIndex == 0;
        set
        {
            if (value)
                ModeIndex = 0;
        }
    }

    public bool ModeTrusted
    {
        get => ModeIndex == 1;
        set
        {
            if (value)
                ModeIndex = 1;
        }
    }

    public bool ModeEveryone
    {
        get => ModeIndex == 2;
        set
        {
            if (value)
                ModeIndex = 2;
        }
    }

    public void SetNetworkName(string? name)
    {
        _networkName = name;
        Raise(nameof(SearchText));
    }

    /// <summary>Resumen de lo que se arrastra ("3 archivos · 286 MB"); el tamaño llega después.</summary>
    public void SetDragInfo(int count, long? bytes) =>
        DragSummary = bytes is { } b ? S.FileCountSize(count, Format.Bytes(b)) : S.Items(count);

    private void RaiseLayout()
    {
        Raise(nameof(ShowNobodyView));
        Raise(nameof(ShowReadyCard));
        Raise(nameof(ShowPeersArea));
        Raise(nameof(ShowRecentArea));
        Raise(nameof(ShowDropForLater));
        Raise(nameof(PeersCountText));
    }

    public bool NetworkPublic
    {
        get => _networkPublic;
        set => Set(ref _networkPublic, value);
    }

    public int ModeIndex
    {
        get => _modeIndex;
        set
        {
            if (!Set(ref _modeIndex, value))
                return;
            Raise(nameof(ModeNobody));
            Raise(nameof(ModeTrusted));
            Raise(nameof(ModeEveryone));
            if (_syncingMode)
                return;
            _app.Agent.SetReceiveMode(value switch
            {
                0 => ReceiveMode.Nobody,
                2 => ReceiveMode.Everyone,
                _ => ReceiveMode.TrustedOnly,
            });
        }
    }

    public string? ModeHint { get => _modeHint; private set => Set(ref _modeHint, value); }
    public bool HasModeHint => ModeHint is not null;

    public bool DragOver
    {
        get => _dragOver;
        set
        {
            if (!Set(ref _dragOver, value))
                return;
            foreach (var p in Peers)
                p.IsDragging = value;
            RaiseLayout();
            UpdateChasqui();
        }
    }

    // ── Modo "elegir destino" ────────────────────────────────────────────────────

    public IReadOnlyList<string>? PendingPaths => _pending;
    public bool HasPending => _pending is { Count: > 0 };
    public string PendingTitle { get => _pendingTitle; private set => Set(ref _pendingTitle, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
    private string? MessageOrNull => string.IsNullOrWhiteSpace(Message) ? null : Message.Trim();

    public void SetPending(IReadOnlyList<string>? paths)
    {
        _pending = paths is { Count: > 0 } ? paths : null;
        if (_pending is { } pending)
        {
            // El tamaño se calcula en segundo plano: una carpeta con miles de archivos no congela el panel.
            PendingTitle = S.PickTitle(pending.Count, "…");
            _ = Task.Run(() => Measure(pending)).ContinueWith(t =>
            {
                if (ReferenceEquals(_pending, pending) && t.IsCompletedSuccessfully)
                    PendingTitle = S.PickTitle(Math.Max(t.Result.Count, pending.Count), Format.Bytes(t.Result.Size));
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        else
        {
            Message = "";
        }
        Raise(nameof(PendingPaths));
        Raise(nameof(HasPending));
        RaiseLayout();
    }

    private static (int Count, long Size) Measure(IReadOnlyList<string> paths)
    {
        long size = 0;
        var count = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p))
                {
                    size += new FileInfo(p).Length;
                    count++;
                }
                else if (Directory.Exists(p))
                {
                    foreach (var f in new DirectoryInfo(p).EnumerateFiles("*", options).Take(100_000))
                    {
                        size += f.Length;
                        count++;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Sin permiso para leer algo: se informa al enviar, no aquí.
            }
        }
        return (count, size);
    }

    // ── Sincronización con el agente ─────────────────────────────────────────────

    public void SyncPeers(IReadOnlyList<PeerInfo> snapshot)
    {
        var visible = snapshot.Where(p => p.IsTrusted || p.Presence == PeerPresence.Online).ToList();
        for (int i = Peers.Count - 1; i >= 0; i--)
        {
            if (visible.All(v => v.DeviceId != Peers[i].DeviceId))
                Peers.RemoveAt(i);
        }
        for (int i = 0; i < visible.Count; i++)
        {
            var info = visible[i];
            var existing = Peers.FirstOrDefault(p => p.DeviceId == info.DeviceId);
            if (existing is null)
            {
                existing = new PeerViewModel(info.DeviceId);
                Peers.Insert(Math.Min(i, Peers.Count), existing);
            }
            else
            {
                var at = Peers.IndexOf(existing);
                if (at != i && i < Peers.Count)
                    Peers.Move(at, i);
            }
            existing.Update(info);
        }
        if (visible.Any(p => p.Presence == PeerPresence.Online))
            _searching = false;
        Raise(nameof(HasPeers));
        Raise(nameof(ShowNobody));
        Raise(nameof(ShowSearching));
        RaiseLayout();
        UpdateChasqui();
    }

    /// <summary>Al abrir el panel se consulta la red; tras unos segundos sin nadie se muestra la ayuda.</summary>
    public void BeginSearch()
    {
        _searching = !Peers.Any(p => p.IsOnline);
        _searchStarted = DateTimeOffset.Now;
        Raise(nameof(ShowNobody));
        Raise(nameof(ShowSearching));
        UpdateChasqui();
    }

    public void EndSearch()
    {
        _searching = false;
        Raise(nameof(ShowNobody));
        Raise(nameof(ShowSearching));
        UpdateChasqui();
    }

    public void SyncRecent(IEnumerable<HistoryEntry> entries)
    {
        Recent.Clear();
        foreach (var e in entries.Take(3))
            Recent.Add(new HistoryItemViewModel(e, _app.Retry, _app.OpenHistoryItem));
        Raise(nameof(HasRecent));
        Raise(nameof(ShowRecentArea));
    }

    /// <summary>Una vez por segundo: el contador de búsqueda y el tiempo de "Todos".</summary>
    public void Tick()
    {
        if (ShowNobodyView)
            Raise(nameof(SearchText));
    }

    public void SyncMode(ReceiveMode mode, DateTimeOffset? everyoneUntil, int trustedCount, bool networkPublic)
    {
        _mode = mode;
        _trustedCount = trustedCount;
        _syncingMode = true;
        ModeIndex = mode switch
        {
            ReceiveMode.Nobody => 0,
            ReceiveMode.Everyone => 2,
            _ => 1,
        };
        _syncingMode = false;
        NetworkPublic = networkPublic;
        ModeHint = mode switch
        {
            ReceiveMode.Everyone => S.ModeHintEveryone(everyoneUntil is { } until ? until - DateTimeOffset.UtcNow : null),
            ReceiveMode.Nobody => S.ModeNobodyHint,
            _ => S.ModeHintTrusted(_trustedCount),
        };
        Raise(nameof(HasModeHint));
        Raise(nameof(VisibilityText));
        Raise(nameof(IsVisibleOnNetwork));
    }

    private (ChasquiState State, double Progress, string Text)? _activity;
    private (ChasquiState State, string Text, DateTimeOffset Until)? _flash;

    /// <summary>Lo que hacen las transferencias en curso, para que Chasqui lo refleje.</summary>
    public void SyncActivity(IReadOnlyList<TransferViewModel> transfers)
    {
        var receiving = transfers.FirstOrDefault(t => t.IsActive && t.Info.Direction == TransferDirection.Receive && t.Info.State == TransferState.Transferring);
        var sending = transfers.FirstOrDefault(t => t.IsActive && t.Info.Direction == TransferDirection.Send);
        var current = receiving ?? sending;
        _activity = current is null ? null : (current.Chasqui, current.Progress, current.Title);
        UpdateChasqui();
    }

    /// <summary>Éxito, error o rechazo: se muestra un momento y vuelve al reposo.</summary>
    public void Flash(ChasquiState state, string text)
    {
        _flash = (state, text, DateTimeOffset.Now + ChasquiStates.ClipDuration(state) + TimeSpan.FromSeconds(1.8));
        UpdateChasqui();
    }

    public void UpdateChasqui()
    {
        if (_flash is { } f && f.Until > DateTimeOffset.Now && _activity is null)
        {
            Chasqui = f.State;
            Status = f.Text;
            return;
        }
        _flash = null;
        if (_activity is { } a)
        {
            Chasqui = a.State;
            ChasquiProgress = a.Progress;
            Status = a.Text;
        }
        else if (DragOver)
        {
            Chasqui = ChasquiState.Attentive;
            Status = S.DropHere;
        }
        else if (ShowSearching || Peers.Count == 0)
        {
            // Sin nadie a la vista, Chasqui sigue buscando con el radar.
            Chasqui = ChasquiState.Searching;
            Status = Peers.Count == 0 && !ShowSearching ? S.NobodyFound : S.Searching;
        }
        else
        {
            Chasqui = ChasquiState.Idle;
            Status = Peers.Count == 0 ? S.NobodyFound : S.Tagline;
        }
    }
}

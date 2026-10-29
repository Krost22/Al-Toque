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
        ModeItems = [S.ModeNobody, S.ModeTrustedShort, S.ModeEveryoneShort];
    }

    public ObservableCollection<PeerViewModel> Peers { get; } = [];
    public ObservableCollection<HistoryItemViewModel> Recent { get; } = [];
    public IReadOnlyList<string> ModeItems { get; }

    public RelayCommand OpenSettings { get; }
    public RelayCommand OpenHistory { get; }
    public RelayCommand Close { get; }
    public RelayCommand Pair { get; }
    public RelayCommand Connect { get; }
    public RelayCommand ShowMyCode { get; }
    public RelayCommand ClearPending { get; }
    public RelayCommand AllowPublicNetwork { get; }
    public RelayCommand PeerClicked { get; }

    public ChasquiState Chasqui { get => _chasqui; set => Set(ref _chasqui, value); }
    public double ChasquiProgress { get => _chasquiProgress; set => Set(ref _chasquiProgress, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool ReduceMotion { get => _reduceMotion; set => Set(ref _reduceMotion, value); }

    public bool HasPeers => Peers.Count > 0;
    public bool ShowNobody => Peers.Count == 0 && !_searching;
    public bool ShowSearching => Peers.Count == 0 && _searching;
    public bool HasRecent => Recent.Count > 0;

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
            if (!Set(ref _modeIndex, value) || _syncingMode)
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
            if (Set(ref _dragOver, value))
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
        if (_pending is not null)
        {
            long size = 0;
            var count = 0;
            foreach (var p in _pending)
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
                        foreach (var f in new DirectoryInfo(p).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Take(20_000))
                        {
                            size += f.Length;
                            count++;
                        }
                    }
                }
                catch (IOException)
                {
                }
            }
            PendingTitle = S.PickTitle(Math.Max(count, _pending.Count), Format.Bytes(size));
        }
        else
        {
            Message = "";
        }
        Raise(nameof(PendingPaths));
        Raise(nameof(HasPending));
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
        UpdateChasqui();
    }

    /// <summary>Al abrir el panel se consulta la red; tras unos segundos sin nadie se muestra la ayuda.</summary>
    public void BeginSearch()
    {
        _searching = !Peers.Any(p => p.IsOnline);
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
        foreach (var e in entries.Take(4))
            Recent.Add(new HistoryItemViewModel(e));
        Raise(nameof(HasRecent));
    }

    public void SyncMode(ReceiveMode mode, DateTimeOffset? everyoneUntil, bool trustedEmpty, bool networkPublic)
    {
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
            ReceiveMode.Everyone when everyoneUntil is { } until => S.EveryoneRemaining(until - DateTimeOffset.UtcNow),
            ReceiveMode.Nobody => S.ModeNobodyHint,
            ReceiveMode.TrustedOnly when trustedEmpty => S.EmptyTrustedHint,
            _ => null,
        };
        Raise(nameof(HasModeHint));
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
        else if (ShowSearching)
        {
            Chasqui = ChasquiState.Searching;
            Status = S.Searching;
        }
        else
        {
            Chasqui = ChasquiState.Idle;
            Status = Peers.Count == 0 ? S.NobodyFound : S.Tagline;
        }
    }
}

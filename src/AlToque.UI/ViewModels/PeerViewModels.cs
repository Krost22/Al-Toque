using AlToque.Controls;
using AlToque.Controls.Avatars;
using AlToque.Core.Discovery;
using AlToque.Core.Security;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;
using AlToque.Localization;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AlToque.ViewModels;

public static class Brushes2
{
    private static readonly Dictionary<string, IBrush> Cache = [];

    public static IBrush FromHex(string hex)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(hex, out var brush))
                Cache[hex] = brush = new ImmutableSolidColorBrush(Color.Parse(hex));
            return brush;
        }
    }

    public static IBrush ForDevice(string deviceId) => FromHex(IdentityVisuals.ColorHex(deviceId));
}

/// <summary>Un compañero en el "radar" del panel.</summary>
public sealed class PeerViewModel : ViewModelBase
{
    private string _name = "";
    private string _status = "";
    private bool _isOnline;
    private bool _isTrusted;
    private bool _isPairing;
    private bool _isBusy;
    private bool _isChecking;
    private bool _isDropTarget;
    private bool _isDragging;
    private bool _isFavorite;
    private int _unread;
    private string? _preview;
    private string _os = "";
    private string _avatarId;

    public PeerViewModel(string deviceId)
    {
        DeviceId = deviceId;
        ColorHex = IdentityVisuals.ColorHex(deviceId);
        Color = Brushes2.FromHex(ColorHex);
        Symbols = IdentityVisuals.SymbolSequence(deviceId);
        ShortCode = IdentityVisuals.ShortCode(deviceId);
        _avatarId = AvatarCatalog.DefaultFor(deviceId);
    }

    public string DeviceId { get; }
    public string ColorHex { get; }
    public IBrush Color { get; }
    public string Symbols { get; }
    public string ShortCode { get; }
    public string AvatarId { get => _avatarId; private set => Set(ref _avatarId, value); }

    public string Name
    {
        get => _name;
        private set
        {
            if (Set(ref _name, value))
                Raise(nameof(Initial));
        }
    }

    public string Initial => IdentityVisuals.Initial(Name);
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Os { get => _os; private set => Set(ref _os, value); }

    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (Set(ref _isOnline, value))
                Raise(nameof(Opacity));
        }
    }

    public bool IsTrusted { get => _isTrusted; private set => Set(ref _isTrusted, value); }

    /// <summary>Favorito: aparece primero y su estrella queda siempre a la vista.</summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        private set
        {
            if (Set(ref _isFavorite, value))
                Raise(nameof(FavoriteTip));
        }
    }

    public string FavoriteTip => IsFavorite ? S.Unfavorite(Name) : S.MakeFavorite(Name);
    public string RemoveTip => IsTrusted ? S.RemovePeerTip(Name) : S.HidePeerTip(Name);
    public bool IsPairing { get => _isPairing; private set => Set(ref _isPairing, value); }

    /// <summary>Equipo cercano que acepta solicitudes de conexión (no hace falta que abra «Emparejar»).</summary>
    public bool AcceptsRequests { get; private set; }

    /// <summary>Equipo cercano al que se le puede pedir conectarse ahora mismo.</summary>
    public bool CanConnect => !IsTrusted && IsOnline && (AcceptsRequests || IsPairing);

    /// <summary>En «Cerca de ti»: qué pasa al tocarlo.</summary>
    public string NearbyHint => IsPairing ? S.WantsToPair : AcceptsRequests ? S.NearbyTapToConnect : S.NearbyNeedsPairing;

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (Set(ref _isDropTarget, value))
                RaiseVisuals();
        }
    }

    /// <summary>Se están arrastrando archivos sobre el panel: los destinos posibles muestran un contorno.</summary>
    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (Set(ref _isDragging, value))
                RaiseVisuals();
        }
    }

    public bool ShowDashedOutline => IsDragging && !IsDropTarget && IsOnline;
    public double Opacity => IsOnline ? 1 : 0.6;
    public bool Dimmed => !IsOnline;

    public AvatarRing Ring => IsDropTarget ? AvatarRing.Drop
        : _isChecking ? AvatarRing.Checking
        : IsTrusted ? AvatarRing.Verified
        : AvatarRing.Unverified;

    public AvatarMood Mood => IsPairing ? AvatarMood.Surprised : _isBusy ? AvatarMood.Focused : AvatarMood.Idle;

    /// <summary>Con mensajes sin leer, la tarjeta muestra el último en lugar del estado.</summary>
    public string StatusLine => IsDropTarget ? S.DropToSend : HasUnread && _preview is { Length: > 0 } preview ? preview : Status;

    public bool HasUnread => _unread > 0;
    public string UnreadText => _unread > 9 ? "9+" : _unread.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Mensajes sin leer de su chat y el último de ellos.</summary>
    public void SetChat(int unread, string? preview)
    {
        if (_unread == unread && _preview == preview)
            return;
        _unread = unread;
        _preview = preview is { Length: > 60 } ? preview[..57] + "…" : preview;
        Raise(nameof(HasUnread));
        Raise(nameof(UnreadText));
        Raise(nameof(StatusLine));
    }

    public string AccessibleName => $"{Name}, {Status}{(IsTrusted ? ", " + S.Trusted : ", " + S.Unverified)}";

    private void RaiseVisuals()
    {
        Raise(nameof(Ring));
        Raise(nameof(StatusLine));
        Raise(nameof(ShowDashedOutline));
    }

    public void Update(PeerInfo info)
    {
        Name = info.DisplayName;
        IsOnline = info.Presence == PeerPresence.Online;
        IsTrusted = info.IsTrusted;
        IsFavorite = info.IsTrusted && info.IsFavorite;
        IsPairing = info.Pairing && !info.IsTrusted;
        AcceptsRequests = info.AcceptsRequests && !info.IsTrusted;
        _isBusy = info.Busy;
        _isChecking = info.Presence == PeerPresence.Checking;
        AvatarId = AvatarCatalog.Resolve(info.Avatar, info.DeviceId);
        Os = info.Os switch
        {
            "windows" => "Windows",
            "macos" => "macOS",
            "linux" => "Linux",
            "android" => "Android",
            _ => "",
        };
        Status = info.Presence switch
        {
            PeerPresence.Checking => S.Checking,
            PeerPresence.Offline => S.Offline,
            _ when IsPairing => S.WantsToPair,
            _ when info.Busy => S.Busy,
            _ => Os.Length > 0 ? Os : S.Available,
        };
        Raise(nameof(AccessibleName));
        Raise(nameof(FavoriteTip));
        Raise(nameof(RemoveTip));
        Raise(nameof(Dimmed));
        Raise(nameof(Mood));
        Raise(nameof(AcceptsRequests));
        Raise(nameof(CanConnect));
        Raise(nameof(NearbyHint));
        RaiseVisuals();
    }
}

/// <summary>Compañero de confianza en la pestaña "Compañeros".</summary>
public sealed class TrustedPeerViewModel : ViewModelBase
{
    private readonly TrustStore _store;
    private string _localName;
    private bool _favorite;
    private bool _autoAccept;

    public TrustedPeerViewModel(TrustedPeer peer, TrustStore store)
    {
        _store = store;
        DeviceId = peer.DeviceId;
        Alias = peer.Alias;
        _localName = peer.LocalName ?? "";
        _favorite = peer.Favorite;
        _autoAccept = peer.AutoAccept;
        ColorHex = IdentityVisuals.ColorHex(peer.DeviceId);
        Color = Brushes2.FromHex(ColorHex);
        AvatarId = AvatarCatalog.Resolve(peer.Avatar, peer.DeviceId);
        Symbols = IdentityVisuals.SymbolSequence(peer.DeviceId);
        Initial = IdentityVisuals.Initial(peer.DisplayName);
        Title = peer.DisplayName;
        Subtitle = S.PairedOn(peer.PairedAt) + " · " + IdentityVisuals.ShortCode(peer.DeviceId);
        Forget = new RelayCommand(() => _store.Remove(DeviceId));
    }

    public string DeviceId { get; }
    public string Alias { get; }
    public string ColorHex { get; }
    public string AvatarId { get; }
    public IBrush Color { get; }
    public string Symbols { get; }
    public string Initial { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public RelayCommand Forget { get; }

    public string LocalName
    {
        get => _localName;
        set
        {
            if (Set(ref _localName, value))
                _store.Update(DeviceId, p => p.LocalName = string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    public bool Favorite
    {
        get => _favorite;
        set
        {
            if (Set(ref _favorite, value))
                _store.Update(DeviceId, p => p.Favorite = value);
        }
    }

    public bool AutoAccept
    {
        get => _autoAccept;
        set
        {
            if (Set(ref _autoAccept, value))
                _store.Update(DeviceId, p => p.AutoAccept = value);
        }
    }
}

public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(HistoryEntry e, Action<HistoryEntry>? retry = null, Action<HistoryEntry>? open = null)
    {
        Entry = e;
        var sent = e.Direction == "sent";
        var first = e.SampleNames.FirstOrDefault() ?? "";
        Name = e.FileCount <= 1 ? first : $"{first} · {S.Items(e.FileCount)}";
        IsFailed = e.Result is "failed" or "partial";
        IsOk = e.Result == "ok";
        var who = e.PeerName;
        var when = Format.Ago(e.Time);
        Subtitle = e.Result switch
        {
            "failed" => $"{(sent ? S.RecentFailed(who) : S.FromPeer(who))} · {when}",
            "partial" => $"{S.RecentPartial} · {when}",
            "declined" => $"{(sent ? S.RecentDeclined(who) : S.RecentYouDeclined(who))} · {when}",
            "cancelled" => $"{S.RecentCancelled} · {when}",
            _ => $"{(sent ? S.ToPeer(who) : S.FromPeer(who))} · {Format.Bytes(e.TotalBytes)} · {when}",
        };
        Icon = IsFailed ? Ic.Alert : e.FileCount > 1 ? Ic.Folder : IconFor(first);
        ColorHex = IdentityVisuals.ColorHex(string.IsNullOrEmpty(e.PeerId) ? e.PeerName : e.PeerId);
        AvatarId = AvatarCatalog.Resolve(e.Avatar, string.IsNullOrEmpty(e.PeerId) ? e.PeerName : e.PeerId);
        Initial = IdentityVisuals.Initial(e.PeerName);
        CanOpen = !sent && e.Result is "ok" or "partial" && (e.FirstFile ?? e.Folder) is { } p && (File.Exists(p) || Directory.Exists(p));
        CanRetry = sent && e.Result is "failed" or "declined" or "cancelled" or "partial" && e.Paths is { Count: > 0 } && retry is not null;
        Retry = new RelayCommand(() => retry?.Invoke(e));
        Open = new RelayCommand(() => open?.Invoke(e));

        // Formato compacto anterior (historial completo).
        Arrow = sent ? "↑" : "↓";
        Title = sent ? $"{Name} → {who}" : $"{Name} ← {who}";
    }

    public HistoryEntry Entry { get; }
    public string Name { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Arrow { get; }
    public Ic Icon { get; }
    public bool IsFailed { get; }
    public bool IsOk { get; }
    public bool ShowOk => IsOk && !CanOpen;
    public string ColorHex { get; }
    public string AvatarId { get; }
    public string Initial { get; }
    public bool CanOpen { get; }
    public bool CanRetry { get; }
    public RelayCommand Retry { get; }
    public RelayCommand Open { get; }

    public static Ic IconFor(string name) => FileKinds.Categorize(name) switch
    {
        FileCategory.Image => Ic.Image,
        FileCategory.Video => Ic.Video,
        FileCategory.Audio => Ic.Music,
        FileCategory.Archive or FileCategory.DiskImage => Ic.Archive,
        FileCategory.Program or FileCategory.Script or FileCategory.Shortcut => Ic.Program,
        FileCategory.Document or FileCategory.Text => Ic.FileText,
        _ => Ic.File,
    };
}

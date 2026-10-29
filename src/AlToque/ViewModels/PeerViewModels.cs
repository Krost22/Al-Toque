using AlToque.Core.Discovery;
using AlToque.Core.Security;
using AlToque.Core.Storage;
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
    private bool _isDropTarget;
    private string _os = "";

    public PeerViewModel(string deviceId)
    {
        DeviceId = deviceId;
        Color = Brushes2.ForDevice(deviceId);
        Symbols = IdentityVisuals.SymbolSequence(deviceId);
        ShortCode = IdentityVisuals.ShortCode(deviceId);
    }

    public string DeviceId { get; }
    public IBrush Color { get; }
    public string Symbols { get; }
    public string ShortCode { get; }

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
    public bool IsPairing { get => _isPairing; private set => Set(ref _isPairing, value); }
    public bool IsDropTarget { get => _isDropTarget; set => Set(ref _isDropTarget, value); }
    public double Opacity => IsOnline ? 1 : 0.55;

    public string AccessibleName => $"{Name}, {Status}{(IsTrusted ? ", " + S.Trusted : ", " + S.Unverified)}";

    public void Update(PeerInfo info)
    {
        Name = info.DisplayName;
        IsOnline = info.Presence == PeerPresence.Online;
        IsTrusted = info.IsTrusted;
        IsPairing = info.Pairing && !info.IsTrusted;
        Os = info.Os switch
        {
            "windows" => "Windows",
            "macos" => "macOS",
            "linux" => "Linux",
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
        Color = Brushes2.ForDevice(peer.DeviceId);
        Symbols = IdentityVisuals.SymbolSequence(peer.DeviceId);
        Initial = IdentityVisuals.Initial(peer.DisplayName);
        Title = peer.DisplayName;
        Subtitle = S.PairedOn(peer.PairedAt) + " · " + IdentityVisuals.ShortCode(peer.DeviceId);
        Forget = new RelayCommand(() => _store.Remove(DeviceId));
    }

    public string DeviceId { get; }
    public string Alias { get; }
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
    public HistoryItemViewModel(HistoryEntry e)
    {
        Entry = e;
        var sent = e.Direction == "sent";
        Arrow = sent ? "↑" : "↓";
        var what = e.FileCount == 1 ? e.SampleNames.FirstOrDefault() ?? "" : S.Items(e.FileCount);
        Title = sent ? $"{what} → {e.PeerName}" : $"{what} ← {e.PeerName}";
        Subtitle = $"{Format.Bytes(e.TotalBytes)} · {Format.Ago(e.Time)}";
        (ResultText, ResultBrush) = e.Result switch
        {
            "ok" => ("✓", Brushes2.FromHex("#1F9D55")),
            "partial" => ("!", Brushes2.FromHex("#F5A524")),
            "declined" => ("—", Brushes2.FromHex("#8A8AA0")),
            "cancelled" => ("—", Brushes2.FromHex("#8A8AA0")),
            _ => ("✕", Brushes2.FromHex("#E5484D")),
        };
        Color = Brushes2.ForDevice(string.IsNullOrEmpty(e.PeerId) ? e.PeerName : e.PeerId);
        CanOpen = !sent && (e.FirstFile ?? e.Folder) is { } p && (File.Exists(p) || Directory.Exists(p));
    }

    public HistoryEntry Entry { get; }
    public string Arrow { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string ResultText { get; }
    public IBrush ResultBrush { get; }
    public IBrush Color { get; }
    public bool CanOpen { get; }
}

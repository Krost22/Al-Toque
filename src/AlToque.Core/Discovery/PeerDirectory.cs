using System.Net;
using AlToque.Core.Storage;

namespace AlToque.Core.Discovery;

public enum PeerPresence
{
    /// <summary>Conocido por caché; aún no se ha visto en esta red ("verificando…").</summary>
    Checking,
    Online,
    Offline,
}

/// <summary>Instantánea inmutable de un compañero para la interfaz.</summary>
public sealed record PeerInfo
{
    public required string DeviceId { get; init; }
    public required string DisplayName { get; init; }
    public string? Os { get; init; }
    public IPEndPoint? EndPoint { get; init; }
    public PeerPresence Presence { get; init; }
    public bool IsTrusted { get; init; }
    public bool IsFavorite { get; init; }
    public bool AutoAccept { get; init; }
    public bool Busy { get; init; }

    /// <summary>Tiene la ventana de emparejamiento abierta.</summary>
    public bool Pairing { get; init; }

    /// <summary>Se comprobó su clave por TLS en esta ejecución.</summary>
    public bool VerifiedThisSession { get; init; }

    public DateTimeOffset LastSeen { get; init; }
}

/// <summary>
/// Combina las pistas del descubrimiento, la lista de confianza y la caché de direcciones.
/// Los desconocidos solo aparecen si anuncian alias (modo "Todos" o emparejamiento).
/// </summary>
public sealed class PeerDirectory
{
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(140);
    private const int MaxLivePeers = 256;

    private readonly TrustStore _trust;
    private readonly PeerCacheStore _cache;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, LivePeer> _live = [];
    private readonly HashSet<string> _verified = [];
    private Timer? _debounce;

    public PeerDirectory(TrustStore trust, PeerCacheStore cache)
    {
        _trust = trust;
        _cache = cache;
        _trust.Changed += RaiseChanged;
    }

    /// <summary>Se dispara como mucho cada 150 ms.</summary>
    public event Action? Changed;

    private sealed class LivePeer
    {
        public required string DeviceId;
        public IPEndPoint? EndPoint;
        public string? Alias;
        public string? Os;
        public bool Busy;
        public bool Pairing;
        public DateTimeOffset LastSeen;
    }

    public void OnHint(DiscoveryHint hint)
    {
        lock (_lock)
        {
            if (!_live.TryGetValue(hint.DeviceId, out var peer))
            {
                if (_live.Count >= MaxLivePeers)
                {
                    var oldest = _live.Values.MinBy(p => p.LastSeen);
                    if (oldest is not null)
                        _live.Remove(oldest.DeviceId);
                }
                peer = new LivePeer { DeviceId = hint.DeviceId };
                _live[hint.DeviceId] = peer;
            }
            peer.EndPoint = hint.EndPoint;
            peer.Alias = hint.Alias;
            peer.Os = hint.Os ?? peer.Os;
            peer.Busy = hint.Busy;
            peer.Pairing = hint.Pairing;
            peer.LastSeen = DateTimeOffset.UtcNow;
        }
        if (_trust.Find(hint.DeviceId) is { } trusted)
        {
            _cache.Remember(new PeerCacheEntry
            {
                DeviceId = hint.DeviceId,
                Alias = trusted.Alias,
                Os = hint.Os ?? trusted.Os,
                Address = hint.EndPoint.Address.ToString(),
                Port = hint.EndPoint.Port,
                LastSeen = DateTimeOffset.UtcNow,
            });
        }
        RaiseChanged();
    }

    public void OnBye(string deviceId)
    {
        lock (_lock)
        {
            if (!_live.Remove(deviceId))
                return;
        }
        RaiseChanged();
    }

    /// <summary>
    /// La clave se comprobó por TLS en una conexión real. <paramref name="reachableAt"/> es el punto de escucha
    /// del compañero (solo se conoce en conexiones salientes).
    /// </summary>
    public void MarkVerified(string deviceId, IPEndPoint? reachableAt, string? alias, string? os)
    {
        lock (_lock)
        {
            _verified.Add(deviceId);
            _live.TryGetValue(deviceId, out var peer);
            if (peer is null && reachableAt is not null)
            {
                peer = new LivePeer { DeviceId = deviceId };
                _live[deviceId] = peer;
            }
            if (peer is not null)
            {
                if (reachableAt is not null)
                    peer.EndPoint = new IPEndPoint(reachableAt.Address.IsIPv4MappedToIPv6 ? reachableAt.Address.MapToIPv4() : reachableAt.Address, reachableAt.Port);
                peer.Alias ??= alias;
                peer.Os ??= os;
                peer.LastSeen = DateTimeOffset.UtcNow;
            }
        }
        RaiseChanged();
    }

    public void Sweep()
    {
        var changed = false;
        var limit = DateTimeOffset.UtcNow - OfflineAfter;
        lock (_lock)
        {
            foreach (var stale in _live.Values.Where(p => p.LastSeen < limit).Select(p => p.DeviceId).ToList())
            {
                _live.Remove(stale);
                changed = true;
            }
        }
        if (changed)
            RaiseChanged();
    }

    public PeerInfo? Find(string deviceId) => Snapshot(includeOfflineTrusted: true).FirstOrDefault(p => p.DeviceId == deviceId);

    /// <summary>Dirección conocida más reciente: anuncio en vivo o caché.</summary>
    public IPEndPoint? ResolveEndPoint(string deviceId) => Resolve(deviceId)?.EndPoint;

    /// <summary>Dirección y si viene de un anuncio en vivo (<c>Live</c>) o solo de la caché de otra sesión.</summary>
    public (IPEndPoint EndPoint, bool Live)? Resolve(string deviceId)
    {
        lock (_lock)
        {
            if (_live.TryGetValue(deviceId, out var live) && live.EndPoint is not null)
                return (live.EndPoint, true);
        }
        var cached = _cache.Get(deviceId);
        if (cached?.Address is not null && IPAddress.TryParse(cached.Address, out var ip) && cached.Port is > 0 and <= 65535)
            return (new IPEndPoint(ip, cached.Port), false);
        return null;
    }

    public IReadOnlyList<PeerInfo> Snapshot(bool includeOfflineTrusted = true)
    {
        var trusted = _trust.All.ToDictionary(t => t.DeviceId);
        var result = new List<PeerInfo>();
        lock (_lock)
        {
            foreach (var t in trusted.Values)
            {
                _live.TryGetValue(t.DeviceId, out var live);
                var cached = live is null ? _cache.Get(t.DeviceId) : null;
                var presence = live is not null ? PeerPresence.Online : cached is not null ? PeerPresence.Checking : PeerPresence.Offline;
                if (presence == PeerPresence.Offline && !includeOfflineTrusted)
                    continue;
                IPEndPoint? ep = live?.EndPoint;
                if (ep is null && cached?.Address is not null && IPAddress.TryParse(cached.Address, out var ip))
                    ep = new IPEndPoint(ip, cached.Port);
                result.Add(new PeerInfo
                {
                    DeviceId = t.DeviceId,
                    DisplayName = t.DisplayName,
                    Os = live?.Os ?? t.Os ?? cached?.Os,
                    EndPoint = ep,
                    Presence = presence,
                    IsTrusted = true,
                    IsFavorite = t.Favorite,
                    AutoAccept = t.AutoAccept,
                    Busy = live?.Busy ?? false,
                    Pairing = live?.Pairing ?? false,
                    VerifiedThisSession = _verified.Contains(t.DeviceId),
                    LastSeen = live?.LastSeen ?? cached?.LastSeen ?? t.PairedAt,
                });
            }
            foreach (var live in _live.Values)
            {
                if (trusted.ContainsKey(live.DeviceId) || string.IsNullOrEmpty(live.Alias))
                    continue;
                result.Add(new PeerInfo
                {
                    DeviceId = live.DeviceId,
                    DisplayName = live.Alias!,
                    Os = live.Os,
                    EndPoint = live.EndPoint,
                    Presence = PeerPresence.Online,
                    Busy = live.Busy,
                    Pairing = live.Pairing,
                    VerifiedThisSession = _verified.Contains(live.DeviceId),
                    LastSeen = live.LastSeen,
                });
            }
        }
        return result
            .OrderByDescending(p => p.IsFavorite)
            .ThenByDescending(p => p.IsTrusted)
            .ThenBy(p => p.Presence == PeerPresence.Online ? 0 : p.Presence == PeerPresence.Checking ? 1 : 2)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void RaiseChanged()
    {
        lock (_lock)
        {
            _debounce ??= new Timer(_ =>
            {
                lock (_lock)
                {
                    _debounce?.Dispose();
                    _debounce = null;
                }
                Changed?.Invoke();
            }, null, 150, Timeout.Infinite);
        }
    }
}

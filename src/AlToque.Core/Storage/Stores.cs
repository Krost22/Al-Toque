using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlToque.Core.Security;

namespace AlToque.Core.Storage;

/// <summary>Ajustes sellados con HMAC.</summary>
public sealed class SettingsStore
{
    private readonly string _path;
    private readonly Sealer _sealer;
    private readonly Lock _lock = new();
    private AppSettings _current;

    private SettingsStore(string path, Sealer sealer, AppSettings current, bool tampered)
    {
        _path = path;
        _sealer = sealer;
        _current = current;
        WasTampered = tampered;
    }

    public event Action<AppSettings>? Changed;

    /// <summary>Copia de los ajustes actuales.</summary>
    public AppSettings Current
    {
        get
        {
            lock (_lock)
                return _current.Clone();
        }
    }

    /// <summary>El archivo existía pero su sello no coincidía: se restablecieron los valores por defecto.</summary>
    public bool WasTampered { get; }

    public bool IsFirstRun { get; private init; }

    public static SettingsStore Load(AppPaths paths, Sealer sealer)
    {
        var raw = ReadText(paths.SettingsFile);
        AppSettings? loaded = null;
        var tampered = false;
        if (raw is not null)
        {
            var payload = sealer.Unseal(raw);
            if (payload is null)
                tampered = true;
            else
            {
                try
                {
                    loaded = JsonSerializer.Deserialize(payload, StorageJsonContext.Default.AppSettings);
                }
                catch (JsonException)
                {
                    tampered = true;
                }
            }
        }
        var settings = loaded ?? new AppSettings();
        settings.Normalize();
        var store = new SettingsStore(paths.SettingsFile, sealer, settings, tampered) { IsFirstRun = raw is null };
        if (raw is null || tampered)
            store.Save(settings);
        return store;
    }

    public void Update(Action<AppSettings> mutate)
    {
        AppSettings snapshot;
        lock (_lock)
        {
            var copy = _current.Clone();
            mutate(copy);
            copy.Normalize();
            Save(copy);
            _current = copy;
            snapshot = copy.Clone();
        }
        Changed?.Invoke(snapshot);
    }

    private void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, StorageJsonContext.Default.AppSettings);
        SecureFile.WriteAllText(_path, _sealer.Seal(json));
    }

    internal static string? ReadText(string path)
    {
        var bytes = SecureFile.TryReadAllBytes(path);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }
}

/// <summary>Lista de compañeros de confianza con sus claves fijadas, sellada con HMAC.</summary>
public sealed class TrustStore
{
    public const int MaxPeers = 512;

    private readonly string _path;
    private readonly Sealer _sealer;
    private readonly Lock _lock = new();
    private List<TrustedPeer> _peers;

    private TrustStore(string path, Sealer sealer, List<TrustedPeer> peers, bool tampered)
    {
        _path = path;
        _sealer = sealer;
        _peers = peers;
        WasTampered = tampered;
    }

    public event Action? Changed;

    /// <summary>La lista fue modificada fuera de la app y se descartó por seguridad.</summary>
    public bool WasTampered { get; }

    public static TrustStore Load(AppPaths paths, Sealer sealer)
    {
        var raw = SettingsStore.ReadText(paths.TrustFile);
        var tampered = false;
        List<TrustedPeer>? peers = null;
        if (raw is not null)
        {
            var payload = sealer.Unseal(raw);
            if (payload is null)
                tampered = true;
            else
            {
                try
                {
                    peers = JsonSerializer.Deserialize(payload, StorageJsonContext.Default.ListTrustedPeer);
                }
                catch (JsonException)
                {
                    tampered = true;
                }
            }
        }
        peers = (peers ?? []).Where(IsValid).Take(MaxPeers).ToList();
        var store = new TrustStore(paths.TrustFile, sealer, peers, tampered);
        if (tampered)
            store.SaveLocked();
        return store;
    }

    private static bool IsValid(TrustedPeer p)
    {
        if (!PeerKeys.IsValidDeviceId(p.DeviceId))
            return false;
        try
        {
            var spki = Convert.FromBase64String(p.PublicKey);
            return PeerKeys.DeviceIdFromSpki(spki) == p.DeviceId;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public IReadOnlyList<TrustedPeer> All
    {
        get
        {
            lock (_lock)
                return _peers.Select(Copy).ToList();
        }
    }

    public TrustedPeer? Find(string deviceId)
    {
        lock (_lock)
        {
            var p = _peers.FirstOrDefault(x => x.DeviceId == deviceId);
            return p is null ? null : Copy(p);
        }
    }

    /// <summary>Comprueba que la clave presentada coincide exactamente con la fijada.</summary>
    public bool IsTrustedKey(ReadOnlySpan<byte> spki)
    {
        var id = PeerKeys.DeviceIdFromSpki(spki);
        lock (_lock)
        {
            var p = _peers.FirstOrDefault(x => x.DeviceId == id);
            if (p is null)
                return false;
            var pinned = Convert.FromBase64String(p.PublicKey);
            return CryptographicOperations.FixedTimeEquals(pinned, spki);
        }
    }

    public void AddOrReplace(TrustedPeer peer)
    {
        if (!IsValid(peer))
            throw new ArgumentException("Compañero inválido.", nameof(peer));
        lock (_lock)
        {
            var existing = _peers.FindIndex(x => x.DeviceId == peer.DeviceId);
            if (existing >= 0)
            {
                // Conserva preferencias locales al volver a emparejar.
                var old = _peers[existing];
                peer.LocalName ??= old.LocalName;
                peer.Avatar ??= old.Avatar;
                peer.Favorite |= old.Favorite;
                peer.AutoAccept = old.AutoAccept;
                _peers[existing] = Copy(peer);
            }
            else
            {
                if (_peers.Count >= MaxPeers)
                    throw new InvalidOperationException("Demasiados compañeros de confianza.");
                _peers.Add(Copy(peer));
            }
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public bool Update(string deviceId, Action<TrustedPeer> mutate)
    {
        lock (_lock)
        {
            var p = _peers.FirstOrDefault(x => x.DeviceId == deviceId);
            if (p is null)
                return false;
            var copy = Copy(p);
            mutate(copy);
            copy.DeviceId = p.DeviceId;
            copy.PublicKey = p.PublicKey;
            copy.LocalName = TextLimits.CleanAlias(copy.LocalName);
            _peers[_peers.IndexOf(p)] = copy;
            SaveLocked();
        }
        Changed?.Invoke();
        return true;
    }

    public bool Remove(string deviceId)
    {
        bool removed;
        lock (_lock)
        {
            removed = _peers.RemoveAll(x => x.DeviceId == deviceId) > 0;
            if (removed)
                SaveLocked();
        }
        if (removed)
            Changed?.Invoke();
        return removed;
    }

    private void SaveLocked()
    {
        var json = JsonSerializer.Serialize(_peers, StorageJsonContext.Default.ListTrustedPeer);
        SecureFile.WriteAllText(_path, _sealer.Seal(json));
    }

    private static TrustedPeer Copy(TrustedPeer p) => new()
    {
        DeviceId = p.DeviceId,
        PublicKey = p.PublicKey,
        Alias = p.Alias,
        LocalName = p.LocalName,
        Os = p.Os,
        Avatar = TextLimits.CleanAvatar(p.Avatar),
        PairedAt = p.PairedAt,
        AutoAccept = p.AutoAccept,
        Favorite = p.Favorite,
    };
}

/// <summary>Caché de últimas direcciones conocidas, para mostrar compañeros al instante.</summary>
public sealed class PeerCacheStore
{
    private const int MaxEntries = 256;
    private readonly string _path;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, PeerCacheEntry> _entries;

    private PeerCacheStore(string path, Dictionary<string, PeerCacheEntry> entries)
    {
        _path = path;
        _entries = entries;
    }

    public static PeerCacheStore Load(AppPaths paths)
    {
        var raw = SettingsStore.ReadText(paths.PeerCacheFile);
        var dict = new Dictionary<string, PeerCacheEntry>();
        if (raw is not null)
        {
            try
            {
                foreach (var e in JsonSerializer.Deserialize(raw, StorageJsonContext.Default.ListPeerCacheEntry) ?? [])
                {
                    if (PeerKeys.IsValidDeviceId(e.DeviceId))
                        dict[e.DeviceId] = e;
                }
            }
            catch (JsonException)
            {
            }
        }
        return new PeerCacheStore(paths.PeerCacheFile, dict);
    }

    public PeerCacheEntry? Get(string deviceId)
    {
        lock (_lock)
            return _entries.TryGetValue(deviceId, out var e) ? e : null;
    }

    public void Remember(PeerCacheEntry entry)
    {
        lock (_lock)
        {
            _entries[entry.DeviceId] = entry;
            if (_entries.Count > MaxEntries)
            {
                foreach (var old in _entries.Values.OrderBy(x => x.LastSeen).Take(_entries.Count - MaxEntries).ToList())
                    _entries.Remove(old.DeviceId);
            }
        }
    }

    public void Forget(string deviceId)
    {
        lock (_lock)
            _entries.Remove(deviceId);
    }

    public void Save()
    {
        List<PeerCacheEntry> list;
        lock (_lock)
            list = _entries.Values.ToList();
        try
        {
            SecureFile.WriteAllText(_path, JsonSerializer.Serialize(list, StorageJsonContext.Default.ListPeerCacheEntry));
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Historial local en JSON Lines.</summary>
public sealed class HistoryStore
{
    private const int MaxEntries = 500;
    private readonly string _path;
    private readonly Lock _lock = new();
    private readonly List<HistoryEntry> _entries;
    private int _fileLines;

    private HistoryStore(string path, List<HistoryEntry> entries, int fileLines)
    {
        _path = path;
        _entries = entries;
        _fileLines = fileLines;
    }

    public event Action? Changed;

    public static HistoryStore Load(AppPaths paths)
    {
        var list = new List<HistoryEntry>();
        var lines = 0;
        try
        {
            if (File.Exists(paths.HistoryFile))
            {
                foreach (var line in File.ReadLines(paths.HistoryFile))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    lines++;
                    try
                    {
                        if (JsonSerializer.Deserialize(line, StorageJsonContext.Default.HistoryEntry) is { } e)
                            list.Add(e);
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        if (list.Count > MaxEntries)
            list.RemoveRange(0, list.Count - MaxEntries);
        return new HistoryStore(paths.HistoryFile, list, lines);
    }

    /// <summary>Entradas de la más reciente a la más antigua.</summary>
    public IReadOnlyList<HistoryEntry> Recent(int max = int.MaxValue)
    {
        lock (_lock)
            return _entries.AsEnumerable().Reverse().Take(max).ToList();
    }

    public void Add(HistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            try
            {
                if (_fileLines >= MaxEntries * 2)
                    Rewrite();
                else
                {
                    SecureFile.EnsurePrivateDirectory(Path.GetDirectoryName(_path)!);
                    File.AppendAllText(_path, JsonSerializer.Serialize(entry, StorageJsonContext.Default.HistoryEntry) + "\n");
                    _fileLines++;
                }
            }
            catch (IOException)
            {
            }
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _fileLines = 0;
            SecureFile.TryDelete(_path);
        }
        Changed?.Invoke();
    }

    private void Rewrite()
    {
        var sb = new StringBuilder();
        foreach (var e in _entries)
            sb.Append(JsonSerializer.Serialize(e, StorageJsonContext.Default.HistoryEntry)).Append('\n');
        SecureFile.WriteAllText(_path, sb.ToString());
        _fileLines = _entries.Count;
    }
}

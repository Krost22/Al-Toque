using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AlToque.Core.Security;
using AlToque.Core.Storage;

namespace AlToque.Core.Discovery;

/// <summary>Lo que este equipo anuncia. <c>null</c> = invisible (modo "Nadie").</summary>
public sealed record DiscoveryAdvert(int Port, string? Alias, string? Os, bool Busy, bool Pairing, string? Avatar = null);

/// <summary>Pista recibida por UDP. No prueba nada: la identidad se verifica después con TLS.</summary>
public sealed record DiscoveryHint(
    string DeviceId, IPEndPoint EndPoint, string? Alias, string? Os, bool Busy, bool Pairing, IReadOnlyList<string> Caps,
    string? Avatar = null);

/// <summary>
/// Descubrimiento por UDP multicast (239.255.0.0/16, TTL 1) con broadcast dirigido a la subred como respaldo.
/// Anuncia al iniciar, al cambiar de red y cada 30 a 60 s; responde consultas con un retardo aleatorio.
/// </summary>
public sealed class DiscoveryService : IDisposable
{
    private const int RateWindowMs = 10_000;
    private const int RateMaxPerWindow = 30;

    private readonly string _ownId;
    private readonly Func<DiscoveryAdvert?> _advert;
    private readonly Func<bool> _excludeVirtual;
    private readonly int _port;
    private readonly Action<string>? _log;
    private readonly Dictionary<IPAddress, (long Start, int Count)> _rate = [];
    private readonly Dictionary<string, long> _recentNonces = [];
    private readonly HashSet<IPAddress> _joined = [];
    private readonly SemaphoreSlim _announceSignal = new(0, int.MaxValue);
    private readonly Lock _lock = new();
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _announceLoop;

    public DiscoveryService(string ownId, Func<DiscoveryAdvert?> advert, Func<bool> excludeVirtual,
        int port = AppInfo.DiscoveryPort, Action<string>? log = null)
    {
        _ownId = ownId;
        _advert = advert;
        _excludeVirtual = excludeVirtual;
        _port = port;
        _log = log;
    }

    public event Action<DiscoveryHint>? HintReceived;
    public event Action<string>? ByeReceived;

    public bool IsRunning => _socket is not null;
    public string? LastError { get; private set; }

    public void Start()
    {
        if (_socket is not null)
            return;
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;
            socket.MulticastLoopback = true;
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            if (OperatingSystem.IsWindows())
            {
                // Evita que un ICMP "puerto inalcanzable" rompa las lecturas siguientes.
                const int SioUdpConnReset = -1744830452;
                socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
            }
            socket.Bind(new IPEndPoint(IPAddress.Any, _port));
            _socket = socket;
        }
        catch (SocketException ex)
        {
            LastError = ex.Message;
            _log?.Invoke("Descubrimiento no disponible: " + ex.Message);
            return;
        }

        _cts = new CancellationTokenSource();
        JoinGroups();
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _announceLoop = Task.Run(() => AnnounceLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        var socket = _socket;
        if (socket is null)
            return;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        if (_advert() is not null)
            Send(DiscoveryPacket.Types.Bye, null, null);
        _cts?.Cancel();
        _socket = null;
        socket.Dispose();
        lock (_lock)
            _joined.Clear();
    }

    /// <summary>Anuncia en breve (agrupa varias peticiones seguidas).</summary>
    public void AnnounceSoon() => _announceSignal.Release();

    /// <summary>Avisa que este equipo deja de estar visible (al pasar a "Nadie").</summary>
    public void SendBye() => Send(DiscoveryPacket.Types.Bye, null, null);

    /// <summary>Pregunta quién está, para no esperar al siguiente anuncio.</summary>
    public void Query() => Send(DiscoveryPacket.Types.Query, null, null);

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        LocalNetwork.InvalidateCache();
        JoinGroups();
        AnnounceSoon();
    }

    private void JoinGroups()
    {
        var socket = _socket;
        if (socket is null)
            return;
        var current = LocalNetwork.GetIPv4Interfaces(_excludeVirtual()).Select(i => i.Address).ToHashSet();
        lock (_lock)
        {
            foreach (var gone in _joined.Where(a => !current.Contains(a)).ToList())
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, new MulticastOption(AppInfo.MulticastGroup, gone));
                }
                catch (SocketException)
                {
                }
                _joined.Remove(gone);
            }
            foreach (var address in current.Where(a => !_joined.Contains(a)))
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(AppInfo.MulticastGroup, address));
                    _joined.Add(address);
                }
                catch (SocketException ex)
                {
                    _log?.Invoke($"No se pudo unir al grupo multicast en {address}: {ex.SocketErrorCode}");
                }
            }
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        try
        {
            // Ráfaga inicial para sobrevivir a pérdidas de paquetes.
            AnnounceNow();
            Query();
            await Task.Delay(1500, ct).ConfigureAwait(false);
            AnnounceNow();
            await Task.Delay(2500, ct).ConfigureAwait(false);
            AnnounceNow();
            while (!ct.IsCancellationRequested)
            {
                var wait = TimeSpan.FromMilliseconds(Random.Shared.Next(30_000, 60_000));
                if (await _announceSignal.WaitAsync(wait, ct).ConfigureAwait(false))
                {
                    await Task.Delay(300, ct).ConfigureAwait(false);
                    while (_announceSignal.CurrentCount > 0)
                        await _announceSignal.WaitAsync(ct).ConfigureAwait(false);
                }
                AnnounceNow();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void AnnounceNow()
    {
        var advert = _advert();
        if (advert is not null)
            Send(DiscoveryPacket.Types.Announce, advert, null);
    }

    private void Send(string type, DiscoveryAdvert? advert, IPEndPoint? unicastTo)
    {
        var socket = _socket;
        if (socket is null)
            return;
        advert ??= type is DiscoveryPacket.Types.Announce or DiscoveryPacket.Types.Reply ? _advert() : null;
        var packet = new DiscoveryPacket
        {
            App = DiscoveryPacket.AppTag,
            V = AppInfo.ProtocolVersion,
            T = type,
            Id = _ownId,
            Port = advert?.Port ?? 0,
            N = Base32.Encode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)),
            Alias = advert?.Alias,
            Os = advert?.Os,
            Av = advert?.Alias is null ? null : TextLimits.CleanAvatar(advert.Avatar),
            Busy = advert?.Busy ?? false,
            Pairing = advert?.Pairing ?? false,
            Caps = advert is null ? null : [.. Protocol.Capabilities.Local],
        };
        var bytes = packet.ToBytes();
        try
        {
            if (unicastTo is not null)
            {
                socket.SendTo(bytes, unicastTo);
                return;
            }
            var groupEndPoint = new IPEndPoint(AppInfo.MulticastGroup, _port);
            foreach (var nic in LocalNetwork.GetIPv4InterfacesCached(_excludeVirtual()))
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, nic.Address.GetAddressBytes());
                    socket.SendTo(bytes, groupEndPoint);
                }
                catch (SocketException)
                {
                }
                try
                {
                    socket.SendTo(bytes, new IPEndPoint(nic.Broadcast, _port));
                }
                catch (SocketException)
                {
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException ex)
        {
            _log?.Invoke("Error al anunciar: " + ex.SocketErrorCode);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[2048];
        while (!ct.IsCancellationRequested)
        {
            var socket = _socket;
            if (socket is null)
                return;
            SocketReceiveFromResult result;
            try
            {
                result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                continue;
            }
            catch (SocketException ex)
            {
                _log?.Invoke("Error de descubrimiento: " + ex.SocketErrorCode);
                await Task.Delay(1000, ct).ConfigureAwait(false);
                continue;
            }

            if (result.RemoteEndPoint is not IPEndPoint from || result.ReceivedBytes > DiscoveryPacket.MaxBytes)
                continue;
            var source = from.Address.IsIPv4MappedToIPv6 ? from.Address.MapToIPv4() : from.Address;
            if (!AllowFrom(source))
                continue;
            var packet = DiscoveryPacket.TryParse(buffer.AsSpan(0, result.ReceivedBytes));
            if (packet is null || packet.Id == _ownId || IsDuplicate(packet))
                continue;
            Handle(packet, new IPEndPoint(source, from.Port));
        }
    }

    private void Handle(DiscoveryPacket packet, IPEndPoint from)
    {
        switch (packet.T)
        {
            case DiscoveryPacket.Types.Query:
                if (_advert() is not null)
                {
                    var delay = Random.Shared.Next(0, 100);
                    _ = Task.Delay(delay).ContinueWith(_ => Send(DiscoveryPacket.Types.Reply, null, from), TaskScheduler.Default);
                }
                break;
            case DiscoveryPacket.Types.Bye:
                ByeReceived?.Invoke(packet.Id);
                break;
            default:
                HintReceived?.Invoke(new DiscoveryHint(packet.Id, new IPEndPoint(from.Address, packet.Port), packet.Alias,
                    packet.Os, packet.Busy, packet.Pairing, packet.Caps ?? [], packet.Av));
                break;
        }
    }

    /// <summary>Ignora ráfagas de una misma fuente para evitar saturación.</summary>
    private bool AllowFrom(IPAddress source)
    {
        var now = Environment.TickCount64;
        lock (_lock)
        {
            if (_rate.Count > 2048)
                _rate.Clear();
            if (!_rate.TryGetValue(source, out var entry) || now - entry.Start > RateWindowMs)
            {
                _rate[source] = (now, 1);
                return true;
            }
            if (entry.Count >= RateMaxPerWindow)
                return false;
            _rate[source] = (entry.Start, entry.Count + 1);
            return true;
        }
    }

    private bool IsDuplicate(DiscoveryPacket packet)
    {
        var key = packet.Id + ":" + packet.N;
        var now = Environment.TickCount64;
        lock (_lock)
        {
            if (_recentNonces.Count > 512)
            {
                foreach (var old in _recentNonces.Where(kv => now - kv.Value > 5000).Select(kv => kv.Key).ToList())
                    _recentNonces.Remove(old);
                if (_recentNonces.Count > 512)
                    _recentNonces.Clear();
            }
            if (_recentNonces.TryGetValue(key, out var seen) && now - seen < 5000)
                return true;
            _recentNonces[key] = now;
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

using System.Net;
using System.Security.Cryptography;
using AlToque.Core.Discovery;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;

namespace AlToque.Core.Chat;

/// <summary>
/// Enlace permanente con cada compañero de confianza que está en línea: al conectarse, cada lado manda lo que al otro
/// le falta del chat, y después los mensajes llegan al momento. Hay una sola conexión por pareja: si los dos marcan a
/// la vez, queda la que inició el equipo de identificador menor. Una señal de vida cada 15 s detecta las conexiones
/// muertas (por ejemplo, al cambiar de red) y el enlace se rehace solo con la dirección nueva.
/// </summary>
public sealed class LinkService : IAsyncDisposable
{
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DeadAfter = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);
    private static readonly int[] BackoffSeconds = [2, 4, 8, 15, 30, 60];

    /// <summary>Dos enlaces que nacen con menos de esto de diferencia son los dos lados marcando a la vez.</summary>
    private static readonly TimeSpan SimultaneousDialWindow = TimeSpan.FromSeconds(5);

    /// <summary>A un equipo que no habla el enlace (versión anterior) se le vuelve a probar cada 10 minutos.</summary>
    private const int IncompatibleBackoffSeconds = 600;

    private readonly DeviceIdentity _identity;
    private readonly TrustStore _trust;
    private readonly PeerDirectory _peers;
    private readonly ChatStore _chats;
    private readonly Func<HelloMessage> _hello;
    private readonly Action<string>? _log;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Link> _links = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Retry> _retries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dialing = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private Timer? _timer;

    private sealed class Link(PeerConnection conn, string peerId, bool outgoing)
    {
        public readonly PeerConnection Conn = conn;
        public readonly string PeerId = peerId;
        public readonly bool Outgoing = outgoing;
        public readonly CancellationTokenSource Cts = new();
        public readonly SemaphoreSlim PushGate = new(1, 1);
        public readonly long CreatedAt = Environment.TickCount64;
        public long LastHeard = Environment.TickCount64;

        /// <summary>Hasta qué número de mis mensajes ya mandé por este enlace.</summary>
        public long SentUpTo = -1;
    }

    private sealed class Retry
    {
        public int Failures;
        public long NextTick;
    }

    public LinkService(DeviceIdentity identity, TrustStore trust, PeerDirectory peers, ChatStore chats, Func<HelloMessage> hello, Action<string>? log = null)
    {
        _identity = identity;
        _trust = trust;
        _peers = peers;
        _chats = chats;
        _hello = hello;
        _log = log;
    }

    /// <summary>Un compañero quedó enlazado o perdió el enlace (presencia en tiempo real).</summary>
    public event Action<string>? PresenceChanged;

    public bool IsLinked(string peerId)
    {
        lock (_lock)
            return _links.ContainsKey(peerId);
    }

    public int LinkCount
    {
        get
        {
            lock (_lock)
                return _links.Count;
        }
    }

    public void Start() => _timer ??= new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TickInterval);

    /// <summary>Intentar enlazar ya con ese compañero (acaba de aparecer o hay algo que mandarle).</summary>
    public void Kick(string peerId)
    {
        lock (_lock)
            _retries.Remove(peerId);
        TryDial(peerId);
    }

    /// <summary>Hay mensajes nuevos para ese compañero: si está enlazado, salen ya; si no, en cuanto se enlace.</summary>
    public void Push(string peerId)
    {
        Link? link;
        lock (_lock)
            _links.TryGetValue(peerId, out link);
        if (link is not null)
            _ = PushAsync(link);
        else
            Kick(peerId);
    }

    /// <summary>La red cambió: cada enlace debe responder enseguida o se rehace.</summary>
    public void CheckAll()
    {
        List<Link> links;
        lock (_lock)
            links = [.. _links.Values];
        foreach (var link in links)
        {
            link.LastHeard = Math.Min(link.LastHeard, Environment.TickCount64 - (long)(DeadAfter.TotalMilliseconds - 8000));
            _ = SendAsync(link, FrameType.Ping, new PingMessage { T = Environment.TickCount64 }, ProtocolJsonContext.Default.PingMessage);
        }
    }

    // ── Marcar ──────────────────────────────────────────────────────────────────

    private void Tick()
    {
        if (_cts.IsCancellationRequested)
            return;
        var now = Environment.TickCount64;
        List<Link> dead = [];
        lock (_lock)
        {
            foreach (var link in _links.Values)
            {
                if (now - link.LastHeard > DeadAfter.TotalMilliseconds)
                    dead.Add(link);
            }
        }
        foreach (var link in dead)
        {
            _log?.Invoke("Enlace sin respuesta, se rehace: " + IdentityVisuals.ShortCode(link.PeerId));
            link.Cts.Cancel();
        }
        foreach (var peer in _trust.All)
            TryDial(peer.DeviceId);
    }

    private void TryDial(string peerId)
    {
        if (_cts.IsCancellationRequested || _trust.Find(peerId) is not { } trusted || _peers.SupportsChat(peerId) == false)
            return;
        var resolved = _peers.Resolve(peerId);
        if (resolved is null)
            return;
        lock (_lock)
        {
            if (_links.ContainsKey(peerId) || _dialing.Contains(peerId))
                return;
            if (_retries.TryGetValue(peerId, out var retry) && Environment.TickCount64 < retry.NextTick)
                return;
            _dialing.Add(peerId);
        }
        byte[] key;
        try
        {
            key = Convert.FromBase64String(trusted.PublicKey);
        }
        catch (FormatException)
        {
            lock (_lock)
                _dialing.Remove(peerId);
            return;
        }
        _ = Task.Run(() => DialAsync(peerId, resolved.Value.EndPoint, key));
    }

    private async Task DialAsync(string peerId, IPEndPoint endPoint, byte[] key)
    {
        var ct = _cts.Token;
        PeerConnection? conn = null;
        var incompatible = false;
        try
        {
            conn = await PeerConnection.ConnectAsync(endPoint, _identity, k => CryptographicOperations.FixedTimeEquals(k, key), ct).ConfigureAwait(false);
            await conn.Frames.SendAsync(FrameType.Hello, _hello(), ProtocolJsonContext.Default.HelloMessage, ct).ConfigureAwait(false);
            HelloMessage reply;
            try
            {
                using var frame = await conn.Frames.ReadExpectAsync(HelloTimeout, ct, FrameType.Hello).ConfigureAwait(false);
                reply = frame.Parse(ProtocolJsonContext.Default.HelloMessage);
            }
            catch (EndOfStreamException)
            {
                // Cerró sin responder al HELLO del enlace: es una versión sin chats.
                incompatible = true;
                throw;
            }
            if (reply.Purpose != Purposes.Link)
            {
                incompatible = true;
                return;
            }
            var link = new Link(conn, peerId, outgoing: true);
            if (!Register(link))
                return;
            conn = null; // Ahora es del enlace.
            lock (_lock)
                _retries.Remove(peerId);
            await RunAsync(link).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is PeerConnectException or IOException or TimeoutException or ProtocolException
                                       or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException
                                       or ObjectDisposedException or OperationCanceledException)
        {
            if (!incompatible)
                Failed(peerId, incompatible: false);
        }
        finally
        {
            if (conn is not null)
                await conn.DisposeAsync().ConfigureAwait(false);
            lock (_lock)
                _dialing.Remove(peerId);
        }
        if (incompatible)
            Failed(peerId, incompatible: true);
    }

    private void Failed(string peerId, bool incompatible)
    {
        lock (_lock)
        {
            if (!_retries.TryGetValue(peerId, out var retry))
                _retries[peerId] = retry = new Retry();
            var seconds = incompatible ? IncompatibleBackoffSeconds : BackoffSeconds[Math.Min(retry.Failures, BackoffSeconds.Length - 1)];
            retry.Failures++;
            retry.NextTick = Environment.TickCount64 + seconds * 1000L;
        }
    }

    // ── Aceptar (lado servidor) ─────────────────────────────────────────────────

    /// <summary>Un compañero de confianza abrió el enlace. Vuelve cuando el enlace termina.</summary>
    public async Task AcceptAsync(PeerConnection conn, CancellationToken ct)
    {
        if (!_trust.IsTrustedKey(conn.RemotePublicKey) || _cts.IsCancellationRequested)
            return;
        await conn.Frames.SendAsync(FrameType.Hello, _hello(), ProtocolJsonContext.Default.HelloMessage, ct).ConfigureAwait(false);
        var link = new Link(conn, conn.RemoteDeviceId, outgoing: false);
        if (!Register(link))
        {
            await SendQuietlyAsync(conn, FrameType.Bye, new ByeMessage(), ProtocolJsonContext.Default.ByeMessage).ConfigureAwait(false);
            return;
        }
        lock (_lock)
            _retries.Remove(link.PeerId);
        await RunAsync(link).ConfigureAwait(false);
    }

    // ── Un enlace por pareja ────────────────────────────────────────────────────

    private bool Register(Link link)
    {
        Link? replaced = null;
        lock (_lock)
        {
            if (_cts.IsCancellationRequested)
                return false;
            if (_links.TryGetValue(link.PeerId, out var existing))
            {
                if (!Prefer(link, existing))
                    return false;
                replaced = existing;
            }
            _links[link.PeerId] = link;
        }
        replaced?.Cts.Cancel();
        _log?.Invoke($"Enlace con {IdentityVisuals.ShortCode(link.PeerId)} ({(link.Outgoing ? "saliente" : "entrante")}).");
        if (replaced is null)
            PresenceChanged?.Invoke(link.PeerId);
        return true;
    }

    /// <summary>
    /// ¿Se queda el enlace nuevo? Cada lado marca solo cuando no tiene enlace, así que un enlace nuevo sobre uno que
    /// ya tiene unos segundos significa que el otro perdió el viejo (cambió de red, se reinició): el nuevo lo
    /// reemplaza. Si los dos marcan a la vez, manda el que inició el identificador menor; entre dos del mismo tipo,
    /// el más nuevo. Un enlace callado hace rato siempre se reemplaza.
    /// </summary>
    private bool Prefer(Link candidate, Link existing)
    {
        var now = Environment.TickCount64;
        return Prefer(IsCanonical(candidate), IsCanonical(existing), TimeSpan.FromMilliseconds(now - existing.CreatedAt),
            TimeSpan.FromMilliseconds(now - existing.LastHeard));
    }

    /// <summary>La regla de <see cref="Prefer(Link, Link)"/>, sin estado (se prueba aparte).</summary>
    internal static bool Prefer(bool candidateCanonical, bool existingCanonical, TimeSpan existingAge, TimeSpan existingSilence) =>
        existingSilence > 2 * PingInterval || existingAge > SimultaneousDialWindow || candidateCanonical || !existingCanonical;

    private bool IsCanonical(Link link) => link.Outgoing == (string.CompareOrdinal(_identity.DeviceId, link.PeerId) < 0);

    private void Unregister(Link link)
    {
        var removed = false;
        lock (_lock)
        {
            if (_links.TryGetValue(link.PeerId, out var current) && current == link)
            {
                _links.Remove(link.PeerId);
                removed = true;
                // Se vuelve a intentar enseguida (cambio de red, el otro se reinició).
                _retries.Remove(link.PeerId);
            }
        }
        if (removed)
            PresenceChanged?.Invoke(link.PeerId);
    }

    // ── Conversación por el enlace ──────────────────────────────────────────────

    private async Task RunAsync(Link link)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, link.Cts.Token);
        var ct = linked.Token;
        var pinger = PingLoopAsync(link, ct);
        try
        {
            await link.Conn.Frames.SendAsync(FrameType.ChatSync, new ChatSyncMessage { HaveYours = _chats.TheirUpTo(link.PeerId) },
                ProtocolJsonContext.Default.ChatSyncMessage, ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                using var frame = await link.Conn.Frames.ReadOrEndAsync(DeadAfter, ct).ConfigureAwait(false);
                if (frame is null)
                    break;
                link.LastHeard = Environment.TickCount64;
                switch (frame.Type)
                {
                    case FrameType.ChatSync:
                        // "Tengo tus mensajes hasta el N": va todo lo posterior.
                        var have = frame.Parse(ProtocolJsonContext.Default.ChatSyncMessage).HaveYours;
                        _chats.CatchUp(link.PeerId, have);
                        link.SentUpTo = have;
                        await PushAsync(link).ConfigureAwait(false);
                        break;
                    case FrameType.ChatMessage:
                        _chats.AddRemote(link.PeerId, frame.Parse(ProtocolJsonContext.Default.ChatWireMessage));
                        await link.Conn.Frames.SendAsync(FrameType.ChatAck, new ChatAckMessage { Received = _chats.TheirUpTo(link.PeerId) },
                            ProtocolJsonContext.Default.ChatAckMessage, ct).ConfigureAwait(false);
                        break;
                    case FrameType.ChatAck:
                        _chats.MarkDelivered(link.PeerId, frame.Parse(ProtocolJsonContext.Default.ChatAckMessage).Received);
                        break;
                    case FrameType.Ping:
                        await link.Conn.Frames.SendAsync(FrameType.Pong, new PingMessage(), ProtocolJsonContext.Default.PingMessage, ct).ConfigureAwait(false);
                        break;
                    case FrameType.Pong:
                        break;
                    case FrameType.Bye:
                        return;
                    default:
                        throw new ProtocolException("Mensaje inesperado en el enlace: " + frame.Type);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ProtocolException or ObjectDisposedException
                                       or System.Net.Sockets.SocketException or TransferException)
        {
            _log?.Invoke($"Enlace con {IdentityVisuals.ShortCode(link.PeerId)} cerrado: {ex.Message}");
        }
        finally
        {
            link.Cts.Cancel();
            Unregister(link);
            await link.Conn.DisposeAsync().ConfigureAwait(false);
            try
            {
                await pinger.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task PushAsync(Link link)
    {
        if (link.SentUpTo < 0)
            return; // Aún no llegó su ChatSync: no sabemos qué tiene.
        await link.PushGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var m in _chats.OwnAfter(link.PeerId, link.SentUpTo))
            {
                var wire = new ChatWireMessage { Id = m.Id, Seq = m.Seq, Time = m.Time, Text = m.Text ?? "" };
                if (!await SendAsync(link, FrameType.ChatMessage, wire, ProtocolJsonContext.Default.ChatWireMessage).ConfigureAwait(false))
                    return;
                link.SentUpTo = m.Seq;
            }
        }
        finally
        {
            link.PushGate.Release();
        }
    }

    private async Task PingLoopAsync(Link link, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(PingInterval, ct).ConfigureAwait(false);
            await SendAsync(link, FrameType.Ping, new PingMessage { T = Environment.TickCount64 }, ProtocolJsonContext.Default.PingMessage).ConfigureAwait(false);
        }
    }

    private static async Task<bool> SendAsync<T>(Link link, FrameType type, T message, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        try
        {
            await link.Conn.Frames.SendAsync(type, message, info, link.Cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or TransferException
                                       or System.Net.Sockets.SocketException)
        {
            link.Cts.Cancel();
            return false;
        }
    }

    private static async Task SendQuietlyAsync<T>(PeerConnection conn, FrameType type, T message, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await conn.Frames.SendAsync(type, message, info, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        _cts.Cancel();
        List<Link> links;
        lock (_lock)
            links = [.. _links.Values];
        foreach (var link in links)
            link.Cts.Cancel();
        // Dar un momento a que se cierren limpias.
        var deadline = Environment.TickCount64 + 2000;
        while (LinkCount > 0 && Environment.TickCount64 < deadline)
            await Task.Delay(50).ConfigureAwait(false);
    }
}

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using AlToque.Core.Discovery;
using AlToque.Core.Pairing;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;

namespace AlToque.Core.Transfer;

/// <summary>Dependencias del servidor, provistas por el agente.</summary>
public sealed class ServerContext
{
    public required DeviceIdentity Identity { get; init; }
    public required TrustStore Trust { get; init; }
    public required PairingService Pairing { get; init; }
    public required Func<AppSettings> Settings { get; init; }
    public required Func<ReceiveMode> EffectiveMode { get; init; }

    /// <summary>Claves bloqueadas por el usuario en esta ejecución ("rechazar y bloquear").</summary>
    public required Func<string, bool> IsBlocked { get; init; }

    public required Action<string> Block { get; init; }

    public Func<bool> IsPublicNetwork { get; init; } = () => false;
    public Action<string>? Log { get; init; }

    /// <summary>Enlace permanente de un compañero de confianza (chats); vuelve cuando el enlace termina.</summary>
    public Func<PeerConnection, CancellationToken, Task>? AcceptLink { get; init; }
}

/// <summary>
/// Servidor TLS: recibe ofertas, emparejamientos y conexiones de datos. Solo acepta datos dentro de una sesión
/// aceptada por el usuario y con un token válido.
/// </summary>
public sealed class TransferServer : IAsyncDisposable
{
    public static readonly TimeSpan OfferTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(10);
    private const int MaxActiveSessions = 3;
    // Los enlaces de chat ocupan una conexión por compañero en línea.
    private const int MaxConnections = 96;
    private const int MaxOffersPerMinute = 5;

    private readonly ServerContext _ctx;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, ReceiveSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<long>> _offerTimes = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _connectionSlots = new(MaxConnections, MaxConnections);
    private SslStreamCertificateContext? _certContext;
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private Timer? _watchdog;

    public TransferServer(ServerContext ctx) => _ctx = ctx;

    /// <summary>Llega una oferta que el usuario tiene que aceptar o rechazar.</summary>
    public event Action<IncomingOffer>? OfferReceived;

    /// <summary>Empieza una recepción aceptada (también las aceptadas automáticamente).</summary>
    public event Action<TransferInfo>? TransferStarted;

    public event Action<TransferInfo>? TransferFinished;

    /// <summary>Un compañero se conectó y su clave quedó comprobada por TLS.</summary>
    public event Action<string, string?, string?, string?>? PeerVerified;

    public int Port { get; private set; }
    public bool IsListening => _listener is not null;

    public bool HasActiveSessions
    {
        get
        {
            lock (_lock)
                return _sessions.Values.Any(s => s.IsActive);
        }
    }

    /// <summary>Escucha en el puerto preferido o, si está ocupado, en uno libre.</summary>
    public void Start(int preferredPort)
    {
        if (_listener is not null)
            return;
        _certContext ??= PeerConnection.CreateServerContext(_ctx.Identity);
        Socket? socket = null;
        foreach (var port in new[] { preferredPort, preferredPort + 1, preferredPort + 2, 0 })
        {
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                if (OperatingSystem.IsWindows())
                    socket.ExclusiveAddressUse = true;
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                socket.Listen(64);
                break;
            }
            catch (SocketException)
            {
                socket?.Dispose();
                socket = null;
            }
        }
        if (socket is null)
            throw new InvalidOperationException("No hay puerto TCP disponible.");
        _listener = socket;
        Port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(socket, _cts.Token));
        _watchdog ??= new Timer(_ => CheckIdleSessions(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        _ctx.Log?.Invoke($"Escuchando en el puerto {Port}.");
    }

    /// <summary>Deja de aceptar conexiones nuevas; las sesiones en curso siguen.</summary>
    public void Stop()
    {
        var listener = _listener;
        if (listener is null)
            return;
        _listener = null;
        _cts?.Cancel();
        listener.Dispose();
    }

    /// <summary>Cancela una recepción en curso desde la interfaz.</summary>
    public async Task CancelReceiveAsync(TransferInfo info)
    {
        ReceiveSession? session;
        lock (_lock)
            session = _sessions.Values.FirstOrDefault(s => s.Info.Id == info.Id);
        if (session is null)
            return;
        if (session.NotifyCancel is { } notify)
        {
            try
            {
                await notify().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
        session.Fail(ErrorCodes.Cancelled, null, cancelled: true);
    }

    private async Task AcceptLoopAsync(Socket listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            if (!_connectionSlots.Wait(0))
            {
                client.Dispose();
                continue;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleConnectionAsync(client).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _ctx.Log?.Invoke("Conexión descartada: " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    _connectionSlots.Release();
                }
            });
        }
    }

    /// <summary>Política durante el apretón de manos: si no pasa, se corta sin revelar nada.</summary>
    private bool AllowKeyAtHandshake(byte[] spki)
    {
        var id = PeerKeys.DeviceIdFromSpki(spki);
        if (id == _ctx.Identity.DeviceId || _ctx.IsBlocked(id))
            return false;
        if (_ctx.Trust.IsTrustedKey(spki))
            return true;
        if (_ctx.EffectiveMode() == ReceiveMode.Everyone || _ctx.Pairing.IsWindowOpen)
            return true;
        // Un equipo cercano puede pedir conectarse (solo sirve para emparejar: lo demás exige confianza).
        if (_ctx.Pairing.AcceptsRequestFrom?.Invoke(id) == true)
            return true;
        lock (_lock)
            return _sessions.Values.Any(s => s.IsActive && s.IsSender(spki));
    }

    private async Task HandleConnectionAsync(Socket client)
    {
        var settings = _ctx.Settings();
        var local = (IPEndPoint?)client.LocalEndPoint;
        if (local is null || !LocalNetwork.IsEligibleLocalAddress(local.Address, settings.ExcludeVirtualAdapters))
        {
            client.Dispose();
            return;
        }
        var ct = _cts?.Token ?? CancellationToken.None;
        var conn = await PeerConnection.AcceptAsync(client, _ctx.Identity, _certContext!, AllowKeyAtHandshake, ct).ConfigureAwait(false);
        if (conn is null)
            return;

        await using (conn.ConfigureAwait(false))
        {
            Frame first;
            try
            {
                first = await conn.Frames.ReadExpectAsync(FirstFrameTimeout, ct, FrameType.Hello, FrameType.StreamOpen).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ProtocolException or IOException or TimeoutException)
            {
                return;
            }

            using (first)
            {
                if (first.Type == FrameType.StreamOpen)
                {
                    await HandleDataAsync(conn, first.Parse(ProtocolJsonContext.Default.StreamOpenMessage)).ConfigureAwait(false);
                    return;
                }
                var hello = first.Parse(ProtocolJsonContext.Default.HelloMessage);
                if (hello.V < 1)
                    return;
                var trusted = _ctx.Trust.IsTrustedKey(conn.RemotePublicKey);
                if (trusted)
                    PeerVerified?.Invoke(conn.RemoteDeviceId, TextLimits.CleanAlias(hello.Alias), hello.Os, Capabilities.AvatarOf(hello.Caps));

                switch (hello.Purpose)
                {
                    case Purposes.Pair:
                        await _ctx.Pairing.HandleResponderAsync(conn, hello, ct).ConfigureAwait(false);
                        break;
                    case Purposes.Probe:
                        await conn.Frames.SendAsync(FrameType.Hello, LocalHello(Purposes.Probe), ProtocolJsonContext.Default.HelloMessage, ct).ConfigureAwait(false);
                        break;
                    case Purposes.Transfer:
                        await HandleOfferAsync(conn, hello, trusted, ct).ConfigureAwait(false);
                        break;
                    case Purposes.Link when trusted && _ctx.AcceptLink is { } acceptLink:
                        await acceptLink(conn, ct).ConfigureAwait(false);
                        break;
                }
            }
        }
    }

    private HelloMessage LocalHello(string purpose) => new()
    {
        V = AppInfo.ProtocolVersion,
        Purpose = purpose,
        Alias = _ctx.Settings().Alias,
        Os = AppInfo.OsName,
        AppVersion = AppInfo.Version,
        Caps = Capabilities.ForHello(_ctx.Settings().Avatar),
    };

    private static Task SendErrorAsync(PeerConnection conn, string code, string? message, CancellationToken ct) =>
        conn.Frames.SendAsync(FrameType.Error, new ErrorMessage { Code = code, Message = message }, ProtocolJsonContext.Default.ErrorMessage, ct);

    private static Task SendDeclineAsync(PeerConnection conn, string sessionId, string reason, CancellationToken ct, long? missing = null) =>
        conn.Frames.SendAsync(FrameType.Decision, new DecisionMessage
        {
            SessionId = sessionId,
            Result = DecisionResults.Declined,
            Reason = reason,
            MissingBytes = missing,
        }, ProtocolJsonContext.Default.DecisionMessage, ct);

    private async Task HandleOfferAsync(PeerConnection conn, HelloMessage hello, bool trusted, CancellationToken ct)
    {
        var mode = _ctx.EffectiveMode();
        if (!trusted && mode != ReceiveMode.Everyone)
        {
            // Llegó con la ventana de emparejamiento abierta: solo sirve para emparejar.
            await SendErrorAsync(conn, ErrorCodes.Policy, null, ct).ConfigureAwait(false);
            return;
        }
        if (_ctx.IsPublicNetwork())
        {
            await SendErrorAsync(conn, ErrorCodes.NetworkPublic, null, ct).ConfigureAwait(false);
            return;
        }

        await conn.Frames.SendAsync(FrameType.Hello, LocalHello(Purposes.Transfer), ProtocolJsonContext.Default.HelloMessage, ct).ConfigureAwait(false);

        OfferMessage offer;
        using (var offerFrame = await conn.Frames.ReadExpectAsync(TimeSpan.FromSeconds(30), ct, FrameType.Offer).ConfigureAwait(false))
            offer = offerFrame.Parse(ProtocolJsonContext.Default.OfferMessage);

        if (!Base32.IsValid(offer.SessionId, 26))
            return;
        var peerId = conn.RemoteDeviceId;
        var rateKeyIp = conn.RemoteEndPoint.Address.ToString();
        if (!CheckRate(peerId) || !CheckRate("ip:" + rateKeyIp))
        {
            await SendDeclineAsync(conn, offer.SessionId, ErrorCodes.RateLimited, ct).ConfigureAwait(false);
            return;
        }

        var settings = _ctx.Settings();
        ReceivePlan plan;
        try
        {
            plan = ReceivePlan.Build(offer, settings.ReceiveFolder, settings.ConflictPolicy, NameSanitizer.UseWindowsRulesByDefault);
        }
        catch (TransferException ex)
        {
            _ctx.Log?.Invoke("Oferta rechazada: " + ex.Message);
            await SendDeclineAsync(conn, offer.SessionId, ex.Code, ct).ConfigureAwait(false);
            return;
        }

        var missing = MissingSpace(plan);
        if (missing > 0)
        {
            await SendDeclineAsync(conn, offer.SessionId, ErrorCodes.DiskFull, ct, missing).ConfigureAwait(false);
            return;
        }

        var trustedPeer = trusted ? _ctx.Trust.Find(peerId) : null;
        var info = new TransferInfo(TransferDirection.Receive, trustedPeer?.DisplayName ?? TextLimits.CleanAlias(hello.Alias) ?? IdentityVisuals.ShortCode(peerId))
        {
            PeerId = peerId,
            PeerAvatar = Capabilities.AvatarOf(hello.Caps),
            PeerTrusted = trusted,
            FileCount = plan.Files.Count,
            TotalBytes = plan.AcceptedBytes,
            FirstName = plan.Files.FirstOrDefault()?.Components[0] ?? plan.Dirs.FirstOrDefault()?[0] ?? "",
            Message = plan.Message,
            AcceptDeadline = DateTimeOffset.Now + OfferTimeout,
            DestinationFolder = plan.SingleTopFolder ?? plan.Root,
        };
        var session = new ReceiveSession(offer.SessionId, conn.RemotePublicKey, info, plan, settings.ConflictPolicy);

        bool busy;
        lock (_lock)
        {
            var active = _sessions.Values.Where(s => s.IsActive).ToList();
            busy = active.Count >= MaxActiveSessions ||
                   active.Any(s => s.State == ReceiveState.Offered && s.IsSender(conn.RemotePublicKey)) ||
                   _sessions.ContainsKey(offer.SessionId);
            if (!busy)
                _sessions[offer.SessionId] = session;
        }
        if (busy)
        {
            await SendDeclineAsync(conn, offer.SessionId, ErrorCodes.Busy, ct).ConfigureAwait(false);
            return;
        }

        info.SetState(TransferState.WaitingForAccept);
        var incoming = new IncomingOffer(info, plan, peerId, trusted, info.AcceptDeadline.Value);

        // Lector de control: detecta si el emisor se retira o cancela mientras esperamos la respuesta.
        var senderGone = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controlReader = ReadControlAsync(conn, session, senderGone, ct);

        try
        {
            OfferResponse response;
            // De un compañero de confianza se acepta sin preguntar, salvo que traiga programas: esos siempre se confirman.
            if (trustedPeer is { AutoAccept: true } && !plan.HasDangerous)
            {
                response = new OfferResponse(OfferResponseKind.Accept);
            }
            else
            {
                if (OfferReceived is null)
                    response = new OfferResponse(OfferResponseKind.Decline);
                else
                {
                    OfferReceived.Invoke(incoming);
                    var winner = await Task.WhenAny(incoming.Response, senderGone.Task, Task.Delay(OfferTimeout, ct)).ConfigureAwait(false);
                    if (winner == incoming.Response)
                        response = await incoming.Response.ConfigureAwait(false);
                    else
                    {
                        incoming.Withdraw();
                        var timedOut = winner != senderGone.Task;
                        if (timedOut)
                            await SendDeclineAsync(conn, offer.SessionId, ErrorCodes.AcceptTimeout, ct).ConfigureAwait(false);
                        session.SetDeclined(timedOut ? ErrorCodes.AcceptTimeout : ErrorCodes.Cancelled);
                        return;
                    }
                }
            }

            if (response.Kind != OfferResponseKind.Accept)
            {
                if (response.Kind == OfferResponseKind.DeclineAndBlock)
                    _ctx.Block(peerId);
                await SendDeclineAsync(conn, offer.SessionId, ErrorCodes.Declined, ct).ConfigureAwait(false);
                session.SetDeclined(ErrorCodes.Declined);
                return;
            }

            // Otra carpeta elegida en la tarjeta: se vuelve a planificar y validar.
            var finalPlan = plan;
            if (response.Folder is { } folder && !string.Equals(Path.GetFullPath(folder), plan.Root, StringComparison.OrdinalIgnoreCase))
            {
                finalPlan = ReceivePlan.Build(offer, folder, settings.ConflictPolicy, NameSanitizer.UseWindowsRulesByDefault);
                var missingNew = MissingSpace(finalPlan);
                if (missingNew > 0)
                {
                    await SendDeclineAsync(conn, offer.SessionId, ErrorCodes.DiskFull, ct, missingNew).ConfigureAwait(false);
                    session.Fail(ErrorCodes.DiskFull, null);
                    return;
                }
                info.DestinationFolder = finalPlan.SingleTopFolder ?? finalPlan.Root;
                info.TotalBytes = finalPlan.AcceptedBytes;
            }

            // Archivos que el usuario desmarcó en la tarjeta (por ejemplo, un programa que no esperaba).
            if (response.SkipFileIds is { } skip)
            {
                finalPlan = finalPlan.WithSkipped(skip);
                info.TotalBytes = finalPlan.AcceptedBytes;
                info.FileCount = finalPlan.AcceptedCount;
            }

            // Nada se escribe en disco antes de este punto.
            Directory.CreateDirectory(finalPlan.Root);
            foreach (var dir in finalPlan.Dirs)
                SafeFs.EnsureDirectories(finalPlan.Root, dir);

            var cancelNotified = false;
            session.NotifyCancel = async () =>
            {
                cancelNotified = true;
                await TrySendAsync(conn, FrameType.Cancel, new CancelMessage { Reason = ErrorCodes.Cancelled }, ProtocolJsonContext.Default.CancelMessage).ConfigureAwait(false);
            };
            var accepted = session.Accept(finalPlan);
            await conn.Frames.SendAsync(FrameType.Decision, new DecisionMessage
            {
                SessionId = offer.SessionId,
                Result = accepted.Count == finalPlan.Files.Count ? DecisionResults.Accepted : DecisionResults.Partial,
                Files = accepted,
            }, ProtocolJsonContext.Default.DecisionMessage, ct).ConfigureAwait(false);

            info.SetState(TransferState.Transferring);
            TransferStarted?.Invoke(info);
            session.CompleteIfNothingPending();

            var done = await Task.WhenAny(session.Finished, senderGone.Task).ConfigureAwait(false);
            if (done == senderGone.Task && !session.Finished.IsCompleted)
            {
                var why = await senderGone.Task.ConfigureAwait(false);
                // Un BYE con todo terminado da tiempo a que lleguen los últimos resultados.
                if (why == "bye")
                {
                    if (await Task.WhenAny(session.Finished, Task.Delay(TimeSpan.FromSeconds(10), ct)).ConfigureAwait(false) != session.Finished)
                        session.Fail(ErrorCodes.Io, "El emisor terminó sin completar.");
                }
                else
                {
                    session.Fail(why == "cancel" ? ErrorCodes.Cancelled : ErrorCodes.Unreachable, null, cancelled: why == "cancel");
                }
            }

            if (info.State == TransferState.Cancelled && !senderGone.Task.IsCompleted && !cancelNotified)
            {
                // Cancelado desde este lado: se avisa al emisor.
                await TrySendAsync(conn, FrameType.Cancel, new CancelMessage { Reason = ErrorCodes.Cancelled }, ProtocolJsonContext.Default.CancelMessage).ConfigureAwait(false);
            }
            else if (info.IsSuccess || info.State == TransferState.PartiallyCompleted)
            {
                await TrySendAsync(conn, FrameType.Bye, new ByeMessage(), ProtocolJsonContext.Default.ByeMessage).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is ProtocolException or IOException or TimeoutException or TransferException or OperationCanceledException)
        {
            incoming.Withdraw();
            session.Fail(ex switch
            {
                TransferException te => te.Code,
                IOException => ErrorCodes.Unreachable,
                TimeoutException => ErrorCodes.Timeout,
                _ => ErrorCodes.Protocol,
            }, ex.Message);
        }
        finally
        {
            incoming.Withdraw();
            if (!session.Info.IsFinished)
            {
                if (session.State == ReceiveState.Offered)
                    session.SetDeclined(ErrorCodes.Cancelled);
                else
                    session.Fail(ErrorCodes.Protocol, null);
            }
            TransferFinished?.Invoke(session.Info);
            lock (_lock)
                _sessions.Remove(offer.SessionId);
            conn.Abort();
            await controlReader.ConfigureAwait(false);
        }
    }

    /// <summary>Lee el canal de control mientras dura la sesión. Solo admite CANCEL o BYE.</summary>
    private static async Task ReadControlAsync(PeerConnection conn, ReceiveSession session, TaskCompletionSource<string> gone, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                using var frame = await conn.Frames.ReadOrEndAsync(TimeSpan.FromDays(1), ct).ConfigureAwait(false);
                if (frame is null)
                {
                    gone.TrySetResult("closed");
                    return;
                }
                switch (frame.Type)
                {
                    case FrameType.Cancel:
                        gone.TrySetResult("cancel");
                        return;
                    case FrameType.Bye:
                        gone.TrySetResult("bye");
                        return;
                    default:
                        session.Fail(ErrorCodes.Protocol, "Mensaje inesperado en el canal de control.");
                        gone.TrySetResult("protocol");
                        return;
                }
            }
        }
        catch (Exception)
        {
            gone.TrySetResult("closed");
        }
    }

    private async Task HandleDataAsync(PeerConnection conn, StreamOpenMessage open)
    {
        ReceiveSession? session;
        lock (_lock)
            _sessions.TryGetValue(open.SessionId, out session);
        if (session is null || !session.IsSender(conn.RemotePublicKey))
            return;

        session.AddConnection(conn);
        try
        {
            var current = open;
            while (true)
            {
                var planned = session.TryOpenStream(current, conn.RemotePublicKey);
                if (planned is null)
                {
                    session.Fail(ErrorCodes.Protocol, "Flujo de datos no autorizado.");
                    return;
                }
                await ReceiveFileAsync(conn, session, planned).ConfigureAwait(false);

                using var next = await conn.Frames.ReadOrEndAsync(IdleTimeout, session.Token).ConfigureAwait(false);
                if (next is null)
                    return;
                if (next.Type != FrameType.StreamOpen)
                {
                    session.Fail(ErrorCodes.Protocol, "Mensaje inesperado en el canal de datos.");
                    return;
                }
                current = next.Parse(ProtocolJsonContext.Default.StreamOpenMessage);
                if (current.SessionId != session.SessionId)
                {
                    session.Fail(ErrorCodes.Protocol, "Sesión distinta en el mismo flujo.");
                    return;
                }
            }
        }
        catch (TransferException ex)
        {
            session.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex) when (ex is ProtocolException or TimeoutException or System.Security.Authentication.AuthenticationException)
        {
            session.Fail(ex is TimeoutException ? ErrorCodes.Timeout : ErrorCodes.Protocol, ex.Message);
        }
        catch (IOException ex)
        {
            session.RemoveConnection(conn);
            await session.FailAfterGraceAsync(ErrorCodes.Unreachable, ex.Message).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            session.RemoveConnection(conn);
        }
    }

    /// <summary>Recibe un archivo: CHUNK* y FILE_END. Responde con FILE_RESULT.</summary>
    private async Task ReceiveFileAsync(PeerConnection conn, ReceiveSession session, PlannedFile planned)
    {
        var ct = session.Token;
        IncomingFile? file = null;
        string? openError = null;
        try
        {
            file = IncomingFile.Create(planned, session.Plan.Root, session.Policy);
        }
        catch (TransferException ex) when (ex.Code != ErrorCodes.DiskFull)
        {
            openError = ex.Code;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (ex is IOException io && IncomingFile.IsDiskFull(io))
                throw new TransferException(ErrorCodes.DiskFull, ex.Message);
            openError = ErrorCodes.Io;
        }

        await using (file)
        {
            long received = 0;
            while (true)
            {
                using var frame = await conn.Frames.ReadAsync(IdleTimeout, ct).ConfigureAwait(false);
                session.Touch();
                if (frame.Type == FrameType.Chunk)
                {
                    received += frame.Length;
                    if (received > planned.Offer.Size)
                        throw new ProtocolException("Llegaron más bytes de los declarados.");
                    if (file is not null)
                        await file.WriteAsync(frame.PayloadMemory, ct).ConfigureAwait(false);
                    session.Info.AddBytes(frame.Length);
                    continue;
                }
                if (frame.Type != FrameType.FileEnd)
                    throw new ProtocolException("Mensaje inesperado durante un archivo.");

                var end = frame.Parse(ProtocolJsonContext.Default.FileEndMessage);
                if (end.FileId != planned.Offer.FileId)
                    throw new ProtocolException("FILE_END de otro archivo.");

                string? error = openError;
                string? finalPath = null;
                if (error is null && end.Aborted)
                    error = ErrorCodes.SourceChanged;
                if (error is null && end.Sha256.Length != 64)
                    throw new ProtocolException("Hash inválido.");
                if (error is null)
                {
                    try
                    {
                        finalPath = await file!.CompleteAsync(end, session.SenderKey is { } k ? PeerKeys.DeviceIdFromSpki(k) : "unknown").ConfigureAwait(false);
                    }
                    catch (TransferException ex) when (ex.Code != ErrorCodes.DiskFull)
                    {
                        error = ex.Code;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        error = ErrorCodes.Io;
                    }
                }

                await conn.Frames.SendAsync(FrameType.FileResult, new FileResultMessage
                {
                    FileId = planned.Offer.FileId,
                    Ok = error is null,
                    Error = error,
                }, ProtocolJsonContext.Default.FileResultMessage, ct).ConfigureAwait(false);
                session.MarkFileDone(planned.Offer.FileId, error is null, finalPath);
                return;
            }
        }
    }

    private static long MissingSpace(ReceivePlan plan)
    {
        try
        {
            var root = Path.GetPathRoot(plan.Root);
            if (string.IsNullOrEmpty(root))
                return 0;
            var free = new DriveInfo(root).AvailableFreeSpace;
            var needed = plan.AcceptedBytes + plan.AcceptedBytes / 20;
            return needed > free ? needed - free : 0;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private bool CheckRate(string key)
    {
        var now = Environment.TickCount64;
        lock (_lock)
        {
            if (_offerTimes.Count > 1024)
                _offerTimes.Clear();
            if (!_offerTimes.TryGetValue(key, out var queue))
                _offerTimes[key] = queue = new Queue<long>();
            while (queue.Count > 0 && now - queue.Peek() > 60_000)
                queue.Dequeue();
            if (queue.Count >= MaxOffersPerMinute)
                return false;
            queue.Enqueue(now);
            return true;
        }
    }

    private void CheckIdleSessions()
    {
        List<ReceiveSession> idle;
        lock (_lock)
        {
            idle = _sessions.Values
                .Where(s => s.State is ReceiveState.Accepted or ReceiveState.Transferring && s.IdleMilliseconds > IdleTimeout.TotalMilliseconds)
                .ToList();
        }
        foreach (var s in idle)
            s.Fail(ErrorCodes.Timeout, "Sin actividad.");
    }

    private static async Task TrySendAsync<T>(PeerConnection conn, FrameType type, T message, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await conn.Frames.SendAsync(type, message, info, cts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        _watchdog?.Dispose();
        List<ReceiveSession> all;
        lock (_lock)
            all = _sessions.Values.ToList();
        foreach (var s in all)
            s.Fail(ErrorCodes.Cancelled, null, cancelled: true);
        if (_acceptLoop is not null)
            await _acceptLoop.ConfigureAwait(false);
    }
}

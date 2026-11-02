using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using AlToque.Core.Discovery;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;

namespace AlToque.Core.Transfer;

/// <summary>A quién se envía y cómo se comprueba su identidad.</summary>
public sealed record PeerTarget
{
    public required IPEndPoint EndPoint { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>deviceId anunciado o elegido; la clave del certificado debe corresponderle.</summary>
    public string? ExpectedDeviceId { get; init; }

    /// <summary>Clave fijada de un compañero de confianza; debe coincidir byte a byte.</summary>
    public byte[]? PinnedKey { get; init; }

    /// <summary>Código de conexión usado; su huella parcial debe coincidir.</summary>
    public ConnectionCode.Parsed? Code { get; init; }

    /// <summary>
    /// La dirección sale de la caché de otra sesión, no de un anuncio en vivo. Si ahí responde otra clave, lo más
    /// probable es que la IP se haya reasignado: se informa como "no pude conectar", no como suplantación.
    /// </summary>
    public bool FromCache { get; init; }
}

/// <summary>Lado emisor: oferta, espera la decisión y envía los archivos por 1 a 4 conexiones de datos.</summary>
public sealed class TransferClient
{
    public static readonly TimeSpan DecisionTimeout = TransferServer.OfferTimeout + TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(60);
    private const int ChunkSize = 1 << 20;
    private const int MaxDataConnections = 4;

    private readonly DeviceIdentity _identity;
    private readonly TrustStore _trust;
    private readonly Func<string> _alias;
    private readonly Func<string?> _avatar;

    public TransferClient(DeviceIdentity identity, TrustStore trust, Func<string> alias, Func<string?>? avatar = null)
    {
        _identity = identity;
        _trust = trust;
        _alias = alias;
        _avatar = avatar ?? (() => null);
    }

    /// <summary>La clave del receptor quedó verificada (deviceId, punto de escucha, alias, SO).</summary>
    public event Action<string, IPEndPoint, string?, string?, string?>? PeerVerified;

    public TransferInfo CreateInfo(PeerTarget target, OutgoingManifest manifest) => new(TransferDirection.Send, target.DisplayName)
    {
        PeerId = target.ExpectedDeviceId,
        PeerTrusted = target.PinnedKey is not null,
        FileCount = manifest.FileCount,
        TotalBytes = manifest.TotalSize,
        FirstName = manifest.FirstName,
        Message = manifest.Offer.Message,
    };

    /// <summary>Un archivo por enviar: su token de un solo uso y desde qué byte (más de 0 al retomar tras un corte).</summary>
    private readonly record struct Job(OutgoingFile File, string Token, long Offset);

    /// <summary>Esperas entre intentos de reconexión tras un corte (unos dos minutos y medio en total).</summary>
    internal TimeSpan[] ReconnectDelays { get; set; } =
        [.. new[] { 1, 2, 3, 5, 8, 13, 20, 30, 30, 30 }.Select(s => TimeSpan.FromSeconds(s))];

    /// <summary>Solo para pruebas: una pausa por bloque, para que un envío dure lo bastante como para cortarlo.</summary>
    internal TimeSpan ChunkDelayForTesting { get; set; }

    /// <summary>Cortes que se aguantan en un mismo envío antes de darlo por perdido.</summary>
    private const int MaxResumes = 20;

    /// <summary>
    /// Ejecuta el envío completo. Nunca lanza: el resultado queda en <paramref name="info"/>. Con un compañero de
    /// confianza que sabe reanudar, un corte de red (cambio de Wi-Fi, suspensión) no lo termina: se reconecta solo
    /// —en la dirección nueva que dé <paramref name="relocate"/>— y cada archivo sigue desde donde quedó.
    /// </summary>
    public async Task SendAsync(PeerTarget target, OutgoingManifest manifest, TransferInfo info, Func<CancellationToken, Task<PeerTarget?>>? relocate = null)
    {
        var ct = info.CancellationToken;
        PeerConnection? control = null;
        var workers = new List<DataWorker>();
        try
        {
            info.SetState(TransferState.Connecting);
            control = await ConnectVerifiedAsync(target, expectedKey: null, ct).ConfigureAwait(false);
            info.PeerId = control.RemoteDeviceId;
            info.PeerTrusted = _trust.IsTrustedKey(control.RemotePublicKey);

            var hello = await ExchangeHelloAsync(control, target, ct).ConfigureAwait(false);
            if (!info.PeerTrusted && TextLimits.CleanAlias(hello.Alias) is { } alias)
                info.PeerName = alias;
            info.PeerAvatar = Capabilities.AvatarOf(hello.Caps);
            var canResume = info.PeerTrusted && hello.Caps?.Contains(Capabilities.Resume) == true;

            await control.Frames.SendAsync(FrameType.Offer, manifest.Offer, ProtocolJsonContext.Default.OfferMessage, ct).ConfigureAwait(false);
            info.AcceptDeadline = DateTimeOffset.Now + TransferServer.OfferTimeout;
            info.SetState(TransferState.WaitingForAccept);

            var decision = await ReadDecisionAsync(control, manifest, ct).ConfigureAwait(false);
            if (decision.Result == DecisionResults.Declined)
            {
                var reason = decision.Reason is { } r && ErrorCodes.IsKnown(r) ? r : ErrorCodes.Declined;
                var detail = decision.MissingBytes is { } missing ? missing.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                info.SetState(reason is ErrorCodes.Declined or ErrorCodes.AcceptTimeout ? TransferState.Declined : TransferState.Failed, reason, detail);
                return;
            }

            // Resultado de cada archivo ya confirmado por el receptor (para no contarlo dos veces al reanudar).
            var results = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
            var jobs = JobsFrom(decision, manifest, results, info, resuming: false);
            var accepted = jobs.Select(j => j.File).ToList();
            info.TotalBytes = accepted.Sum(f => f.Size);
            info.FileCount = accepted.Count;
            info.SetState(TransferState.Transferring);

            var remoteKey = control.RemotePublicKey;
            var resumes = 0;
            while (jobs.Count > 0)
            {
                try
                {
                    await RunJobsAsync(control, target, remoteKey, manifest.Offer.SessionId, jobs, results, info, workers, ct).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (canResume && resumes < MaxResumes && !ct.IsCancellationRequested && IsConnectionLoss(ex))
                {
                    resumes++;
                    info.SetState(TransferState.Reconnecting);
                    foreach (var w in workers)
                        w.Abort();
                    workers.Clear();
                    await control.DisposeAsync().ConfigureAwait(false);
                    control = null;
                    (control, decision, target) = await ReconnectAsync(target, relocate, remoteKey, manifest, ct).ConfigureAwait(false);
                    if (decision.Result == DecisionResults.Declined)
                        throw new TransferException(decision.Reason is { } why && ErrorCodes.IsKnown(why) ? why : ErrorCodes.Unreachable, "No se pudo retomar.");
                    jobs = JobsFrom(decision, manifest, results, info, resuming: true);
                    // Lo que el receptor ya tiene (algo pudo perderse en vuelo al cortarse).
                    info.SetTransferred(accepted.Sum(f => results.ContainsKey(f.FileId) ? f.Size : 0) + jobs.Sum(j => j.Offset));
                    info.SetState(TransferState.Transferring);
                }
            }

            await TrySendAsync(control, FrameType.Bye, new ByeMessage(), ProtocolJsonContext.Default.ByeMessage).ConfigureAwait(false);
            info.CurrentFile = null;
            info.SetState(info.FilesFailed > 0 ? TransferState.PartiallyCompleted : TransferState.Completed,
                info.FilesFailed > 0 ? workers.Select(w => w.LastError).FirstOrDefault(e => e is not null) ?? ErrorCodes.Io : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (control is not null)
                await TrySendAsync(control, FrameType.Cancel, new CancelMessage { Reason = ErrorCodes.Cancelled }, ProtocolJsonContext.Default.CancelMessage).ConfigureAwait(false);
            info.SetState(TransferState.Cancelled, ErrorCodes.Cancelled);
        }
        catch (PeerConnectException ex)
        {
            info.SetState(TransferState.Failed, ex.Failure switch
            {
                ConnectFailure.Unreachable => ErrorCodes.Unreachable,
                ConnectFailure.Rejected => ErrorCodes.Policy,
                ConnectFailure.IdentityMismatch when target.FromCache => ErrorCodes.Unreachable,
                ConnectFailure.IdentityMismatch => ErrorCodes.IdentityMismatch,
                _ => ErrorCodes.Protocol,
            }, ex.Message);
        }
        catch (TransferException ex)
        {
            info.SetState(ex.Code == ErrorCodes.Cancelled ? TransferState.Cancelled : ex.Code is ErrorCodes.Declined ? TransferState.Declined : TransferState.Failed, ex.Code, ex.Detail);
        }
        catch (TimeoutException ex)
        {
            info.SetState(TransferState.Failed, info.State == TransferState.WaitingForAccept ? ErrorCodes.AcceptTimeout : ErrorCodes.Timeout, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or System.Security.Authentication.AuthenticationException or ObjectDisposedException
                                       or System.Net.Sockets.SocketException)
        {
            info.SetState(TransferState.Failed, ErrorCodes.Unreachable, ex.Message);
        }
        finally
        {
            foreach (var w in workers)
                w.Abort();
            if (control is not null)
                await control.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Un corte de la red o de la conexión (no un rechazo, una cancelación ni un error del protocolo).</summary>
    private static bool IsConnectionLoss(Exception ex) => ex switch
    {
        TransferException te => te.Code is ErrorCodes.Unreachable or ErrorCodes.Timeout,
        IOException or TimeoutException or ObjectDisposedException or System.Net.Sockets.SocketException => true,
        System.Security.Authentication.AuthenticationException => true,
        OperationCanceledException => false,
        _ => false,
    };

    private async Task<HelloMessage> ExchangeHelloAsync(PeerConnection control, PeerTarget target, CancellationToken ct)
    {
        await control.Frames.SendAsync(FrameType.Hello, new HelloMessage
        {
            V = AppInfo.ProtocolVersion,
            Purpose = Purposes.Transfer,
            Alias = _alias(),
            Os = AppInfo.OsName,
            AppVersion = AppInfo.Version,
            Caps = Capabilities.ForHello(_avatar()),
        }, ProtocolJsonContext.Default.HelloMessage, ct).ConfigureAwait(false);

        HelloMessage hello;
        try
        {
            using var helloFrame = await control.Frames.ReadExpectAsync(TimeSpan.FromSeconds(10), ct, FrameType.Hello, FrameType.Error).ConfigureAwait(false);
            if (helloFrame.Type == FrameType.Error)
                throw RemoteError(helloFrame);
            hello = helloFrame.Parse(ProtocolJsonContext.Default.HelloMessage);
        }
        catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or ProtocolException)
        {
            // En TLS 1.3 el receptor rechaza nuestro certificado después del apretón de manos.
            throw new TransferException(ErrorCodes.Policy, ex.Message);
        }
        PeerVerified?.Invoke(control.RemoteDeviceId, target.EndPoint, TextLimits.CleanAlias(hello.Alias), hello.Os, Capabilities.AvatarOf(hello.Caps));
        return hello;
    }

    private static async Task<DecisionMessage> ReadDecisionAsync(PeerConnection control, OutgoingManifest manifest, CancellationToken ct)
    {
        DecisionMessage decision;
        using (var decisionFrame = await control.Frames.ReadExpectAsync(DecisionTimeout, ct, FrameType.Decision, FrameType.Error).ConfigureAwait(false))
        {
            if (decisionFrame.Type == FrameType.Error)
                throw RemoteError(decisionFrame);
            decision = decisionFrame.Parse(ProtocolJsonContext.Default.DecisionMessage);
        }
        if (decision.SessionId != manifest.Offer.SessionId)
            throw new ProtocolException("Decisión de otra sesión.");
        return decision;
    }

    /// <summary>
    /// Lo que hay que enviar según la decisión. Al reanudar, lo ya terminado se cuenta (una sola vez) y lo demás sigue
    /// desde el offset que dice tener el receptor.
    /// </summary>
    private static List<Job> JobsFrom(DecisionMessage decision, OutgoingManifest manifest,
        System.Collections.Concurrent.ConcurrentDictionary<string, bool> results, TransferInfo info, bool resuming)
    {
        var jobs = new List<Job>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in decision.Files ?? [])
        {
            if (!manifest.Files.TryGetValue(a.FileId, out var f) || !seen.Add(a.FileId))
                throw new ProtocolException("Decisión con archivos desconocidos o repetidos.");
            if (!resuming && (a.Offset != 0 || a.Done is not null || a.Failed is not null))
                throw new ProtocolException("Decisión inesperada.");
            if (a.Done == true)
            {
                var ok = a.Failed != true;
                if (results.TryAdd(a.FileId, ok))
                {
                    if (ok)
                        info.FileSucceeded(null);
                    else
                        info.FileFailed();
                }
                continue;
            }
            if (!Base32.IsValid(a.Token, 26) || a.Offset < 0 || a.Offset > f.Size)
                throw new ProtocolException("Decisión con datos inválidos.");
            jobs.Add(new Job(f, a.Token, a.Offset));
        }
        return jobs;
    }

    /// <summary>
    /// Tras un corte: espera, busca la dirección actual del compañero (pudo cambiar de red) y le ofrece retomar la
    /// misma sesión. Reintenta unos minutos; si no vuelve, el envío falla como "no se pudo conectar".
    /// </summary>
    private async Task<(PeerConnection Control, DecisionMessage Decision, PeerTarget Target)> ReconnectAsync(PeerTarget target,
        Func<CancellationToken, Task<PeerTarget?>>? relocate, byte[] remoteKey, OutgoingManifest manifest, CancellationToken ct)
    {
        var resumeOffer = new OfferMessage
        {
            SessionId = manifest.Offer.SessionId,
            Files = manifest.Offer.Files,
            Dirs = manifest.Offer.Dirs,
            TotalSize = manifest.Offer.TotalSize,
            Message = manifest.Offer.Message,
            Resume = true,
        };
        Exception? last = null;
        foreach (var delay in ReconnectDelays)
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            var current = target;
            if (relocate is not null)
            {
                try
                {
                    current = await relocate(ct).ConfigureAwait(false) ?? target;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    current = target;
                }
            }
            PeerConnection? conn = null;
            try
            {
                conn = await ConnectVerifiedAsync(current, remoteKey, ct).ConfigureAwait(false);
                await ExchangeHelloAsync(conn, current, ct).ConfigureAwait(false);
                await conn.Frames.SendAsync(FrameType.Offer, resumeOffer, ProtocolJsonContext.Default.OfferMessage, ct).ConfigureAwait(false);
                var decision = await ReadDecisionAsync(conn, manifest, ct).ConfigureAwait(false);
                var done = conn;
                conn = null;
                return (done, decision, current);
            }
            catch (Exception ex) when (ex is PeerConnectException || IsConnectionLoss(ex) || ex is TransferException { Code: ErrorCodes.Policy })
            {
                last = ex;
            }
            finally
            {
                if (conn is not null)
                    await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
        throw new TransferException(ErrorCodes.Unreachable, last?.Message ?? "No volvió la conexión.");
    }

    /// <summary>Envía la lista por 1 a 4 conexiones de datos; lanza la primera falla (un corte, un error del receptor…).</summary>
    private async Task RunJobsAsync(PeerConnection control, PeerTarget target, byte[] remoteKey, string sessionId, List<Job> jobs,
        System.Collections.Concurrent.ConcurrentDictionary<string, bool> results, TransferInfo info, List<DataWorker> workers, CancellationToken ct)
    {
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var controlWatch = WatchControlAsync(control, watchCts);

        var queue = new ConcurrentQueue<Job>(jobs);
        var connections = Math.Min(MaxDataConnections, jobs.Count);
        for (int i = 0; i < connections; i++)
            workers.Add(new DataWorker(this, target, remoteKey, sessionId, queue, results, info));
        var running = workers.ToList();

        // El primer fallo de cualquier conexión corta las demás; se conserva la causa original.
        Exception? firstFailure = null;
        var workTasks = running.Select(async w =>
        {
            try
            {
                await w.RunAsync(sessionCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException && sessionCts.IsCancellationRequested))
                    Interlocked.CompareExchange(ref firstFailure, ex, null);
                sessionCts.Cancel();
                foreach (var other in running)
                    other.Abort();
            }
        }).ToList();
        var all = Task.WhenAll(workTasks);
        if (await Task.WhenAny(all, controlWatch).ConfigureAwait(false) == controlWatch && await controlWatch.ConfigureAwait(false) is { } remote)
        {
            Interlocked.CompareExchange(ref firstFailure, remote, null);
            sessionCts.Cancel();
            foreach (var w in running)
                w.Abort();
        }
        await all.ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (firstFailure is not null)
        {
            // Si el receptor canceló, su aviso por el canal de control llega casi a la vez que el corte de datos.
            if (firstFailure is not TransferException && !controlWatch.IsCompleted)
                await Task.WhenAny(controlWatch, Task.Delay(1000, ct)).ConfigureAwait(false);
            if (controlWatch.IsCompletedSuccessfully && controlWatch.Result is TransferException { Code: ErrorCodes.Cancelled } cancelled)
                throw cancelled;
            watchCts.Cancel();
            throw firstFailure;
        }
        watchCts.Cancel();
    }

    /// <summary>Comprueba que el puerto responde y devuelve la identidad que presenta (para "Conectar por IP").</summary>
    public async Task<(string DeviceId, byte[] Key, string Alias, string Os)> ProbeAsync(PeerTarget target, CancellationToken ct)
    {
        await using var conn = await ConnectVerifiedAsync(target, null, ct).ConfigureAwait(false);
        await conn.Frames.SendAsync(FrameType.Hello, new HelloMessage
        {
            V = AppInfo.ProtocolVersion,
            Purpose = Purposes.Probe,
            Alias = _alias(),
            Os = AppInfo.OsName,
            AppVersion = AppInfo.Version,
            Caps = Capabilities.ForHello(_avatar()),
        }, ProtocolJsonContext.Default.HelloMessage, ct).ConfigureAwait(false);
        using var frame = await conn.Frames.ReadExpectAsync(TimeSpan.FromSeconds(5), ct, FrameType.Hello).ConfigureAwait(false);
        var hello = frame.Parse(ProtocolJsonContext.Default.HelloMessage);
        PeerVerified?.Invoke(conn.RemoteDeviceId, target.EndPoint, TextLimits.CleanAlias(hello.Alias), hello.Os, Capabilities.AvatarOf(hello.Caps));
        return (conn.RemoteDeviceId, conn.RemotePublicKey, TextLimits.CleanAlias(hello.Alias) ?? "", hello.Os);
    }

    private Task<PeerConnection> ConnectVerifiedAsync(PeerTarget target, byte[]? expectedKey, CancellationToken ct) =>
        PeerConnection.ConnectAsync(target.EndPoint, _identity, key =>
        {
            if (expectedKey is not null)
                return CryptographicOperations.FixedTimeEquals(expectedKey, key);
            if (target.PinnedKey is not null)
                return CryptographicOperations.FixedTimeEquals(target.PinnedKey, key);
            var id = PeerKeys.DeviceIdFromSpki(key);
            if (id == _identity.DeviceId)
                return false;
            if (target.ExpectedDeviceId is not null && id != target.ExpectedDeviceId)
                return false;
            if (target.Code is not null && !target.Code.MatchesFingerprint(PeerKeys.Fingerprint(key)))
                return false;
            return true;
        }, ct);

    /// <summary>Vigila el canal de control durante la transferencia: CANCEL o cierre del receptor.</summary>
    private static async Task<Exception?> WatchControlAsync(PeerConnection control, CancellationTokenSource session)
    {
        try
        {
            using var frame = await control.Frames.ReadOrEndAsync(TimeSpan.FromDays(1), session.Token).ConfigureAwait(false);
            if (frame is null)
                return new TransferException(ErrorCodes.Unreachable, "El receptor cerró la conexión.");
            return frame.Type switch
            {
                FrameType.Cancel => new TransferException(ErrorCodes.Cancelled, "El receptor canceló."),
                FrameType.Error => RemoteError(frame),
                FrameType.Bye => null,
                _ => new ProtocolException("Mensaje inesperado."),
            };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            return session.IsCancellationRequested ? null : new TransferException(ErrorCodes.Unreachable, ex.Message);
        }
    }

    private static TransferException RemoteError(Frame frame)
    {
        var err = frame.Parse(ProtocolJsonContext.Default.ErrorMessage);
        return new TransferException(ErrorCodes.IsKnown(err.Code) ? err.Code : ErrorCodes.Protocol, TextLimits.CleanShortText(err.Message, 200));
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

    /// <summary>
    /// Una conexión de datos. Envía archivos de la cola uno tras otro sin esperar la respuesta de cada uno
    /// ("modo ráfaga"); un lector paralelo recoge los FILE_RESULT.
    /// </summary>
    private sealed class DataWorker(
        TransferClient owner, PeerTarget target, byte[] remoteKey, string sessionId,
        ConcurrentQueue<Job> queue, ConcurrentDictionary<string, bool> results, TransferInfo info)
    {
        private static readonly TimeSpan FinalResultsTimeout = TimeSpan.FromMinutes(2);
        private readonly ConcurrentDictionary<string, bool> _awaiting = new();
        private PeerConnection? _conn;
        private int _sent;
        private int _received;
        private bool _doneSending;

        public string? LastError { get; private set; }

        public void Abort() => _conn?.Abort();

        public async Task RunAsync(CancellationToken ct)
        {
            _conn = await owner.ConnectVerifiedAsync(target, remoteKey, ct).ConfigureAwait(false);
            await using var _ = _conn.ConfigureAwait(false);
            using var readerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var reader = ReadResultsAsync(_conn, readerCts.Token, ct);

            var buffer = ArrayPool<byte>.Shared.Rent(FrameChannel.HeaderSize + ChunkSize);
            try
            {
                while (!reader.IsCompleted && queue.TryDequeue(out var job))
                {
                    ct.ThrowIfCancellationRequested();
                    _awaiting[job.File.FileId] = true;
                    Interlocked.Increment(ref _sent);
                    await SendFileAsync(_conn, job.File, job.Token, job.Offset, buffer, ct).ConfigureAwait(false);
                }
                await _conn.Frames.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                Volatile.Write(ref _doneSending, true);
                if (Volatile.Read(ref _received) == Volatile.Read(ref _sent))
                    readerCts.Cancel();
            }
            try
            {
                await reader.WaitAsync(FinalResultsTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _conn.Abort();
                throw new TransferException(ErrorCodes.Timeout, "El receptor no confirmó los últimos archivos.");
            }
        }

        private bool AllAnswered => Volatile.Read(ref _doneSending) && Volatile.Read(ref _received) == Volatile.Read(ref _sent);

        /// <param name="offset">Lo que el receptor ya tiene de este archivo (al retomar tras un corte).</param>
        private async Task SendFileAsync(PeerConnection conn, OutgoingFile file, string token, long offset, byte[] buffer, CancellationToken ct)
        {
            info.CurrentFile = Path.GetFileName(file.RelativePath);
            await conn.Frames.SendAsync(FrameType.StreamOpen, new StreamOpenMessage
            {
                SessionId = sessionId,
                FileId = file.FileId,
                Token = token,
                Offset = offset,
            }, ProtocolJsonContext.Default.StreamOpenMessage, ct).ConfigureAwait(false);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long sent = offset;
            var aborted = false;
            FileStream? fs = null;
            try
            {
                fs = new FileStream(file.LocalPath, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite | FileShare.Delete,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    BufferSize = 0,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                aborted = true;
            }

            if (fs is not null)
            {
                await using (fs.ConfigureAwait(false))
                {
                    // Al retomar, lo que el receptor ya tiene entra al hash sin volver a enviarse: el hash final cubre
                    // el archivo entero y detecta si el origen cambió entretanto.
                    long hashed = 0;
                    while (hashed < offset && !aborted)
                    {
                        int n;
                        try
                        {
                            n = await fs.ReadAsync(buffer.AsMemory(FrameChannel.HeaderSize, (int)Math.Min(ChunkSize, offset - hashed)), ct).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            n = 0;
                        }
                        if (n == 0)
                        {
                            aborted = true;
                            break;
                        }
                        hash.AppendData(buffer, FrameChannel.HeaderSize, n);
                        hashed += n;
                    }
                    while (!aborted && sent < file.Size)
                    {
                        var want = (int)Math.Min(ChunkSize, file.Size - sent);
                        int n;
                        try
                        {
                            n = await fs.ReadAsync(buffer.AsMemory(FrameChannel.HeaderSize, want), ct).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            n = 0;
                        }
                        if (n == 0)
                        {
                            aborted = true; // Se acortó o dejó de poder leerse mientras se enviaba.
                            break;
                        }
                        hash.AppendData(buffer, FrameChannel.HeaderSize, n);
                        await conn.Frames.SendChunkAsync(buffer, n, ct).ConfigureAwait(false);
                        sent += n;
                        info.AddBytes(n);
                        if (owner.ChunkDelayForTesting > TimeSpan.Zero)
                            await Task.Delay(owner.ChunkDelayForTesting, ct).ConfigureAwait(false);
                    }
                    if (!aborted)
                    {
                        int extra;
                        try
                        {
                            extra = await fs.ReadAsync(buffer.AsMemory(FrameChannel.HeaderSize, 1), ct).ConfigureAwait(false);
                        }
                        catch (IOException)
                        {
                            extra = 0;
                        }
                        aborted = extra > 0; // Creció mientras se enviaba.
                    }
                }
            }
            if (aborted && sent < file.Size)
                info.AddBytes(file.Size - sent);

            await conn.Frames.SendAsync(FrameType.FileEnd, new FileEndMessage
            {
                FileId = file.FileId,
                Size = sent,
                Sha256 = Convert.ToHexStringLower(hash.GetHashAndReset()),
                Aborted = aborted,
            }, ProtocolJsonContext.Default.FileEndMessage, ct).ConfigureAwait(false);
        }

        private async Task ReadResultsAsync(PeerConnection conn, CancellationToken readToken, CancellationToken sessionToken)
        {
            try
            {
                while (!AllAnswered)
                {
                    using var frame = await conn.Frames.ReadAsync(TimeSpan.FromDays(1), readToken).ConfigureAwait(false);
                    if (frame.Type != FrameType.FileResult)
                        throw new ProtocolException("Se esperaba FILE_RESULT.");
                    var result = frame.Parse(ProtocolJsonContext.Default.FileResultMessage);
                    if (!_awaiting.TryRemove(result.FileId, out _))
                        throw new ProtocolException("FILE_RESULT inesperado.");
                    // Cada archivo cuenta una sola vez, aunque se haya retomado tras un corte.
                    var first = results.TryAdd(result.FileId, result.Ok);
                    if (result.Ok)
                    {
                        if (first)
                            info.FileSucceeded(null);
                    }
                    else
                    {
                        LastError = result.Error is { } e && ErrorCodes.IsKnown(e) ? e : ErrorCodes.Io;
                        if (first)
                            info.FileFailed();
                        if (LastError == ErrorCodes.DiskFull)
                            throw new TransferException(ErrorCodes.DiskFull, "Sin espacio en el receptor.");
                    }
                    Interlocked.Increment(ref _received);
                }
            }
            catch (OperationCanceledException) when (!sessionToken.IsCancellationRequested && AllAnswered)
            {
            }
        }
    }
}

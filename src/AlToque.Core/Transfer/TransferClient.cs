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

    public TransferClient(DeviceIdentity identity, TrustStore trust, Func<string> alias)
    {
        _identity = identity;
        _trust = trust;
        _alias = alias;
    }

    /// <summary>La clave del receptor quedó verificada (deviceId, punto de escucha, alias, SO).</summary>
    public event Action<string, IPEndPoint, string?, string?>? PeerVerified;

    public TransferInfo CreateInfo(PeerTarget target, OutgoingManifest manifest) => new(TransferDirection.Send, target.DisplayName)
    {
        PeerId = target.ExpectedDeviceId,
        PeerTrusted = target.PinnedKey is not null,
        FileCount = manifest.FileCount,
        TotalBytes = manifest.TotalSize,
        FirstName = manifest.FirstName,
        Message = manifest.Offer.Message,
    };

    /// <summary>Ejecuta el envío completo. Nunca lanza: el resultado queda en <paramref name="info"/>.</summary>
    public async Task SendAsync(PeerTarget target, OutgoingManifest manifest, TransferInfo info)
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

            await control.Frames.SendAsync(FrameType.Hello, new HelloMessage
            {
                V = AppInfo.ProtocolVersion,
                Purpose = Purposes.Transfer,
                Alias = _alias(),
                Os = AppInfo.OsName,
                AppVersion = AppInfo.Version,
                Caps = [.. Capabilities.Local],
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

            if (!info.PeerTrusted && TextLimits.CleanAlias(hello.Alias) is { } alias)
                info.PeerName = alias;
            PeerVerified?.Invoke(control.RemoteDeviceId, target.EndPoint, TextLimits.CleanAlias(hello.Alias), hello.Os);

            await control.Frames.SendAsync(FrameType.Offer, manifest.Offer, ProtocolJsonContext.Default.OfferMessage, ct).ConfigureAwait(false);
            info.AcceptDeadline = DateTimeOffset.Now + TransferServer.OfferTimeout;
            info.SetState(TransferState.WaitingForAccept);

            DecisionMessage decision;
            using (var decisionFrame = await control.Frames.ReadExpectAsync(DecisionTimeout, ct, FrameType.Decision, FrameType.Error).ConfigureAwait(false))
            {
                if (decisionFrame.Type == FrameType.Error)
                    throw RemoteError(decisionFrame);
                decision = decisionFrame.Parse(ProtocolJsonContext.Default.DecisionMessage);
            }
            if (decision.SessionId != manifest.Offer.SessionId)
                throw new ProtocolException("Decisión de otra sesión.");
            if (decision.Result == DecisionResults.Declined)
            {
                var reason = decision.Reason is { } r && ErrorCodes.IsKnown(r) ? r : ErrorCodes.Declined;
                var detail = decision.MissingBytes is { } missing ? missing.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                info.SetState(reason is ErrorCodes.Declined or ErrorCodes.AcceptTimeout ? TransferState.Declined : TransferState.Failed, reason, detail);
                return;
            }

            var jobs = new List<(OutgoingFile File, string Token)>();
            foreach (var a in decision.Files ?? [])
            {
                if (!manifest.Files.TryGetValue(a.FileId, out var f) || !Base32.IsValid(a.Token, 26) || a.Offset != 0)
                    throw new ProtocolException("Decisión con archivos desconocidos.");
                if (jobs.Any(j => j.File.FileId == a.FileId))
                    throw new ProtocolException("Archivo aceptado dos veces.");
                jobs.Add((f, a.Token));
            }
            info.TotalBytes = jobs.Sum(j => j.File.Size);
            info.FileCount = jobs.Count;
            info.SetState(TransferState.Transferring);

            if (jobs.Count == 0)
            {
                await TrySendAsync(control, FrameType.Bye, new ByeMessage(), ProtocolJsonContext.Default.ByeMessage).ConfigureAwait(false);
                info.SetState(TransferState.Completed);
                return;
            }

            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var controlWatch = WatchControlAsync(control, watchCts);

            var queue = new ConcurrentQueue<(OutgoingFile File, string Token)>(jobs);
            var connections = Math.Min(MaxDataConnections, jobs.Count);
            var remoteKey = control.RemotePublicKey;
            for (int i = 0; i < connections; i++)
                workers.Add(new DataWorker(this, target, remoteKey, manifest.Offer.SessionId, queue, info));

            // El primer fallo de cualquier conexión corta las demás; se conserva la causa original.
            Exception? firstFailure = null;
            var workTasks = workers.Select(async w =>
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
                    foreach (var other in workers)
                        other.Abort();
                }
            }).ToList();
            var all = Task.WhenAll(workTasks);
            if (await Task.WhenAny(all, controlWatch).ConfigureAwait(false) == controlWatch && await controlWatch.ConfigureAwait(false) is { } remote)
            {
                Interlocked.CompareExchange(ref firstFailure, remote, null);
                sessionCts.Cancel();
                foreach (var w in workers)
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
                throw firstFailure;
            }
            watchCts.Cancel();

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
        catch (Exception ex) when (ex is IOException or ProtocolException or System.Security.Authentication.AuthenticationException or ObjectDisposedException)
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
            Caps = [.. Capabilities.Local],
        }, ProtocolJsonContext.Default.HelloMessage, ct).ConfigureAwait(false);
        using var frame = await conn.Frames.ReadExpectAsync(TimeSpan.FromSeconds(5), ct, FrameType.Hello).ConfigureAwait(false);
        var hello = frame.Parse(ProtocolJsonContext.Default.HelloMessage);
        PeerVerified?.Invoke(conn.RemoteDeviceId, target.EndPoint, TextLimits.CleanAlias(hello.Alias), hello.Os);
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
        ConcurrentQueue<(OutgoingFile File, string Token)> queue, TransferInfo info)
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
            var results = ReadResultsAsync(_conn, readerCts.Token, ct);

            var buffer = ArrayPool<byte>.Shared.Rent(FrameChannel.HeaderSize + ChunkSize);
            try
            {
                while (!results.IsCompleted && queue.TryDequeue(out var job))
                {
                    ct.ThrowIfCancellationRequested();
                    _awaiting[job.File.FileId] = true;
                    Interlocked.Increment(ref _sent);
                    await SendFileAsync(_conn, job.File, job.Token, buffer, ct).ConfigureAwait(false);
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
                await results.WaitAsync(FinalResultsTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _conn.Abort();
                throw new TransferException(ErrorCodes.Timeout, "El receptor no confirmó los últimos archivos.");
            }
        }

        private bool AllAnswered => Volatile.Read(ref _doneSending) && Volatile.Read(ref _received) == Volatile.Read(ref _sent);

        private async Task SendFileAsync(PeerConnection conn, OutgoingFile file, string token, byte[] buffer, CancellationToken ct)
        {
            info.CurrentFile = Path.GetFileName(file.RelativePath);
            await conn.Frames.SendAsync(FrameType.StreamOpen, new StreamOpenMessage
            {
                SessionId = sessionId,
                FileId = file.FileId,
                Token = token,
                Offset = 0,
            }, ProtocolJsonContext.Default.StreamOpenMessage, ct).ConfigureAwait(false);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long sent = 0;
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
                    while (sent < file.Size)
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
                    if (result.Ok)
                        info.FileSucceeded(null);
                    else
                    {
                        LastError = result.Error is { } e && ErrorCodes.IsKnown(e) ? e : ErrorCodes.Io;
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

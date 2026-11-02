using System.Security.Cryptography;
using System.Text;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;

namespace AlToque.Core.Transfer;

/// <summary>Estados de la sesión en el receptor (sección 8.7).</summary>
public enum ReceiveState
{
    Connected,
    Offered,
    Accepted,
    Transferring,

    /// <summary>Se cortó la conexión a mitad: se espera a que el emisor la retome (los temporales siguen abiertos).</summary>
    Parked,

    Completed,
    Failed,
    Declined,
    Cancelled,
}

/// <summary>
/// Sesión de recepción. Solo admite datos en el estado correcto, con un token válido de un solo uso atado a la
/// sesión y a la clave del emisor. Cualquier anomalía la cierra. Con un compañero de confianza que sabe reanudar, un
/// corte de red no la cierra: queda en pausa con lo recibido y el emisor la retoma desde ahí (sección 8.5).
/// </summary>
internal sealed class ReceiveSession
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly List<PeerConnection> _connections = [];
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource _attempt = new();
    private TaskCompletionSource _superseded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _lastActivity = Environment.TickCount64;
    private Slot? _currentFile;

    public ReceiveSession(string sessionId, byte[] senderKey, TransferInfo info, ReceivePlan plan, ConflictPolicy policy)
    {
        SessionId = sessionId;
        SenderKey = senderKey;
        Info = info;
        Plan = plan;
        Policy = policy;
    }

    /// <summary>Avisa al emisor por el canal de control antes de cortar los datos.</summary>
    public Func<Task>? NotifyCancel { get; set; }

    public string SessionId { get; }
    public byte[] SenderKey { get; }
    public TransferInfo Info { get; }
    public ReceivePlan Plan { get; private set; }
    public ConflictPolicy Policy { get; }
    public ReceiveState State { get; private set; } = ReceiveState.Offered;

    /// <summary>Se cancela al fallar o al quedar en pausa; al reanudar hay uno nuevo.</summary>
    public CancellationToken Token
    {
        get
        {
            lock (_lock)
                return _attempt.Token;
        }
    }

    public Task Finished => _finished.Task;
    public long IdleMilliseconds => Environment.TickCount64 - Interlocked.Read(ref _lastActivity);

    /// <summary>Un emisor de confianza que sabe reanudar: un corte deja la sesión en pausa en vez de cerrarla.</summary>
    public bool CanPark { get; init; }

    /// <summary>Cuántas veces se retomó; cada canal de control atiende solo su turno.</summary>
    public int Generation { get; private set; }

    /// <summary>Se completa cuando otra conexión retoma la sesión (la anterior ya no la atiende).</summary>
    public Task Superseded
    {
        get
        {
            lock (_lock)
                return _superseded.Task;
        }
    }

    public DateTimeOffset? ParkedSince { get; private set; }

    /// <summary>El canal de control que la atiende ahora.</summary>
    public PeerConnection? Control { get; set; }

    /// <summary>Solo para pruebas: corta sus conexiones sin cambiar el estado (lo que se ve en un cambio de red).</summary>
    public void AbortConnectionsForTesting()
    {
        List<PeerConnection> all;
        lock (_lock)
            all = [.. _connections];
        Control?.Abort();
        foreach (var c in all)
            c.Abort();
    }

    private int _reported;

    /// <summary>Que el final se informe una sola vez (puede cerrarla su conexión o el vencimiento de la pausa).</summary>
    public bool TryMarkReported() => Interlocked.Exchange(ref _reported, 1) == 0;

    private sealed class Slot
    {
        public required PlannedFile File;
        public required byte[] Token;
        public bool TokenUsed;
        public bool Done;
        public bool Ok;

        /// <summary>Lo recibido de este archivo antes de un corte, con su hash en curso (se sigue escribiendo ahí).</summary>
        public IncomingFile? Partial;

        /// <summary>Un flujo de datos lo está recibiendo ahora.</summary>
        public bool Receiving;
    }

    public bool IsActive
    {
        get
        {
            lock (_lock)
                return State is ReceiveState.Offered or ReceiveState.Accepted or ReceiveState.Transferring or ReceiveState.Parked;
        }
    }

    public bool IsSender(ReadOnlySpan<byte> spki) => CryptographicOperations.FixedTimeEquals(SenderKey, spki);

    public void Touch() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);

    /// <summary>Acepta: crea los tokens y las carpetas vacías. Devuelve lo que viaja en DECISION.</summary>
    public List<AcceptedFile> Accept(ReceivePlan finalPlan)
    {
        lock (_lock)
        {
            if (State != ReceiveState.Offered)
                throw new InvalidOperationException("La sesión ya no espera respuesta.");
            Plan = finalPlan;
            var accepted = new List<AcceptedFile>();
            foreach (var f in finalPlan.Files.Where(f => !f.Skip))
            {
                var token = NewToken();
                _slots[f.Offer.FileId] = new Slot { File = f, Token = Encoding.ASCII.GetBytes(token) };
                accepted.Add(new AcceptedFile { FileId = f.Offer.FileId, Token = token, Offset = 0 });
            }
            State = ReceiveState.Accepted;
            Touch();
            return accepted;
        }
    }

    private static string NewToken() => Base32.Encode(RandomNumberGenerator.GetBytes(16));

    /// <summary>¿La oferta de reanudación describe exactamente estos archivos (mismos ids, rutas y tamaños)?</summary>
    public bool Matches(OfferMessage offer)
    {
        lock (_lock)
        {
            var mine = Plan.Files.ToDictionary(f => f.Offer.FileId, StringComparer.Ordinal);
            return offer.Files.Count == mine.Count && offer.Files.All(f =>
                mine.TryGetValue(f.FileId, out var p) && p.Offer.Path == f.Path && p.Offer.Size == f.Size);
        }
    }

    /// <summary>
    /// Valida un STREAM_OPEN: sesión en estado correcto, emisor correcto, archivo aceptado, token sin usar y offset igual
    /// a lo que ya se tiene de ese archivo. El token queda consumido aunque la transferencia falle después.
    /// </summary>
    public (PlannedFile File, IncomingFile? Partial)? TryOpenStream(StreamOpenMessage open, ReadOnlySpan<byte> connectionKey)
    {
        if (!IsSender(connectionKey))
            return null;
        lock (_lock)
        {
            if (State is not (ReceiveState.Accepted or ReceiveState.Transferring))
                return null;
            if (!_slots.TryGetValue(open.FileId, out var slot) || slot.TokenUsed || slot.Done || slot.Receiving)
                return null;
            if (open.Offset != (slot.Partial?.Written ?? 0))
                return null;
            var presented = Encoding.ASCII.GetBytes(open.Token);
            if (!CryptographicOperations.FixedTimeEquals(presented, slot.Token))
                return null;
            slot.TokenUsed = true;
            slot.Receiving = true;
            var partial = slot.Partial;
            slot.Partial = null;
            State = ReceiveState.Transferring;
            Touch();
            // Con varios flujos en paralelo se muestra el archivo más grande en curso.
            if (_currentFile is null || _currentFile.Done || slot.File.Offer.Size >= _currentFile.File.Offer.Size)
            {
                _currentFile = slot;
                Info.CurrentFile = slot.File.Name;
            }
            return (slot.File, partial);
        }
    }

    /// <summary>
    /// Se cortó el flujo a mitad de un archivo: lo recibido se guarda para retomarlo. Devuelve false si la sesión ya
    /// no lo admite (entonces hay que descartarlo).
    /// </summary>
    public bool StashPartial(string fileId, IncomingFile file)
    {
        lock (_lock)
        {
            if (!_slots.TryGetValue(fileId, out var slot))
                return false;
            slot.Receiving = false;
            if (!CanPark || slot.Done || State is ReceiveState.Completed or ReceiveState.Failed or ReceiveState.Cancelled or ReceiveState.Declined)
                return false;
            slot.Partial = file;
            return true;
        }
    }

    /// <summary>El flujo de ese archivo terminó sin guardar nada (por ejemplo, no se pudo abrir).</summary>
    public void StopReceiving(string fileId)
    {
        lock (_lock)
        {
            if (_slots.TryGetValue(fileId, out var slot))
                slot.Receiving = false;
        }
    }

    public void MarkFileDone(string fileId, bool ok, string? finalPath)
    {
        bool allDone;
        lock (_lock)
        {
            if (!_slots.TryGetValue(fileId, out var slot) || slot.Done)
                return;
            slot.Done = true;
            slot.Ok = ok;
            slot.Receiving = false;
            if (ok)
                Info.FileSucceeded(finalPath);
            else
                Info.FileFailed();
            allDone = _slots.Values.All(s => s.Done);
        }
        Touch();
        if (allDone)
            Complete();
    }

    public void AddConnection(PeerConnection connection)
    {
        lock (_lock)
        {
            if (State is ReceiveState.Completed or ReceiveState.Failed or ReceiveState.Cancelled or ReceiveState.Declined or ReceiveState.Parked)
            {
                connection.Abort();
                return;
            }
            _connections.Add(connection);
        }
    }

    public void RemoveConnection(PeerConnection connection)
    {
        lock (_lock)
            _connections.Remove(connection);
    }

    public bool SetDeclined(string code)
    {
        lock (_lock)
        {
            if (State != ReceiveState.Offered)
                return false;
            State = ReceiveState.Declined;
        }
        Info.SetState(TransferState.Declined, code);
        _finished.TrySetResult();
        return true;
    }

    /// <summary>Si no hubo archivos que transferir (todo omitido o solo carpetas), termina al aceptar.</summary>
    public void CompleteIfNothingPending()
    {
        bool none;
        lock (_lock)
            none = _slots.Values.All(s => s.Done);
        if (none)
            Complete();
    }

    private void Complete()
    {
        lock (_lock)
        {
            if (State is not (ReceiveState.Accepted or ReceiveState.Transferring))
                return;
            State = ReceiveState.Completed;
        }
        Info.CurrentFile = null;
        Info.SetState(Info.FilesFailed > 0 ? TransferState.PartiallyCompleted : TransferState.Completed,
            Info.FilesFailed > 0 ? ErrorCodes.Io : null);
        _finished.TrySetResult();
    }

    // ── Pausa y reanudación ─────────────────────────────────────────────────────

    /// <summary>
    /// Se cortó la conexión con el emisor: si sabe reanudar, la sesión queda en pausa (sus flujos se cierran y lo
    /// recibido se guarda). Devuelve false si no se puede pausar (entonces hay que cerrarla).
    /// </summary>
    /// <param name="generation">Solo si la sesión sigue en ese turno (no la retomó otra conexión mientras tanto).</param>
    public bool TryPark(int? generation = null)
    {
        List<PeerConnection> toAbort;
        CancellationTokenSource attempt;
        lock (_lock)
        {
            if (generation is { } g && g != Generation)
                return true; // Ya la atiende otra conexión.
            if (!CanPark || State is not (ReceiveState.Accepted or ReceiveState.Transferring))
                return State == ReceiveState.Parked;
            State = ReceiveState.Parked;
            ParkedSince = DateTimeOffset.UtcNow;
            toAbort = [.. _connections];
            _connections.Clear();
            attempt = _attempt;
        }
        Cancel(attempt);
        foreach (var c in toAbort)
            c.Abort();
        Info.SetState(TransferState.Reconnecting);
        return true;
    }

    /// <summary>Espera a que los flujos que estaban recibiendo guarden lo suyo (tras pausar).</summary>
    public async Task WaitForReceiversAsync(TimeSpan timeout)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < until)
        {
            lock (_lock)
            {
                if (!_slots.Values.Any(s => s.Receiving))
                    return;
            }
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// El emisor volvió (con la misma sesión): tokens nuevos para lo que falta y, de cada archivo, desde dónde seguir.
    /// Lo ya terminado va marcado para no reenviarlo.
    /// </summary>
    public List<AcceptedFile>? Resume()
    {
        TaskCompletionSource superseded;
        List<AcceptedFile> files;
        lock (_lock)
        {
            if (State != ReceiveState.Parked)
                return null;
            // El anterior no se libera: algún flujo viejo puede seguir mirando su token.
            _attempt = new CancellationTokenSource();
            superseded = _superseded;
            _superseded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Generation++;
            files = [];
            foreach (var (id, slot) in _slots)
            {
                var token = NewToken();
                if (slot.Done)
                {
                    files.Add(new AcceptedFile { FileId = id, Token = token, Done = true, Failed = slot.Ok ? null : true });
                    continue;
                }
                slot.Token = Encoding.ASCII.GetBytes(token);
                slot.TokenUsed = false;
                files.Add(new AcceptedFile { FileId = id, Token = token, Offset = slot.Partial?.Written ?? 0 });
            }
            State = ReceiveState.Accepted;
            ParkedSince = null;
        }
        superseded.TrySetResult();
        Touch();
        Info.SetState(TransferState.Transferring);
        return files;
    }

    /// <summary>
    /// Se cortó una conexión de datos. Antes de declarar el fallo se espera un momento por si el emisor avisó
    /// CANCEL por el canal de control (llega casi a la vez que el corte).
    /// </summary>
    public async Task FailAfterGraceAsync(string code, string? detail)
    {
        await Task.WhenAny(Finished, Task.Delay(1500)).ConfigureAwait(false);
        Fail(code, detail);
    }

    /// <summary>Corta la sesión: cierra todas sus conexiones y descarta los temporales.</summary>
    public void Fail(string code, string? detail, bool cancelled = false)
    {
        List<PeerConnection> toAbort;
        List<IncomingFile> partials;
        CancellationTokenSource attempt;
        lock (_lock)
        {
            if (State is ReceiveState.Completed or ReceiveState.Failed or ReceiveState.Cancelled or ReceiveState.Declined)
                return;
            State = cancelled ? ReceiveState.Cancelled : ReceiveState.Failed;
            toAbort = [.. _connections];
            _connections.Clear();
            partials = [.. _slots.Values.Select(s => s.Partial).OfType<IncomingFile>()];
            foreach (var s in _slots.Values)
                s.Partial = null;
            attempt = _attempt;
        }
        Cancel(attempt);
        foreach (var c in toAbort)
            c.Abort();
        foreach (var p in partials)
            _ = p.DisposeAsync().AsTask();
        Info.SetState(cancelled ? TransferState.Cancelled : TransferState.Failed, code, detail);
        _finished.TrySetResult();
    }

    private static void Cancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

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
    Completed,
    Failed,
    Declined,
    Cancelled,
}

/// <summary>
/// Sesión de recepción. Solo admite datos en el estado correcto, con un token válido de un solo uso atado a la
/// sesión y a la clave del emisor. Cualquier anomalía la cierra.
/// </summary>
internal sealed class ReceiveSession
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly List<PeerConnection> _connections = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    public CancellationToken Token => _cts.Token;
    public Task Finished => _finished.Task;
    public long IdleMilliseconds => Environment.TickCount64 - Interlocked.Read(ref _lastActivity);

    private sealed class Slot
    {
        public required PlannedFile File;
        public required byte[] Token;
        public bool TokenUsed;
        public bool Done;
    }

    public bool IsActive
    {
        get
        {
            lock (_lock)
                return State is ReceiveState.Offered or ReceiveState.Accepted or ReceiveState.Transferring;
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
                var tokenBytes = RandomNumberGenerator.GetBytes(16);
                var token = Base32.Encode(tokenBytes);
                _slots[f.Offer.FileId] = new Slot { File = f, Token = Encoding.ASCII.GetBytes(token) };
                accepted.Add(new AcceptedFile { FileId = f.Offer.FileId, Token = token, Offset = 0 });
            }
            State = ReceiveState.Accepted;
            Touch();
            return accepted;
        }
    }

    /// <summary>
    /// Valida un STREAM_OPEN: sesión en estado correcto, emisor correcto, archivo aceptado y token sin usar.
    /// El token queda consumido aunque la transferencia falle después.
    /// </summary>
    public PlannedFile? TryOpenStream(StreamOpenMessage open, ReadOnlySpan<byte> connectionKey)
    {
        if (!IsSender(connectionKey) || open.Offset != 0)
            return null;
        lock (_lock)
        {
            if (State is not (ReceiveState.Accepted or ReceiveState.Transferring))
                return null;
            if (!_slots.TryGetValue(open.FileId, out var slot) || slot.TokenUsed)
                return null;
            var presented = Encoding.ASCII.GetBytes(open.Token);
            if (!CryptographicOperations.FixedTimeEquals(presented, slot.Token))
                return null;
            slot.TokenUsed = true;
            State = ReceiveState.Transferring;
            Touch();
            // Con varios flujos en paralelo se muestra el archivo más grande en curso.
            if (_currentFile is null || _currentFile.Done || slot.File.Offer.Size >= _currentFile.File.Offer.Size)
            {
                _currentFile = slot;
                Info.CurrentFile = slot.File.Name;
            }
            return slot.File;
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
            if (State is ReceiveState.Completed or ReceiveState.Failed or ReceiveState.Cancelled or ReceiveState.Declined)
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
            none = _slots.Count == 0;
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
        lock (_lock)
        {
            if (State is ReceiveState.Completed or ReceiveState.Failed or ReceiveState.Cancelled or ReceiveState.Declined)
                return;
            State = cancelled ? ReceiveState.Cancelled : ReceiveState.Failed;
            toAbort = [.. _connections];
            _connections.Clear();
        }
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        foreach (var c in toAbort)
            c.Abort();
        Info.SetState(cancelled ? TransferState.Cancelled : TransferState.Failed, code, detail);
        _finished.TrySetResult();
    }
}

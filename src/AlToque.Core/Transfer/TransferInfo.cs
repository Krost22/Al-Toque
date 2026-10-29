namespace AlToque.Core.Transfer;

public enum TransferDirection
{
    Send,
    Receive,
}

public enum TransferState
{
    Preparing,
    Connecting,
    WaitingForAccept,
    Transferring,
    Completed,
    PartiallyCompleted,
    Declined,
    Failed,
    Cancelled,
}

/// <summary>
/// Estado observable de un envío o una recepción. El motor lo actualiza desde varios hilos;
/// la interfaz lo muestrea (10 a 15 veces por segundo como máximo) y recibe eventos en cada cambio de estado.
/// </summary>
public sealed class TransferInfo
{
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _completedPaths = [];
    private long _transferred;
    private int _filesDone;
    private int _filesFailed;
    private double _speed;
    private long _lastSampleBytes;
    private long _lastSampleTicks;

    public TransferInfo(TransferDirection direction, string peerName)
    {
        Direction = direction;
        PeerName = peerName;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public TransferDirection Direction { get; }
    public string PeerName { get; internal set; }
    public string? PeerId { get; internal set; }

    /// <summary>La clave del compañero estaba fijada (emparejado). Si no, se muestra "No verificado".</summary>
    public bool PeerTrusted { get; internal set; }

    public int FileCount { get; internal set; }
    public long TotalBytes { get; internal set; }
    public string FirstName { get; internal set; } = "";
    public string? Message { get; internal set; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public DateTimeOffset? AcceptDeadline { get; internal set; }

    /// <summary>Carpeta donde quedó lo recibido.</summary>
    public string? DestinationFolder { get; internal set; }

    public TransferState State { get; private set; } = TransferState.Preparing;
    public string? ErrorCode { get; private set; }
    public string? ErrorDetail { get; private set; }
    public string? CurrentFile { get; internal set; }

    public long TransferredBytes => Interlocked.Read(ref _transferred);
    public int FilesDone => Volatile.Read(ref _filesDone);
    public int FilesFailed => Volatile.Read(ref _filesFailed);
    public bool IsFinished => State is TransferState.Completed or TransferState.PartiallyCompleted or TransferState.Declined or TransferState.Failed or TransferState.Cancelled;
    public bool IsSuccess => State is TransferState.Completed;
    public double Fraction => TotalBytes <= 0 ? (IsFinished ? 1 : 0) : Math.Clamp((double)TransferredBytes / TotalBytes, 0, 1);

    public IReadOnlyList<string> CompletedPaths
    {
        get
        {
            lock (_lock)
                return _completedPaths.ToList();
        }
    }

    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>Cambió el estado (no el progreso).</summary>
    public event Action<TransferInfo>? StateChanged;

    public void Cancel()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal void AddBytes(long n) => Interlocked.Add(ref _transferred, n);

    internal void FileSucceeded(string? finalPath)
    {
        Interlocked.Increment(ref _filesDone);
        if (finalPath is not null)
        {
            lock (_lock)
                _completedPaths.Add(finalPath);
        }
    }

    internal void FileFailed() => Interlocked.Increment(ref _filesFailed);

    internal void SetState(TransferState state, string? errorCode = null, string? detail = null)
    {
        lock (_lock)
        {
            if (IsFinished)
                return;
            State = state;
            if (errorCode is not null)
            {
                ErrorCode = errorCode;
                ErrorDetail = detail;
            }
        }
        StateChanged?.Invoke(this);
    }

    /// <summary>Velocidad suavizada con media móvil exponencial, en bytes por segundo.</summary>
    public double SampleSpeed()
    {
        var now = Environment.TickCount64;
        var bytes = TransferredBytes;
        lock (_lock)
        {
            if (_lastSampleTicks == 0)
            {
                _lastSampleTicks = now;
                _lastSampleBytes = bytes;
                return 0;
            }
            var dt = (now - _lastSampleTicks) / 1000.0;
            if (dt < 0.2)
                return _speed;
            var instant = (bytes - _lastSampleBytes) / dt;
            _speed = _speed <= 0 ? instant : _speed * 0.75 + instant * 0.25;
            _lastSampleTicks = now;
            _lastSampleBytes = bytes;
            return _speed;
        }
    }

    public double Speed
    {
        get
        {
            lock (_lock)
                return _speed;
        }
    }

    public TimeSpan? EstimatedRemaining
    {
        get
        {
            var speed = Speed;
            if (speed < 1 || State != TransferState.Transferring)
                return null;
            return TimeSpan.FromSeconds(Math.Max(0, (TotalBytes - TransferredBytes) / speed));
        }
    }
}

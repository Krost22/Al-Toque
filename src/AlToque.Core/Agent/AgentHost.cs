using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AlToque.Core.Chat;
using AlToque.Core.Discovery;
using AlToque.Core.Ipc;
using AlToque.Core.Pairing;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;

namespace AlToque.Core.Agent;

public sealed class AgentOptions
{
    public AppPaths? Paths { get; init; }
    public ISecretProtector? Protector { get; init; }

    /// <summary>Detección de red pública del sistema (en Windows, la categoría de la red).</summary>
    public Func<bool>? IsPublicNetwork { get; init; }

    public bool EnableIpc { get; init; } = true;
    public bool EnableDiscovery { get; init; } = true;
    public int DiscoveryPort { get; init; } = AppInfo.DiscoveryPort;
}

/// <summary>
/// El agente residente: descubrimiento, servidor y cliente de transferencias, identidad, confianza, políticas de
/// recepción e IPC. La interfaz se suscribe a sus eventos; todos llegan desde hilos del pool.
/// </summary>
public sealed class AgentHost : IAsyncDisposable
{
    public static readonly TimeSpan EveryoneDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(300);

    private readonly AgentOptions _options;
    private readonly Lock _lock = new();
    private readonly List<TransferInfo> _active = [];
    private readonly HashSet<string> _blocked = [];
    private readonly Dictionary<string, PendingBatch> _batches = [];
    private readonly ISecretProtector _protector;
    private IpcServer? _ipc;
    private Timer? _everyoneTimer;
    private Timer? _housekeeping;
    private DateTimeOffset? _everyoneUntil;
    private bool _isPublicNetwork;
    private bool _started;

    private sealed class PendingBatch
    {
        public required List<string> Paths;
        public required Timer Timer;
        public required Action<List<string>> Flush;
        public required long StartedAt;
    }

    private AgentHost(AgentOptions options, AppPaths paths, ISecretProtector protector, DeviceIdentity identity, bool identityCreated)
    {
        _options = options;
        _protector = protector;
        Paths = paths;
        Identity = identity;
        IdentityWasCreated = identityCreated;
        var sealer = Sealer.LoadOrCreate(paths, protector);
        Settings = SettingsStore.Load(paths, sealer);
        Trust = TrustStore.Load(paths, sealer);
        History = HistoryStore.Load(paths);
        PeerCache = PeerCacheStore.Load(paths);
        Peers = new PeerDirectory(Trust, PeerCache);
        Pairing = new PairingService(identity, Trust, () => Settings.Current.Alias, () => Settings.Current.Avatar);
        Chats = ChatStore.Load(paths, protector, identity.DeviceId);
        Links = new LinkService(identity, Trust, Peers, Chats, () => LocalHello(Purposes.Link), AgentLog.Write);
        Client = new TransferClient(identity, Trust, () => Settings.Current.Alias, () => Settings.Current.Avatar);
        Server = new TransferServer(new ServerContext
        {
            Identity = identity,
            Trust = Trust,
            Pairing = Pairing,
            Settings = () => Settings.Current,
            EffectiveMode = () => EffectiveMode,
            IsBlocked = IsBlocked,
            Block = Block,
            IsPublicNetwork = () => IsReceivingPausedByNetwork,
            Log = AgentLog.Write,
            AcceptLink = (conn, ct) => Links.AcceptAsync(conn, ct),
        });
        if (options.EnableDiscovery)
        {
            Discovery = new DiscoveryService(identity.DeviceId, CurrentAdvert, () => Settings.Current.ExcludeVirtualAdapters,
                options.DiscoveryPort, AgentLog.Write);
        }
        WireEvents();
        EnableAutoAcceptForExistingPeers();
    }

    /// <summary>
    /// "Recibir sin preguntar" pasó a estar activado por defecto: los compañeros emparejados con versiones anteriores
    /// lo reciben una sola vez (quien lo apague después lo deja apagado).
    /// </summary>
    private void EnableAutoAcceptForExistingPeers()
    {
        if (Settings.Current.AutoAcceptMigrated)
            return;
        foreach (var peer in Trust.All)
        {
            if (!peer.AutoAccept)
                Trust.Update(peer.DeviceId, p => p.AutoAccept = true);
        }
        Settings.Update(s => s.AutoAcceptMigrated = true);
    }

    public AppPaths Paths { get; }
    public DeviceIdentity Identity { get; }
    public bool IdentityWasCreated { get; }
    public SettingsStore Settings { get; }
    public TrustStore Trust { get; }
    public HistoryStore History { get; }
    public PeerCacheStore PeerCache { get; }
    public PeerDirectory Peers { get; }
    public PairingService Pairing { get; }
    public TransferServer Server { get; }
    public TransferClient Client { get; }
    public DiscoveryService? Discovery { get; }

    /// <summary>Conversaciones con cada compañero de confianza, guardadas cifradas.</summary>
    public ChatStore Chats { get; }

    /// <summary>Enlaces permanentes con los compañeros en línea: entregan y sincronizan los chats.</summary>
    public LinkService Links { get; }

    public string DeviceId => Identity.DeviceId;

    // ── Eventos para la interfaz ────────────────────────────────────────────────

    public event Action<IncomingOffer>? OfferReceived;
    public event Action<TransferInfo>? TransferStarted;
    public event Action<TransferInfo>? TransferFinished;
    public event Action? ReceiveModeChanged;
    public event Action? NetworkStateChanged;

    /// <summary>El menú contextual pidió elegir destino para estas rutas.</summary>
    public event Action<IReadOnlyList<string>>? PickRequested;

    /// <summary>El menú contextual pidió conectar por código para estas rutas.</summary>
    public event Action<IReadOnlyList<string>>? CodeRequested;

    /// <summary>Otra instancia pidió mostrar el panel.</summary>
    public event Action? ShowRequested;

    public event Action? QuitRequested;

    public static AgentHost Create(AgentOptions? options = null)
    {
        options ??= new AgentOptions();
        var paths = options.Paths ?? AppPaths.Resolve();
        AgentLog.Initialize(paths);
        var protector = options.Protector ?? SecretProtector.CreateDefault();
        var identity = DeviceIdentity.LoadOrCreate(paths, protector, out var created);
        AgentLog.Write($"{AppInfo.Name} {AppInfo.Version} · equipo {IdentityVisuals.ShortCode(identity.DeviceId)}{(created ? " (identidad nueva)" : "")}");
        return new AgentHost(options, paths, protector, identity, created);
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        UpdateListening();
        // La categoría de red se consulta en segundo plano: no retrasa el arranque.
        _ = Task.Run(CheckPublicNetwork);
        Discovery?.Start();
        if (_options.EnableIpc)
        {
            _ipc = new IpcServer(HandleIpcAsync);
            _ipc.Start();
        }
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        ImportHistoryIntoChats();
        Links.Start();
        _housekeeping = new Timer(_ => Housekeeping(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    private void WireEvents()
    {
        Server.OfferReceived += offer => OfferReceived?.Invoke(offer);
        Server.TransferStarted += info =>
        {
            lock (_lock)
                _active.Add(info);
            RecordInChat(info);
            TransferStarted?.Invoke(info);
            Discovery?.AnnounceSoon();
        };
        Server.TransferFinished += info =>
        {
            lock (_lock)
                _active.Remove(info);
            RecordHistory(info);
            RecordInChat(info);
            TransferFinished?.Invoke(info);
        };
        Server.PeerVerified += (id, alias, os, avatar) => OnPeerVerified(id, null, alias, os, avatar);
        Client.PeerVerified += (id, ep, alias, os, avatar) => OnPeerVerified(id, ep, alias, os, avatar);
        Discovery?.HintReceived += hint =>
        {
            Peers.OnHint(hint);
            // Un compañero que aparece (o cambió de dirección) se enlaza enseguida.
            if (Trust.Find(hint.DeviceId) is not null && !Links.IsLinked(hint.DeviceId))
                Links.Kick(hint.DeviceId);
        };
        Discovery?.ByeReceived += id => Peers.OnBye(id);
        Pairing.WindowChanged += () =>
        {
            UpdateListening();
            Discovery?.AnnounceSoon();
            ReceiveModeChanged?.Invoke();
        };
        Settings.Changed += _ =>
        {
            UpdateListening();
            Discovery?.AnnounceSoon();
            NetworkStateChanged?.Invoke();
        };
    }

    // ── Modo de recepción ────────────────────────────────────────────────────────

    /// <summary>Modo vigente, incluido "Todos" temporal.</summary>
    public ReceiveMode EffectiveMode
    {
        get
        {
            lock (_lock)
            {
                if (_everyoneUntil is { } until && until > DateTimeOffset.UtcNow)
                    return ReceiveMode.Everyone;
            }
            return Settings.Current.ReceiveMode;
        }
    }

    public DateTimeOffset? EveryoneUntil
    {
        get
        {
            lock (_lock)
                return _everyoneUntil is { } u && u > DateTimeOffset.UtcNow ? u : null;
        }
    }

    public bool IsPublicNetwork => _isPublicNetwork;

    /// <summary>En red pública solo se recibe si el usuario lo permitió (en ajustes o para esta sesión).</summary>
    public bool PublicNetworkAllowed => _publicOverride || Settings.Current.AllowPublicNetworks;

    /// <summary>La recepción está en pausa porque la red es pública y no se permitió.</summary>
    public bool IsReceivingPausedByNetwork => _isPublicNetwork && !PublicNetworkAllowed;

    private bool _publicOverride;

    /// <summary>Permite recibir en la red pública actual hasta cerrar la app.</summary>
    public void AllowPublicNetworkForSession()
    {
        _publicOverride = true;
        AgentLog.Write("Recepción permitida en red pública durante esta sesión.");
        UpdateListening();
        Discovery?.AnnounceSoon();
        NetworkStateChanged?.Invoke();
    }
    public bool IsListening => Server.IsListening;

    /// <summary>"Todos" dura 10 minutos y vuelve solo al modo anterior.</summary>
    public void SetReceiveMode(ReceiveMode mode)
    {
        if (mode == ReceiveMode.Everyone)
        {
            lock (_lock)
            {
                _everyoneUntil = DateTimeOffset.UtcNow + EveryoneDuration;
                _everyoneTimer?.Dispose();
                _everyoneTimer = new Timer(_ => EndEveryone(), null, EveryoneDuration, Timeout.InfiniteTimeSpan);
            }
            UpdateListening();
            Discovery?.AnnounceSoon();
        }
        else
        {
            lock (_lock)
            {
                _everyoneUntil = null;
                _everyoneTimer?.Dispose();
                _everyoneTimer = null;
            }
            if (Settings.Current.ReceiveMode != mode)
                Settings.Update(s => s.ReceiveMode = mode);
            else
                UpdateListening();
            if (mode == ReceiveMode.Nobody)
                Discovery?.SendBye();
            Discovery?.AnnounceSoon();
        }
        ReceiveModeChanged?.Invoke();
    }

    private void EndEveryone()
    {
        lock (_lock)
        {
            _everyoneUntil = null;
            _everyoneTimer?.Dispose();
            _everyoneTimer = null;
        }
        UpdateListening();
        Discovery?.AnnounceSoon();
        ReceiveModeChanged?.Invoke();
    }

    private bool ShouldListen => !IsReceivingPausedByNetwork && (EffectiveMode != ReceiveMode.Nobody || Pairing.IsWindowOpen);

    private readonly Lock _listenLock = new();

    private void UpdateListening()
    {
        lock (_listenLock)
        {
            try
            {
                if (ShouldListen && !Server.IsListening)
                    Server.Start(Settings.Current.TcpPort);
                else if (!ShouldListen && Server.IsListening)
                    Server.Stop();
            }
            catch (InvalidOperationException ex)
            {
                AgentLog.Write("No se pudo escuchar: " + ex.Message);
            }
        }
    }

    private DiscoveryAdvert? CurrentAdvert()
    {
        if (!Server.IsListening || IsReceivingPausedByNetwork)
            return null;
        var mode = EffectiveMode;
        var pairing = Pairing.IsWindowOpen;
        if (mode == ReceiveMode.Nobody && !pairing)
            return null;
        var showAlias = mode == ReceiveMode.Everyone || pairing;
        return new DiscoveryAdvert(Server.Port, showAlias ? Settings.Current.Alias : null, showAlias ? AppInfo.OsName : null,
            Server.HasActiveSessions, pairing, showAlias ? Settings.Current.Avatar : null);
    }

    private void OnPeerVerified(string deviceId, System.Net.IPEndPoint? endPoint, string? alias, string? os, string? avatar)
    {
        Peers.MarkVerified(deviceId, endPoint, alias, os, avatar);
        // El avatar de un compañero de confianza se recuerda para mostrarlo aunque no esté conectado.
        if (avatar is not null && Trust.Find(deviceId) is { } trusted && trusted.Avatar != avatar)
            Trust.Update(deviceId, t => t.Avatar = avatar);
    }

    /// <summary>Bloqueado en esta sesión: no puede conectarse ni aparece en la lista.</summary>
    public bool IsBlocked(string deviceId)
    {
        lock (_lock)
            return _blocked.Contains(deviceId);
    }

    public void Block(string deviceId)
    {
        lock (_lock)
            _blocked.Add(deviceId);
        AgentLog.Write("Equipo bloqueado en esta sesión: " + IdentityVisuals.ShortCode(deviceId));
    }

    // ── Red ──────────────────────────────────────────────────────────────────────

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        LocalNetwork.InvalidateCache();
        CheckPublicNetwork();
        // Con otra dirección, los enlaces viejos quedan colgados: tienen que responder ya o se rehacen.
        Links.CheckAll();
    }

    private bool SafeIsPublic()
    {
        try
        {
            return _options.IsPublicNetwork?.Invoke() ?? false;
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudo consultar la categoría de red: " + ex.Message);
            return false;
        }
    }

    private void CheckPublicNetwork()
    {
        var now = SafeIsPublic();
        if (now == _isPublicNetwork)
            return;
        _isPublicNetwork = now;
        AgentLog.Write(now ? "Red pública: recepción en pausa." : "Red privada: recepción activa.");
        UpdateListening();
        Discovery?.AnnounceSoon();
        NetworkStateChanged?.Invoke();
    }

    private void Housekeeping()
    {
        Peers.Sweep();
        CheckPublicNetwork();
        PeerCache.Save();
    }

    /// <summary>Código de conexión de este equipo (por ejemplo "K7Q2-9XM4"), o null sin red.</summary>
    public string? MyConnectionCode
    {
        get
        {
            var ip = LocalNetwork.PrimaryAddress(Settings.Current.ExcludeVirtualAdapters);
            if (ip is null)
                return null;
            var port = Server.IsListening ? Server.Port : Settings.Current.TcpPort;
            return ConnectionCode.Create(ip, port, Identity.Fingerprint);
        }
    }

    public IReadOnlyList<IPAddress> MyAddresses =>
        LocalNetwork.GetIPv4Interfaces(Settings.Current.ExcludeVirtualAdapters).Select(i => i.Address).Where(a => !IPAddress.IsLoopback(a)).ToList();

    // ── Destinos ─────────────────────────────────────────────────────────────────

    public PeerTarget? TargetForPeer(string deviceId)
    {
        if (Peers.Resolve(deviceId) is not var (ep, live))
            return null;
        var trusted = Trust.Find(deviceId);
        return new PeerTarget
        {
            EndPoint = ep,
            FromCache = !live,
            DisplayName = trusted?.DisplayName ?? Peers.Find(deviceId)?.DisplayName ?? IdentityVisuals.ShortCode(deviceId),
            ExpectedDeviceId = deviceId,
            PinnedKey = trusted is null ? null : Convert.FromBase64String(trusted.PublicKey),
        };
    }

    /// <summary>Busca a un compañero por deviceId o por nombre; si no se conoce su dirección, pregunta en la red.</summary>
    public async Task<PeerTarget?> LocateAsync(string deviceIdOrName, TimeSpan wait)
    {
        var id = ResolveDeviceId(deviceIdOrName);
        if (id is null)
            return null;
        var peer = Peers.Find(id);
        if (peer?.Presence == PeerPresence.Online)
            return TargetForPeer(id);

        // Solo caché o sin dirección: se consulta la red para no usar una IP vieja.
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged()
        {
            if (Peers.Find(id)?.Presence == PeerPresence.Online)
                tcs.TrySetResult();
        }
        Peers.Changed += OnChanged;
        try
        {
            Discovery?.Query();
            await Task.WhenAny(tcs.Task, Task.Delay(wait)).ConfigureAwait(false);
        }
        finally
        {
            Peers.Changed -= OnChanged;
        }
        return TargetForPeer(id);
    }

    public string? ResolveDeviceId(string deviceIdOrName)
    {
        var text = deviceIdOrName.Trim();
        if (PeerKeys.IsValidDeviceId(text))
            return text;
        var all = Peers.Snapshot();
        return all.FirstOrDefault(p => p.IsTrusted && string.Equals(p.DisplayName, text, StringComparison.CurrentCultureIgnoreCase))?.DeviceId
               ?? all.FirstOrDefault(p => string.Equals(p.DisplayName, text, StringComparison.CurrentCultureIgnoreCase))?.DeviceId;
    }

    /// <summary>Destino a partir de "192.168.1.20", "192.168.1.20:45831" o un nombre de equipo.</summary>
    public static async Task<PeerTarget?> TargetForAddressAsync(string text, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        var port = AppInfo.DefaultTcpPort;
        var host = text;
        var colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon && int.TryParse(text[(colon + 1)..], out var p) && p is > 0 and <= 65535)
        {
            host = text[..colon];
            port = p;
        }
        if (!IPAddress.TryParse(host, out var ip))
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
                ip = addresses.FirstOrDefault();
            }
            catch (SocketException)
            {
                return null;
            }
        }
        if (ip is null)
            return null;
        return new PeerTarget { EndPoint = new IPEndPoint(ip, port), DisplayName = host };
    }

    public static PeerTarget? TargetForCode(string code)
    {
        var parsed = ConnectionCode.TryParse(code);
        if (parsed is null)
            return null;
        return new PeerTarget { EndPoint = new IPEndPoint(parsed.Address, parsed.Port), DisplayName = code.Trim().ToUpperInvariant(), Code = parsed };
    }

    // ── Envíos ───────────────────────────────────────────────────────────────────

    public IReadOnlyList<TransferInfo> ActiveTransfers
    {
        get
        {
            lock (_lock)
                return _active.ToList();
        }
    }

    /// <summary>Empieza un envío y devuelve su estado al instante (interfaz optimista).</summary>
    public TransferInfo BeginSend(PeerTarget target, IReadOnlyList<string> paths, string? message = null)
    {
        var info = new TransferInfo(TransferDirection.Send, target.DisplayName)
        {
            PeerId = target.ExpectedDeviceId,
            PeerTrusted = target.PinnedKey is not null,
            FirstName = paths.Count > 0 ? Path.GetFileName(Path.TrimEndingDirectorySeparator(paths[0])) : "",
            FileCount = paths.Count,
            SourcePaths = [.. paths],
            OutgoingMessage = message,
            PeerAvatar = target.ExpectedDeviceId is { } id ? Peers.Find(id)?.Avatar : null,
        };
        lock (_lock)
            _active.Add(info);
        RecordInChat(info);
        TransferStarted?.Invoke(info);

        _ = Task.Run(async () =>
        {
            try
            {
                OutgoingManifest manifest;
                try
                {
                    manifest = OutgoingManifest.Build(paths, message, info.CancellationToken);
                }
                catch (TransferException ex)
                {
                    info.SetState(TransferState.Failed, ex.Code, ex.Detail);
                    return;
                }
                catch (OperationCanceledException)
                {
                    info.SetState(TransferState.Cancelled, ErrorCodes.Cancelled);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    info.SetState(TransferState.Failed, ErrorCodes.Io, ex.Message);
                    return;
                }
                info.FileCount = manifest.FileCount;
                info.TotalBytes = manifest.TotalSize;
                info.FirstName = manifest.FirstName;
                info.Message = manifest.Offer.Message;
                await Client.SendAsync(target, manifest, info).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AgentLog.Write("Error inesperado al enviar: " + ex);
                info.SetState(TransferState.Failed, ErrorCodes.Io, ex.Message);
            }
            finally
            {
                lock (_lock)
                    _active.Remove(info);
                RecordHistory(info);
                RecordInChat(info);
                TransferFinished?.Invoke(info);
            }
        });
        return info;
    }

    public void CancelTransfer(TransferInfo info)
    {
        if (info.Direction == TransferDirection.Send)
            info.Cancel();
        else
            _ = Server.CancelReceiveAsync(info);
    }

    // ── Chats ───────────────────────────────────────────────────────────────────

    /// <summary>Escribe en el chat de ese compañero y lo entrega ya si está enlazado (si no, en cuanto se enlace).</summary>
    public ChatMessage SendChat(string peerId, string text)
    {
        var message = Chats.AddOwnText(peerId, text);
        Links.Push(peerId);
        return message;
    }

    /// <summary>Todo lo que se manda o se recibe de un compañero de confianza queda también en su chat.</summary>
    private void RecordInChat(TransferInfo info)
    {
        if (info.PeerId is { } peerId && Trust.Find(peerId) is not null)
            Chats.RecordTransfer(peerId, info);
    }

    private void ImportHistoryIntoChats()
    {
        if (Settings.Current.ChatsImported)
            return;
        Chats.ImportHistory(History.Recent(), id => Trust.Find(id) is not null);
        Settings.Update(s => s.ChatsImported = true);
    }

    private HelloMessage LocalHello(string purpose) => new()
    {
        V = AppInfo.ProtocolVersion,
        Purpose = purpose,
        Alias = Settings.Current.Alias,
        Os = AppInfo.OsName,
        AppVersion = AppInfo.Version,
        Caps = Capabilities.ForHello(Settings.Current.Avatar),
    };

    private void RecordHistory(TransferInfo info)
    {
        if (info.State == TransferState.Declined && info.Direction == TransferDirection.Receive && info.ErrorCode == ErrorCodes.Cancelled)
            return;
        History.Add(new HistoryEntry
        {
            Time = DateTimeOffset.Now,
            Direction = info.Direction == TransferDirection.Send ? "sent" : "received",
            PeerId = info.PeerId ?? "",
            PeerName = info.PeerName,
            FileCount = info.FileCount,
            TotalBytes = info.TotalBytes,
            SampleNames = [info.FirstName],
            Result = info.State switch
            {
                TransferState.Completed => "ok",
                TransferState.PartiallyCompleted => "partial",
                TransferState.Declined => "declined",
                TransferState.Cancelled => "cancelled",
                _ => "failed",
            },
            Folder = info.DestinationFolder,
            FirstFile = info.CompletedPaths.Count == 1 ? info.CompletedPaths[0] : null,
            Paths = info.Direction == TransferDirection.Send && info.SourcePaths.Count <= HistoryEntry.MaxPaths ? [.. info.SourcePaths] : null,
            Avatar = info.PeerAvatar,
        });
    }

    /// <summary>Genera una identidad nueva. Los compañeros verán "la clave cambió" y habrá que volver a emparejar.</summary>
    public void ResetIdentity()
    {
        var fresh = DeviceIdentity.CreateNew();
        fresh.Save(Paths, _protector);
        fresh.Dispose();
    }

    // ── IPC (menú contextual, "Enviar a", segunda instancia) ─────────────────────

    private async Task<IpcResponse> HandleIpcAsync(IpcRequest request)
    {
        switch (request.Command)
        {
            case "ping":
                return new IpcResponse { Ok = true };
            case "show":
                ShowRequested?.Invoke();
                return new IpcResponse { Ok = true };
            case "quit":
                QuitRequested?.Invoke();
                return new IpcResponse { Ok = true };
            case "send":
                var paths = (request.Paths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Take(OutgoingManifest.MaxFiles).ToList();
                if (paths.Count == 0)
                    return new IpcResponse { Ok = false, Error = "no-paths" };
                if (request.Code)
                {
                    Batch("code", paths, list => CodeRequested?.Invoke(list));
                    return new IpcResponse { Ok = true };
                }
                if (request.To is { } to && !request.Pick)
                {
                    var id = ResolveDeviceId(to);
                    if (id is not null)
                    {
                        Batch("to:" + id, paths, list => _ = SendToAsync(id, list, request.Message));
                        return new IpcResponse { Ok = true };
                    }
                }
                Batch("pick", paths, list => PickRequested?.Invoke(list));
                return await Task.FromResult(new IpcResponse { Ok = true }).ConfigureAwait(false);
            default:
                return new IpcResponse { Ok = false, Error = "unknown-command" };
        }
    }

    private async Task SendToAsync(string deviceId, List<string> paths, string? message)
    {
        var target = await LocateAsync(deviceId, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        if (target is null)
        {
            // No sabemos dónde está: se ofrece elegir otro destino o conectar por código.
            PickRequested?.Invoke(paths);
            return;
        }
        BeginSend(target, paths, message);
    }

    /// <summary>
    /// Con verbos del registro, Windows lanza una invocación por archivo seleccionado: se agrupan las que llegan
    /// en una ventana de unos 300 ms en un solo envío.
    /// </summary>
    private void Batch(string key, List<string> paths, Action<List<string>> flush)
    {
        lock (_lock)
        {
            if (_batches.TryGetValue(key, out var batch))
            {
                batch.Paths.AddRange(paths);
                if (Environment.TickCount64 - batch.StartedAt < 3000)
                    batch.Timer.Change(BatchWindow, Timeout.InfiniteTimeSpan);
                return;
            }
            var created = new PendingBatch
            {
                Paths = [.. paths],
                Flush = flush,
                StartedAt = Environment.TickCount64,
                Timer = null!,
            };
            created.Timer = new Timer(_ => FlushBatch(key), null, BatchWindow, Timeout.InfiniteTimeSpan);
            _batches[key] = created;
        }
    }

    private void FlushBatch(string key)
    {
        PendingBatch? batch;
        lock (_lock)
        {
            if (!_batches.Remove(key, out batch))
                return;
        }
        batch.Timer.Dispose();
        var distinct = batch.Paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        try
        {
            batch.Flush(distinct);
        }
        catch (Exception ex)
        {
            AgentLog.Write("Error al procesar una petición local: " + ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        _housekeeping?.Dispose();
        _everyoneTimer?.Dispose();
        foreach (var t in ActiveTransfers)
            CancelTransfer(t);
        await Links.DisposeAsync().ConfigureAwait(false);
        Discovery?.Dispose();
        if (_ipc is not null)
            await _ipc.DisposeAsync().ConfigureAwait(false);
        await Server.DisposeAsync().ConfigureAwait(false);
        PeerCache.Save();
        Identity.Dispose();
    }
}

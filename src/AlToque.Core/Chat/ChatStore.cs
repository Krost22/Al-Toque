using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlToque.Core.Agent;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;

namespace AlToque.Core.Chat;

/// <summary>
/// Las conversaciones con cada compañero, una por identidad (deviceId): aunque cambie de red o de nombre es el mismo
/// chat. Cada conversación es un registro de eventos que solo crece, cifrado línea a línea con AES-GCM y una llave
/// protegida por el sistema; se escribe en disco antes de avisar a la interfaz, así nada se pierde al reiniciar.
/// </summary>
public sealed class ChatStore
{
    public const int MaxTextLength = 8000;
    public const long MaxSeq = 1_000_000_000_000;
    private const int MaxFilesListed = 20;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private readonly string _dir;
    private readonly byte[] _key;
    private readonly string _me;
    private readonly Dictionary<string, Conversation> _conversations = new(StringComparer.Ordinal);

    private sealed class Conversation(string peerId)
    {
        public readonly string PeerId = peerId;
        public readonly List<ChatMessage> Messages = [];
        public readonly Dictionary<string, ChatMessage> ById = new(StringComparer.Ordinal);
        public readonly SortedSet<long> TheirAhead = [];
        public long MySeq;
        public long TheirUpTo;
        public long DeliveredUpTo;
        public DateTimeOffset ReadAt = DateTimeOffset.MinValue;
    }

    private ChatStore(string dir, byte[] key, string me)
    {
        _dir = dir;
        _key = key;
        _me = me;
    }

    /// <summary>Algo cambió en la conversación con ese compañero; el mensaje es null si cambió más de uno.</summary>
    public event Action<string, ChatMessage?>? Changed;

    public static ChatStore Load(AppPaths paths, ISecretProtector protector, string myDeviceId)
    {
        var store = new ChatStore(paths.ChatDir, LoadOrCreateKey(paths, protector), myDeviceId);
        store.LoadAll();
        return store;
    }

    private static byte[] LoadOrCreateKey(AppPaths paths, ISecretProtector protector)
    {
        if (SecureFile.TryReadAllBytes(paths.ChatKeyFile) is { } stored)
        {
            try
            {
                var key = protector.Unprotect(stored);
                if (key.Length == 32)
                    return key;
            }
            catch (CryptographicException)
            {
                // Perfil copiado de otro equipo o usuario: los chats anteriores no se pueden leer.
                AgentLog.Write("La llave de los chats no se pudo recuperar; se crea una nueva.");
            }
        }
        var fresh = RandomNumberGenerator.GetBytes(32);
        SecureFile.WriteAllBytes(paths.ChatKeyFile, protector.Protect(fresh));
        return fresh;
    }

    private void LoadAll()
    {
        if (!Directory.Exists(_dir))
            return;
        foreach (var file in Directory.EnumerateFiles(_dir, "*.log"))
        {
            var peerId = Path.GetFileNameWithoutExtension(file);
            if (!PeerKeys.IsValidDeviceId(peerId))
                continue;
            var conv = Get(peerId);
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (Decrypt(peerId, line) is { } ev)
                        Apply(conv, ev);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AgentLog.Write("No se pudo leer un chat: " + ex.Message);
            }
        }
    }

    // ── Lectura ─────────────────────────────────────────────────────────────────

    public IReadOnlyList<ChatSummary> Summaries()
    {
        lock (_lock)
        {
            return _conversations.Values
                .Where(c => c.Messages.Count > 0)
                .Select(c => new ChatSummary(c.PeerId, c.Messages[^1], UnreadLocked(c), c.Messages[^1].ReceivedAt))
                .OrderByDescending(s => s.LastActivity)
                .ToList();
        }
    }

    public IReadOnlyList<ChatMessage> Messages(string peerId)
    {
        lock (_lock)
            return _conversations.TryGetValue(peerId, out var c) ? [.. c.Messages] : [];
    }

    public int Unread(string peerId)
    {
        lock (_lock)
            return _conversations.TryGetValue(peerId, out var c) ? UnreadLocked(c) : 0;
    }

    public int TotalUnread()
    {
        lock (_lock)
            return _conversations.Values.Sum(UnreadLocked);
    }

    private int UnreadLocked(Conversation c)
    {
        // Se recorre entera: con el reloj del compañero atrasado, lo nuevo puede quedar antes de lo ya leído.
        var count = 0;
        foreach (var m in c.Messages)
        {
            if (m.ReceivedAt > c.ReadAt && m.Author != _me)
                count++;
        }
        return count;
    }

    /// <summary>Hasta qué número tengo los mensajes del compañero, sin huecos.</summary>
    public long TheirUpTo(string peerId)
    {
        lock (_lock)
            return _conversations.TryGetValue(peerId, out var c) ? c.TheirUpTo : 0;
    }

    /// <summary>Mis mensajes de texto posteriores a ese número, en orden: lo que falta entregar.</summary>
    public IReadOnlyList<ChatMessage> OwnAfter(string peerId, long seq)
    {
        lock (_lock)
        {
            if (!_conversations.TryGetValue(peerId, out var c))
                return [];
            return c.Messages.Where(m => m.Author == _me && m.Kind == ChatKinds.Text && m.Seq > seq).OrderBy(m => m.Seq).ToList();
        }
    }

    // ── Escritura ───────────────────────────────────────────────────────────────

    public ChatMessage AddOwnText(string peerId, string text)
    {
        var clean = Clean(text);
        if (clean.Length == 0)
            throw new ArgumentException("Mensaje vacío.", nameof(text));
        CheckPeer(peerId);
        ChatMessage message;
        lock (_lock)
        {
            var conv = Get(peerId);
            var now = DateTimeOffset.UtcNow;
            message = new ChatMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                Author = _me,
                Seq = conv.MySeq + 1,
                Time = now,
                ReceivedAt = now,
                Text = clean,
            };
            AddLocked(conv, message);
            // Escribir es haber visto la conversación.
            conv.ReadAt = now;
            Append(peerId, new ChatEvent { T = "msg", Msg = message }, new ChatEvent { T = "read", At = now });
        }
        Changed?.Invoke(peerId, message);
        return message;
    }

    /// <summary>Guarda un mensaje del compañero. Devuelve false si ya estaba (mismo id o mismo número): no se duplica.</summary>
    public bool AddRemote(string peerId, ChatWireMessage wire)
    {
        if (!IsValidId(wire.Id) || wire.Seq is < 1 or > MaxSeq)
            throw new ProtocolException("Mensaje de chat inválido.");
        var text = Clean(wire.Text);
        if (text.Length == 0)
            throw new ProtocolException("Mensaje de chat vacío.");
        CheckPeer(peerId);
        ChatMessage message;
        lock (_lock)
        {
            var conv = Get(peerId);
            if (conv.ById.ContainsKey(wire.Id) || wire.Seq <= conv.TheirUpTo || conv.TheirAhead.Contains(wire.Seq))
                return false;
            var now = DateTimeOffset.UtcNow;
            message = new ChatMessage
            {
                Id = wire.Id,
                Author = peerId,
                Seq = wire.Seq,
                // Un reloj adelantado no puede dejar el mensaje "en el futuro", arriba de todo.
                Time = wire.Time > now + MaxClockSkew ? now : wire.Time,
                ReceivedAt = now,
                Text = text,
            };
            AddLocked(conv, message);
            Append(peerId, new ChatEvent { T = "msg", Msg = message });
        }
        Changed?.Invoke(peerId, message);
        return true;
    }

    /// <summary>El compañero confirmó mis mensajes hasta ese número.</summary>
    public void MarkDelivered(string peerId, long upTo)
    {
        lock (_lock)
        {
            if (!_conversations.TryGetValue(peerId, out var conv) || upTo <= conv.DeliveredUpTo)
                return;
            upTo = Math.Min(upTo, conv.MySeq);
            if (upTo <= conv.DeliveredUpTo)
                return;
            ApplyDelivered(conv, upTo);
            Append(peerId, new ChatEvent { T = "delivered", UpTo = upTo });
        }
        Changed?.Invoke(peerId, null);
    }

    public void MarkRead(string peerId)
    {
        lock (_lock)
        {
            if (!_conversations.TryGetValue(peerId, out var conv) || UnreadLocked(conv) == 0)
                return;
            var now = DateTimeOffset.UtcNow;
            conv.ReadAt = now;
            Append(peerId, new ChatEvent { T = "read", At = now });
        }
        Changed?.Invoke(peerId, null);
    }

    /// <summary>Un envío o una recepción de archivos con ese compañero queda en su chat (y se actualiza al terminar).</summary>
    public void RecordTransfer(string peerId, TransferInfo info)
    {
        if (!PeerKeys.IsValidDeviceId(peerId))
            return;
        var id = "t" + info.Id.ToString("N");
        var mine = info.Direction == TransferDirection.Send;
        var result = !info.IsFinished ? ChatResults.Active : info.State switch
        {
            TransferState.Completed => ChatResults.Ok,
            TransferState.PartiallyCompleted => ChatResults.Partial,
            TransferState.Declined => ChatResults.Declined,
            TransferState.Cancelled => ChatResults.Cancelled,
            _ => ChatResults.Failed,
        };
        // En orden de nombre: los dos lados ven lo mismo, sin importar qué archivo terminó de llegar primero.
        var paths = (mine ? info.SourcePaths : info.CompletedPaths)
            .OrderBy(p => Path.GetFileName(Path.TrimEndingDirectorySeparator(p)), StringComparer.CurrentCultureIgnoreCase);
        var files = paths.Take(MaxFilesListed).Select(p => new ChatFile { Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(p)), Path = p }).ToList();
        if (files.Count == 0 && info.FirstName.Length > 0)
            files.Add(new ChatFile { Name = info.FirstName });
        ChatMessage message;
        lock (_lock)
        {
            var conv = Get(peerId);
            if (conv.ById.TryGetValue(id, out var existing))
            {
                existing.Result = result;
                existing.Files = files;
                existing.FileCount = info.FileCount;
                existing.TotalBytes = info.TotalBytes;
                existing.Folder = info.DestinationFolder;
                existing.Text ??= info.Message ?? info.OutgoingMessage;
                message = existing;
                Append(peerId, new ChatEvent
                {
                    T = "update", Id = id, Result = result, Files = files, FileCount = info.FileCount, TotalBytes = info.TotalBytes,
                    Folder = info.DestinationFolder,
                });
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                message = new ChatMessage
                {
                    Id = id,
                    Author = mine ? _me : peerId,
                    Time = info.StartedAt.ToUniversalTime(),
                    ReceivedAt = now,
                    Kind = ChatKinds.Files,
                    Text = info.Message ?? info.OutgoingMessage,
                    Files = files,
                    FileCount = info.FileCount,
                    TotalBytes = info.TotalBytes,
                    Result = result,
                    Folder = info.DestinationFolder,
                };
                AddLocked(conv, message);
                Append(peerId, new ChatEvent { T = "msg", Msg = message });
            }
        }
        Changed?.Invoke(peerId, message);
    }

    /// <summary>Una sola vez: lo que ya estaba en el historial pasa al chat de cada compañero de confianza.</summary>
    public void ImportHistory(IEnumerable<HistoryEntry> entries, Func<string, bool> isContact)
    {
        var pending = new Dictionary<string, List<ChatEvent>>(StringComparer.Ordinal);
        lock (_lock)
        {
            foreach (var e in entries.OrderBy(e => e.Time))
            {
                if (!PeerKeys.IsValidDeviceId(e.PeerId) || !isContact(e.PeerId))
                    continue;
                var conv = Get(e.PeerId);
                var id = "h" + e.Time.UtcTicks.ToString("x") + (e.Direction == "sent" ? "s" : "r");
                if (conv.ById.ContainsKey(id))
                    continue;
                var paths = e.Paths ?? (e.FirstFile is { } f ? [f] : []);
                var message = new ChatMessage
                {
                    Id = id,
                    Author = e.Direction == "sent" ? _me : e.PeerId,
                    Time = e.Time.ToUniversalTime(),
                    ReceivedAt = e.Time.ToUniversalTime(),
                    Kind = ChatKinds.Files,
                    Files = paths.Count > 0
                        ? paths.Take(MaxFilesListed).Select(p => new ChatFile { Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(p)), Path = p }).ToList()
                        : e.SampleNames.Select(n => new ChatFile { Name = n }).ToList(),
                    FileCount = e.FileCount,
                    TotalBytes = e.TotalBytes,
                    Result = e.Result,
                    Folder = e.Folder,
                };
                AddLocked(conv, message);
                if (!pending.TryGetValue(e.PeerId, out var events))
                    pending[e.PeerId] = events = [];
                events.Add(new ChatEvent { T = "msg", Msg = message });
            }
            // Lo importado ya se había visto. Una escritura por conversación: son cientos de líneas.
            var now = DateTimeOffset.UtcNow;
            foreach (var (peerId, events) in pending)
            {
                _conversations[peerId].ReadAt = now;
                events.Add(new ChatEvent { T = "read", At = now });
                Append(peerId, [.. events]);
            }
        }
        foreach (var peerId in pending.Keys)
            Changed?.Invoke(peerId, null);
    }

    /// <summary>
    /// El compañero dice que tiene mis mensajes hasta el N. Normalmente voy por N o más; si voy por menos, perdí mi
    /// registro (se borró la carpeta de chats, otra llave) y lo que escribí desde entonces lleva números que él ya
    /// vio: sigo desde el N y lo renumero, para que no lo descarte como repetido.
    /// </summary>
    public void CatchUp(string peerId, long haveYours)
    {
        if (haveYours is < 0 or > MaxSeq)
            throw new ProtocolException("Número de chat inválido.");
        CheckPeer(peerId);
        lock (_lock)
        {
            var conv = Get(peerId);
            if (haveYours <= conv.MySeq)
                return;
            var next = haveYours;
            List<ChatEvent> events = [];
            foreach (var m in conv.Messages.Where(m => m.Author == _me && m.Kind == ChatKinds.Text && m.Seq > 0).OrderBy(m => m.Seq).ToList())
            {
                m.Seq = ++next;
                m.Delivered = false;
                events.Add(new ChatEvent { T = "seq", Id = m.Id, MySeq = m.Seq });
            }
            conv.MySeq = next;
            conv.DeliveredUpTo = haveYours;
            events.Add(new ChatEvent { T = "seq", MySeq = next, UpTo = haveYours });
            Append(peerId, [.. events]);
            AgentLog.Write($"Chat con {IdentityVisuals.ShortCode(peerId)}: la numeración sigue desde {haveYours} (registro perdido).");
        }
        Changed?.Invoke(peerId, null);
    }

    /// <summary>
    /// Borra la conversación de este equipo. Los contadores se conservan: el compañero no vuelve a mandar lo ya recibido.
    /// </summary>
    public void Clear(string peerId)
    {
        lock (_lock)
        {
            if (!_conversations.TryGetValue(peerId, out var conv))
                return;
            var ev = new ChatEvent { T = "clear", MySeq = conv.MySeq, TheirUpTo = Math.Max(conv.TheirUpTo, conv.TheirAhead.Count > 0 ? conv.TheirAhead.Max : 0), At = DateTimeOffset.UtcNow };
            Apply(conv, ev);
            try
            {
                SecureFile.WriteAllText(PathFor(peerId), Encrypt(peerId, ev) + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AgentLog.Write("No se pudo borrar el chat: " + ex.Message);
            }
        }
        Changed?.Invoke(peerId, null);
    }

    // ── Estado en memoria ───────────────────────────────────────────────────────

    private Conversation Get(string peerId)
    {
        if (!_conversations.TryGetValue(peerId, out var conv))
            _conversations[peerId] = conv = new Conversation(peerId);
        return conv;
    }

    private void Apply(Conversation conv, ChatEvent ev)
    {
        switch (ev.T)
        {
            case "msg" when ev.Msg is { } m && !string.IsNullOrEmpty(m.Id):
                AddLocked(conv, m);
                break;
            case "delivered" when ev.UpTo is { } upTo:
                ApplyDelivered(conv, upTo);
                break;
            case "update" when ev.Id is { } id && conv.ById.TryGetValue(id, out var m):
                m.Result = ev.Result ?? m.Result;
                m.Files = ev.Files ?? m.Files;
                m.FileCount = ev.FileCount ?? m.FileCount;
                m.TotalBytes = ev.TotalBytes ?? m.TotalBytes;
                m.Folder = ev.Folder ?? m.Folder;
                break;
            case "seq" when ev.Id is { } renumbered && ev.MySeq is { } newSeq:
                if (conv.ById.TryGetValue(renumbered, out var own))
                {
                    own.Seq = newSeq;
                    own.Delivered = false;
                }
                break;
            case "seq" when ev.MySeq is { } mySeq:
                conv.MySeq = Math.Max(conv.MySeq, mySeq);
                conv.DeliveredUpTo = Math.Max(conv.DeliveredUpTo, ev.UpTo ?? 0);
                break;
            case "read" when ev.At is { } at:
                if (at > conv.ReadAt)
                    conv.ReadAt = at;
                break;
            case "clear":
                conv.Messages.Clear();
                conv.ById.Clear();
                conv.TheirAhead.Clear();
                conv.MySeq = Math.Max(conv.MySeq, ev.MySeq ?? 0);
                conv.TheirUpTo = Math.Max(conv.TheirUpTo, ev.TheirUpTo ?? 0);
                conv.DeliveredUpTo = conv.MySeq;
                if (ev.At is { } clearedAt && clearedAt > conv.ReadAt)
                    conv.ReadAt = clearedAt;
                break;
        }
    }

    private void AddLocked(Conversation conv, ChatMessage m)
    {
        if (!conv.ById.TryAdd(m.Id, m))
            return;
        // Orden por hora del autor; casi siempre va al final.
        var i = conv.Messages.Count;
        while (i > 0 && conv.Messages[i - 1].Time > m.Time)
            i--;
        conv.Messages.Insert(i, m);
        if (m.Kind != ChatKinds.Text || m.Seq <= 0)
            return;
        if (m.Author == _me)
        {
            conv.MySeq = Math.Max(conv.MySeq, m.Seq);
            if (m.Seq <= conv.DeliveredUpTo)
                m.Delivered = true;
        }
        else if (m.Seq == conv.TheirUpTo + 1)
        {
            conv.TheirUpTo++;
            while (conv.TheirAhead.Remove(conv.TheirUpTo + 1))
                conv.TheirUpTo++;
        }
        else if (m.Seq > conv.TheirUpTo)
        {
            conv.TheirAhead.Add(m.Seq);
        }
    }

    private void ApplyDelivered(Conversation conv, long upTo)
    {
        conv.DeliveredUpTo = Math.Max(conv.DeliveredUpTo, upTo);
        foreach (var m in conv.Messages)
        {
            if (m.Author == _me && m.Kind == ChatKinds.Text && m.Seq <= conv.DeliveredUpTo)
                m.Delivered = true;
        }
    }

    // ── Disco ───────────────────────────────────────────────────────────────────

    private string PathFor(string peerId) => Path.Combine(_dir, peerId + ".log");

    private void Append(string peerId, params ReadOnlySpan<ChatEvent> events)
    {
        var text = new StringBuilder();
        foreach (var ev in events)
            text.Append(Encrypt(peerId, ev)).Append('\n');
        try
        {
            SecureFile.EnsurePrivateDirectory(_dir);
            var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var fs = new FileStream(PathFor(peerId), options);
            fs.Write(Encoding.ASCII.GetBytes(text.ToString()));
            fs.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Write("No se pudo guardar el chat: " + ex.Message);
        }
    }

    private string Encrypt(string peerId, ChatEvent ev)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(ev, ChatJsonContext.Default.ChatEvent);
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        // La conversación va como dato asociado: una línea no se puede mover a otro chat.
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), Encoding.ASCII.GetBytes(peerId));
        return Convert.ToBase64String(output);
    }

    private ChatEvent? Decrypt(string peerId, string line)
    {
        try
        {
            var data = Convert.FromBase64String(line.Trim());
            if (data.Length < NonceSize + TagSize)
                return null;
            var plain = new byte[data.Length - NonceSize - TagSize];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain, Encoding.ASCII.GetBytes(peerId));
            return JsonSerializer.Deserialize(plain, ChatJsonContext.Default.ChatEvent);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            // Línea a medio escribir (corte de luz) o de otra llave: se ignora.
            return null;
        }
    }

    // ── Validación ──────────────────────────────────────────────────────────────

    private static void CheckPeer(string peerId)
    {
        if (!PeerKeys.IsValidDeviceId(peerId))
            throw new ArgumentException("Compañero inválido.", nameof(peerId));
    }

    private static bool IsValidId(string? id) => id is { Length: 32 } && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    /// <summary>Sin caracteres de control (salvo saltos de línea y tabuladores), sin espacios al final y con un largo máximo.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new StringBuilder(Math.Min(text.Length, MaxTextLength));
        foreach (var c in text.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            if (sb.Length >= MaxTextLength)
                break;
            if (c is '\n' or '\t' || !char.IsControl(c))
                sb.Append(c);
        }
        return sb.ToString().TrimEnd();
    }
}

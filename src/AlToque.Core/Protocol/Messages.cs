using System.Text.Json.Serialization;

namespace AlToque.Core.Protocol;

/// <summary>Tipo de trama. Cualquier valor desconocido cierra la conexión.</summary>
public enum FrameType : byte
{
    Hello = 1,
    Offer = 2,
    Decision = 3,
    StreamOpen = 4,
    Chunk = 5,
    FileEnd = 7,
    FileResult = 8,
    Cancel = 11,
    Error = 12,
    Bye = 13,
    PairCommit = 20,
    PairNonce = 21,
    PairReveal = 22,
    PairConfirm = 23,

    // Enlace permanente entre compañeros de confianza (chats).
    ChatSync = 30,
    ChatMessage = 31,
    ChatAck = 32,
    Ping = 33,
    Pong = 34,
}

public static class Purposes
{
    public const string Transfer = "transfer";
    public const string Pair = "pair";
    public const string Probe = "probe";

    /// <summary>Enlace permanente entre dos compañeros de confianza: chats y presencia.</summary>
    public const string Link = "link";
}

public static class Capabilities
{
    public const string Burst = "burst";
    public const string Metadata = "metadata";
    public const string Message = "message";

    /// <summary>Habla el enlace permanente y sincroniza chats.</summary>
    public const string Chat = "chat";

    /// <summary>Acepta solicitudes de conexión sin que su usuario abra «Emparejar» (las ve como un aviso).</summary>
    public const string PairRequest = "pairreq";

    public static readonly List<string> Local = [Burst, Metadata, Message, Chat];

    /// <summary>Prefijo con el que el avatar viaja dentro de las capacidades del HELLO.</summary>
    public const string AvatarPrefix = "av:";

    /// <summary>Capacidades locales más el avatar, si hay uno.</summary>
    public static List<string> ForHello(string? avatar) =>
        Storage.TextLimits.CleanAvatar(avatar) is { } a ? [.. Local, AvatarPrefix + a] : [.. Local];

    public static string? AvatarOf(IReadOnlyList<string>? caps)
    {
        if (caps is null)
            return null;
        foreach (var c in caps)
        {
            if (c.StartsWith(AvatarPrefix, StringComparison.Ordinal))
                return Storage.TextLimits.CleanAvatar(c[AvatarPrefix.Length..]);
        }
        return null;
    }
}

public static class DecisionResults
{
    public const string Accepted = "accepted";
    public const string Partial = "partial";
    public const string Declined = "declined";
}

public sealed class HelloMessage
{
    public required int V { get; init; }
    public required string Purpose { get; init; }
    public required string Alias { get; init; }
    public required string Os { get; init; }
    public required string AppVersion { get; init; }
    public required List<string> Caps { get; init; }
}

public sealed class OfferMessage
{
    public required string SessionId { get; init; }
    public required List<OfferFile> Files { get; init; }

    /// <summary>Carpetas vacías que también hay que crear.</summary>
    public List<string>? Dirs { get; init; }

    public required long TotalSize { get; init; }
    public string? Message { get; init; }
}

public sealed class OfferFile
{
    public required string FileId { get; init; }

    /// <summary>Ruta relativa con "/" como separador.</summary>
    public required string Path { get; init; }

    public required long Size { get; init; }
    public DateTimeOffset? Modified { get; init; }
    public DateTimeOffset? Created { get; init; }
    public string? Mime { get; init; }
    public bool Executable { get; init; }
}

public sealed class DecisionMessage
{
    public required string SessionId { get; init; }
    public required string Result { get; init; }

    /// <summary>Código de error cuando se rechaza (E_DECLINED, E_DISK_FULL…).</summary>
    public string? Reason { get; init; }

    public List<AcceptedFile>? Files { get; init; }

    /// <summary>Bytes que faltan en disco, con E_DISK_FULL.</summary>
    public long? MissingBytes { get; init; }
}

public sealed class AcceptedFile
{
    public required string FileId { get; init; }

    /// <summary>Token aleatorio de un solo uso, atado a la sesión y a la clave del emisor.</summary>
    public required string Token { get; init; }

    public long Offset { get; init; }
}

public sealed class StreamOpenMessage
{
    public required string SessionId { get; init; }
    public required string FileId { get; init; }
    public required string Token { get; init; }
    public long Offset { get; init; }
}

public sealed class FileEndMessage
{
    public required string FileId { get; init; }
    public required long Size { get; init; }

    /// <summary>SHA-256 del archivo completo en hexadecimal.</summary>
    public required string Sha256 { get; init; }

    /// <summary>El emisor no pudo leer el archivo completo (cambió o hubo un error).</summary>
    public bool Aborted { get; init; }
}

public sealed class FileResultMessage
{
    public required string FileId { get; init; }
    public required bool Ok { get; init; }
    public string? Error { get; init; }
}

public sealed class ErrorMessage
{
    public required string Code { get; init; }
    public string? Message { get; init; }
}

public sealed class CancelMessage
{
    public string? Reason { get; init; }
}

public sealed class ByeMessage
{
    public string? Reason { get; init; }
}

public sealed class PairCommitMessage
{
    public required string Commit { get; init; }
}

public sealed class PairNonceMessage
{
    public required string Nonce { get; init; }
}

public sealed class PairRevealMessage
{
    public required string Nonce { get; init; }
}

public sealed class PairConfirmMessage
{
    public required bool Ok { get; init; }
}

/// <summary>Al abrir el enlace: "tengo tus mensajes hasta el número N" (contiguo). El otro manda lo que falta.</summary>
public sealed class ChatSyncMessage
{
    public required long HaveYours { get; init; }
}

/// <summary>Un mensaje de chat. El id lo pone el autor y el número de orden crece de uno en uno por conversación.</summary>
public sealed class ChatWireMessage
{
    public required string Id { get; init; }
    public required long Seq { get; init; }
    public required DateTimeOffset Time { get; init; }
    public required string Text { get; init; }
}

/// <summary>Acuse: "tengo tus mensajes hasta el número N".</summary>
public sealed class ChatAckMessage
{
    public required long Received { get; init; }
}

/// <summary>Señal de vida del enlace (también se responde con Pong).</summary>
public sealed class PingMessage
{
    public long T { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    MaxDepth = 8)]
[JsonSerializable(typeof(HelloMessage))]
[JsonSerializable(typeof(OfferMessage))]
[JsonSerializable(typeof(DecisionMessage))]
[JsonSerializable(typeof(StreamOpenMessage))]
[JsonSerializable(typeof(FileEndMessage))]
[JsonSerializable(typeof(FileResultMessage))]
[JsonSerializable(typeof(ErrorMessage))]
[JsonSerializable(typeof(CancelMessage))]
[JsonSerializable(typeof(ByeMessage))]
[JsonSerializable(typeof(PairCommitMessage))]
[JsonSerializable(typeof(PairNonceMessage))]
[JsonSerializable(typeof(PairRevealMessage))]
[JsonSerializable(typeof(PairConfirmMessage))]
[JsonSerializable(typeof(ChatSyncMessage))]
[JsonSerializable(typeof(ChatWireMessage))]
[JsonSerializable(typeof(ChatAckMessage))]
[JsonSerializable(typeof(PingMessage))]
public sealed partial class ProtocolJsonContext : JsonSerializerContext;

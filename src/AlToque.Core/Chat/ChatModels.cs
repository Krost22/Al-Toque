using System.Text.Json.Serialization;

namespace AlToque.Core.Chat;

public static class ChatKinds
{
    public const string Text = "text";

    /// <summary>Un envío o una recepción de archivos con ese compañero (registro local, no se sincroniza).</summary>
    public const string Files = "files";
}

/// <summary>Resultado de un envío de archivos dentro del chat.</summary>
public static class ChatResults
{
    public const string Active = "active";
    public const string Ok = "ok";
    public const string Partial = "partial";
    public const string Declined = "declined";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

/// <summary>
/// Un mensaje de una conversación. Los de texto llevan un id que pone el autor y un número de orden por autor: así se
/// sincronizan sin duplicarse. Los de archivos son registros locales de cada lado (cada uno ve su envío o recepción).
/// </summary>
public sealed class ChatMessage
{
    public required string Id { get; init; }

    /// <summary>deviceId de quien lo escribió o envió.</summary>
    public required string Author { get; init; }

    /// <summary>Orden del autor en la conversación (1, 2, 3…); 0 en los registros de archivos.</summary>
    public long Seq { get; set; }

    /// <summary>Cuándo lo escribió el autor (su reloj, acotado al recibir).</summary>
    public DateTimeOffset Time { get; init; }

    /// <summary>Cuándo llegó a este equipo: sirve para contar los no leídos.</summary>
    public DateTimeOffset ReceivedAt { get; init; }

    public string Kind { get; init; } = ChatKinds.Text;

    /// <summary>Texto del mensaje, o el mensaje que acompañaba al envío de archivos.</summary>
    public string? Text { get; set; }

    /// <summary>Mensaje propio que ya confirmó el otro equipo (✓✓).</summary>
    public bool Delivered { get; set; }

    // ── Archivos ────────────────────────────────────────────────────────────────

    public List<ChatFile>? Files { get; set; }
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }

    /// <summary>active, ok, partial, declined, cancelled o failed.</summary>
    public string? Result { get; set; }

    /// <summary>Carpeta donde quedó lo recibido.</summary>
    public string? Folder { get; set; }

    [JsonIgnore]
    public bool IsFiles => Kind == ChatKinds.Files;
}

public sealed class ChatFile
{
    public string Name { get; set; } = "";

    /// <summary>Ruta local: de origen al enviar, la recibida al recibir.</summary>
    public string? Path { get; set; }
}

/// <summary>Resumen de una conversación para la lista: último mensaje y no leídos.</summary>
public sealed record ChatSummary(string PeerId, ChatMessage? Last, int Unread, DateTimeOffset LastActivity);

/// <summary>Una línea del registro de una conversación (se guarda cifrada).</summary>
internal sealed class ChatEvent
{
    /// <summary>msg, delivered, update, read o clear.</summary>
    public string T { get; set; } = "";

    public ChatMessage? Msg { get; set; }
    public string? Id { get; set; }
    public long? UpTo { get; set; }
    public DateTimeOffset? At { get; set; }
    public string? Result { get; set; }
    public List<ChatFile>? Files { get; set; }
    public int? FileCount { get; set; }
    public long? TotalBytes { get; set; }
    public string? Folder { get; set; }

    /// <summary>clear: los contadores sobreviven al borrado para no volver a recibir lo ya visto.</summary>
    public long? MySeq { get; set; }

    public long? TheirUpTo { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatEvent))]
internal sealed partial class ChatJsonContext : JsonSerializerContext;

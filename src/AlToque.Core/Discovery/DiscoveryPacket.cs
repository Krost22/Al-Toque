using System.Text.Json;
using System.Text.Json.Serialization;
using AlToque.Core.Security;
using AlToque.Core.Storage;

namespace AlToque.Core.Discovery;

/// <summary>
/// Anuncio UDP. Es solo una pista de quién está en la red: nunca marca a un equipo como verificado.
/// No incluye datos personales; el alias solo viaja en modo "Todos" o con la ventana de emparejamiento abierta.
/// </summary>
public sealed class DiscoveryPacket
{
    public const string AppTag = "altoque";
    public const int MaxBytes = 1024;

    public required string App { get; init; }
    public required int V { get; init; }

    /// <summary>"announce", "query", "reply" o "bye".</summary>
    public required string T { get; init; }

    public required string Id { get; init; }
    public int Port { get; init; }
    public List<string>? Caps { get; init; }

    /// <summary>Nonce aleatorio, para descartar duplicados.</summary>
    public required string N { get; init; }

    public string? Alias { get; init; }
    public string? Os { get; init; }
    public bool Busy { get; init; }

    /// <summary>La ventana de emparejamiento está abierta.</summary>
    public bool Pairing { get; init; }

    public static class Types
    {
        public const string Announce = "announce";
        public const string Query = "query";
        public const string Reply = "reply";
        public const string Bye = "bye";
    }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, DiscoveryJsonContext.Default.DiscoveryPacket);

    public static DiscoveryPacket? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length is 0 or > MaxBytes)
            return null;
        DiscoveryPacket? p;
        try
        {
            p = JsonSerializer.Deserialize(data, DiscoveryJsonContext.Default.DiscoveryPacket);
        }
        catch (JsonException)
        {
            return null;
        }
        if (p is null || p.App != AppTag || p.V < 1 || !PeerKeys.IsValidDeviceId(p.Id) || p.N.Length > 32)
            return null;
        if (p.T is not (Types.Announce or Types.Query or Types.Reply or Types.Bye))
            return null;
        if (p.T is Types.Announce or Types.Reply && p.Port is < 1 or > 65535)
            return null;
        if (p.Caps is { Count: > 16 } || p.Caps?.Any(c => c.Length > 32) == true)
            return null;
        if (p.Os is { Length: > 16 })
            return null;
        if (p.Alias is not null && TextLimits.CleanAlias(p.Alias) != p.Alias)
            return null;
        return p;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    MaxDepth = 4)]
[JsonSerializable(typeof(DiscoveryPacket))]
internal sealed partial class DiscoveryJsonContext : JsonSerializerContext;

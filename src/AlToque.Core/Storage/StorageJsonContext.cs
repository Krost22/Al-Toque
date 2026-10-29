using System.Text.Json.Serialization;
using AlToque.Core.Security;

namespace AlToque.Core.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SealedEnvelope))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<TrustedPeer>))]
[JsonSerializable(typeof(List<PeerCacheEntry>))]
[JsonSerializable(typeof(HistoryEntry))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;

namespace AlToque.Core.Protocol;

/// <summary>Códigos de error del protocolo y de la interfaz (sección 8.8).</summary>
public static class ErrorCodes
{
    public const string Declined = "E_DECLINED";
    public const string AcceptTimeout = "E_ACCEPT_TIMEOUT";
    public const string Unreachable = "E_UNREACHABLE";
    public const string IdentityMismatch = "E_IDENTITY_MISMATCH";
    public const string Policy = "E_POLICY";
    public const string DiskFull = "E_DISK_FULL";
    public const string HashMismatch = "E_HASH_MISMATCH";
    public const string NetworkPublic = "E_NETWORK_PUBLIC";
    public const string BadManifest = "E_BAD_MANIFEST";
    public const string Busy = "E_BUSY";
    public const string RateLimited = "E_RATE_LIMIT";
    public const string Cancelled = "E_CANCELLED";
    public const string Protocol = "E_PROTOCOL";
    public const string Io = "E_IO";
    public const string SourceChanged = "E_SOURCE_CHANGED";
    public const string TooLarge = "E_TOO_LARGE";
    public const string NotPairing = "E_NOT_PAIRING";
    public const string PairingLocked = "E_PAIRING_LOCKED";
    public const string PairingMismatch = "E_PAIRING_MISMATCH";
    public const string Timeout = "E_TIMEOUT";
    public const string Skipped = "E_SKIPPED";
    public const string Version = "E_VERSION";

    public static bool IsKnown(string code) => code.Length <= 32 && code.StartsWith("E_", StringComparison.Ordinal);
}

/// <summary>Violación del protocolo: la conexión se cierra sin más explicaciones.</summary>
public sealed class ProtocolException(string message) : Exception(message);

/// <summary>Error con código conocido que se puede explicar al usuario.</summary>
public sealed class TransferException(string code, string? detail = null) : Exception(detail ?? code)
{
    public string Code { get; } = code;
    public string? Detail { get; } = detail;
}

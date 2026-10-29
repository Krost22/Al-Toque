using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlToque.Core.Storage;

namespace AlToque.Core.Security;

/// <summary>
/// Sella archivos de configuración con HMAC-SHA256 para detectar modificaciones externas.
/// La clave vive protegida por el almacén del sistema.
/// </summary>
public sealed class Sealer
{
    private readonly byte[] _key;

    private Sealer(byte[] key) => _key = key;

    public static Sealer LoadOrCreate(AppPaths paths, ISecretProtector protector)
    {
        var stored = SecureFile.TryReadAllBytes(paths.SealKeyFile);
        if (stored is not null)
        {
            try
            {
                var key = protector.Unprotect(stored);
                if (key.Length == 32)
                    return new Sealer(key);
            }
            catch (CryptographicException)
            {
            }
        }
        var fresh = RandomNumberGenerator.GetBytes(32);
        SecureFile.WriteAllBytes(paths.SealKeyFile, protector.Protect(fresh));
        return new Sealer(fresh);
    }

    internal static Sealer ForTests(byte[] key) => new(key);

    public string Seal(string payloadJson)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payloadJson));
        var envelope = new SealedEnvelope { V = 1, Payload = payloadJson, Mac = Convert.ToBase64String(mac) };
        return JsonSerializer.Serialize(envelope, StorageJsonContext.Default.SealedEnvelope);
    }

    /// <returns>El contenido si el sello es válido; <c>null</c> si falta o fue alterado.</returns>
    public string? Unseal(string envelopeJson)
    {
        SealedEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(envelopeJson, StorageJsonContext.Default.SealedEnvelope);
        }
        catch (JsonException)
        {
            return null;
        }
        if (envelope?.Payload is null || envelope.Mac is null)
            return null;

        byte[] mac;
        try
        {
            mac = Convert.FromBase64String(envelope.Mac);
        }
        catch (FormatException)
        {
            return null;
        }
        var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(envelope.Payload));
        return CryptographicOperations.FixedTimeEquals(mac, expected) ? envelope.Payload : null;
    }
}

public sealed class SealedEnvelope
{
    public int V { get; set; }
    public string? Payload { get; set; }
    public string? Mac { get; set; }
}

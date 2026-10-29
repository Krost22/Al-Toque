using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AlToque.Core.Security;

/// <summary>
/// Identidad criptográfica de este equipo: un par de claves ECDSA P-256 y un certificado
/// autofirmado sin datos personales. El <see cref="DeviceId"/> se deriva de la clave pública.
/// </summary>
public sealed class DeviceIdentity : IDisposable
{
    private DeviceIdentity(X509Certificate2 certificate)
    {
        Certificate = certificate;
        PublicKey = PeerKeys.ExportSpki(certificate);
        Fingerprint = PeerKeys.Fingerprint(PublicKey);
        DeviceId = PeerKeys.DeviceIdFromSpki(PublicKey);
    }

    /// <summary>Certificado con clave privada, para TLS mutuo.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>SubjectPublicKeyInfo en DER.</summary>
    public byte[] PublicKey { get; }

    /// <summary>SHA-256 de la clave pública.</summary>
    public byte[] Fingerprint { get; }

    public string DeviceId { get; }

    public static DeviceIdentity LoadOrCreate(AppPaths paths, ISecretProtector protector, out bool created)
    {
        var stored = SecureFile.TryReadAllBytes(paths.IdentityFile);
        if (stored is not null)
        {
            try
            {
                var pfx = protector.Unprotect(stored);
                try
                {
                    var identity = FromPkcs12(pfx);
                    created = false;
                    return identity;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(pfx);
                }
            }
            catch (CryptographicException)
            {
                // La clave no se puede recuperar (se copió a otro usuario o equipo): se genera una nueva.
            }
        }

        var fresh = CreateNew();
        fresh.Save(paths, protector);
        created = true;
        return fresh;
    }

    public static DeviceIdentity CreateNew()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var subject = new X500DistinguishedName("CN=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)));
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-2);
        using var ephemeral = request.CreateSelfSigned(notBefore, notBefore.AddYears(30));

        // Se recarga desde PKCS#12: en Windows, SChannel no acepta claves efímeras en el lado servidor.
        var pfx = ephemeral.Export(X509ContentType.Pkcs12);
        try
        {
            return FromPkcs12(pfx);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }

    public void Save(AppPaths paths, ISecretProtector protector)
    {
        var pfx = Certificate.Export(X509ContentType.Pkcs12);
        try
        {
            SecureFile.WriteAllBytes(paths.IdentityFile, protector.Protect(pfx));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }

    private static DeviceIdentity FromPkcs12(byte[] pfx)
    {
        var flags = OperatingSystem.IsLinux() ? X509KeyStorageFlags.EphemeralKeySet : X509KeyStorageFlags.UserKeySet;
        var cert = X509CertificateLoader.LoadPkcs12(pfx, null, flags | X509KeyStorageFlags.Exportable);
        if (!cert.HasPrivateKey || !PeerKeys.IsAcceptableKey(cert))
        {
            cert.Dispose();
            throw new CryptographicException("Identidad inválida.");
        }
        return new DeviceIdentity(cert);
    }

    public void Dispose() => Certificate.Dispose();
}

/// <summary>Utilidades para identificar a un compañero a partir de su certificado.</summary>
public static class PeerKeys
{
    /// <summary>Longitud del deviceId: 128 bits en base32 sin relleno.</summary>
    public const int DeviceIdLength = 26;

    public static byte[] ExportSpki(X509Certificate2 cert) => cert.PublicKey.ExportSubjectPublicKeyInfo();

    public static byte[] Fingerprint(ReadOnlySpan<byte> spki) => SHA256.HashData(spki);

    public static string DeviceIdFromSpki(ReadOnlySpan<byte> spki) => Base32.Encode(SHA256.HashData(spki).AsSpan(0, 16));

    public static bool IsValidDeviceId(string? id) => Base32.IsValid(id, DeviceIdLength);

    /// <summary>Solo se admiten claves ECDSA P-256: reduce la superficie de ataque.</summary>
    public static bool IsAcceptableKey(X509Certificate2 cert)
    {
        using var ec = cert.GetECDsaPublicKey();
        if (ec is null || ec.KeySize != 256)
            return false;
        var parameters = ec.ExportParameters(false);
        return parameters.Curve.IsNamed &&
               (parameters.Curve.Oid.Value == "1.2.840.10045.3.1.7" || parameters.Curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256");
    }
}

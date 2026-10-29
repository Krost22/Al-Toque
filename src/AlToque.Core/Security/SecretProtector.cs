using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AlToque.Core.Security;

/// <summary>Protege secretos en reposo con el almacén del sistema.</summary>
public interface ISecretProtector
{
    /// <summary>Indica si la protección depende del sistema operativo y no solo de permisos de archivo.</summary>
    bool IsHardwareOrOsBacked { get; }

    byte[] Protect(ReadOnlySpan<byte> plaintext);

    /// <exception cref="CryptographicException">Si el secreto no se puede recuperar (otro usuario, otro equipo).</exception>
    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}

public static class SecretProtector
{
    public static ISecretProtector CreateDefault() =>
        OperatingSystem.IsWindows() ? new DpapiSecretProtector() : new FilePermissionSecretProtector();
}

/// <summary>DPAPI de Windows, ligado a la cuenta del usuario actual.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class DpapiSecretProtector : ISecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AlToque.secret.v1");

    public bool IsHardwareOrOsBacked => true;

    public unsafe byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        fixed (byte* pIn = plaintext)
        fixed (byte* pEntropy = Entropy)
        {
            var input = new DataBlob { cbData = (uint)plaintext.Length, pbData = pIn };
            var entropy = new DataBlob { cbData = (uint)Entropy.Length, pbData = pEntropy };
            if (!CryptProtectData(&input, null, &entropy, 0, 0, CryptProtectUiForbidden, out var output))
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            return TakeBlob(output);
        }
    }

    public unsafe byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        fixed (byte* pIn = protectedData)
        fixed (byte* pEntropy = Entropy)
        {
            var input = new DataBlob { cbData = (uint)protectedData.Length, pbData = pIn };
            var entropy = new DataBlob { cbData = (uint)Entropy.Length, pbData = pEntropy };
            if (!CryptUnprotectData(&input, 0, &entropy, 0, 0, CryptProtectUiForbidden, out var output))
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            return TakeBlob(output);
        }
    }

    private static unsafe byte[] TakeBlob(DataBlob blob)
    {
        try
        {
            return new ReadOnlySpan<byte>(blob.pbData, checked((int)blob.cbData)).ToArray();
        }
        finally
        {
            if (blob.pbData != null)
            {
                new Span<byte>(blob.pbData, (int)blob.cbData).Clear();
                LocalFree((nint)blob.pbData);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DataBlob
    {
        public uint cbData;
        public byte* pbData;
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CryptProtectData(DataBlob* pDataIn, string? szDataDescr, DataBlob* pOptionalEntropy,
        nint pvReserved, nint pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CryptUnprotectData(DataBlob* pDataIn, nint ppszDataDescr, DataBlob* pOptionalEntropy,
        nint pvReserved, nint pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint hMem);
}

/// <summary>
/// Respaldo para macOS y Linux mientras llega la integración con Llavero y libsecret:
/// el secreto queda en un archivo con permisos 0600 dentro de un directorio 0700.
/// </summary>
public sealed class FilePermissionSecretProtector : ISecretProtector
{
    private static readonly byte[] Magic = "ATQ0"u8.ToArray();

    public bool IsHardwareOrOsBacked => false;

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var result = new byte[Magic.Length + plaintext.Length];
        Magic.CopyTo(result, 0);
        plaintext.CopyTo(result.AsSpan(Magic.Length));
        return result;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        if (protectedData.Length < Magic.Length || !protectedData[..Magic.Length].SequenceEqual(Magic))
            throw new CryptographicException("Formato de secreto desconocido.");
        return protectedData[Magic.Length..].ToArray();
    }
}

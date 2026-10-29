using System.Net;
using AlToque.Core.Security;

namespace AlToque.Core.Discovery;

/// <summary>
/// Código de conexión para redes que bloquean el multicast, por ejemplo <c>K7Q2-9XM4</c>.
/// Codifica la IPv4 y unos bits de la huella (para detectar errores de tipeo); el puerto se añade si no es el
/// predeterminado. <b>No es un mecanismo de seguridad</b>: la identidad se verifica igual que siempre.
/// </summary>
public static class ConnectionCode
{
    public sealed record Parsed(IPAddress Address, int Port, uint Check, int CheckBits)
    {
        /// <summary>¿La huella del equipo al que nos conectamos coincide con la del código?</summary>
        public bool MatchesFingerprint(ReadOnlySpan<byte> fingerprint) => CheckValue(fingerprint, CheckBits) == Check;
    }

    public static string Create(IPAddress address, int port, ReadOnlySpan<byte> fingerprint)
    {
        var ip = address.MapToIPv4().GetAddressBytes();
        ulong ipValue = (uint)(ip[0] << 24 | ip[1] << 16 | ip[2] << 8 | ip[3]);
        string raw;
        if (port == AppInfo.DefaultTcpPort)
        {
            // 40 bits = 32 de IP + 8 de huella.
            raw = CrockfordBase32.EncodeBits(ipValue << 8 | CheckValue(fingerprint, 8), 40);
        }
        else
        {
            // 60 bits = 32 de IP + 16 de puerto + 12 de huella.
            raw = CrockfordBase32.EncodeBits(ipValue << 28 | (ulong)(uint)port << 12 | CheckValue(fingerprint, 12), 60);
        }
        return string.Join('-', Enumerable.Range(0, raw.Length / 4).Select(i => raw.Substring(i * 4, 4)));
    }

    public static Parsed? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !CrockfordBase32.TryDecode(text.Trim(), out var value, out var chars))
            return null;
        switch (chars)
        {
            case 8:
            {
                var ip = (uint)(value >> 8);
                return new Parsed(ToAddress(ip), AppInfo.DefaultTcpPort, (uint)(value & 0xFF), 8);
            }
            case 12:
            {
                var ip = (uint)(value >> 28);
                var port = (int)((value >> 12) & 0xFFFF);
                if (port is < 1 or > 65535)
                    return null;
                return new Parsed(ToAddress(ip), port, (uint)(value & 0xFFF), 12);
            }
            default:
                return null;
        }
    }

    private static IPAddress ToAddress(uint ip) =>
        new([(byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip]);

    private static uint CheckValue(ReadOnlySpan<byte> fingerprint, int bits)
    {
        var v = (uint)(fingerprint[0] << 8 | fingerprint[1]);
        return v >> (16 - bits);
    }
}

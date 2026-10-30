using System.Security.Cryptography;
using System.Text;

namespace AlToque.Core.Security;

/// <summary>
/// Huella visual derivada del deviceId: un color estable y una secuencia corta de símbolos.
/// Ayuda a notar a un impostor de un vistazo ("Andrés siempre es naranja").
/// </summary>
public static class IdentityVisuals
{
    private static readonly string[] Symbols =
        ["●", "▲", "■", "◆", "★", "♥", "♣", "♠", "♪", "☀", "☾", "✚", "✿", "☂", "⚑", "✈"];

    private static byte[] Hash(string deviceId) => SHA256.HashData(Encoding.UTF8.GetBytes("altoque-visual:" + deviceId));

    /// <summary>
    /// Color en formato <c>#RRGGBB</c>, con luminosidad suficiente para texto blanco encima. Se saltan los tonos
    /// 225°–260° para que ningún compañero se confunda con el índigo de la marca.
    /// </summary>
    public static string ColorHex(string deviceId)
    {
        var h = Hash(deviceId);
        double hue = ((h[0] << 8) | h[1]) % 325;
        if (hue >= 225)
            hue += 35;
        double saturation = 0.55 + h[2] / 255.0 * 0.2;
        double lightness = 0.40 + h[3] / 255.0 * 0.08;
        var (r, g, b) = HslToRgb(hue, saturation, lightness);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>Cuatro símbolos, por ejemplo "★ ♪ ▲ ☂".</summary>
    public static string SymbolSequence(string deviceId)
    {
        var h = Hash(deviceId);
        var sb = new StringBuilder();
        for (int i = 0; i < 4; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(Symbols[h[8 + i] & 15]);
        }
        return sb.ToString();
    }

    /// <summary>Forma legible y corta del deviceId, por ejemplo "MZXW-6YTB".</summary>
    public static string ShortCode(string deviceId)
    {
        var upper = deviceId.ToUpperInvariant();
        return upper.Length >= 8 ? upper[..4] + "-" + upper[4..8] : upper;
    }

    public static string Initial(string alias)
    {
        foreach (var rune in alias.Trim().EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
                return rune.ToString().ToUpperInvariant();
        }
        return "?";
    }

    private static (int R, int G, int B) HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return ((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
}

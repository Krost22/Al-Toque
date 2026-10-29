using System.Text;

namespace AlToque.Core.Security;

/// <summary>Base32 sin relleno. Minúsculas RFC 4648 para identificadores.</summary>
public static class Base32
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static bool IsValid(string? value, int expectedLength)
    {
        if (value is null || value.Length != expectedLength)
            return false;
        foreach (var c in value)
        {
            if (Alphabet.IndexOf(c) < 0)
                return false;
        }
        return true;
    }
}

/// <summary>
/// Base32 de Crockford para códigos que la gente escribe a mano: sin I, L, O ni U,
/// y al decodificar acepta minúsculas y confusiones típicas (O→0, I/L→1).
/// </summary>
public static class CrockfordBase32
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string EncodeBits(ulong value, int bitCount)
    {
        var chars = new char[(bitCount + 4) / 5];
        for (int i = chars.Length - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }
        return new string(chars);
    }

    public static bool TryDecode(string text, out ulong value, out int charCount)
    {
        value = 0;
        charCount = 0;
        foreach (var raw in text)
        {
            if (raw is '-' or ' ' or '_')
                continue;
            var c = char.ToUpperInvariant(raw) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            };
            var idx = Alphabet.IndexOf(c);
            if (idx < 0 || charCount >= 12)
                return false;
            value = (value << 5) | (uint)idx;
            charCount++;
        }
        return charCount > 0;
    }
}

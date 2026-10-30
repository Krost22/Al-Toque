using Avalonia.Media;

namespace AlToque.Controls;

/// <summary>Operaciones de color para el sombreado de los personajes y los tonos derivados de la identidad.</summary>
public static class ColorUtil
{
    public static Color Hex(string hex)
    {
        if (Color.TryParse(hex, out var c))
            return c;
        return Color.FromRgb(0x5B, 0x5B, 0xF0);
    }

    public static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), c.R, c.G, c.B);

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t),
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>Sube la luminosidad en HSL (amount en 0..1, absoluto).</summary>
    public static Color Lighten(Color c, double amount)
    {
        var (h, s, l) = ToHsl(c);
        return FromHsl(h, s, Math.Clamp(l + amount, 0, 1), c.A);
    }

    public static Color Darken(Color c, double amount) => Lighten(c, -amount);

    /// <summary>Tono desplazado, para sombras con algo de color en lugar de gris sucio.</summary>
    public static Color ShadowOf(Color background, double alpha)
    {
        var (h, s, l) = ToHsl(background);
        return FromHsl(h, Math.Min(1, s + 0.1), Math.Max(0, l * 0.35), (byte)Math.Round(alpha * 255));
    }

    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        if (max - min > 1e-9)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r)
                h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g)
                h = (b - r) / d + 2;
            else
                h = (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    public static Color FromHsl(double h, double s, double l, byte a = 255)
    {
        h = ((h % 360) + 360) % 360;
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
        return Color.FromArgb(a, ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
}

using System.Text;
using AlToque.Core.Storage;

namespace AlToque.Core.Transfer;

/// <summary>
/// Saneamiento de rutas relativas recibidas (sección 12.6). Rechaza todo lo que pueda escapar de la carpeta de
/// destino o engañar al usuario; elimina los caracteres de control bidireccional y lo marca con advertencia.
/// </summary>
public static class NameSanitizer
{
    public const int MaxComponentBytes = 255;
    public const int MaxDepth = 64;
    public const int MaxPathChars = 4096;

    private static readonly HashSet<string> WindowsReserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    // En Android el almacenamiento compartido tiene las mismas restricciones de nombres que FAT/Windows.
    public static bool UseWindowsRulesByDefault => OperatingSystem.IsWindows() || OperatingSystem.IsAndroid();

    public readonly record struct Result(string[] Components, bool HadHiddenCharacters);

    /// <summary>Valida una ruta relativa con "/" como separador.</summary>
    /// <param name="error">Motivo legible del rechazo.</param>
    public static bool TryValidate(string? relativePath, bool windowsRules, out Result result, out string? error)
    {
        result = default;
        error = null;
        if (string.IsNullOrEmpty(relativePath))
            return Fail("ruta vacía", out error);
        if (relativePath.Length > MaxPathChars)
            return Fail("ruta demasiado larga", out error);

        string normalized;
        try
        {
            normalized = relativePath.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return Fail("Unicode inválido", out error);
        }

        if (normalized.Contains('\\'))
            return Fail("separador no permitido", out error);
        if (normalized.StartsWith('/'))
            return Fail("ruta absoluta", out error);

        var parts = normalized.Split('/');
        if (parts.Length > MaxDepth)
            return Fail("demasiados niveles de carpetas", out error);

        var hidden = false;
        var components = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!TryCleanComponent(parts[i], windowsRules, out var clean, out var hadHidden, out error))
                return false;
            hidden |= hadHidden;
            components[i] = clean;
        }
        result = new Result(components, hidden);
        return true;
    }

    private static bool TryCleanComponent(string part, bool windowsRules, out string clean, out bool hadHidden, out string? error)
    {
        clean = "";
        hadHidden = false;
        error = null;
        if (part.Length == 0)
            return Fail("componente vacío", out error);

        var sb = new StringBuilder(part.Length);
        foreach (var rune in part.EnumerateRunes())
        {
            var cp = rune.Value;
            if (cp < 0x20 || cp is >= 0x7F and <= 0x9F)
                return Fail("caracteres de control", out error);
            if (NameSanitizerRules.IsBidiControl(cp))
            {
                hadHidden = true;
                continue;
            }
            if (windowsRules && cp is ':' or '<' or '>' or '"' or '|' or '?' or '*')
                return Fail($"carácter no permitido «{(char)cp}»", out error);
            sb.Append(rune.ToString());
        }
        clean = sb.ToString();

        if (clean.Length == 0)
            return Fail("nombre vacío", out error);
        if (clean is "." or "..")
            return Fail("componente relativo", out error);
        if (Encoding.UTF8.GetByteCount(clean) > MaxComponentBytes)
            return Fail("nombre demasiado largo", out error);
        if (clean.EndsWith(AppInfo.PartExtension, StringComparison.OrdinalIgnoreCase))
            return Fail("extensión reservada", out error);
        if (windowsRules)
        {
            if (clean.EndsWith('.') || clean.EndsWith(' '))
                return Fail("termina en punto o espacio", out error);
            var dot = clean.IndexOf('.');
            var stem = (dot >= 0 ? clean[..dot] : clean).TrimEnd(' ');
            if (WindowsReserved.Contains(stem))
                return Fail($"nombre reservado «{stem}»", out error);
        }
        return true;
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }

    /// <summary>Une los componentes a la raíz y comprueba que la ruta canónica queda dentro de ella.</summary>
    public static string Combine(string root, IReadOnlyList<string> components)
    {
        var fullRoot = Path.GetFullPath(root);
        var combined = Path.GetFullPath(Path.Combine([fullRoot, .. components]));
        if (!IsInside(fullRoot, combined))
            throw new InvalidOperationException("La ruta queda fuera de la carpeta de destino.");
        return combined;
    }

    public static bool IsInside(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsAndroid() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var c = Path.GetFullPath(candidate);
        return c.Length > r.Length + 1 && c.StartsWith(r, comparison) && c[r.Length] == Path.DirectorySeparatorChar;
    }
}

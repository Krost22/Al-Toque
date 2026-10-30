using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AlToque.Core.Storage;

public static partial class KnownFolders
{
    public static string DefaultReceiveFolder() => Path.Combine(Downloads(), AppInfo.Name);

    public static string Downloads()
    {
        if (OperatingSystem.IsWindows())
        {
            var path = WindowsDownloads();
            if (!string.IsNullOrEmpty(path))
                return path;
        }
        else if (OperatingSystem.IsLinux())
        {
            var xdg = XdgDownloadDir();
            if (!string.IsNullOrEmpty(xdg))
                return xdg;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private static string? XdgDownloadDir()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".config");
            var file = Path.Combine(configHome, "user-dirs.dirs");
            if (!File.Exists(file))
                return null;
            foreach (var line in File.ReadLines(file))
            {
                if (!line.StartsWith("XDG_DOWNLOAD_DIR=", StringComparison.Ordinal))
                    continue;
                var value = line["XDG_DOWNLOAD_DIR=".Length..].Trim('"');
                return value.Replace("$HOME", home, StringComparison.Ordinal);
            }
        }
        catch (IOException)
        {
        }
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsDownloads()
    {
        var folderDownloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(in folderDownloads, 0, 0, out var ptr) != 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    [LibraryImport("shell32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);
}

public static class TextLimits
{
    public const int MaxAliasLength = 40;
    public const int MaxMessageLength = 500;

    /// <summary>Limpia un texto corto que viaja por la red: sin controles ni caracteres bidireccionales.</summary>
    public static string? CleanShortText(string? text, int maxLength)
    {
        if (text is null)
            return null;
        var sb = new System.Text.StringBuilder(Math.Min(text.Length, maxLength));
        foreach (var rune in text.Normalize(System.Text.NormalizationForm.FormC).EnumerateRunes())
        {
            if (System.Text.Rune.IsControl(rune) && rune.Value != '\n')
                continue;
            if (NameSanitizerRules.IsBidiControl(rune.Value) || NameSanitizerRules.IsInvisibleFormat(rune.Value))
                continue;
            if (sb.Length + rune.Utf16SequenceLength > maxLength)
                break;
            sb.Append(rune.ToString());
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    public static string? CleanAlias(string? alias) => CleanShortText(alias?.Replace('\n', ' '), MaxAliasLength);

    /// <summary>Id de avatar: de 2 a 16 letras ASCII minúsculas; cualquier otra cosa se descarta.</summary>
    public static string? CleanAvatar(string? avatar) =>
        avatar is { Length: >= 2 and <= 16 } && avatar.All(c => c is >= 'a' and <= 'z') ? avatar : null;
}

/// <summary>Conjuntos de caracteres peligrosos compartidos por el saneamiento de nombres y textos.</summary>
public static class NameSanitizerRules
{
    public static bool IsBidiControl(int cp) =>
        cp is >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069 or 0x200E or 0x200F or 0x061C;

    /// <summary>Caracteres de ancho cero o de formato que permiten nombres engañosos.</summary>
    public static bool IsInvisibleFormat(int cp) =>
        cp is 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF or 0x00AD or >= 0xFFF9 and <= 0xFFFB;
}

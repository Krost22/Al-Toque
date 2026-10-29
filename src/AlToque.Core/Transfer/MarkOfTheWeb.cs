using System.Text;

namespace AlToque.Core.Transfer;

/// <summary>
/// Marca de origen de los archivos recibidos. En Windows es el flujo alternativo <c>Zone.Identifier</c> con
/// <c>ZoneId=3</c> (Internet), que activa SmartScreen y la Vista protegida de Office.
/// </summary>
public static class MarkOfTheWeb
{
    /// <summary>¿El sistema de archivos de esta carpeta admite la marca? (FAT32 y exFAT no.)</summary>
    public static bool IsSupported(string folder)
    {
        if (!OperatingSystem.IsWindows())
            return true;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root))
                return false;
            // Carpeta de red: no se puede saber el sistema de archivos del servidor; se intenta igual.
            if (root.StartsWith(@"\\", StringComparison.Ordinal))
                return true;
            var format = new DriveInfo(root).DriveFormat;
            return format.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || format.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Apply(string path, string originTag)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            var content = new StringBuilder()
                .Append("[ZoneTransfer]\r\n")
                .Append("ZoneId=3\r\n")
                .Append("ReferrerUrl=altoque://").Append(originTag).Append("\r\n")
                .ToString();
            File.WriteAllText(path + ":Zone.Identifier", content, Encoding.ASCII);
        }
        catch (IOException)
        {
            // Sistema de archivos sin flujos alternativos: se avisa al usuario desde ajustes.
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}

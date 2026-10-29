namespace AlToque.Core.Transfer;

public enum FileCategory
{
    Other,
    Program,
    Script,
    Shortcut,
    DiskImage,
    Document,
    Image,
    Video,
    Audio,
    Archive,
    Text,
}

/// <summary>
/// Clasificación por extensión real (la última). El receptor la calcula por su cuenta: nunca confía en el
/// indicador "ejecutable" que manda el emisor.
/// </summary>
public static class FileKinds
{
    private static readonly Dictionary<string, FileCategory> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        // Programas e instaladores
        ["exe"] = FileCategory.Program, ["msi"] = FileCategory.Program, ["msix"] = FileCategory.Program,
        ["msixbundle"] = FileCategory.Program, ["appx"] = FileCategory.Program, ["appxbundle"] = FileCategory.Program,
        ["msp"] = FileCategory.Program, ["com"] = FileCategory.Program, ["scr"] = FileCategory.Program,
        ["pif"] = FileCategory.Program, ["cpl"] = FileCategory.Program, ["dll"] = FileCategory.Program,
        ["sys"] = FileCategory.Program, ["jar"] = FileCategory.Program, ["application"] = FileCategory.Program,
        ["gadget"] = FileCategory.Program, ["app"] = FileCategory.Program, ["apk"] = FileCategory.Program,
        ["deb"] = FileCategory.Program, ["rpm"] = FileCategory.Program, ["appimage"] = FileCategory.Program,
        ["pkg"] = FileCategory.Program, ["msc"] = FileCategory.Program,
        // Scripts
        ["bat"] = FileCategory.Script, ["cmd"] = FileCategory.Script, ["ps1"] = FileCategory.Script,
        ["psm1"] = FileCategory.Script, ["psd1"] = FileCategory.Script, ["vbs"] = FileCategory.Script,
        ["vbe"] = FileCategory.Script, ["js"] = FileCategory.Script, ["jse"] = FileCategory.Script,
        ["wsf"] = FileCategory.Script, ["wsh"] = FileCategory.Script, ["hta"] = FileCategory.Script,
        ["reg"] = FileCategory.Script, ["inf"] = FileCategory.Script, ["scf"] = FileCategory.Script,
        ["chm"] = FileCategory.Script, ["sh"] = FileCategory.Script, ["command"] = FileCategory.Script,
        ["py"] = FileCategory.Script, ["pyw"] = FileCategory.Script, ["xll"] = FileCategory.Script,
        ["settingcontent-ms"] = FileCategory.Script, ["library-ms"] = FileCategory.Script,
        // Accesos directos
        ["lnk"] = FileCategory.Shortcut, ["url"] = FileCategory.Shortcut, ["website"] = FileCategory.Shortcut,
        ["desktop"] = FileCategory.Shortcut,
        // Imágenes de disco (históricamente usadas para esquivar Mark of the Web)
        ["iso"] = FileCategory.DiskImage, ["img"] = FileCategory.DiskImage, ["vhd"] = FileCategory.DiskImage,
        ["vhdx"] = FileCategory.DiskImage, ["dmg"] = FileCategory.DiskImage,
        // Comunes
        ["pdf"] = FileCategory.Document, ["doc"] = FileCategory.Document, ["docx"] = FileCategory.Document,
        ["xls"] = FileCategory.Document, ["xlsx"] = FileCategory.Document, ["ppt"] = FileCategory.Document,
        ["pptx"] = FileCategory.Document, ["odt"] = FileCategory.Document, ["ods"] = FileCategory.Document,
        ["odp"] = FileCategory.Document, ["rtf"] = FileCategory.Document, ["docm"] = FileCategory.Document,
        ["xlsm"] = FileCategory.Document, ["pptm"] = FileCategory.Document,
        ["jpg"] = FileCategory.Image, ["jpeg"] = FileCategory.Image, ["png"] = FileCategory.Image,
        ["gif"] = FileCategory.Image, ["webp"] = FileCategory.Image, ["bmp"] = FileCategory.Image,
        ["heic"] = FileCategory.Image, ["svg"] = FileCategory.Image, ["tif"] = FileCategory.Image,
        ["tiff"] = FileCategory.Image, ["psd"] = FileCategory.Image, ["raw"] = FileCategory.Image,
        ["mp4"] = FileCategory.Video, ["mov"] = FileCategory.Video, ["mkv"] = FileCategory.Video,
        ["avi"] = FileCategory.Video, ["webm"] = FileCategory.Video, ["wmv"] = FileCategory.Video,
        ["mp3"] = FileCategory.Audio, ["wav"] = FileCategory.Audio, ["flac"] = FileCategory.Audio,
        ["ogg"] = FileCategory.Audio, ["m4a"] = FileCategory.Audio, ["aac"] = FileCategory.Audio,
        ["zip"] = FileCategory.Archive, ["7z"] = FileCategory.Archive, ["rar"] = FileCategory.Archive,
        ["tar"] = FileCategory.Archive, ["gz"] = FileCategory.Archive, ["xz"] = FileCategory.Archive,
        ["txt"] = FileCategory.Text, ["md"] = FileCategory.Text, ["csv"] = FileCategory.Text,
        ["json"] = FileCategory.Text, ["xml"] = FileCategory.Text, ["log"] = FileCategory.Text,
    };

    private static readonly Dictionary<string, string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "application/pdf", ["txt"] = "text/plain", ["md"] = "text/markdown", ["csv"] = "text/csv",
        ["json"] = "application/json", ["xml"] = "application/xml", ["html"] = "text/html",
        ["jpg"] = "image/jpeg", ["jpeg"] = "image/jpeg", ["png"] = "image/png", ["gif"] = "image/gif",
        ["webp"] = "image/webp", ["svg"] = "image/svg+xml", ["mp4"] = "video/mp4", ["mov"] = "video/quicktime",
        ["mkv"] = "video/x-matroska", ["webm"] = "video/webm", ["mp3"] = "audio/mpeg", ["wav"] = "audio/wav",
        ["zip"] = "application/zip", ["7z"] = "application/x-7z-compressed", ["exe"] = "application/vnd.microsoft.portable-executable",
        ["apk"] = "application/vnd.android.package-archive", ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    /// <summary>Extensión real, sin punto: la que va después del último punto.</summary>
    public static string Extension(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ext.Length > 1 ? ext[1..] : "";
    }

    public static FileCategory Categorize(string fileName) =>
        Categories.TryGetValue(Extension(fileName), out var c) ? c : FileCategory.Other;

    /// <summary>¿Puede ejecutar código al abrirlo con doble clic?</summary>
    public static bool IsDangerous(string fileName) =>
        Categorize(fileName) is FileCategory.Program or FileCategory.Script or FileCategory.Shortcut or FileCategory.DiskImage;

    public static string? GuessMime(string fileName) => Mimes.TryGetValue(Extension(fileName), out var m) ? m : null;

    /// <summary>
    /// Doble extensión engañosa, como <c>factura.pdf.exe</c>: la penúltima parece inocente y la última es peligrosa.
    /// </summary>
    public static bool HasMisleadingDoubleExtension(string fileName)
    {
        if (!IsDangerous(fileName))
            return false;
        var withoutLast = Path.GetFileNameWithoutExtension(fileName);
        var inner = Categorize(withoutLast);
        return inner is FileCategory.Document or FileCategory.Image or FileCategory.Video or FileCategory.Audio or FileCategory.Text or FileCategory.Archive;
    }
}

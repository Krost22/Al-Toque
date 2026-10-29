using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;

namespace AlToque.Core.Transfer;

public sealed class OutgoingFile
{
    public required string FileId { get; init; }
    public required string LocalPath { get; init; }
    public required string RelativePath { get; init; }
    public required long Size { get; init; }
    public DateTime ModifiedUtc { get; init; }
}

/// <summary>Lista de lo que se va a enviar, construida a partir de rutas locales (archivos y carpetas).</summary>
public sealed class OutgoingManifest
{
    public const int MaxFiles = 100_000;

    private OutgoingManifest(OfferMessage offer, Dictionary<string, OutgoingFile> files)
    {
        Offer = offer;
        Files = files;
    }

    public OfferMessage Offer { get; }
    public IReadOnlyDictionary<string, OutgoingFile> Files { get; }
    public long TotalSize => Offer.TotalSize;
    public int FileCount => Offer.Files.Count;

    /// <summary>Nombre a mostrar: el primer elemento de primer nivel.</summary>
    public string FirstName => Offer.Files.Count > 0
        ? Offer.Files[0].Path.Split('/')[0]
        : Offer.Dirs?.FirstOrDefault()?.Split('/')[0] ?? "";

    /// <exception cref="TransferException">Rutas inexistentes o demasiados archivos.</exception>
    public static OutgoingManifest Build(IReadOnlyList<string> paths, string? message, CancellationToken ct = default)
    {
        var files = new List<OutgoingFile>();
        var emptyDirs = new List<string>();
        var topNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenRoots = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        foreach (var raw in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw.Trim().Trim('"')));
            if (!seenRoots.Add(full))
                continue;

            if (File.Exists(full))
            {
                var name = UniqueTopName(Path.GetFileName(full), topNames);
                AddFile(files, new FileInfo(full), name);
            }
            else if (Directory.Exists(full))
            {
                var dirName = Path.GetFileName(full);
                if (string.IsNullOrEmpty(dirName))
                    dirName = full.TrimEnd(':', '\\', '/').Replace(":", "", StringComparison.Ordinal);
                var name = UniqueTopName(string.IsNullOrEmpty(dirName) ? "Carpeta" : dirName, topNames);
                WalkDirectory(new DirectoryInfo(full), name, files, emptyDirs, ct);
            }
            else
            {
                throw new TransferException(ErrorCodes.Io, "No encuentro: " + full);
            }
        }

        if (files.Count == 0 && emptyDirs.Count == 0)
            throw new TransferException(ErrorCodes.Io, "No hay nada que enviar.");

        var offerFiles = files.Select(f => new OfferFile
        {
            FileId = f.FileId,
            Path = f.RelativePath,
            Size = f.Size,
            Modified = ToOffset(f.ModifiedUtc),
            Created = ToOffset(SafeCreation(f.LocalPath)),
            Mime = FileKinds.GuessMime(f.RelativePath),
            Executable = FileKinds.IsDangerous(f.RelativePath),
        }).ToList();

        var offer = new OfferMessage
        {
            SessionId = Base32.Encode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
            Files = offerFiles,
            Dirs = emptyDirs.Count > 0 ? emptyDirs : null,
            TotalSize = files.Sum(f => f.Size),
            Message = TextLimits.CleanShortText(message, TextLimits.MaxMessageLength),
        };
        return new OutgoingManifest(offer, files.ToDictionary(f => f.FileId));
    }

    private static string UniqueTopName(string name, HashSet<string> taken)
    {
        if (taken.Add(name))
            return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (taken.Add(candidate))
                return candidate;
        }
    }

    private static void AddFile(List<OutgoingFile> files, FileInfo info, string relative)
    {
        if (files.Count >= MaxFiles)
            throw new TransferException(ErrorCodes.TooLarge, $"Son más de {MaxFiles:N0} archivos. Comprime la carpeta y envía el .zip.");
        if (info.Name.EndsWith(AppInfo.PartExtension, StringComparison.OrdinalIgnoreCase))
            return;
        files.Add(new OutgoingFile
        {
            FileId = (files.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            LocalPath = info.FullName,
            RelativePath = relative,
            Size = info.Length,
            ModifiedUtc = info.LastWriteTimeUtc,
        });
    }

    private static void WalkDirectory(DirectoryInfo root, string rootName, List<OutgoingFile> files, List<string> emptyDirs, CancellationToken ct)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };
        var stack = new Stack<(DirectoryInfo Dir, string Rel, int Depth)>();
        stack.Push((root, rootName, 1));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, rel, depth) = stack.Pop();
            if (depth > NameSanitizer.MaxDepth - 1)
                throw new TransferException(ErrorCodes.TooLarge, "La carpeta tiene demasiados niveles.");
            var any = false;
            foreach (var entry in dir.EnumerateFileSystemInfos("*", options))
            {
                any = true;
                var childRel = rel + "/" + entry.Name;
                if (entry is DirectoryInfo sub)
                {
                    // No se siguen enlaces simbólicos ni junctions (evita bucles y salir de la carpeta).
                    if (sub.LinkTarget is not null)
                        continue;
                    stack.Push((sub, childRel, depth + 1));
                }
                else if (entry is FileInfo file)
                {
                    if (file.LinkTarget is not null)
                        continue;
                    AddFile(files, file, childRel);
                }
            }
            if (!any)
                emptyDirs.Add(rel);
        }
    }

    private static DateTime SafeCreation(string path)
    {
        try
        {
            return File.GetCreationTimeUtc(path);
        }
        catch (IOException)
        {
            return default;
        }
    }

    private static DateTimeOffset? ToOffset(DateTime utc) =>
        utc.Year is < 1980 or > 2200 ? null : new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}

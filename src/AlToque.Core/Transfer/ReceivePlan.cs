using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;

namespace AlToque.Core.Transfer;

public sealed class PlannedFile
{
    public required OfferFile Offer { get; init; }

    /// <summary>Ruta relativa ya saneada y, si hizo falta, renombrada.</summary>
    public required string[] Components { get; init; }

    public required string FinalPath { get; init; }

    /// <summary>Tenía caracteres ocultos (bidireccionales) que se eliminaron.</summary>
    public bool HadHiddenCharacters { get; init; }

    public bool Dangerous { get; init; }
    public bool MisleadingName { get; init; }

    /// <summary>Ya existe y la política es "omitir".</summary>
    public bool Skip { get; init; }

    public string DisplayPath => string.Join('/', Components);
    public string Name => Components[^1];
}

/// <summary>
/// Valida una oferta y decide dónde iría cada archivo. Toda la oferta se rechaza ante cualquier anomalía:
/// identificadores repetidos, rutas maliciosas o tamaños inconsistentes.
/// </summary>
public sealed class ReceivePlan
{
    public const int MaxFiles = 100_000;
    public const long MaxFileSize = 1L << 50;

    private ReceivePlan(string root, List<PlannedFile> files, List<string[]> dirs, string? message)
    {
        Root = root;
        Files = files;
        Dirs = dirs;
        Message = message;
    }

    public string Root { get; }
    public IReadOnlyList<PlannedFile> Files { get; }
    public IReadOnlyList<string[]> Dirs { get; }
    public string? Message { get; }

    public long AcceptedBytes => Files.Where(f => !f.Skip).Sum(f => f.Offer.Size);
    public long TotalBytes => Files.Sum(f => f.Offer.Size);
    public int AcceptedCount => Files.Count(f => !f.Skip);
    public bool HasDangerous => Files.Any(f => f.Dangerous);
    public bool HasHiddenCharacters => Files.Any(f => f.HadHiddenCharacters);
    public bool HasMisleadingNames => Files.Any(f => f.MisleadingName);

    /// <summary>Carpeta de primer nivel creada por el envío, si todo va dentro de una sola.</summary>
    public string? SingleTopFolder
    {
        get
        {
            var tops = Files.Select(f => f.Components).Concat(Dirs).Select(c => c.Length > 1 ? c[0] : null).Distinct().ToList();
            return tops.Count == 1 && tops[0] is { } t ? Path.Combine(Root, t) : null;
        }
    }

    /// <exception cref="TransferException">Con <see cref="ErrorCodes.BadManifest"/> si la oferta no es válida.</exception>
    public static ReceivePlan Build(OfferMessage offer, string root, ConflictPolicy policy, bool windowsRules)
    {
        if (!Base32.IsValid(offer.SessionId, 26))
            throw Bad("identificador de sesión inválido");
        if (offer.Files.Count > MaxFiles || offer.Dirs?.Count > MaxFiles)
            throw Bad("demasiados archivos");
        if (offer.Files.Count == 0 && (offer.Dirs is null || offer.Dirs.Count == 0))
            throw Bad("oferta vacía");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        var validated = new List<(OfferFile Offer, NameSanitizer.Result Name)>(offer.Files.Count);
        foreach (var f in offer.Files)
        {
            if (!IsValidFileId(f.FileId) || !ids.Add(f.FileId))
                throw Bad("identificador de archivo repetido o inválido");
            if (f.Size is < 0 or > MaxFileSize)
                throw Bad("tamaño inválido");
            total = checked(total + f.Size);
            if (f.Mime is { Length: > 128 })
                throw Bad("tipo MIME inválido");
            if (!NameSanitizer.TryValidate(f.Path, windowsRules, out var name, out var error))
                throw Bad($"nombre no válido ({error}): {Preview(f.Path)}");
            validated.Add((f, name));
        }
        if (total != offer.TotalSize)
            throw Bad("el tamaño total no coincide");

        var dirs = new List<string[]>();
        foreach (var d in offer.Dirs ?? [])
        {
            if (!NameSanitizer.TryValidate(d, windowsRules, out var name, out var error))
                throw Bad($"carpeta no válida ({error}): {Preview(d)}");
            dirs.Add(name.Components);
        }

        // Rutas únicas y sin conflictos archivo/carpeta ("a" como archivo y "a/b" a la vez).
        var comparer = windowsRules || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var filePaths = new HashSet<string>(comparer);
        var dirPaths = new HashSet<string>(comparer);
        foreach (var (_, name) in validated)
        {
            if (!filePaths.Add(string.Join('/', name.Components)))
                throw Bad("hay dos archivos con la misma ruta");
            for (int i = 1; i < name.Components.Length; i++)
                dirPaths.Add(string.Join('/', name.Components[..i]));
        }
        foreach (var d in dirs)
        {
            for (int i = 1; i <= d.Length; i++)
                dirPaths.Add(string.Join('/', d[..i]));
        }
        if (filePaths.Overlaps(dirPaths))
            throw Bad("un archivo y una carpeta comparten nombre");

        var fullRoot = Path.GetFullPath(root);
        var renames = policy == ConflictPolicy.Rename
            ? PlanTopLevelRenames(fullRoot, validated.Select(v => v.Name.Components).Concat(dirs), comparer)
            : new Dictionary<string, string>(comparer);

        var planned = new List<PlannedFile>(validated.Count);
        foreach (var (f, name) in validated)
        {
            var components = ApplyRename(name.Components, renames);
            var finalPath = NameSanitizer.Combine(fullRoot, components);
            var exists = File.Exists(finalPath) || Directory.Exists(finalPath);
            planned.Add(new PlannedFile
            {
                Offer = f,
                Components = components,
                FinalPath = finalPath,
                HadHiddenCharacters = name.HadHiddenCharacters,
                Dangerous = FileKinds.IsDangerous(components[^1]),
                MisleadingName = name.HadHiddenCharacters || FileKinds.HasMisleadingDoubleExtension(components[^1]),
                Skip = policy == ConflictPolicy.Skip && exists,
            });
        }
        var plannedDirs = dirs.Select(d => ApplyRename(d, renames)).ToList();
        foreach (var d in plannedDirs)
            NameSanitizer.Combine(fullRoot, d);

        return new ReceivePlan(fullRoot, planned, plannedDirs, TextLimits.CleanShortText(offer.Message, TextLimits.MaxMessageLength));
    }

    /// <summary>
    /// Si "Proyecto" o "foto.jpg" ya existen en destino, el elemento de primer nivel pasa a "Proyecto (1)" o
    /// "foto (1).jpg"; así un envío repetido nunca pisa nada.
    /// </summary>
    private static Dictionary<string, string> PlanTopLevelRenames(string root, IEnumerable<string[]> all, StringComparer comparer)
    {
        var tops = all.Select(c => (Name: c[0], IsFile: c.Length == 1)).DistinctBy(t => t.Name, comparer).ToList();
        var taken = new HashSet<string>(tops.Select(t => t.Name), comparer);
        var result = new Dictionary<string, string>(comparer);
        foreach (var (name, isFile) in tops)
        {
            if (!ExistsOnDisk(root, name))
                continue;
            var stem = isFile ? Path.GetFileNameWithoutExtension(name) : name;
            var ext = isFile ? Path.GetExtension(name) : "";
            for (int i = 1; i < 10_000; i++)
            {
                var candidate = $"{stem} ({i}){ext}";
                if (!taken.Contains(candidate) && !ExistsOnDisk(root, candidate))
                {
                    taken.Add(candidate);
                    result[name] = candidate;
                    break;
                }
            }
        }
        return result;
    }

    private static bool ExistsOnDisk(string root, string name)
    {
        var p = Path.Combine(root, name);
        return File.Exists(p) || Directory.Exists(p) || File.Exists(p + AppInfo.PartExtension);
    }

    private static string[] ApplyRename(string[] components, Dictionary<string, string> renames)
    {
        if (!renames.TryGetValue(components[0], out var renamed))
            return components;
        var copy = (string[])components.Clone();
        copy[0] = renamed;
        return copy;
    }

    public static bool IsValidFileId(string? id)
    {
        if (id is null || id.Length is 0 or > 16)
            return false;
        foreach (var c in id)
        {
            if (c is not (>= 'a' and <= 'z' or >= '0' and <= '9'))
                return false;
        }
        return true;
    }

    private static string Preview(string? path)
    {
        if (path is null)
            return "";
        var clean = new string(path.Where(c => !char.IsControl(c)).Take(80).ToArray());
        return clean;
    }

    private static TransferException Bad(string detail) => new(ErrorCodes.BadManifest, detail);
}

using AlToque.Core.Agent;
using Avalonia.Platform.Storage;

namespace AlToque.Mobile;

/// <summary>
/// Lo que se elige en el teléfono llega como contenido (content://), no como ruta. El agente envía rutas, así que
/// se copia a una carpeta temporal propia que se borra al terminar el envío.
/// </summary>
public static class OutgoingFiles
{
    public static string Root(string cacheDirectory) => Path.Combine(cacheDirectory, "outgoing");

    /// <summary>Carpeta nueva para una tanda de copias.</summary>
    public static string NewBatch(string cacheDirectory)
    {
        var dir = Path.Combine(Root(cacheDirectory), Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Rutas listas para enviar y la carpeta de copias que hay que borrar después (null si no hizo falta copiar).</summary>
    public static async Task<(IReadOnlyList<string> Paths, string? CopiesDir)> MaterializeAsync(IReadOnlyList<IStorageFile> files, string cacheDirectory)
    {
        var local = files.Select(f => f.TryGetLocalPath()).ToList();
        if (local.All(p => p is not null && File.Exists(p)))
            return (local!, null);

        var dir = NewBatch(cacheDirectory);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        try
        {
            foreach (var file in files)
            {
                var path = Path.Combine(dir, UniqueName(SafeName(file.Name), used));
                await using (var source = await file.OpenReadAsync().ConfigureAwait(false))
                await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                    await source.CopyToAsync(target).ConfigureAwait(false);
                try
                {
                    if ((await file.GetBasicPropertiesAsync().ConfigureAwait(false)).DateModified is { } modified)
                        File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                }
                paths.Add(path);
            }
        }
        catch
        {
            Delete(dir);
            throw;
        }
        return (paths, dir);
    }

    /// <summary>Copia un archivo compartido desde otra app (Android entrega un flujo y un nombre).</summary>
    public static async Task<string> CopyAsync(Stream source, string? name, string batchDir, HashSet<string> used)
    {
        var path = Path.Combine(batchDir, UniqueName(SafeName(name), used));
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        await source.CopyToAsync(target).ConfigureAwait(false);
        return path;
    }

    public static void Delete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Write("No se pudo borrar la copia temporal: " + ex.Message);
        }
    }

    /// <summary>Al arrancar no queda nada pendiente: las copias de la sesión anterior sobran.</summary>
    public static void Clean(string cacheDirectory) => Delete(Root(cacheDirectory));

    private static string SafeName(string? name)
    {
        var clean = Path.GetFileName((name ?? "").Replace('\\', '/').Split('/').Last()).Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            clean = clean.Replace(c, '_');
        return clean is "" or "." or ".." ? "archivo" : clean;
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        var candidate = name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int i = 2; !used.Add(candidate); i++)
            candidate = $"{stem} ({i}){ext}";
        return candidate;
    }
}

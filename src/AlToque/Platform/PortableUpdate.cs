using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using AlToque.Core;
using AlToque.Core.Agent;

namespace AlToque.Platform;

/// <summary>
/// Actualizar una copia que no se instaló con el instalador: la carpeta portable (Windows .zip, Linux .tar.gz) o la
/// app de macOS (.app). Baja el paquete de la versión nueva, lo deja listo junto a la app y, al reiniciar o al cerrar,
/// cambia los archivos: los viejos quedan como ".old" (Windows deja renombrar un programa abierto) y se borran en el
/// siguiente arranque. Si algo falla a medio camino, se deshace y la versión anterior sigue funcionando.
/// </summary>
internal sealed class PortableUpdate
{
    private const string StagingName = ".altoque-update";
    private const string OldSuffix = ".old";

    private readonly bool _bundle;

    /// <summary>La carpeta portable, o la .app de macOS.</summary>
    private readonly string _target;

    private readonly string _staging;
    private string? _ready;

    private PortableUpdate(bool bundle, string target)
    {
        _bundle = bundle;
        _target = target;
        _staging = bundle ? Path.Combine(Path.GetDirectoryName(target)!, StagingName) : Path.Combine(target, StagingName);
    }

    /// <summary>¿Esta copia se puede actualizar sola? Null si no (instalada por el sistema, sin permiso de escritura…).</summary>
    public static PortableUpdate? Detect()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            PortableUpdate? update = null;
            if (OperatingSystem.IsMacOS())
            {
                // …/Al Toque.app/Contents/MacOS
                var app = Path.GetFullPath(Path.Combine(baseDir, "..", ".."));
                if (app.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && CanWrite(Path.GetDirectoryName(app)!))
                    update = new PortableUpdate(bundle: true, app);
            }
            else if ((OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                && File.Exists(Path.Combine(baseDir, AppPaths.PortableFlagName)) && CanWrite(baseDir))
            {
                update = new PortableUpdate(bundle: false, baseDir);
            }
            update?.CleanLeftovers();
            return update;
        }
        catch (Exception ex)
        {
            AgentLog.Write("Sin actualización de la copia portable: " + ex.Message);
            return null;
        }
    }

    /// <summary>El paquete de esta versión para este sistema, si la versión lo trae.</summary>
    public string? AssetFor(ReleaseCheck.Release release)
    {
        if (release.Assets is null)
            return null;
        var rid = Rid();
        var name = _bundle ? $"AlToque-{release.Version}-{rid}.zip"
            : OperatingSystem.IsWindows() ? $"AlToque-{release.Version}-{rid}-portable.zip"
            : $"AlToque-{release.Version}-{rid}-portable.tar.gz";
        return release.Assets.TryGetValue(name, out var url) ? url : null;
    }

    /// <summary>Descarga y descomprime la versión nueva junto a la app (todavía no cambia nada).</summary>
    public async Task DownloadAsync(string url, string version, Action<int> progress, CancellationToken ct)
    {
        _ready = null;
        DeleteQuietly(_staging);
        Directory.CreateDirectory(_staging);
        var package = Path.Combine(_staging, url.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? "paquete.tar.gz" : "paquete.zip");

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
        {
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AlToque", AppInfo.Version));
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = File.Create(package);
            var buffer = new byte[81920];
            long done = 0;
            int read, last = -1;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                var percent = total > 0 ? (int)(done * 100 / total) : 0;
                if (percent != last)
                    progress(last = percent);
            }
        }

        var unpacked = Path.Combine(_staging, "nuevo");
        Directory.CreateDirectory(unpacked);
        await Task.Run(() =>
        {
            if (package.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                using var file = File.OpenRead(package);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzip, unpacked, overwriteFiles: true);
            }
            else
            {
                ZipFile.ExtractToDirectory(package, unpacked, overwriteFiles: true);
            }
        }, ct).ConfigureAwait(false);
        File.Delete(package);

        var root = _bundle
            ? Directory.EnumerateDirectories(unpacked, "*.app", SearchOption.AllDirectories).FirstOrDefault()
            : SingleFolder(unpacked);
        var exe = root is null ? null : ExecutableIn(root, _bundle);
        if (root is null || exe is null || !File.Exists(exe))
            throw new InvalidDataException("El paquete de la versión " + version + " no trae Al Toque.");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        _ready = root;
    }

    /// <summary>Pone la versión descargada en lugar de la actual. Devuelve false si no había nada listo o si falló.</summary>
    public bool Apply()
    {
        if (_ready is not { } source || !Directory.Exists(source))
            return false;
        try
        {
            if (_bundle)
                ApplyBundle(source);
            else
                ApplyFolder(source);
            _ready = null;
            DeleteQuietly(_staging);
            AgentLog.Write("Copia portable actualizada.");
            return true;
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudo cambiar a la versión nueva: " + ex.Message);
            return false;
        }
    }

    /// <summary>Abre la versión nueva (que espera a que esta termine de cerrarse).</summary>
    public void Restart(IEnumerable<string> args)
    {
        var all = args.Append("--restart").ToList();
        var start = _bundle
            ? new ProcessStartInfo("open") { ArgumentList = { "-n", _target, "--args" } }
            : new ProcessStartInfo(ExecutableIn(_target, bundle: false)!) { WorkingDirectory = _target };
        start.UseShellExecute = false;
        foreach (var arg in all)
            start.ArgumentList.Add(arg);
        Process.Start(start)?.Dispose();
    }

    private void ApplyFolder(string source)
    {
        var moved = new List<(string Dest, string? Backup)>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(_target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                string? backup = null;
                if (File.Exists(dest))
                {
                    backup = dest + OldSuffix;
                    if (File.Exists(backup))
                        File.Delete(backup);
                    File.Move(dest, backup);
                }
                moved.Add((dest, backup));
                File.Move(file, dest);
            }
        }
        catch
        {
            // Deshacer: la versión que estaba sigue igual que antes.
            for (int i = moved.Count - 1; i >= 0; i--)
            {
                var (dest, backup) = moved[i];
                try
                {
                    if (File.Exists(dest) && backup is not null && File.Exists(backup))
                        File.Delete(dest);
                    if (backup is not null && File.Exists(backup))
                        File.Move(backup, dest);
                }
                catch (Exception ex)
                {
                    AgentLog.Write("No se pudo deshacer el cambio de " + dest + ": " + ex.Message);
                }
            }
            throw;
        }
    }

    private void ApplyBundle(string source)
    {
        var backup = _target + OldSuffix;
        DeleteQuietly(backup);
        Directory.Move(_target, backup);
        try
        {
            Directory.Move(source, _target);
        }
        catch
        {
            Directory.Move(backup, _target);
            throw;
        }
    }

    /// <summary>Lo que dejó la actualización anterior: archivos ".old" y la carpeta de descarga.</summary>
    private void CleanLeftovers()
    {
        DeleteQuietly(_staging);
        if (_bundle)
        {
            DeleteQuietly(_target + OldSuffix);
            return;
        }
        foreach (var old in Directory.EnumerateFiles(_target, "*" + OldSuffix, SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception)
            {
                // Todavía en uso (la versión anterior terminando de cerrarse): se borra la próxima vez.
            }
        }
    }

    private static string? ExecutableIn(string root, bool bundle) =>
        bundle ? Path.Combine(root, "Contents", "MacOS", "AlToque")
        : Path.Combine(root, OperatingSystem.IsWindows() ? "AlToque.exe" : "AlToque");

    /// <summary>El .tar.gz trae todo dentro de "AlToque/"; el .zip de Windows, suelto.</summary>
    private static string SingleFolder(string dir) =>
        Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir) is [var only] ? only : dir;

    private static string Rid()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return os + "-" + arch;
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".altoque-probe-" + Environment.ProcessId);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudo borrar " + path + ": " + ex.Message);
        }
    }
}

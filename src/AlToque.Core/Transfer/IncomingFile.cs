using System.Security.Cryptography;
using AlToque.Core.Protocol;
using AlToque.Core.Storage;

namespace AlToque.Core.Transfer;

/// <summary>Operaciones de disco del receptor que nunca siguen enlaces ni salen de la carpeta destino.</summary>
internal static class SafeFs
{
    /// <summary>Crea las carpetas intermedias comprobando cada nivel; falla si alguno es un enlace o un archivo.</summary>
    public static void EnsureDirectories(string root, IReadOnlyList<string> dirComponents)
    {
        Directory.CreateDirectory(root);
        var current = root;
        foreach (var component in dirComponents)
        {
            var next = Path.Combine(current, component);
            if (Directory.Exists(next))
            {
                if (new DirectoryInfo(next).LinkTarget is not null)
                    throw new TransferException(ErrorCodes.Io, "La ruta pasa por un enlace simbólico.");
            }
            else if (File.Exists(next))
            {
                throw new TransferException(ErrorCodes.Io, "Ya existe un archivo con el nombre de una carpeta.");
            }
            else
            {
                Directory.CreateDirectory(next);
            }
            current = next;
        }
        if (dirComponents.Count > 0 && !NameSanitizer.IsInside(root, current))
            throw new TransferException(ErrorCodes.Io, "Ruta fuera del destino.");
    }

    public static bool IsLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (IOException)
        {
            return true;
        }
    }
}

/// <summary>
/// Un archivo en recepción: se escribe como <c>.altoque-part</c>, se verifica su SHA-256 y solo entonces se
/// renombra de forma atómica a su nombre final, con la marca de origen ya aplicada.
/// </summary>
internal sealed class IncomingFile : IAsyncDisposable
{
    private readonly PlannedFile _plan;
    private readonly string _root;
    private readonly ConflictPolicy _policy;
    private readonly FileStream _stream;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _finished;

    /// <summary>Un renombrado final a la vez en todo el proceso.</summary>
    private static readonly SemaphoreSlim FinalizeGate = new(1, 1);

    private IncomingFile(PlannedFile plan, string root, ConflictPolicy policy, string partPath, FileStream stream)
    {
        _plan = plan;
        _root = root;
        _policy = policy;
        PartPath = partPath;
        _stream = stream;
    }

    public string PartPath { get; }

    public static string PartPathFor(string finalPath, string sessionTag) =>
        finalPath + "." + sessionTag.ToLowerInvariant() + AppInfo.PartExtension;

    /// <summary>¿Alguien está recibiendo ahora (o dejó a medias) un archivo con ese nombre final?</summary>
    public static bool AnyPartFor(string finalPath)
    {
        if (File.Exists(finalPath + AppInfo.PartExtension))
            return true;
        var dir = Path.GetDirectoryName(finalPath);
        if (dir is null || !Directory.Exists(dir))
            return false;
        var name = Path.GetFileName(finalPath);
        // Los nombres ya saneados no traen comodines; por si acaso, se compara a mano.
        return Directory.EnumerateFiles(dir, "*" + AppInfo.PartExtension).Any(f =>
        {
            var n = Path.GetFileName(f);
            return n.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase) && n.Length > name.Length + 1 + AppInfo.PartExtension.Length;
        });
    }
    public long Written { get; private set; }
    public long Expected => _plan.Offer.Size;

    /// <param name="sessionTag">
    /// Parte del id de la sesión: cada recepción tiene su propio temporal, así dos personas que mandan a la vez un
    /// archivo con el mismo nombre no se pisan (al final, uno queda como "nombre (1)").
    /// </param>
    public static IncomingFile Create(PlannedFile plan, string root, ConflictPolicy policy, string sessionTag)
    {
        SafeFs.EnsureDirectories(root, plan.Components[..^1]);
        if (sessionTag.Length is 0 or > 16 || !sessionTag.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("Etiqueta de sesión inválida.", nameof(sessionTag));
        var partPath = PartPathFor(plan.FinalPath, sessionTag);
        if (!NameSanitizer.IsInside(root, partPath))
            throw new TransferException(ErrorCodes.Io, "Ruta fuera del destino.");
        if (File.Exists(partPath))
        {
            // Restos de esta misma sesión (un intento cortado): solo se borran si es un archivo normal.
            if (SafeFs.IsLink(partPath))
                throw new TransferException(ErrorCodes.Io, "Hay un enlace en lugar del archivo temporal.");
            File.Delete(partPath);
        }
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = 0,
            PreallocationSize = plan.Offer.Size,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        try
        {
            return new IncomingFile(plan, root, policy, partPath, new FileStream(partPath, options));
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            SecureFile.TryDelete(partPath);
            throw new TransferException(ErrorCodes.DiskFull, ex.Message);
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (Written + data.Length > Expected)
            throw new ProtocolException("Llegaron más bytes de los declarados.");
        try
        {
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            throw new TransferException(ErrorCodes.DiskFull, ex.Message);
        }
        // Se cuenta (y entra al hash) solo lo que quedó escrito: si se corta a mitad, se retoma desde aquí.
        _hash.AppendData(data.Span);
        Written += data.Length;
    }

    /// <summary>
    /// Antes de seguir tras un corte: el temporal queda exactamente con lo contado (una escritura cortada pudo dejar
    /// bytes de más) y la posición al final.
    /// </summary>
    public void PrepareResume()
    {
        _stream.SetLength(Written);
        _stream.Position = Written;
    }

    /// <summary>Verifica tamaño y hash, sincroniza a disco, marca el origen y renombra. Devuelve la ruta final.</summary>
    public async Task<string> CompleteAsync(FileEndMessage end, string originTag)
    {
        if (end.Size != Expected || Written != Expected)
            throw new TransferException(ErrorCodes.HashMismatch, "Tamaño distinto al declarado.");
        var actual = Convert.ToHexStringLower(_hash.GetHashAndReset());
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(actual), System.Text.Encoding.ASCII.GetBytes(end.Sha256.ToLowerInvariant())))
            throw new TransferException(ErrorCodes.HashMismatch, "El hash no coincide.");

        await _stream.FlushAsync().ConfigureAwait(false);
        _stream.Flush(flushToDisk: true);
        await _stream.DisposeAsync().ConfigureAwait(false);

        MarkOfTheWeb.Apply(PartPath, originTag);

        // Elegir el nombre libre y renombrar, de a una recepción por vez: dos que terminan en el mismo instante con el
        // mismo nombre no se pisan (File.Move sin sobrescribir no es atómico en todos los sistemas).
        string final;
        await FinalizeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Exactamente lo mismo ya está con ese nombre (se volvió a mandar, o un reintento): no se crea otra copia.
            if (_policy != ConflictPolicy.Replace)
            {
                foreach (var existing in new[] { _plan.WantedPath, _plan.FinalPath }.OfType<string>().Distinct(StringComparer.Ordinal))
                {
                    if (await HasSameContentAsync(existing, Expected, actual).ConfigureAwait(false) && NameSanitizer.IsInside(_root, existing))
                    {
                        SecureFile.TryDelete(PartPath);
                        _finished = true;
                        return existing;
                    }
                }
            }
            final = _plan.FinalPath;
            if (File.Exists(final) || Directory.Exists(final))
            {
                switch (_policy)
                {
                    case ConflictPolicy.Replace when File.Exists(final) && !SafeFs.IsLink(final):
                        break;
                    case ConflictPolicy.Skip:
                        throw new TransferException(ErrorCodes.Skipped, "Ya existía.");
                    default:
                        // Apareció entre la aceptación y el final: se busca otro nombre libre.
                        final = NextFreeName(final);
                        break;
                }
            }
            if (!NameSanitizer.IsInside(_root, final))
                throw new TransferException(ErrorCodes.Io, "Ruta fuera del destino.");
            await MoveWithRetryAsync(PartPath, final, _policy == ConflictPolicy.Replace).ConfigureAwait(false);
        }
        finally
        {
            FinalizeGate.Release();
        }
        _finished = true;
        ApplyTimestamps(final);
        return final;
    }

    /// <summary>¿Ese archivo existe, es normal y tiene el mismo tamaño y el mismo SHA-256?</summary>
    private static async Task<bool> HasSameContentAsync(string path, long size, string sha256Hex)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != size || SafeFs.IsLink(path))
                return false;
            await using var fs = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
            var hash = await SHA256.HashDataAsync(fs).ConfigureAwait(false);
            return string.Equals(Convert.ToHexStringLower(hash), sha256Hex, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// En Windows el antivirus suele abrir el archivo recién cerrado para analizarlo; mientras tanto el renombrado
    /// falla con "archivo en uso". Se reintenta unas veces antes de darlo por perdido.
    /// </summary>
    private static async Task MoveWithRetryAsync(string from, string to, bool overwrite)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(from, to, overwrite);
                return;
            }
            catch (IOException) when (attempt < 6 && !File.Exists(to))
            {
                await Task.Delay(150 * attempt).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < 6)
            {
                await Task.Delay(150 * attempt).ConfigureAwait(false);
            }
        }
    }

    private void ApplyTimestamps(string path)
    {
        try
        {
            if (_plan.Offer.Modified is { } m && m.Year is >= 1980 and <= 2200)
                File.SetLastWriteTimeUtc(path, m.UtcDateTime);
            if (_plan.Offer.Created is { } c && c.Year is >= 1980 and <= 2200 && !OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid())
                File.SetCreationTimeUtc(path, c.UtcDateTime);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string NextFreeName(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        throw new TransferException(ErrorCodes.Io, "No hay nombre libre.");
    }

    internal static bool IsDiskFull(IOException ex)
    {
        const int ErrorDiskFull = unchecked((int)0x80070070);
        const int ErrorHandleDiskFull = unchecked((int)0x80070027);
        return ex.HResult is ErrorDiskFull or ErrorHandleDiskFull || ex.HResult == 28 /* ENOSPC */ ||
               ex.Message.Contains("No space left", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Descarta el archivo temporal si no se completó.</summary>
    public async ValueTask DisposeAsync()
    {
        _hash.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
        if (!_finished)
            SecureFile.TryDelete(PartPath);
    }
}

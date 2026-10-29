using System.Text;

namespace AlToque.Core.Agent;

/// <summary>Registro local y rotativo. Nunca contiene contenido de archivos ni claves.</summary>
public static class AgentLog
{
    private const long MaxBytes = 1 << 20;
    private static readonly Lock Gate = new();
    private static string? _file;

    public static void Initialize(AppPaths paths)
    {
        try
        {
            SecureFile.EnsurePrivateDirectory(paths.LogDir);
            _file = Path.Combine(paths.LogDir, "altoque.log");
        }
        catch (IOException)
        {
            _file = null;
        }
    }

    public static void Write(string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        var file = _file;
        if (file is null)
            return;
        lock (Gate)
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(file, file + ".1", overwrite: true);
                File.AppendAllText(file, line, Encoding.UTF8);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

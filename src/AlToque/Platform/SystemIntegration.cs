using System.Diagnostics;
using System.Runtime.InteropServices;
using AlToque.Core;
using AlToque.Core.Agent;

namespace AlToque.Platform;

/// <summary>Preferencias del sistema: reducir movimiento y sonidos.</summary>
public static partial class SystemPrefs
{
    /// <summary>"Efectos de animación" de Windows desactivados, o <c>enable-animations=false</c> en GNOME.</summary>
    public static bool PrefersReducedMotion()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                const uint SpiGetClientAreaAnimation = 0x1042;
                return SystemParametersInfoW(SpiGetClientAreaAnimation, 0, out var enabled, 0) && enabled == 0;
            }
            if (OperatingSystem.IsLinux())
                return Environment.GetEnvironmentVariable("ALTOQUE_REDUCE_MOTION") == "1";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return false;
    }

    public enum Sound
    {
        Incoming,
        Success,
        Error,
    }

    /// <summary>Sonidos del sistema, cortos y asíncronos (los propios llegan en v2).</summary>
    public static void Play(Sound sound)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndAlias = 0x00010000;
            var alias = sound switch
            {
                Sound.Incoming => "Notification.Default",
                Sound.Success => "SystemAsterisk",
                _ => "SystemExclamation",
            };
            PlaySoundW(alias, 0, SndAlias | SndAsync | SndNoDefault);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);

    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(string pszSound, nint hmod, uint fdwSound);
}

/// <summary>Abre carpetas y archivos. Nunca se abre nada automáticamente: solo a pedido del usuario.</summary>
public static class Opener
{
    public static void ShowInFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
                else
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false })?.Dispose();
            }
            else
            {
                var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
                // En macOS, Finder abre la carpeta con el archivo seleccionado.
                if (OperatingSystem.IsMacOS())
                    Process.Start(new ProcessStartInfo("open", File.Exists(path) ? $"-R \"{path}\"" : $"\"{dir}\"") { UseShellExecute = false })?.Dispose();
                else
                    Process.Start(new ProcessStartInfo("xdg-open", $"\"{dir}\"") { UseShellExecute = false })?.Dispose();
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AgentLog.Write("No se pudo abrir la carpeta: " + ex.Message);
        }
    }

    /// <summary>Abre con la aplicación asociada; la marca de origen hace que Windows avise si es un programa.</summary>
    /// <summary>Abre la carpeta en el explorador de archivos (la crea si aún no existe).</summary>
    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        OpenFile(path);
    }

    /// <summary>Abre una página web en el navegador predeterminado.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AgentLog.Write("No se pudo abrir la página: " + ex.Message);
        }
    }

    public static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AgentLog.Write("No se pudo abrir el archivo: " + ex.Message);
        }
    }
}

/// <summary>
/// Regla de firewall para redes privadas y de dominio; las públicas solo si el usuario lo pide. Pide elevación una vez y ejecuta
/// <c>AlToque.exe firewall-setup</c>, que llama a <c>netsh</c>.
/// </summary>
public static class Firewall
{
    public const string SetupCommand = "firewall-setup";

    public static bool RequestRule(int tcpPort, bool includePublic = false)
    {
        var exe = Environment.ProcessPath;
        if (!OperatingSystem.IsWindows() || exe is null)
            return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, $"{SetupCommand} {tcpPort}{(includePublic ? " public" : "")}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            p?.WaitForExit(30_000);
            return p?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // El usuario no autorizó la elevación.
        }
    }

    /// <summary>Se ejecuta ya elevado.</summary>
    public static int Apply(int tcpPort, bool includePublic = false)
    {
        var exe = Environment.ProcessPath!;
        var profiles = includePublic ? "private,domain,public" : "private,domain";
        var commands = new[]
        {
            "advfirewall firewall delete rule name=\"Al Toque (TCP)\"",
            "advfirewall firewall delete rule name=\"Al Toque (UDP)\"",
            $"advfirewall firewall add rule name=\"Al Toque (TCP)\" dir=in action=allow program=\"{exe}\" enable=yes profile={profiles} protocol=TCP localport={tcpPort}",
            $"advfirewall firewall add rule name=\"Al Toque (UDP)\" dir=in action=allow program=\"{exe}\" enable=yes profile={profiles} protocol=UDP localport={AppInfo.DiscoveryPort}",
        };
        var result = 0;
        foreach (var args in commands)
        {
            using var p = Process.Start(new ProcessStartInfo("netsh.exe", args) { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(15_000);
            if (args.Contains(" add ", StringComparison.Ordinal) && p?.ExitCode != 0)
                result = 1;
        }
        return result;
    }
}

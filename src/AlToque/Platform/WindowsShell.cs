using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AlToque.Core.Agent;
using AlToque.Localization;
using Microsoft.Win32;

namespace AlToque.Platform;

/// <summary>Compañero que aparece en el submenú del clic derecho.</summary>
public sealed record MenuPeer(string DeviceId, string Name);

/// <summary>
/// Menú clásico del clic derecho sin firma ni DLL (sección 9.1.1): un verbo en cascada en
/// <c>HKCU\Software\Classes</c> cuyo submenú lista a los compañeros de confianza. El agente lo reescribe cada vez
/// que cambia la lista. Además, un acceso directo en "Enviar a".
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class WindowsShell
{
    private const string VerbName = "AlToque";
    private const string MenuKey = "AlToque.Menu";
    private const int MaxPeers = 6;

    public static string SendToShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "SendTo", "Al Toque.lnk");

    public static void Install(string exePath, IReadOnlyList<MenuPeer> peers)
    {
        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", writable: true);
            foreach (var target in new[] { "*", "Directory" })
            {
                using var verb = classes.CreateSubKey($@"{target}\shell\{VerbName}", writable: true);
                verb.SetValue("MUIVerb", S.Spanish ? "Enviar con Al Toque" : "Send with Al Toque");
                verb.SetValue("Icon", $"{exePath},0");
                verb.SetValue("ExtendedSubCommandsKey", MenuKey);
                verb.SetValue("MultiSelectModel", "Player");
            }

            // El submenú se reescribe entero; el orden sale del nombre de la clave (01, 02… 98, 99).
            classes.DeleteSubKeyTree(MenuKey, throwOnMissingSubKey: false);
            using var shell = classes.CreateSubKey($@"{MenuKey}\shell", writable: true);
            var index = 1;
            foreach (var peer in peers.Take(MaxPeers))
                AddCommand(shell, index++.ToString("00"), EscapeLabel(peer.Name), $"\"{exePath}\" send --to {peer.DeviceId} \"%1\"", exePath, separatorAfter: false);
            AddCommand(shell, "98", S.Spanish ? "Elegir destino…" : "Choose destination…", $"\"{exePath}\" send --pick \"%1\"", null,
                separatorBefore: peers.Count > 0);
            AddCommand(shell, "99", S.Spanish ? "Conectar por código…" : "Connect by code…", $"\"{exePath}\" send --code \"%1\"", null);

            CreateSendToShortcut(exePath);
            NotifyAssociationsChanged();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or COMException)
        {
            AgentLog.Write("No se pudo instalar el menú contextual: " + ex.Message);
        }
    }

    public static void Uninstall()
    {
        try
        {
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
            if (classes is not null)
            {
                classes.DeleteSubKeyTree($@"*\shell\{VerbName}", throwOnMissingSubKey: false);
                classes.DeleteSubKeyTree($@"Directory\shell\{VerbName}", throwOnMissingSubKey: false);
                classes.DeleteSubKeyTree(MenuKey, throwOnMissingSubKey: false);
            }
            if (File.Exists(SendToShortcutPath))
                File.Delete(SendToShortcutPath);
            NotifyAssociationsChanged();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            AgentLog.Write("No se pudo quitar el menú contextual: " + ex.Message);
        }
    }

    private static void AddCommand(RegistryKey shell, string key, string label, string command, string? icon,
        bool separatorBefore = false, bool separatorAfter = false)
    {
        const int EcfSeparatorBefore = 0x20;
        const int EcfSeparatorAfter = 0x40;
        using var item = shell.CreateSubKey(key, writable: true);
        item.SetValue("MUIVerb", label);
        item.SetValue("MultiSelectModel", "Player");
        if (icon is not null)
            item.SetValue("Icon", $"{icon},0");
        var flags = (separatorBefore ? EcfSeparatorBefore : 0) | (separatorAfter ? EcfSeparatorAfter : 0);
        if (flags != 0)
            item.SetValue("CommandFlags", flags, RegistryValueKind.DWord);
        using var cmd = item.CreateSubKey("command", writable: true);
        cmd.SetValue("", command);
    }

    /// <summary>"&amp;" es un acelerador y "@" al inicio indica un recurso: ambos se neutralizan.</summary>
    private static string EscapeLabel(string name)
    {
        var label = name.Replace("&", "&&", StringComparison.Ordinal);
        return label.StartsWith('@') ? " " + label : label;
    }

    private static void CreateSendToShortcut(string exePath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                ShellLink.Create(SendToShortcutPath, exePath, "send --pick", Path.GetDirectoryName(exePath)!,
                    S.Spanish ? "Enviar con Al Toque" : "Send with Al Toque");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            AgentLog.Write("No se pudo crear el acceso directo de \"Enviar a\": " + failure.Message);
    }

    private static void NotifyAssociationsChanged()
    {
        const int ShcneAssocChanged = 0x08000000;
        SHChangeNotify(ShcneAssocChanged, 0, 0, 0);
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int wEventId, uint uFlags, nint dwItem1, nint dwItem2);
}

/// <summary>Arranque con la sesión: clave <c>Run</c> en Windows, <c>autostart</c> en Linux.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Set(bool enabled)
    {
        var exe = Environment.ProcessPath;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                if (enabled && exe is not null)
                    run.SetValue(Core.AppInfo.Id, $"\"{exe}\" --background");
                else
                    run.DeleteValue(Core.AppInfo.Id, throwOnMissingValue: false);
            }
            else if (OperatingSystem.IsLinux())
            {
                var dir = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
                    ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart");
                var file = Path.Combine(dir, "altoque.desktop");
                if (enabled && exe is not null)
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(file, $"[Desktop Entry]\nType=Application\nName=Al Toque\nExec=\"{exe}\" --background\nX-GNOME-Autostart-enabled=true\n");
                }
                else if (File.Exists(file))
                    File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            AgentLog.Write("No se pudo configurar el inicio automático: " + ex.Message);
        }
    }
}

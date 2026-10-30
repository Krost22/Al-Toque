using System.Diagnostics;
using System.Runtime.InteropServices;
using AlToque.Core.Ipc;
using AlToque.Platform;

namespace AlToque;

/// <summary>
/// Línea de comandos. El menú contextual y "Enviar a" ejecutan <c>AlToque.exe send …</c>: el CLI entrega las rutas
/// al agente por IPC y termina en milisegundos. Si el agente no está corriendo, lo arranca.
/// </summary>
internal static partial class Cli
{
    private static readonly string[] Commands = ["send", "show", "quit", "unintegrate", Firewall.SetupCommand, "--help", "-h", "/?", "--version"];

    public static bool IsCliInvocation(string[] args) =>
        args.Length > 0 && Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var command = args[0].ToLowerInvariant();
        switch (command)
        {
            case "send":
                return ParseSend(args.AsSpan(1), out var request, out var error) ? Deliver(request!, startAgent: true) : Usage(error);
            case "show":
                return Deliver(new IpcRequest { Command = "show" }, startAgent: true);
            case "quit":
                return Deliver(new IpcRequest { Command = "quit" }, startAgent: false);
            case "unintegrate":
                if (OperatingSystem.IsWindows())
                    WindowsShell.Uninstall();
                StartupRegistration.Set(false);
                return 0;
            case Firewall.SetupCommand:
                return args.Length > 1 && int.TryParse(args[1], out var port) && port is > 0 and <= 65535 ? Firewall.Apply(port, args.Length > 2 && args[2] == "public") : 2;
            case "--version":
                Print($"{Core.AppInfo.Name} {Core.AppInfo.Version} (protocolo {Core.AppInfo.Alpn})");
                return 0;
            default:
                return Usage(null);
        }
    }

    internal static bool ParseSend(ReadOnlySpan<string> args, out IpcRequest? request, out string? error)
    {
        string? to = null;
        string? message = null;
        bool pick = false, code = false, onlyPaths = false;
        var paths = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!onlyPaths && a.StartsWith('-'))
            {
                switch (a)
                {
                    case "--":
                        onlyPaths = true;
                        continue;
                    case "--to" when i + 1 < args.Length:
                        to = args[++i];
                        continue;
                    case "--message" or "-m" when i + 1 < args.Length:
                        message = args[++i];
                        continue;
                    case "--pick":
                        pick = true;
                        continue;
                    case "--code":
                        code = true;
                        continue;
                    default:
                        request = null;
                        error = "Opción desconocida: " + a;
                        return false;
                }
            }
            // El agente tiene otro directorio de trabajo: se envían rutas absolutas.
            paths.Add(Path.GetFullPath(a.Trim().Trim('"')));
        }
        if (paths.Count == 0)
        {
            request = null;
            error = "Faltan rutas.";
            return false;
        }
        if (to is null && !code)
            pick = true;
        request = new IpcRequest { Command = "send", Paths = paths, To = to, Pick = pick, Code = code, Message = message };
        error = null;
        return true;
    }

    private static int Deliver(IpcRequest request, bool startAgent)
    {
        var response = IpcChannel.SendAsync(request, TimeSpan.FromMilliseconds(400)).GetAwaiter().GetResult();
        if (response is null && startAgent)
        {
            StartAgent();
            var deadline = Environment.TickCount64 + 10_000;
            while (response is null && Environment.TickCount64 < deadline)
                response = IpcChannel.SendAsync(request, TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult();
        }
        return response?.Ok == true ? 0 : 1;
    }

    private static void StartAgent()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--background") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private static int Usage(string? error)
    {
        var text = """
            Al Toque: envía archivos a otra PC de tu red con un clic derecho.

            Uso:
              AlToque send --to <compañero> [--message "texto"] <rutas…>
              AlToque send --pick <rutas…>      Elegir destino en el panel
              AlToque send --code <rutas…>      Conectar por código o IP
              AlToque show                      Abrir el panel
              AlToque quit                      Cerrar el agente
              AlToque unintegrate               Quitar el menú contextual y el inicio automático

            <compañero> es el nombre que ves en el panel o su identificador.
            """;
        Print(error is null ? text : error + Environment.NewLine + Environment.NewLine + text);
        return error is null ? 0 : 2;
    }

    private static void Print(string text)
    {
        // La app es de ventanas: en Windows hay que engancharse a la consola que la llamó.
        if (OperatingSystem.IsWindows())
            AttachConsole(-1);
        Console.WriteLine(text);
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}

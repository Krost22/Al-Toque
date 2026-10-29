using AlToque.Core.Agent;
using Avalonia;
using Avalonia.Media;

namespace AlToque;

internal static class Program
{
    private static Mutex? _instanceMutex;

    /// <summary>Se arrancó con la sesión: sin mostrar el panel.</summary>
    public static bool StartedInBackground { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Ganchos del instalador (Velopack): al desinstalar se quitan el menú contextual y el inicio automático.
        var velopack = Velopack.VelopackApp.Build();
        if (OperatingSystem.IsWindows())
        {
            velopack = velopack.OnBeforeUninstallFastCallback(_ =>
            {
                if (OperatingSystem.IsWindows())
                    Platform.WindowsShell.Uninstall();
                Platform.StartupRegistration.Set(false);
            });
        }
        velopack.Run();

        if (Cli.IsCliInvocation(args))
            return Cli.Run(args);

        StartedInBackground = args.Contains("--background");

        // Una sola instancia del agente por usuario: las demás le piden que se muestre y terminan.
        var name = "Local\\AlToque.Agent." + Core.Ipc.IpcChannel.InstanceKey;
        _instanceMutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (!createdNew && args.Contains("--restart"))
        {
            // Reinicio pedido por la propia app: se espera a que la instancia anterior termine.
            try
            {
                createdNew = _instanceMutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
            }
        }
        if (!createdNew)
        {
            if (!StartedInBackground)
                Cli.Run(["show"]);
            return 0;
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            AgentLog.Write("Error fatal: " + ex);
            throw;
        }
        finally
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
        if (!OperatingSystem.IsWindows())
            builder = builder.With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" });
        return builder;
    }
}

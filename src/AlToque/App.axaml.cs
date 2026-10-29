using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AlToque;

public partial class App : Application
{
    public AppController? Controller { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Un error inesperado en la interfaz se registra, pero no cierra la app (ni corta un envío en curso).
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Core.Agent.AgentLog.Write("Error no controlado en la interfaz: " + e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Core.Agent.AgentLog.Write("Error en segundo plano: " + e.Exception.GetBaseException().Message);
            e.SetObserved();
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Controller = new AppController(this, desktop);
            Controller.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

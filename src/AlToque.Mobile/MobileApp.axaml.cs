using AlToque.Core.Agent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AlToque.Mobile;

public partial class MobileApp : Application
{
    /// <summary>La pone el proyecto de cada sistema antes de arrancar Avalonia.</summary>
    public static IMobilePlatform? Platform { get; set; }

    public static MobileSession? Session { get; private set; }

    /// <summary>Por qué no arrancó el agente, para mostrarlo en lugar de cerrarse sin decir nada.</summary>
    public static string? StartupError { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AgentLog.Write("Error no controlado en la interfaz: " + e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AgentLog.Write("Error en segundo plano: " + e.Exception.GetBaseException().Message);
            e.SetObserved();
        };
        RequestedThemeVariant = Environment.GetEnvironmentVariable("ALTOQUE_THEME") switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => RequestedThemeVariant,
        };
        try
        {
            Session ??= MobileSession.Start(Platform ?? throw new InvalidOperationException("Falta la plataforma."));
        }
        catch (Exception ex)
        {
            StartupError = ex.Message;
            AgentLog.Write("No se pudo iniciar el agente: " + ex);
        }

        switch (ApplicationLifetime)
        {
            case IActivityApplicationLifetime activity:
                activity.MainViewFactory = CreateView;
                break;
            case ISingleViewApplicationLifetime single:
                single.MainView = CreateView();
                break;
            case IClassicDesktopStyleApplicationLifetime desktop:
                // Vista previa en el escritorio, con tamaño de teléfono.
                desktop.MainWindow = new Window { Title = "Al Toque (vista móvil)", Width = 412, Height = 860, Content = CreateView() };
                break;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static Control CreateView() => Session is { } session
        ? new Views.MobileView { DataContext = session.ViewModel }
        : new TextBlock { Text = "Al Toque no pudo arrancar: " + StartupError, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24) };
}

using AlToque.Core.Agent;
using AlToque.Mobile;
using Avalonia;
using Avalonia.Threading;

// Uso: AlToque.MobilePreview            → ventana de 412×860 con la app del teléfono
//      AlToque.MobilePreview --selftest → empareja dos agentes en local, pasa un archivo y sale (0 = bien)
if (args.Contains("--selftest"))
{
    var result = await SelfTest.RunAsync(Path.Combine(Path.GetTempPath(), "altoque-selftest"));
    Console.WriteLine(result);
    return result.Contains(" OK ") ? 0 : 1;
}

MobileApp.Platform = new PreviewPlatform();
return AppBuilder.Configure<MobileApp>().UsePlatformDetect().WithInterFont().LogToTrace()
    .AfterSetup(_ => Dispatcher.UIThread.Post(() =>
    {
        // Como "Compartir" en Android: ALTOQUE_SHARE="a.jpg;b.pdf" deja esos archivos listos para enviar.
        if (Environment.GetEnvironmentVariable("ALTOQUE_SHARE") is { Length: > 0 } share && MobileApp.Session is { } session)
            session.ViewModel.Share(share.Split(';', StringSplitOptions.RemoveEmptyEntries), null);
    }))
    .StartWithClassicDesktopLifetime(args);

/// <summary>Plataforma de mentira para el escritorio: datos en ALTOQUE_HOME y descargas en la carpeta habitual.</summary>
internal sealed class PreviewPlatform : IMobilePlatform
{
    public string DataDirectory { get; } = Environment.GetEnvironmentVariable("ALTOQUE_HOME") is { Length: > 0 } home
        ? home
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "altoque-mobile-preview");

    public string CacheDirectory { get; } = Path.Combine(Path.GetTempPath(), "altoque-mobile-preview");
    public string? DownloadsDirectory => Environment.GetEnvironmentVariable("ALTOQUE_DOWNLOADS");
    public string DeviceName => Environment.GetEnvironmentVariable("ALTOQUE_DEVICE_NAME") ?? "Teléfono de prueba";
    public void PublishReceived(IReadOnlyList<string> paths) => AgentLog.Write("Recibido: " + string.Join(", ", paths));
    public void OpenReceived(string? path) => AgentLog.Write("Abrir: " + (path ?? "(descargas)"));
    public void OpenUrl(string url) => AgentLog.Write("Abrir enlace: " + url);
    public void SetBusy(bool busy) => AgentLog.Write("Ocupado: " + busy);
}

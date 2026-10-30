using AlToque.Core;
using AlToque.Core.Agent;
using Velopack;
using Velopack.Sources;

namespace AlToque.Platform;

public enum UpdateStatus
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,

    /// <summary>Descargada: se instala al reiniciar o al cerrar la app.</summary>
    Ready,
    Failed,
}

/// <summary>
/// Actualizaciones desde las versiones publicadas en GitHub (Velopack). La app instalada busca cada 6 horas, descarga
/// en segundo plano si "Actualizar automáticamente" está activo y se instala al reiniciar desde el aviso o al cerrar;
/// nunca durante una transferencia sin preguntar. Una copia portable solo avisa y abre la página de descarga.
/// </summary>
public sealed class Updates : IDisposable
{
    public const string RepositoryUrl = ReleaseCheck.RepositoryUrl;
    public const string ReleasesUrl = ReleaseCheck.ReleasesUrl;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly Func<bool> _autoDownload;
    private readonly UpdateManager? _manager;
    private readonly Timer _timer;
    private UpdateInfo? _update;
    private VelopackAsset? _ready;
    private int _busy;

    public Updates(Func<bool> autoDownload)
    {
        _autoDownload = autoDownload;
        try
        {
            var manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false, null));
            if (manager.IsInstalled)
                _manager = manager;
        }
        catch (Exception ex)
        {
            AgentLog.Write("Actualizaciones no disponibles: " + ex.Message);
        }
        CurrentVersion = _manager?.CurrentVersion?.ToString() ?? AppInfo.Version;

        // Una versión ya descargada en una sesión anterior (la app se cerró de golpe): queda lista para instalar.
        if (_manager?.UpdatePendingRestart is { } pending)
        {
            _ready = pending;
            AvailableVersion = pending.Version.ToString();
            Status = UpdateStatus.Ready;
        }
        // Para probar el aviso sin publicar nada: ALTOQUE_FAKE_UPDATE=0.9.0
        if (Environment.GetEnvironmentVariable("ALTOQUE_FAKE_UPDATE") is { Length: > 0 } fake)
        {
            AvailableVersion = fake;
            Status = UpdateStatus.Available;
        }
        _timer = new Timer(_ => _ = CheckAsync(), null, TimeSpan.FromSeconds(90), Interval);
    }

    public UpdateStatus Status { get; private set; } = UpdateStatus.Idle;
    public string CurrentVersion { get; }
    public string? AvailableVersion { get; private set; }
    public int Progress { get; private set; }

    /// <summary>Instalada con el instalador: se actualiza desde la app. Si no (portable), se descarga a mano.</summary>
    public bool CanInstall => _manager is not null;

    /// <summary>Cambió algo; puede llegar desde cualquier hilo.</summary>
    public event Action? Changed;

    private void Set(UpdateStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }

    public async Task CheckAsync()
    {
        if (Status is UpdateStatus.Downloading or UpdateStatus.Ready || Interlocked.Exchange(ref _busy, 1) == 1)
            return;
        try
        {
            Set(UpdateStatus.Checking);
            if (_manager is not null)
            {
                var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
                if (update is null)
                {
                    Set(UpdateStatus.UpToDate);
                    return;
                }
                _update = update;
                AvailableVersion = update.TargetFullRelease.Version.ToString();
                Set(UpdateStatus.Available);
                if (_autoDownload())
                    await DownloadCoreAsync().ConfigureAwait(false);
            }
            else
            {
                var latest = await ReleaseCheck.LatestAsync().ConfigureAwait(false);
                if (latest is not null && IsNewer(latest.Version, CurrentVersion))
                {
                    AvailableVersion = latest.Version;
                    Set(UpdateStatus.Available);
                }
                else
                {
                    Set(UpdateStatus.UpToDate);
                }
            }
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudo buscar actualizaciones: " + ex.Message);
            Set(UpdateStatus.Failed);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>Descarga la versión nueva (o, en una copia portable, abre la página de descarga).</summary>
    public async Task DownloadAsync()
    {
        if (_manager is null || _update is null)
        {
            Opener.OpenUrl(ReleasesUrl);
            return;
        }
        if (Status == UpdateStatus.Downloading || Interlocked.Exchange(ref _busy, 1) == 1)
            return;
        try
        {
            await DownloadCoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudo descargar la actualización: " + ex.Message);
            Set(UpdateStatus.Failed);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private async Task DownloadCoreAsync()
    {
        Progress = 0;
        Set(UpdateStatus.Downloading);
        await _manager!.DownloadUpdatesAsync(_update!, p =>
        {
            Progress = p;
            Changed?.Invoke();
        }, CancellationToken.None).ConfigureAwait(false);
        _ready = _update!.TargetFullRelease;
        AgentLog.Write("Actualización descargada: " + AvailableVersion);
        Set(UpdateStatus.Ready);
    }

    /// <summary>Cierra, instala y vuelve a abrir Al Toque en la versión nueva.</summary>
    public void ApplyAndRestart(string[] args)
    {
        if (_manager is not null && _ready is not null)
            _manager.ApplyUpdatesAndRestart(_ready, args);
    }

    /// <summary>Al salir con una versión descargada, se instala sin volver a abrir la app.</summary>
    public void ApplyOnExit()
    {
        if (_manager is null || _ready is null)
            return;
        try
        {
            _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: false, []);
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudo preparar la actualización al salir: " + ex.Message);
        }
    }

    /// <summary>¿<paramref name="candidate"/> es posterior a <paramref name="current"/>? Ignora sufijos como "-beta".</summary>
    public static bool IsNewer(string candidate, string current) => ReleaseCheck.IsNewer(candidate, current);

    public void Dispose() => _timer.Dispose();
}

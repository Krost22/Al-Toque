using AlToque.Core.Agent;
using Velopack;
using Velopack.Sources;

namespace AlToque.Platform;

/// <summary>
/// Actualizaciones con Velopack (sección 14.4): se buscan como máximo una vez al día, se descargan en segundo plano
/// y se aplican cuando la app se cierra, nunca durante una transferencia.
/// </summary>
public sealed class Updates
{
    public const string RepositoryUrl = "https://github.com/Krost22/Al-Toque";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly Timer _timer;
    private bool _staged;

    public Updates()
    {
        _timer = new Timer(_ => _ = CheckAsync(), null, TimeSpan.FromMinutes(2), Interval);
    }

    public string? StagedVersion { get; private set; }

    private async Task CheckAsync()
    {
        if (_staged)
            return;
        try
        {
            var manager = new UpdateManager(new GithubSource(RepositoryUrl, null, prerelease: false));
            if (!manager.IsInstalled)
                return; // Ejecución de desarrollo o copia suelta: no hay nada que actualizar.
            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
                return;
            await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            // Se aplica cuando el proceso termine (al salir o al apagar), sin reiniciar la app.
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
            _staged = true;
            StagedVersion = update.TargetFullRelease.Version.ToString();
            AgentLog.Write("Actualización lista para el próximo inicio: " + StagedVersion);
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudo buscar actualizaciones: " + ex.Message);
        }
    }
}

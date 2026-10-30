using AlToque.Core;
using AlToque.Core.Agent;
using AlToque.Core.Storage;
using AlToque.Localization;

namespace AlToque.Mobile;

/// <summary>El agente del teléfono y su pantalla. Vive mientras viva el proceso de la app.</summary>
public sealed class MobileSession
{
    private MobileSession(AgentHost agent, MobileViewModel viewModel)
    {
        Agent = agent;
        ViewModel = viewModel;
    }

    public AgentHost Agent { get; }
    public MobileViewModel ViewModel { get; }

    public static MobileSession Start(IMobilePlatform platform)
    {
        KnownFolders.DownloadsOverride = platform.DownloadsDirectory;
        OutgoingFiles.Clean(platform.CacheDirectory);
        // Sin IPC: en el teléfono no hay menú contextual ni línea de comandos que hablen con el agente.
        var agent = AgentHost.Create(new AgentOptions
        {
            Paths = AppPaths.Create(platform.DataDirectory, isPortable: false),
            EnableIpc = false,
        });
        S.Initialize(agent.Settings.Current.Language);
        if (!agent.Settings.Current.WelcomeDone && TextLimits.CleanAlias(platform.DeviceName) is { } name)
            agent.Settings.Update(s => s.Alias = name);
        agent.Start();
        AgentLog.Write($"Al Toque para teléfonos · descargas en {agent.Settings.Current.ReceiveFolder}");
        return new MobileSession(agent, new MobileViewModel(agent, platform));
    }
}

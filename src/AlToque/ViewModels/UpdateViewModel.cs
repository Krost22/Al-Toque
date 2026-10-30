using AlToque.Localization;
using AlToque.Platform;
using Avalonia.Threading;

namespace AlToque.ViewModels;

/// <summary>Estado de las actualizaciones para el aviso del panel, "Acerca de" y la bandeja.</summary>
public sealed class UpdateViewModel : ViewModelBase
{
    private readonly Updates? _updates;
    private readonly Func<bool> _getAuto;
    private readonly Action<bool> _setAuto;
    private bool _dismissed;

    public UpdateViewModel(Updates? updates, Func<bool> getAuto, Action<bool> setAuto, Action install)
    {
        _updates = updates;
        _getAuto = getAuto;
        _setAuto = setAuto;
        Act = new RelayCommand(() =>
        {
            switch (Status)
            {
                case UpdateStatus.Available when _updates?.CanInstall == false:
                    Opener.OpenUrl(Updates.ReleasesUrl);
                    break;
                case UpdateStatus.Available or UpdateStatus.Ready:
                    install();
                    break;
                default:
                    _ = _updates?.CheckAsync();
                    break;
            }
        }, () => Status is not (UpdateStatus.Checking or UpdateStatus.Downloading));
        Check = new RelayCommand(() => _ = _updates?.CheckAsync(), () => Status is not (UpdateStatus.Checking or UpdateStatus.Downloading));
        Dismiss = new RelayCommand(() =>
        {
            _dismissed = true;
            Raise(nameof(ShowBanner));
        });
        OpenNotes = new RelayCommand(() => Opener.OpenUrl(Updates.ReleasesUrl));
        if (_updates is not null)
            _updates.Changed += () => Dispatcher.UIThread.Post(Refresh);
    }

    public RelayCommand Act { get; }
    public RelayCommand Check { get; }
    public RelayCommand Dismiss { get; }
    public RelayCommand OpenNotes { get; }

    public UpdateStatus Status => _updates?.Status ?? UpdateStatus.Idle;
    private string Version => _updates?.AvailableVersion ?? "";
    public string CurrentVersion => _updates?.CurrentVersion ?? Core.AppInfo.Version;

    /// <summary>Hay algo que hacer: aviso en el panel (se puede posponer hasta el próximo cambio).</summary>
    public bool ShowBanner => !_dismissed && Status is UpdateStatus.Available or UpdateStatus.Downloading or UpdateStatus.Ready;

    public bool IsDownloading => Status == UpdateStatus.Downloading;
    public double Progress => _updates?.Progress ?? 0;
    public bool HasNews => Status is UpdateStatus.Available or UpdateStatus.Downloading or UpdateStatus.Ready;

    public string Title => Status switch
    {
        UpdateStatus.Downloading => S.UpdateDownloading(Version),
        UpdateStatus.Ready => S.UpdateReady(Version),
        _ => S.UpdateAvailable(Version),
    };

    public string Detail => Status switch
    {
        UpdateStatus.Downloading => $"{Progress:0} %",
        UpdateStatus.Ready => S.UpdateReadyHint,
        _ when _updates?.CanInstall == false => S.UpdatePortableHint,
        _ => S.UpdateAvailableHint,
    };

    /// <summary>Texto del estado en "Acerca de".</summary>
    public string StatusText => Status switch
    {
        UpdateStatus.Checking => S.UpdateChecking,
        UpdateStatus.UpToDate => S.UpdateUpToDate(CurrentVersion),
        UpdateStatus.Failed => S.UpdateFailed,
        UpdateStatus.Idle => S.UpdateIdle(CurrentVersion),
        _ => Title,
    };

    public string ActionText => Status switch
    {
        UpdateStatus.Ready => S.UpdateRestart,
        UpdateStatus.Available when _updates?.CanInstall == false => S.UpdateOpenPage,
        UpdateStatus.Available or UpdateStatus.Downloading => S.UpdateNow,
        _ => S.UpdateCheck,
    };

    public bool AutoUpdate
    {
        get => _getAuto();
        set
        {
            _setAuto(value);
            Raise();
        }
    }

    private UpdateStatus _lastStatus;

    public void Refresh()
    {
        // Un estado nuevo (otra versión, o ya descargada) vuelve a mostrar el aviso pospuesto.
        if (Status != _lastStatus && Status is UpdateStatus.Available or UpdateStatus.Ready)
            _dismissed = false;
        _lastStatus = Status;
        RaiseAll();
        Act.NotifyCanExecuteChanged();
        Check.NotifyCanExecuteChanged();
    }
}

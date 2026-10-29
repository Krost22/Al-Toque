using System.Collections.ObjectModel;
using AlToque.Core;
using AlToque.Core.Security;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;
using AlToque.Localization;
using AlToque.Platform;
using Avalonia.Media;

namespace AlToque.ViewModels;

/// <summary>Ventana principal: Compañeros · Historial · Ajustes.</summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly AppController _app;
    private int _tab;
    private string _alias;
    private string _folder;
    private string? _notice;
    private string? _firewallStatus;

    public MainViewModel(AppController app)
    {
        _app = app;
        var s = app.Agent.Settings.Current;
        _alias = s.Alias;
        _folder = s.ReceiveFolder;
        PairNew = new RelayCommand(_app.ShowPairing);
        ClearHistory = new RelayCommand(() => _app.Agent.History.Clear());
        OpenHistoryItem = new RelayCommand(p =>
        {
            if (p is HistoryItemViewModel h && (h.Entry.FirstFile ?? h.Entry.Folder) is { } path)
                Opener.ShowInFolder(path);
        });
        ApplyFirewall = new RelayCommand(async () =>
        {
            var port = _app.Agent.Server.IsListening ? _app.Agent.Server.Port : _app.Agent.Settings.Current.TcpPort;
            FirewallStatus = "…";
            // El aviso de UAC puede tardar: nunca se espera en el hilo de la interfaz.
            var ok = await Task.Run(() => Firewall.RequestRule(port));
            FirewallStatus = ok ? "✓" : "—";
        });
        ResetIdentity = new RelayCommand(() => _app.ConfirmResetIdentity());
        ConflictItems = [S.ConflictRename, S.ConflictReplace, S.ConflictSkip];
        ModeItems = [S.ModeNobody, S.ModeTrusted];
        LanguageItems = [S.LanguageAuto, "Español", "English"];
        ReloadPeers();
        ReloadHistory();
        RefreshNotice();
    }

    public int Tab { get => _tab; set => Set(ref _tab, value); }

    public ObservableCollection<TrustedPeerViewModel> TrustedPeers { get; } = [];
    public ObservableCollection<HistoryItemViewModel> History { get; } = [];
    public bool HasTrustedPeers => TrustedPeers.Count > 0;
    public bool NoTrustedPeers => TrustedPeers.Count == 0;
    public bool HasHistory => History.Count > 0;
    public bool NoHistory => History.Count == 0;
    public RelayCommand PairNew { get; }
    public RelayCommand ClearHistory { get; }
    public RelayCommand OpenHistoryItem { get; }
    public RelayCommand ApplyFirewall { get; }
    public RelayCommand ResetIdentity { get; }

    public IReadOnlyList<string> ConflictItems { get; }
    public IReadOnlyList<string> ModeItems { get; }
    public IReadOnlyList<string> LanguageItems { get; }
    public bool IsWindows => OperatingSystem.IsWindows();

    // ── Identidad ────────────────────────────────────────────────────────────────
    public IBrush MyColor => Brushes2.ForDevice(_app.Agent.DeviceId);
    public string MySymbols => IdentityVisuals.SymbolSequence(_app.Agent.DeviceId);
    public string MyShortCode => IdentityVisuals.ShortCode(_app.Agent.DeviceId);
    public string MyInitial => IdentityVisuals.Initial(Alias);
    public string? MyConnectionCode => _app.Agent.MyConnectionCode;
    public string VersionText => S.Version(AppInfo.Version) + " · " + AppInfo.Alpn;

    public string? Notice { get => _notice; private set => Set(ref _notice, value); }
    public bool HasNotice => Notice is not null;
    public string? FirewallStatus { get => _firewallStatus; private set => Set(ref _firewallStatus, value); }

    // ── Ajustes ──────────────────────────────────────────────────────────────────

    public string Alias
    {
        get => _alias;
        set
        {
            if (Set(ref _alias, value))
                Raise(nameof(MyInitial));
        }
    }

    /// <summary>Se guarda al perder el foco, no en cada tecla.</summary>
    public void CommitAlias()
    {
        var clean = TextLimits.CleanAlias(Alias);
        if (clean is null)
        {
            Alias = _app.Agent.Settings.Current.Alias;
            return;
        }
        if (clean != _app.Agent.Settings.Current.Alias)
            _app.Agent.Settings.Update(s => s.Alias = clean);
        Alias = clean;
    }

    public string Folder
    {
        get => _folder;
        set
        {
            if (Set(ref _folder, value))
            {
                _app.Agent.Settings.Update(s => s.ReceiveFolder = value);
                RefreshNotice();
            }
        }
    }

    public int ConflictIndex
    {
        get => (int)_app.Agent.Settings.Current.ConflictPolicy;
        set => Update(s => s.ConflictPolicy = (ConflictPolicy)value);
    }

    public int ModeIndex
    {
        get => _app.Agent.Settings.Current.ReceiveMode == ReceiveMode.Nobody ? 0 : 1;
        set => _app.Agent.SetReceiveMode(value == 0 ? ReceiveMode.Nobody : ReceiveMode.TrustedOnly);
    }

    public int LanguageIndex
    {
        get => _app.Agent.Settings.Current.Language switch { "es" => 1, "en" => 2, _ => 0 };
        set
        {
            Update(s => s.Language = value switch { 1 => "es", 2 => "en", _ => "auto" });
            Notice = S.RestartNeeded;
            Raise(nameof(HasNotice));
        }
    }

    public bool ReduceMotion
    {
        get => _app.Agent.Settings.Current.ReduceMotion;
        set => Update(s => s.ReduceMotion = value);
    }

    public bool Sounds
    {
        get => _app.Agent.Settings.Current.Sounds;
        set => Update(s => s.Sounds = value);
    }

    public bool StartWithSystem
    {
        get => _app.Agent.Settings.Current.StartWithSystem;
        set => Update(s => s.StartWithSystem = value);
    }

    public bool ShellIntegration
    {
        get => _app.Agent.Settings.Current.ShellIntegration;
        set => Update(s => s.ShellIntegration = value);
    }

    public bool AllowPublicNetworks
    {
        get => _app.Agent.Settings.Current.AllowPublicNetworks;
        set => Update(s => s.AllowPublicNetworks = value);
    }

    public bool ExcludeVirtual
    {
        get => _app.Agent.Settings.Current.ExcludeVirtualAdapters;
        set => Update(s => s.ExcludeVirtualAdapters = value);
    }

    private void Update(Action<AppSettings> change)
    {
        _app.Agent.Settings.Update(change);
        RaiseAll();
    }

    public void ReloadPeers()
    {
        var current = _app.Agent.Trust.All.OrderByDescending(p => p.Favorite).ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (current.Select(p => p.DeviceId).SequenceEqual(TrustedPeers.Select(p => p.DeviceId)))
            return;
        TrustedPeers.Clear();
        foreach (var p in current)
            TrustedPeers.Add(new TrustedPeerViewModel(p, _app.Agent.Trust));
        Raise(nameof(HasTrustedPeers));
        Raise(nameof(NoTrustedPeers));
    }

    public void ReloadHistory()
    {
        History.Clear();
        foreach (var e in _app.Agent.History.Recent(200))
            History.Add(new HistoryItemViewModel(e));
        Raise(nameof(HasHistory));
        Raise(nameof(NoHistory));
    }

    private void RefreshNotice()
    {
        if (_app.Agent.Trust.WasTampered)
            Notice = S.TamperedTrust;
        else if (!MarkOfTheWeb.IsSupported(Folder))
            Notice = S.MotwUnsupported;
        else
            Notice = null;
        Raise(nameof(HasNotice));
    }
}

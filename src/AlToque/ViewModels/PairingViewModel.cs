using System.Collections.ObjectModel;
using AlToque.Controls.Avatars;
using AlToque.Core.Discovery;
using AlToque.Core.Pairing;
using AlToque.Core.Security;
using AlToque.Localization;
using Avalonia.Media;

namespace AlToque.ViewModels;

/// <summary>
/// Emparejamiento simétrico: ambos abren la ventana, cada uno ve al otro en la lista y cualquiera de los dos lo elige.
/// Luego las dos pantallas muestran el mismo código de 6 dígitos para compararlo.
/// </summary>
public sealed class PairingViewModel : ViewModelBase
{
    private readonly AppController _app;
    private PairingPrompt? _prompt;
    private string _status = S.PairWaiting;
    private string _countdown = "";
    private string? _result;
    private bool _resultOk;
    private bool _connecting;
    private bool _confirmed;
    private bool _autoAccept;
    private string? _pairedId;
    private double _countdownFraction = 1;

    public PairingViewModel(AppController app)
    {
        _app = app;
        MyColor = Brushes2.ForDevice(app.Agent.DeviceId);
        MySymbols = IdentityVisuals.SymbolSequence(app.Agent.DeviceId);
        Matches = new RelayCommand(() => Answer(true));
        DoesNotMatch = new RelayCommand(() => Answer(false));
        AllowPublicNetwork = new RelayCommand(_app.AllowPublicNetwork);
        PairWith = new RelayCommand(async p =>
        {
            if (p is PeerViewModel peer)
                await StartAsync(peer);
        });
        Finish = new RelayCommand(() => FinishRequested?.Invoke());
        TryAgain = new RelayCommand(Reset);
    }

    /// <summary>"Listo": la ventana se cierra.</summary>
    public event Action? FinishRequested;

    public RelayCommand Finish { get; }
    public RelayCommand TryAgain { get; }

    // ── Avatares y pasos ─────────────────────────────────────────────────────────
    public string MyColorHex => IdentityVisuals.ColorHex(_app.Agent.DeviceId);
    public string MyAvatarId => AvatarCatalog.Resolve(_app.Agent.Settings.Current.Avatar, _app.Agent.DeviceId);
    public string PeerColorHex => Prompt is null ? "#888888" : Prompt.PeerColor;
    public string PeerAvatarId => Prompt is null ? AvatarCatalog.InitialId
        : AvatarCatalog.Resolve(Prompt.PeerAvatar ?? _app.Agent.Peers.Find(Prompt.PeerId)?.Avatar, Prompt.PeerId);
    public string PairWithTitle => Prompt is null ? S.PairTitle : S.PairWith(PeerName);
    public string SeesThisCode => S.SeesThisCode(PeerName);
    private string Digits => new(Code.Where(char.IsAsciiDigit).ToArray());
    public IReadOnlyList<string> CodeFirst => Digits.Length == 6 ? [.. Digits[..3].Select(c => c.ToString())] : [];
    public IReadOnlyList<string> CodeLast => Digits.Length == 6 ? [.. Digits[3..].Select(c => c.ToString())] : [];
    public bool IsDone => HasResult && ResultOk;
    public bool IsFailed => HasResult && !ResultOk;
    public string NowTrustedTitle => S.NowTrusted(PeerName);

    public string StepText => IsDone ? S.Step(3, S.StepDone) : HasPrompt ? S.Step(2, S.StepCompare) : S.Step(1, S.StepChoose);

    public double CountdownFraction { get => _countdownFraction; private set => Set(ref _countdownFraction, value); }

    /// <summary>"Aceptar sus envíos sin preguntar", tras emparejar.</summary>
    public bool AutoAccept
    {
        get => _autoAccept;
        set
        {
            if (Set(ref _autoAccept, value) && _pairedId is { } id)
                _app.Agent.Trust.Update(id, p => p.AutoAccept = value);
        }
    }

    private void RaiseSteps()
    {
        Raise(nameof(StepText));
        Raise(nameof(IsDone));
        Raise(nameof(IsFailed));
        Raise(nameof(PeerColorHex));
        Raise(nameof(PeerAvatarId));
        Raise(nameof(PairWithTitle));
        Raise(nameof(SeesThisCode));
        Raise(nameof(CodeFirst));
        Raise(nameof(CodeLast));
        Raise(nameof(NowTrustedTitle));
    }

    public ObservableCollection<PeerViewModel> Candidates { get; } = [];
    public bool HasCandidates => Candidates.Count > 0;
    public bool NoCandidates => Candidates.Count == 0;

    public IBrush MyColor { get; }
    public string MySymbols { get; }
    public string MyAlias => _app.Agent.Settings.Current.Alias;
    public string MyInitial => IdentityVisuals.Initial(MyAlias);

    public RelayCommand Matches { get; }
    public RelayCommand DoesNotMatch { get; }
    public RelayCommand PairWith { get; }
    public RelayCommand AllowPublicNetwork { get; }

    private bool _networkPaused;

    /// <summary>Red pública sin permiso: este equipo no puede recibir el emparejamiento.</summary>
    public bool NetworkPaused { get => _networkPaused; private set => Set(ref _networkPaused, value); }

    public void SyncNetwork(bool paused) => NetworkPaused = paused;

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Countdown { get => _countdown; private set => Set(ref _countdown, value); }

    public PairingPrompt? Prompt
    {
        get => _prompt;
        private set
        {
            if (Set(ref _prompt, value))
            {
                Raise(nameof(HasPrompt));
                Raise(nameof(ShowChooser));
                Raise(nameof(ShowConfirmButtons));
                Raise(nameof(PeerName));
                Raise(nameof(PeerColor));
                Raise(nameof(PeerSymbols));
                Raise(nameof(PeerInitial));
                Raise(nameof(Code));
                RaiseSteps();
            }
        }
    }

    public bool HasPrompt => Prompt is not null && Result is null;
    public bool ShowChooser => Prompt is null && Result is null && !Connecting;
    public string PeerName => Prompt?.PeerName ?? "";
    public IBrush? PeerColor => Prompt is null ? null : Brushes2.FromHex(Prompt.PeerColor);
    public string PeerSymbols => Prompt?.PeerSymbols ?? "";
    public string PeerInitial => IdentityVisuals.Initial(PeerName);
    public string Code => Prompt?.Code ?? "";

    public bool Confirmed
    {
        get => _confirmed;
        private set
        {
            if (Set(ref _confirmed, value))
                Raise(nameof(ShowConfirmButtons));
        }
    }

    public bool ShowConfirmButtons => HasPrompt && !Confirmed;

    public bool Connecting
    {
        get => _connecting;
        private set
        {
            if (Set(ref _connecting, value))
                Raise(nameof(ShowChooser));
        }
    }

    public string? Result
    {
        get => _result;
        private set
        {
            if (Set(ref _result, value))
            {
                Raise(nameof(HasResult));
                Raise(nameof(HasPrompt));
                Raise(nameof(ShowChooser));
                Raise(nameof(ShowConfirmButtons));
                RaiseSteps();
            }
        }
    }

    public bool HasResult => Result is not null;
    public bool ResultOk { get => _resultOk; private set => Set(ref _resultOk, value); }

    public void Reset()
    {
        Prompt = null;
        Result = null;
        Confirmed = false;
        Connecting = false;
        _pairedId = null;
        _autoAccept = true;
        Raise(nameof(AutoAccept));
        Status = S.PairWaiting;
    }

    public void SyncCandidates(IReadOnlyList<PeerInfo> peers)
    {
        var list = peers.Where(p => p.Pairing && p.Presence == PeerPresence.Online && p.EndPoint is not null).ToList();
        for (int i = Candidates.Count - 1; i >= 0; i--)
        {
            if (list.All(p => p.DeviceId != Candidates[i].DeviceId))
                Candidates.RemoveAt(i);
        }
        foreach (var p in list)
        {
            var vm = Candidates.FirstOrDefault(c => c.DeviceId == p.DeviceId);
            if (vm is null)
            {
                vm = new PeerViewModel(p.DeviceId);
                Candidates.Add(vm);
            }
            vm.Update(p);
        }
        Raise(nameof(HasCandidates));
        Raise(nameof(NoCandidates));
    }

    public void Tick()
    {
        var closes = _app.Agent.Pairing.WindowClosesAt;
        Countdown = closes is { } c && Result is null ? $"{(c - DateTimeOffset.UtcNow):m\\:ss}" : "";
        CountdownFraction = closes is { } d && Result is null ? Math.Clamp((d - DateTimeOffset.UtcNow).TotalSeconds / 120, 0, 1) : 0;
    }

    public void ShowPrompt(PairingPrompt prompt)
    {
        Result = null;
        Confirmed = false;
        Connecting = false;
        Prompt = prompt;
        Status = S.PairCompare;
    }

    public void ShowOutcome(PairingOutcome outcome, string peerName)
    {
        Connecting = false;
        _pairedId = outcome == PairingOutcome.Paired ? Prompt?.PeerId : null;
        // Recién emparejado, "recibir sin preguntar" viene activado; aquí se puede apagar.
        _autoAccept = _pairedId is { } id ? _app.Agent.Trust.Find(id)?.AutoAccept ?? true : true;
        Raise(nameof(AutoAccept));
        ResultOk = outcome == PairingOutcome.Paired;
        Result = ResultOk ? S.PairDone(peerName) : S.PairOutcome(outcome.ToString());
    }

    private void Answer(bool matches)
    {
        if (Prompt is null)
            return;
        Prompt.Confirm(matches);
        Confirmed = true;
        Status = matches ? S.PairWaitingOther : S.PairOutcome("Rejected");
    }

    private async Task StartAsync(PeerViewModel peer)
    {
        var target = _app.Agent.Peers.ResolveEndPoint(peer.DeviceId);
        if (target is null)
            return;
        Connecting = true;
        Status = S.PairConnecting(peer.Name);
        var outcome = await _app.Agent.Pairing.PairWithAsync(target, peer.DeviceId, CancellationToken.None);
        ShowOutcome(outcome, peer.Name);
    }
}

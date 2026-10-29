using System.Collections.ObjectModel;
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

    public PairingViewModel(AppController app)
    {
        _app = app;
        MyColor = Brushes2.ForDevice(app.Agent.DeviceId);
        MySymbols = IdentityVisuals.SymbolSequence(app.Agent.DeviceId);
        Matches = new RelayCommand(() => Answer(true));
        DoesNotMatch = new RelayCommand(() => Answer(false));
        PairWith = new RelayCommand(async p =>
        {
            if (p is PeerViewModel peer)
                await StartAsync(peer);
        });
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

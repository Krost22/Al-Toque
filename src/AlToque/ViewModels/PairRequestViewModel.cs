using AlToque.Controls.Avatars;
using AlToque.Core.Pairing;
using AlToque.Core.Security;
using AlToque.Localization;
using Avalonia.Threading;

namespace AlToque.ViewModels;

/// <summary>
/// Aviso de «X quiere conectarse contigo»: llega sin haber abierto «Emparejar». Muestra el código para comparar y,
/// con «Conectar», confirma de este lado; el otro confirma en el suyo.
/// </summary>
public sealed class PairRequestViewModel : ViewModelBase
{
    private readonly PairingPrompt _prompt;
    private readonly Action<PairRequestViewModel> _close;
    private State _state = State.Asking;

    private enum State
    {
        Asking,
        Waiting,
        Done,
        Failed,
    }

    public PairRequestViewModel(PairingPrompt prompt, Action<string> openChat, Action<PairRequestViewModel> close)
    {
        _prompt = prompt;
        _close = close;
        ShownAt = DateTimeOffset.Now;
        AvatarId = AvatarCatalog.Resolve(prompt.PeerAvatar, prompt.PeerId);
        Accept = new RelayCommand(() =>
        {
            _prompt.Confirm(true);
            Set(State.Waiting);
        });
        Decline = new RelayCommand(() =>
        {
            _prompt.Confirm(false);
            _close(this);
        });
        OpenChat = new RelayCommand(() =>
        {
            openChat(PeerId);
            _close(this);
        });
        Close = new RelayCommand(() =>
        {
            // Cerrar sin responder es «Ahora no».
            if (_state == State.Asking)
                _prompt.Confirm(false);
            _close(this);
        });
        prompt.Completed += p => Dispatcher.UIThread.Post(() =>
        {
            Set(p.Outcome == PairingOutcome.Paired ? State.Done : State.Failed);
            FinishedAt = DateTimeOffset.Now;
        });
    }

    public string PeerId => _prompt.PeerId;
    public string PeerName => _prompt.PeerName;
    public string ColorHex => _prompt.PeerColor;
    public string AvatarId { get; }
    public string Initial => IdentityVisuals.Initial(PeerName);
    public string Title => S.PairRequestTitle(PeerName);
    public string CheckText => S.PairRequestCheck(PeerName);
    public IReadOnlyList<string> CodeFirst => Digits.Length == 6 ? [.. Digits[..3].Select(c => c.ToString())] : [];
    public IReadOnlyList<string> CodeLast => Digits.Length == 6 ? [.. Digits[3..].Select(c => c.ToString())] : [];
    private string Digits => _prompt.Code.Replace(" ", "", StringComparison.Ordinal);

    public DateTimeOffset ShownAt { get; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public bool IsAsking => _state == State.Asking;
    public bool IsWaiting => _state == State.Waiting;
    public bool IsDone => _state == State.Done;
    public bool IsFailed => _state == State.Failed;
    public bool ShowCode => _state is State.Asking or State.Waiting;
    public string WaitingText => S.PairRequestWaiting(PeerName);
    public string DoneText => S.PairRequestDone(PeerName);

    public RelayCommand Accept { get; }
    public RelayCommand Decline { get; }
    public RelayCommand OpenChat { get; }
    public RelayCommand Close { get; }

    private void Set(State state)
    {
        if (_state == state)
            return;
        _state = state;
        RaiseAll();
    }
}

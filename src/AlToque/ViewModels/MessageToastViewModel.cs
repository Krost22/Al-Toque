using AlToque.Controls.Avatars;
using AlToque.Core.Security;
using AlToque.Localization;

namespace AlToque.ViewModels;

/// <summary>Aviso flotante de un mensaje nuevo; si llegan más del mismo compañero, se acumulan en el mismo aviso.</summary>
public sealed class MessageToastViewModel : ViewModelBase
{
    private string _text;
    private int _count = 1;

    public MessageToastViewModel(string peerId, string name, string? avatar, string text, Action<MessageToastViewModel> reply, Action<MessageToastViewModel> close)
    {
        PeerId = peerId;
        Name = name;
        ColorHex = IdentityVisuals.ColorHex(peerId);
        AvatarId = AvatarCatalog.Resolve(avatar, peerId);
        Initial = IdentityVisuals.Initial(name);
        _text = text;
        Reply = new RelayCommand(() => reply(this));
        Close = new RelayCommand(() => close(this));
        ShownAt = DateTimeOffset.Now;
    }

    public string PeerId { get; }
    public string Name { get; }
    public string ColorHex { get; }
    public string AvatarId { get; }
    public string Initial { get; }
    public RelayCommand Reply { get; }
    public RelayCommand Close { get; }
    public DateTimeOffset ShownAt { get; private set; }
    public string Text { get => _text; private set => Set(ref _text, value); }
    public string? CountText => _count > 1 ? S.ChatUnread(_count) : null;
    public bool HasCount => _count > 1;

    public void Add(string text)
    {
        _count++;
        Text = text;
        ShownAt = DateTimeOffset.Now;
        Raise(nameof(CountText));
        Raise(nameof(HasCount));
    }
}

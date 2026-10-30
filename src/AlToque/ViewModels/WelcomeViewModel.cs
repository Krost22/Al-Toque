using AlToque.Controls.Avatars;
using AlToque.Core.Security;

namespace AlToque.ViewModels;

/// <summary>Primera ejecución: nombre y avatar con los que te verán tus compañeros.</summary>
public sealed class WelcomeViewModel : ViewModelBase
{
    private string _alias;
    private string _avatarId;

    public WelcomeViewModel(string deviceId, string alias, string? avatar)
    {
        _alias = alias;
        ColorHex = IdentityVisuals.ColorHex(deviceId);
        _avatarId = AvatarCatalog.Resolve(avatar, deviceId);
        Choices = AvatarCatalog.Ids.Select(id => new AvatarChoiceViewModel(id, ColorHex, Initial, Pick) { IsSelected = id == _avatarId }).ToList();
    }

    public string ColorHex { get; }
    public IReadOnlyList<AvatarChoiceViewModel> Choices { get; }

    public string Alias
    {
        get => _alias;
        set
        {
            if (!Set(ref _alias, value))
                return;
            Raise(nameof(Initial));
            foreach (var c in Choices)
                c.Initial = Initial;
        }
    }

    public string Initial => IdentityVisuals.Initial(Alias);
    public string AvatarId { get => _avatarId; private set => Set(ref _avatarId, value); }

    private void Pick(AvatarChoiceViewModel choice)
    {
        foreach (var c in Choices)
            c.IsSelected = c == choice;
        AvatarId = choice.Id;
    }
}

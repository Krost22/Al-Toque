using AlToque.Controls.Avatars;

namespace AlToque.ViewModels;

/// <summary>Un personaje en el selector de avatares.</summary>
public sealed class AvatarChoiceViewModel : ViewModelBase
{
    private bool _isSelected;

    public AvatarChoiceViewModel(string id, string colorHex, string initial, Action<AvatarChoiceViewModel> pick)
    {
        Id = id;
        ColorHex = colorHex;
        _initial = initial;
        Name = AvatarCatalog.NameOf(id);
        Pick = new RelayCommand(() => pick(this));
    }

    public string Id { get; }
    public string Name { get; }
    public string ColorHex { get; }
    private string _initial = "";

    public string Initial { get => _initial; set => Set(ref _initial, value); }
    public RelayCommand Pick { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
                Raise(nameof(Ring));
        }
    }

    public AvatarRing Ring => IsSelected ? AvatarRing.Selected : AvatarRing.None;
}

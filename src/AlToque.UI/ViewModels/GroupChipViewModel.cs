namespace AlToque.ViewModels;

/// <summary>Una ficha de grupo sobre la lista de compañeros: filtra la lista y recibe compañeros arrastrados.</summary>
public sealed class GroupChipViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _isDropTarget;
    private int _count;

    /// <param name="id">null para «Todos».</param>
    /// <param name="isNew">La ficha «+ Grupo», al final, que crea uno nuevo.</param>
    public GroupChipViewModel(string? id, string name, int count, bool isNew = false)
    {
        Id = id;
        Name = name;
        _count = count;
        IsNew = isNew;
    }

    public string? Id { get; }
    public string Name { get; }
    public bool IsAll => Id is null && !IsNew;
    public bool IsNew { get; }
    public bool IsGroup => Id is not null;

    public int Count
    {
        get => _count;
        set
        {
            if (Set(ref _count, value))
                Raise(nameof(Label));
        }
    }

    /// <summary>"Oficina · 3"; «Todos» va sin número.</summary>
    public string Label => IsGroup ? $"{Name} · {Count}" : Name;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Se está arrastrando un compañero encima.</summary>
    public bool IsDropTarget { get => _isDropTarget; set => Set(ref _isDropTarget, value); }
}

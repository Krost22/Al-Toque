using System.Collections.ObjectModel;

namespace AlToque.ViewModels;

/// <summary>La mini ventana flotante de Chasqui durante las transferencias.</summary>
public sealed class TransfersViewModel : ViewModelBase
{
    private bool _reduceMotion;

    public ObservableCollection<TransferViewModel> Items { get; } = [];

    public bool ReduceMotion { get => _reduceMotion; set => Set(ref _reduceMotion, value); }
}

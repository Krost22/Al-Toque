using System.Collections.ObjectModel;

namespace AlToque.ViewModels;

/// <summary>La mini ventana flotante de Chasqui durante las transferencias.</summary>
public sealed class TransfersViewModel : ViewModelBase
{
    private bool _reduceMotion;

    public ObservableCollection<TransferViewModel> Items { get; } = [];

    /// <summary>Envíos a varios compañeros: una tarjeta por envío, con el progreso de todos.</summary>
    public ObservableCollection<GroupSendViewModel> Groups { get; } = [];

    public bool IsEmpty => Items.Count == 0 && Groups.Count == 0;

    public bool ReduceMotion { get => _reduceMotion; set => Set(ref _reduceMotion, value); }
}

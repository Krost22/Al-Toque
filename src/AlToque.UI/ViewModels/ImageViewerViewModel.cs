using AlToque.Controls;
using AlToque.Localization;
using Avalonia.Media.Imaging;

namespace AlToque.ViewModels;

/// <summary>Visor de fotos: una en grande, con anterior y siguiente entre las de la conversación.</summary>
public sealed class ImageViewerViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> _paths;
    private readonly int _maxSide;
    private int _index;
    private Bitmap? _image;
    private bool _isLoading;
    private bool _isUnreadable;
    private int _loadToken;

    /// <param name="maxSide">Lado mayor en píxeles: el de la pantalla (no hace falta más).</param>
    public ImageViewerViewModel(IReadOnlyList<string> paths, int index, int maxSide, Action<string>? openWith, Action<string>? showInFolder, Action close)
    {
        _paths = paths.Count > 0 ? paths : throw new ArgumentException("Sin fotos.", nameof(paths));
        _index = Math.Clamp(index, 0, paths.Count - 1);
        _maxSide = maxSide;
        Previous = new RelayCommand(() => Go(-1), () => _paths.Count > 1);
        Next = new RelayCommand(() => Go(+1), () => _paths.Count > 1);
        OpenWith = new RelayCommand(() => openWith?.Invoke(Current), () => openWith is not null);
        ShowInFolder = new RelayCommand(() => showInFolder?.Invoke(Current), () => showInFolder is not null);
        Close = new RelayCommand(close);
        Load();
    }

    public string Current => _paths[_index];
    public string Name => System.IO.Path.GetFileName(Current);
    public string Counter => _paths.Count > 1 ? S.ViewerCounter(_index + 1, _paths.Count) : "";
    public bool HasMany => _paths.Count > 1;

    public RelayCommand Previous { get; }
    public RelayCommand Next { get; }
    public RelayCommand OpenWith { get; }
    public RelayCommand ShowInFolder { get; }
    public RelayCommand Close { get; }

    public Bitmap? Image
    {
        get => _image;
        private set => Set(ref _image, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public bool IsUnreadable
    {
        get => _isUnreadable;
        private set => Set(ref _isUnreadable, value);
    }

    /// <summary>Pasa a la anterior o a la siguiente (da la vuelta al llegar al final).</summary>
    public void Go(int step)
    {
        if (_paths.Count < 2)
            return;
        _index = ((_index + step) % _paths.Count + _paths.Count) % _paths.Count;
        Raise(nameof(Current));
        Raise(nameof(Name));
        Raise(nameof(Counter));
        Load();
    }

    private async void Load()
    {
        var token = ++_loadToken;
        IsLoading = true;
        IsUnreadable = false;
        var bitmap = await ImageThumbnails.LoadAsync(Current, _maxSide);
        if (token != _loadToken)
            return;
        Image = bitmap;
        IsLoading = false;
        IsUnreadable = bitmap is null;
        // La siguiente y la anterior quedan listas para pasar sin espera.
        if (_paths.Count > 1)
        {
            _ = ImageThumbnails.LoadAsync(_paths[(_index + 1) % _paths.Count], _maxSide);
            _ = ImageThumbnails.LoadAsync(_paths[(_index - 1 + _paths.Count) % _paths.Count], _maxSide);
        }
    }
}

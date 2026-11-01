using System.Collections.ObjectModel;
using AlToque.Controls;
using AlToque.Controls.Avatars;
using AlToque.Core.Agent;
using AlToque.Core.Chat;
using AlToque.Core.Discovery;
using AlToque.Core.Security;
using AlToque.Localization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AlToque.ViewModels;

/// <summary>Lo que la conversación le pide a la app que la muestra; cada plataforma lo resuelve a su manera.</summary>
public sealed class ChatActions
{
    /// <summary>Elegir archivos y mandárselos (cada plataforma tiene su selector).</summary>
    public Func<Task>? Attach { get; init; }

    public Action<string>? OpenPath { get; init; }
    public Action<string>? ShowInFolder { get; init; }

    /// <summary>Volver a la lista de compañeros (no lo tiene el chat fijado en la esquina).</summary>
    public Action? Back { get; init; }

    /// <summary>Pregunta antes de borrar la conversación.</summary>
    public Func<string, Task<bool>>? Confirm { get; init; }

    /// <summary>Ver las fotos en grande: todas las de la conversación y la que se tocó.</summary>
    public Action<IReadOnlyList<string>, int>? ViewPhotos { get; init; }

    /// <summary>Fijar la conversación en la esquina de la pantalla, o devolverla al panel si ya lo está.</summary>
    public Action? TogglePin { get; init; }

    public Action? Minimize { get; init; }
}

/// <summary>Una conversación abierta: mensajes y archivos con ese compañero, y el cuadro para escribir.</summary>
public sealed class ChatViewModel : ViewModelBase, IDisposable
{
    private readonly AgentHost _agent;
    private readonly ChatActions _actions;
    private string _draft = "";
    private string _name = "";
    private string _avatarId;
    private bool _isVisible;
    private bool _disposed;

    /// <param name="pinned">Vive en su propia ventana en la esquina, no en el panel.</param>
    public ChatViewModel(AgentHost agent, string peerId, ChatActions actions, bool pinned = false)
    {
        _agent = agent;
        _actions = actions;
        IsPinned = pinned;
        PeerId = peerId;
        ColorHex = IdentityVisuals.ColorHex(peerId);
        _avatarId = AvatarCatalog.DefaultFor(peerId);
        SendCommand = new RelayCommand(SendDraft, () => ChatStore.Clean(Draft).Length > 0);
        Attach = new RelayCommand(() => _ = actions.Attach?.Invoke(), () => actions.Attach is not null);
        Back = new RelayCommand(() => actions.Back?.Invoke());
        TogglePin = new RelayCommand(() => actions.TogglePin?.Invoke(), () => actions.TogglePin is not null);
        Minimize = new RelayCommand(() => actions.Minimize?.Invoke(), () => actions.Minimize is not null);
        Clear = new RelayCommand(async () =>
        {
            if (actions.Confirm is null || await actions.Confirm(S.ChatClearConfirm(Name)))
                _agent.Chats.Clear(PeerId);
        });
        _agent.Chats.Changed += OnChatsChanged;
        _agent.Links.PresenceChanged += OnPeerChanged;
        _agent.Peers.Changed += OnPeersChanged;
        RefreshPeer();
        Sync();
    }

    public string PeerId { get; }
    public string ColorHex { get; }
    public string Name { get => _name; private set => Set(ref _name, value); }
    public string AvatarId { get => _avatarId; private set => Set(ref _avatarId, value); }
    public string Initial => IdentityVisuals.Initial(Name);
    public bool IsOnline => _agent.Links.IsLinked(PeerId) || _agent.Peers.Find(PeerId)?.Presence == PeerPresence.Online;
    public string StatusText => IsOnline ? S.ChatOnline : S.ChatOffline;

    public ObservableCollection<ChatItemViewModel> Items { get; } = [];
    public bool IsEmpty => Items.Count == 0;
    public string EmptyText => S.ChatEmpty(Name);

    public RelayCommand SendCommand { get; }
    public RelayCommand Attach { get; }
    public RelayCommand Back { get; }
    public RelayCommand Clear { get; }
    public RelayCommand TogglePin { get; }
    public RelayCommand Minimize { get; }

    public bool IsPinned { get; }
    public bool ShowBack => !IsPinned && _actions.Back is not null;
    public bool CanPin => _actions.TogglePin is not null;
    public bool CanMinimize => _actions.Minimize is not null;
    public Ic PinIcon => IsPinned ? Ic.PinOff : Ic.Pin;
    public string PinTip => IsPinned ? S.ChatUnpin : S.ChatPin;

    /// <summary>Título de la ventana del chat fijado (con los no leídos, que se ven en la barra de tareas).</summary>
    public string WindowTitle
    {
        get
        {
            var unread = _agent.Chats.Unread(PeerId);
            return unread > 0 ? $"({unread}) {S.ChatWindowTitle(Name)}" : S.ChatWindowTitle(Name);
        }
    }

    public string Draft
    {
        get => _draft;
        set
        {
            if (Set(ref _draft, value))
                SendCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>La conversación está a la vista con la ventana activa: lo que llegue cuenta como leído.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (Set(ref _isVisible, value) && value)
                _agent.Chats.MarkRead(PeerId);
        }
    }

    /// <summary>Llegó o se escribió algo nuevo: la vista baja hasta el final.</summary>
    public event Action? MessageAdded;

    private void SendDraft()
    {
        var text = ChatStore.Clean(Draft);
        if (text.Length == 0)
            return;
        Draft = "";
        _agent.SendChat(PeerId, text);
    }

    private void OnChatsChanged(string peerId, ChatMessage? message)
    {
        if (peerId == PeerId)
            Dispatcher.UIThread.Post(() =>
            {
                Sync();
                Raise(nameof(WindowTitle));
            });
    }

    /// <summary>Tocaron una foto: el visor recibe todas las de la conversación, en orden, para pasar de una a otra.</summary>
    private void ViewPhoto(string path)
    {
        if (_actions.ViewPhotos is not { } view)
        {
            _actions.OpenPath?.Invoke(path);
            return;
        }
        var all = Items.Where(i => i.IsPhotos).SelectMany(i => i.PhotoPaths).Where(File.Exists).ToList();
        var index = all.IndexOf(path);
        if (index < 0)
        {
            all = [path];
            index = 0;
        }
        view(all, index);
    }

    private void OnPeerChanged(string peerId)
    {
        if (peerId == PeerId)
            Dispatcher.UIThread.Post(RefreshPeer);
    }

    private void OnPeersChanged() => Dispatcher.UIThread.Post(RefreshPeer);

    private void RefreshPeer()
    {
        if (_disposed)
            return;
        var info = _agent.Peers.Find(PeerId);
        var trusted = _agent.Trust.Find(PeerId);
        Name = info?.DisplayName ?? trusted?.DisplayName ?? IdentityVisuals.ShortCode(PeerId);
        AvatarId = AvatarCatalog.Resolve(info?.Avatar ?? trusted?.Avatar, PeerId);
        Raise(nameof(Initial));
        Raise(nameof(IsOnline));
        Raise(nameof(StatusText));
        Raise(nameof(EmptyText));
        Raise(nameof(WindowTitle));
    }

    /// <summary>Lleva la lista al estado del almacén: casi siempre solo cambia el final.</summary>
    private void Sync()
    {
        if (_disposed)
            return;
        var messages = _agent.Chats.Messages(PeerId);
        var ids = messages.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(Items[i].Id))
                Items.RemoveAt(i);
        }
        var added = false;
        for (int i = 0; i < messages.Count; i++)
        {
            var m = messages[i];
            if (i < Items.Count && Items[i].Id == m.Id)
            {
                Items[i].Refresh(m);
                continue;
            }
            var at = IndexOf(m.Id, i);
            if (at >= 0)
            {
                Items.Move(at, i);
                Items[i].Refresh(m);
            }
            else
            {
                Items.Insert(i, new ChatItemViewModel(m, _agent.DeviceId, _actions.OpenPath, _actions.ShowInFolder, ViewPhoto));
                added = true;
            }
        }
        for (int i = 0; i < Items.Count; i++)
            Items[i].DayHeader = i == 0 || Items[i].LocalDate != Items[i - 1].LocalDate ? DayLabel(Items[i].LocalDate) : null;
        Raise(nameof(IsEmpty));
        if (added)
        {
            if (IsVisible)
                _agent.Chats.MarkRead(PeerId);
            MessageAdded?.Invoke();
        }
    }

    private int IndexOf(string id, int from)
    {
        for (int i = from; i < Items.Count; i++)
        {
            if (Items[i].Id == id)
                return i;
        }
        return -1;
    }

    private static string DayLabel(DateTime day)
    {
        var today = DateTime.Today;
        if (day == today)
            return S.ChatToday;
        if (day == today.AddDays(-1))
            return S.ChatYesterday;
        if (day > today.AddDays(-7))
            return S.Culture.TextInfo.ToTitleCase(day.ToString("dddd", S.Culture));
        return day.ToString(day.Year == today.Year ? "d MMMM" : "d MMMM yyyy", S.Culture);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _agent.Chats.Changed -= OnChatsChanged;
        _agent.Links.PresenceChanged -= OnPeerChanged;
        _agent.Peers.Changed -= OnPeersChanged;
    }
}

/// <summary>Una burbuja del chat: un mensaje o un envío de archivos.</summary>
public sealed class ChatItemViewModel : ViewModelBase
{
    /// <summary>Lado mayor de las miniaturas, en píxeles (el doble de lo que ocupan, para pantallas de alta densidad).</summary>
    private const int SinglePhotoPixels = 560;
    private const int GridPhotoPixels = 260;
    private const int MaxGridPhotos = 4;

    private readonly Action<string>? _openPath;
    private readonly Action<string>? _showInFolder;
    private readonly Action<string>? _viewPhoto;
    private ChatMessage _message;
    private string? _dayHeader;
    private string _photoKey = "";
    private IReadOnlyList<ChatPhotoViewModel> _photos = [];

    public ChatItemViewModel(ChatMessage message, string me, Action<string>? openPath, Action<string>? showInFolder, Action<string>? viewPhoto = null)
    {
        _message = message;
        _openPath = openPath;
        _showInFolder = showInFolder;
        _viewPhoto = viewPhoto;
        Id = message.Id;
        IsMine = message.Author == me;
        var local = message.Time.ToLocalTime();
        LocalDate = local.Date;
        TimeText = local.ToString("t", S.Culture);
        Open = new RelayCommand(OpenFiles);
        ShowInFolder = new RelayCommand(() =>
        {
            if (FirstPath is { } p)
                _showInFolder?.Invoke(p);
        });
        UpdatePhotos();
    }

    public string Id { get; }
    public bool IsMine { get; }
    public bool IsTheirs => !IsMine;
    public DateTime LocalDate { get; }
    public string TimeText { get; }
    public RelayCommand Open { get; }
    public RelayCommand ShowInFolder { get; }

    public string? DayHeader
    {
        get => _dayHeader;
        set
        {
            if (Set(ref _dayHeader, value))
                Raise(nameof(HasDayHeader));
        }
    }

    public bool HasDayHeader => DayHeader is not null;

    public bool IsText => !_message.IsFiles;
    public bool IsFiles => _message.IsFiles;

    /// <summary>Archivos que no son solo fotos (o que no llegaron): la tarjeta de archivo de siempre.</summary>
    public bool IsFileCard => _message.IsFiles && !IsPhotos;
    public string? Text => _message.Text;
    public bool HasText => !string.IsNullOrEmpty(_message.Text);

    // ── Texto propio: pendiente o entregado ─────────────────────────────────────

    public bool IsDelivered => IsMine && IsText && _message.Delivered;
    public bool IsPending => IsMine && IsText && !_message.Delivered;
    public string DeliveryTip => IsDelivered ? S.ChatDelivered : S.ChatPending;

    // ── Archivos ────────────────────────────────────────────────────────────────

    private string FirstName => _message.Files is { Count: > 0 } f ? f[0].Name : "";

    private string? FirstPath => _message.Files is { Count: > 0 } f ? f[0].Path : null;

    public string FileTitle => S.ItemsNamed(FirstName, Math.Max(1, _message.FileCount));

    public Ic FileIcon => IsFileError ? Ic.Alert : _message.FileCount > 1 ? Ic.Folder : HistoryItemViewModel.IconFor(FirstName);

    public bool IsFileActive => _message.Result == ChatResults.Active;
    public bool IsFileError => _message.Result is ChatResults.Failed or ChatResults.Declined or ChatResults.Cancelled or ChatResults.Partial;

    public string FileDetail
    {
        get
        {
            var size = _message.TotalBytes > 0 ? Format.Bytes(_message.TotalBytes) : S.ChatFiles(Math.Max(1, _message.FileCount));
            var state = _message.Result switch
            {
                ChatResults.Active => IsMine ? S.ChatSending : S.ChatReceiving,
                ChatResults.Declined => S.ChatFileDeclined,
                ChatResults.Cancelled => S.ChatFileCancelled,
                ChatResults.Partial => S.ChatFilePartial,
                ChatResults.Failed => S.ChatFileFailed,
                _ => null,
            };
            return state is null ? size : $"{size} · {state}";
        }
    }

    /// <summary>Lo recibido (o enviado) que todavía existe en el disco se puede abrir.</summary>
    public bool CanOpen => _message.Result is ChatResults.Ok or ChatResults.Partial && FirstPath is { } p && (File.Exists(p) || Directory.Exists(p));

    private void OpenFiles()
    {
        if (FirstPath is not { } path)
            return;
        if (_message.FileCount == 1 && File.Exists(path))
            _openPath?.Invoke(path);
        else
            _showInFolder?.Invoke(path);
    }

    // ── Fotos ───────────────────────────────────────────────────────────────────

    /// <summary>Solo imágenes (hasta 20) que llegaron bien o están viajando: se ven como fotos, no como archivos.</summary>
    public bool IsPhotos { get; private set; }

    public IReadOnlyList<ChatPhotoViewModel> Photos => _photos;
    public ChatPhotoViewModel? FirstPhoto => _photos.Count > 0 ? _photos[0] : null;
    public bool IsSinglePhoto => IsPhotos && _photos.Count == 1;
    public bool IsPhotoGrid => IsPhotos && _photos.Count > 1;

    /// <summary>Las rutas de todas las fotos del envío (para el visor), no solo las cuatro que se muestran.</summary>
    public IEnumerable<string> PhotoPaths => IsPhotos && _message.Files is { } files
        ? files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).Select(f => f.Path).OfType<string>()
        : [];

    /// <summary>La hora sobre la foto; mientras viaja, también el estado.</summary>
    public string PhotoMeta => IsFileActive ? $"{(IsMine ? S.ChatSending : S.ChatReceiving)} · {TimeText}" : TimeText;

    private void UpdatePhotos()
    {
        var files = _message.Files;
        var photos = _message.IsFiles && !IsFileError && files is { Count: > 0 } && files.Count == Math.Max(1, _message.FileCount)
                     && files.All(f => ImageThumbnails.IsImage(f.Name));
        var key = photos ? string.Join('|', files!.Select(f => f.Path ?? "")) : "";
        if (key == _photoKey && photos == IsPhotos)
            return;
        _photoKey = key;
        IsPhotos = photos;
        if (!photos || files is null)
        {
            _photos = [];
            return;
        }
        // En orden de nombre también para lo guardado antes de que el almacén los ordenara.
        files = [.. files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)];
        var shown = files.Take(MaxGridPhotos).ToList();
        _photos = shown.Select((f, i) => new ChatPhotoViewModel(f.Path, f.Name,
            i == shown.Count - 1 ? files.Count - shown.Count : 0, _viewPhoto)).ToList();
        var pixels = _photos.Count == 1 ? SinglePhotoPixels : GridPhotoPixels;
        foreach (var photo in _photos)
            photo.Load(pixels);
    }

    public void Refresh(ChatMessage message)
    {
        _message = message;
        UpdatePhotos();
        RaiseAll();
    }
}

/// <summary>Una foto dentro de una burbuja: su miniatura se carga aparte, sin trabar la conversación.</summary>
public sealed class ChatPhotoViewModel : ViewModelBase
{
    private Bitmap? _thumb;
    private bool _isMissing;

    /// <param name="extra">Cuántas fotos más hay detrás de esta (se muestra "+N" sobre la última).</param>
    public ChatPhotoViewModel(string? path, string name, int extra, Action<string>? view)
    {
        Path = path;
        Name = name;
        Extra = extra;
        View = new RelayCommand(() =>
        {
            if (Path is { } p && Thumb is not null)
                view?.Invoke(p);
        });
    }

    public string? Path { get; }
    public string Name { get; }
    public int Extra { get; }
    public bool HasExtra => Extra > 0;
    public string ExtraText => "+" + Extra;
    public RelayCommand View { get; }

    public Bitmap? Thumb
    {
        get => _thumb;
        private set
        {
            if (Set(ref _thumb, value))
            {
                Raise(nameof(HasThumb));
                Raise(nameof(IsPlaceholder));
            }
        }
    }

    public bool HasThumb => Thumb is not null;
    public bool IsPlaceholder => Thumb is null;

    /// <summary>Ya no está en el disco (se borró o se movió) o no se pudo leer.</summary>
    public bool IsMissing
    {
        get => _isMissing;
        private set => Set(ref _isMissing, value);
    }

    public async void Load(int maxSide)
    {
        if (Path is null)
            return;
        var bitmap = await ImageThumbnails.LoadAsync(Path, maxSide);
        Dispatcher.UIThread.Post(() =>
        {
            Thumb = bitmap;
            IsMissing = bitmap is null;
        });
    }
}

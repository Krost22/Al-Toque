using System.Collections.ObjectModel;
using AlToque.Controls;
using AlToque.Controls.Avatars;
using AlToque.Core.Agent;
using AlToque.Core.Chat;
using AlToque.Core.Discovery;
using AlToque.Core.Security;
using AlToque.Localization;
using Avalonia.Threading;

namespace AlToque.ViewModels;

/// <summary>Una conversación abierta: mensajes y archivos con ese compañero, y el cuadro para escribir.</summary>
public sealed class ChatViewModel : ViewModelBase, IDisposable
{
    private readonly AgentHost _agent;
    private readonly Action<string>? _openPath;
    private readonly Action<string>? _showInFolder;
    private string _draft = "";
    private string _name = "";
    private string _avatarId;
    private bool _isVisible;
    private bool _disposed;

    /// <param name="attach">Elegir archivos y mandárselos (cada plataforma tiene su selector).</param>
    /// <param name="back">Volver a la lista de compañeros.</param>
    /// <param name="confirm">Pregunta antes de borrar la conversación.</param>
    public ChatViewModel(AgentHost agent, string peerId, Func<Task>? attach, Action<string>? openPath, Action<string>? showInFolder, Action? back,
        Func<string, Task<bool>>? confirm = null)
    {
        _agent = agent;
        _openPath = openPath;
        _showInFolder = showInFolder;
        PeerId = peerId;
        ColorHex = IdentityVisuals.ColorHex(peerId);
        _avatarId = AvatarCatalog.DefaultFor(peerId);
        SendCommand = new RelayCommand(SendDraft, () => ChatStore.Clean(Draft).Length > 0);
        Attach = new RelayCommand(() => _ = attach?.Invoke(), () => attach is not null);
        Back = new RelayCommand(() => back?.Invoke());
        Clear = new RelayCommand(async () =>
        {
            if (confirm is null || await confirm(S.ChatClearConfirm(Name)))
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
            Dispatcher.UIThread.Post(Sync);
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
                Items.Insert(i, new ChatItemViewModel(m, _agent.DeviceId, _openPath, _showInFolder));
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
    private readonly Action<string>? _openPath;
    private readonly Action<string>? _showInFolder;
    private ChatMessage _message;
    private string? _dayHeader;

    public ChatItemViewModel(ChatMessage message, string me, Action<string>? openPath, Action<string>? showInFolder)
    {
        _message = message;
        _openPath = openPath;
        _showInFolder = showInFolder;
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

    public void Refresh(ChatMessage message)
    {
        _message = message;
        RaiseAll();
    }
}

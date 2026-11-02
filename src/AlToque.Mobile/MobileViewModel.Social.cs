using System.Collections.ObjectModel;
using AlToque.Controls;
using AlToque.Core.Agent;
using AlToque.Core.Chat;
using AlToque.Core.Discovery;
using AlToque.Core.Pairing;
using AlToque.Core.Storage;
using AlToque.Localization;
using AlToque.ViewModels;
using Avalonia.Platform.Storage;

namespace AlToque.Mobile;

/// <summary>
/// Lo mismo que en el escritorio, en el teléfono: chats con fotos, "Cerca de ti" para conectar en pocos toques, favoritos,
/// grupos, enviar a varios y "Mantener conectado".
/// </summary>
public sealed partial class MobileViewModel
{
    private void InitSocial()
    {
        OpenPeerActions = new RelayCommand(p =>
        {
            if (p is not PeerViewModel peer)
                return;
            ActionPeer = peer;
            RefreshActionGroups();
            Open(MobileSheet.PeerActions);
        });
        ActionChat = new RelayCommand(() =>
        {
            if (ActionPeer is { IsTrusted: true } peer)
                OpenChat(peer.DeviceId);
        });
        ActionSendFiles = new RelayCommand(() =>
        {
            if (ActionPeer is { IsTrusted: true } peer)
            {
                Open(MobileSheet.None);
                _ = SendFilesToAsync(peer.DeviceId);
            }
        });
        ActionToggleFavorite = new RelayCommand(() =>
        {
            if (ActionPeer is { IsTrusted: true } peer)
            {
                _agent.Trust.Update(peer.DeviceId, t => t.Favorite = !t.Favorite);
                Raise(nameof(FavoriteActionText));
            }
        });
        ActionRemove = new RelayCommand(async () =>
        {
            if (ActionPeer is not { } peer)
                return;
            if (!await AskAsync(peer.IsTrusted ? S.RemovePeerConfirm(peer.Name) : S.HidePeerConfirm(peer.Name)))
                return;
            Open(MobileSheet.None);
            if (peer.IsTrusted)
            {
                _agent.Trust.Remove(peer.DeviceId);
                SaveGroups(g => ContactGroups.Prune(g, id => _agent.Trust.Find(id) is not null));
            }
            else
            {
                _agent.Block(peer.DeviceId);
            }
            RefreshPeers();
        });
        ActionConnect = new RelayCommand(() =>
        {
            if (ActionPeer is { IsTrusted: false } peer)
                RequestConnection(peer);
        });
        ActionNewGroup = new RelayCommand(() =>
        {
            if (ActionPeer is { IsTrusted: true } peer)
                AskGroupName(null, [peer.DeviceId]);
        });

        SelectGroupChip = new RelayCommand(p =>
        {
            if (p is GroupChipViewModel { IsNew: true })
                AskGroupName(null, SelectedIds);
            else if (p is GroupChipViewModel chip)
                FilterBy(chip);
        });
        OpenGroupActions = new RelayCommand(() =>
        {
            RefreshArchived();
            Open(MobileSheet.GroupActions);
        });
        GroupSend = new RelayCommand(() =>
        {
            if (FilteredGroup is { } g)
            {
                Open(MobileSheet.None);
                _ = SendToGroupAsync(g);
            }
        });
        GroupRename = new RelayCommand(() =>
        {
            if (FilteredGroup is { } g)
                AskGroupName(g.Id, []);
        });
        GroupArchive = new RelayCommand(() =>
        {
            if (FilteredGroup is { } g)
            {
                SaveGroups(list => ContactGroups.SetArchived(list, g.Id, true));
                Open(MobileSheet.None);
            }
        });
        GroupDelete = new RelayCommand(async () =>
        {
            if (FilteredGroup is not { } g || !await AskAsync(S.GroupDeleteConfirm(g.Name)))
                return;
            SaveGroups(list => ContactGroups.Delete(list, g.Id));
            Open(MobileSheet.None);
        });
        SaveGroupName = new RelayCommand(() =>
        {
            var name = ContactGroups.CleanName(GroupNameInput);
            if (name is null)
                return;
            if (_groupNameFor is { } id)
                SaveGroups(g => ContactGroups.Rename(g, id, name));
            else
            {
                var members = _groupNameMembers;
                SaveGroups(g => ContactGroups.Create(g, name, members).Groups);
                if (members.Count > 1)
                    IsSelecting = false;
            }
            Open(MobileSheet.None);
        });

        StartSelecting = new RelayCommand(() => IsSelecting = true);
        CancelSelecting = new RelayCommand(() => IsSelecting = false);
        SendSelected = new RelayCommand(async () =>
        {
            var ids = SelectedIds;
            if (ids.Count == 0)
                return;
            IsSelecting = false;
            if (HasPending)
            {
                var (paths, copies) = (_pending, _pendingCopyDir);
                _pendingCopyDir = null;
                SetPending([], null);
                await SendToManyAsync(ids, paths, copies, null);
            }
            else if (await PickMaterializedAsync() is { } picked)
            {
                await SendToManyAsync(ids, picked.Paths, picked.Dir, null);
            }
        }, () => SelectedCount > 0);

        ConfirmYes = new RelayCommand(() => AnswerConfirm(true));
        ConfirmNo = new RelayCommand(() => AnswerConfirm(false));

        if (KeepConnected)
            _platform.SetKeepAlive(true);
    }

    private string NameOf(string peerId) => _agent.Peers.Find(peerId)?.DisplayName ?? _agent.Trust.Find(peerId)?.DisplayName ?? "";

    /// <summary>Elegir archivos y dejarlos listos (copiados a la carpeta temporal) sin ponerlos como pendientes.</summary>
    private async Task<(IReadOnlyList<string> Paths, string? Dir)?> PickMaterializedAsync()
    {
        if (PickFiles is null)
            return null;
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await PickFiles();
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudo abrir el selector de archivos: " + ex.Message);
            return null;
        }
        if (files.Count == 0)
            return null;
        IsPreparing = true;
        try
        {
            var (paths, dir) = await OutgoingFiles.MaterializeAsync(files, _platform.CacheDirectory);
            return (paths, dir);
        }
        catch (Exception ex)
        {
            AgentLog.Write("No se pudieron preparar los archivos: " + ex.Message);
            ShowFlash(S.ErrorText("E_IO", "", ex.Message));
            return null;
        }
        finally
        {
            IsPreparing = false;
            foreach (var f in files)
                f.Dispose();
        }
    }

    private async Task SendFilesToAsync(string peerId)
    {
        if (await PickMaterializedAsync() is { } picked)
            await SendAsync(peerId, NameOf(peerId), picked.Paths, picked.Dir);
    }

    // ── Cerca de ti: conectar en pocos toques ────────────────────────────────────

    public ObservableCollection<PeerViewModel> Nearby { get; } = [];
    public bool HasNearby => Nearby.Count > 0;

    private (string PeerId, IReadOnlyList<string> Paths, string? Copies)? _afterPairing;

    /// <summary>Tocar un equipo cercano: se le pide conectarse; lo que había para enviar sale al quedar conectados.</summary>
    private void RequestConnection(PeerViewModel peer)
    {
        _afterPairing = HasPending ? (peer.DeviceId, _pending, _pendingCopyDir) : null;
        if (HasPending)
        {
            _pendingCopyDir = null; // Las copias pasan a esperar el emparejamiento.
            SetPending([], null);
        }
        Open(MobileSheet.Pair);
        _ = PairWithAsync(peer);
    }

    /// <summary>Terminó un emparejamiento: lo que esperaba sale (o vuelve a quedar pendiente si no se conectaron).</summary>
    private void PairingEnded(string deviceId, PairingOutcome outcome)
    {
        if (_afterPairing is not { } after || after.PeerId != deviceId)
            return;
        _afterPairing = null;
        if (outcome == PairingOutcome.Paired)
            _ = SendAsync(deviceId, NameOf(deviceId), after.Paths, after.Copies);
        else
            SetPending(after.Paths, after.Copies);
    }

    private void AfterPaired(string deviceId) => _ = OfferMergeAfterReinstallAsync(deviceId);

    /// <summary>Se emparejó con alguien que se llama igual que un compañero que no está: ¿es el mismo, reinstalado?</summary>
    private async Task OfferMergeAfterReinstallAsync(string newId)
    {
        if (_agent.Trust.Find(newId) is not { } fresh)
            return;
        var old = _agent.Trust.All.FirstOrDefault(t => t.DeviceId != newId
            && string.Equals(t.DisplayName, fresh.DisplayName, StringComparison.CurrentCultureIgnoreCase)
            && _agent.Peers.Find(t.DeviceId)?.Presence != PeerPresence.Online);
        if (old is null || !await AskAsync(S.MergeReinstalledAsk(fresh.DisplayName)))
            return;
        _agent.Chats.Merge(old.DeviceId, newId);
        _agent.Trust.Update(newId, t =>
        {
            t.Favorite |= old.Favorite;
            t.AutoAccept = old.AutoAccept;
        });
        SaveGroups(g => ContactGroups.ReplaceMember(g, old.DeviceId, newId));
        _agent.Trust.Remove(old.DeviceId);
        RefreshPeers();
    }

    // ── Chats ────────────────────────────────────────────────────────────────────

    private ChatViewModel? _chat;
    private bool _foreground = true;

    /// <summary>La conversación abierta (hoja a pantalla completa).</summary>
    public ChatViewModel? Chat
    {
        get => _chat;
        private set
        {
            if (_chat == value)
                return;
            _chat?.Dispose();
            _chat = value;
            Raise();
        }
    }

    public void OpenChat(string peerId)
    {
        if (_agent.Trust.Find(peerId) is null)
            return;
        if (Chat?.PeerId != peerId)
        {
            Chat = new ChatViewModel(_agent, peerId, new ChatActions
            {
                Attach = () => SendFilesToAsync(peerId),
                OpenPath = _platform.OpenReceived,
                ShowInFolder = p => _platform.OpenReceived(p),
                Back = () => Open(MobileSheet.None),
                Confirm = AskAsync,
                ViewPhotos = ViewPhotos,
            });
        }
        Open(MobileSheet.Chat);
        UpdateChatVisibility();
    }

    /// <summary>La app pasó a primer o segundo plano (lo avisa la actividad de Android).</summary>
    public void SetForeground(bool foreground)
    {
        _foreground = foreground;
        UpdateChatVisibility();
    }

    /// <summary>El chat a la vista, con la app en primer plano: lo que llega cuenta como leído.</summary>
    private void UpdateChatVisibility()
    {
        if (Chat is not { } chat)
            return;
        chat.IsVisible = _foreground && Sheet == MobileSheet.Chat && Viewer is null;
        if (chat.IsVisible)
            _platform.ClearMessages(chat.PeerId);
    }

    private void OnChatChanged(string peerId, ChatMessage? message)
    {
        SyncChats();
        var fresh = message is { IsFiles: false } m && m.Author == peerId && DateTimeOffset.UtcNow - m.ReceivedAt < TimeSpan.FromSeconds(15);
        if (!fresh || Chat is { IsVisible: true } open && open.PeerId == peerId)
            return;
        var name = NameOf(peerId);
        // Con la app abierta, un aviso breve; cerrada (o en otra app), una notificación del sistema.
        if (_foreground)
            ShowFlash($"{name}: {message!.Text}");
        else
            _platform.NotifyMessage(peerId, name, message!.Text ?? "");
    }

    /// <summary>No leídos y último mensaje de cada compañero, para sus tarjetas.</summary>
    private void SyncChats()
    {
        var summaries = _agent.Chats.Summaries();
        foreach (var peer in Peers)
        {
            var summary = summaries.FirstOrDefault(s => s.PeerId == peer.DeviceId);
            var last = summary?.Last;
            var preview = last is null || last.Author == _agent.DeviceId ? null
                : last.IsFiles && last.Files is { Count: > 0 } photos && photos.All(p => ImageThumbnails.IsImage(p.Name)) ? S.ChatPhotos(Math.Max(1, last.FileCount))
                : last.IsFiles ? S.ItemsNamed(last.Files is { Count: > 0 } f ? f[0].Name : "", Math.Max(1, last.FileCount))
                : last.Text;
            peer.SetChat(summary?.Unread ?? 0, preview);
        }
    }

    // ── Visor de fotos ───────────────────────────────────────────────────────────

    private ImageViewerViewModel? _viewer;

    public ImageViewerViewModel? Viewer
    {
        get => _viewer;
        private set
        {
            if (!Set(ref _viewer, value))
                return;
            Raise(nameof(HasViewer));
            UpdateChatVisibility();
        }
    }

    public bool HasViewer => Viewer is not null;

    private void ViewPhotos(IReadOnlyList<string> paths, int index) =>
        Viewer = new ImageViewerViewModel(paths, index, 2048, _platform.OpenReceived, null, () => Viewer = null);

    // ── Confirmar ────────────────────────────────────────────────────────────────

    private TaskCompletionSource<bool>? _confirm;
    private string _confirmText = "";

    public string ConfirmText { get => _confirmText; private set => Set(ref _confirmText, value); }
    public bool HasConfirm => _confirm is not null;
    public RelayCommand ConfirmYes { get; private set; } = null!;
    public RelayCommand ConfirmNo { get; private set; } = null!;

    /// <summary>Una pregunta sí/no sobre todo lo demás (borrar un chat, quitar a un compañero…).</summary>
    public Task<bool> AskAsync(string text)
    {
        _confirm?.TrySetResult(false);
        _confirm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfirmText = text;
        Raise(nameof(HasConfirm));
        return _confirm.Task;
    }

    private void AnswerConfirm(bool yes)
    {
        var pending = _confirm;
        _confirm = null;
        Raise(nameof(HasConfirm));
        pending?.TrySetResult(yes);
    }

    // ── Acciones de un compañero ─────────────────────────────────────────────────

    private PeerViewModel? _actionPeer;

    public PeerViewModel? ActionPeer
    {
        get => _actionPeer;
        private set
        {
            if (!Set(ref _actionPeer, value))
                return;
            Raise(nameof(ActionPeerTrusted));
            Raise(nameof(ActionPeerNearby));
            Raise(nameof(FavoriteActionText));
            Raise(nameof(RemoveActionText));
            Raise(nameof(ActionGroupsTitle));
        }
    }

    public bool ActionPeerTrusted => ActionPeer?.IsTrusted == true;
    public bool ActionPeerNearby => ActionPeer is { IsTrusted: false, CanConnect: true };
    public string FavoriteActionText => _agent.Trust.Find(ActionPeer?.DeviceId ?? "")?.Favorite == true ? S.MobileUnfavorite : S.MobileFavorite;
    public string RemoveActionText => ActionPeerTrusted ? S.MobileRemovePeer : S.MobileHidePeer;
    public string ActionGroupsTitle => S.MobileGroupsOf(ActionPeer?.Name ?? "");
    public ObservableCollection<GroupToggleViewModel> ActionGroups { get; } = [];
    public bool HasActionGroups => ActionGroups.Count > 0;

    public RelayCommand OpenPeerActions { get; private set; } = null!;
    public RelayCommand ActionChat { get; private set; } = null!;
    public RelayCommand ActionSendFiles { get; private set; } = null!;
    public RelayCommand ActionToggleFavorite { get; private set; } = null!;
    public RelayCommand ActionRemove { get; private set; } = null!;
    public RelayCommand ActionConnect { get; private set; } = null!;
    public RelayCommand ActionNewGroup { get; private set; } = null!;

    private void RefreshActionGroups()
    {
        ActionGroups.Clear();
        if (ActionPeer is { IsTrusted: true } peer)
        {
            foreach (var g in Groups.Where(g => !g.Archived))
            {
                var id = g.Id;
                ActionGroups.Add(new GroupToggleViewModel(g.Name, g.Members.Contains(peer.DeviceId), on =>
                    SaveGroups(list => on ? ContactGroups.AddMember(list, id, peer.DeviceId) : ContactGroups.RemoveMember(list, id, peer.DeviceId))));
            }
        }
        Raise(nameof(HasActionGroups));
    }

    // ── Grupos ───────────────────────────────────────────────────────────────────

    private string? _groupFilter;
    private string? _groupNameFor;
    private IReadOnlyList<string> _groupNameMembers = [];
    private string _groupNameInput = "";

    public IReadOnlyList<ContactGroup> Groups => _agent.Settings.Current.ContactGroups ?? [];
    public ObservableCollection<GroupChipViewModel> GroupChips { get; } = [];
    public bool ShowGroupChips => Peers.Count >= 2 || Groups.Count > 0;
    private ContactGroup? FilteredGroup => _groupFilter is null ? null : Groups.FirstOrDefault(g => g.Id == _groupFilter);
    public bool IsGroupFiltered => FilteredGroup is not null;
    public string GroupActionsTitle => FilteredGroup?.Name ?? S.GroupArchived;
    public bool ShowGroupEmpty => FilteredGroup is not null && Peers.All(p => !p.InFilter);
    public string GroupEmptyText => FilteredGroup is { } g ? S.GroupEmpty(g.Name) : "";
    public ObservableCollection<ArchivedGroupViewModel> ArchivedGroups { get; } = [];
    public bool HasArchivedGroups => ArchivedGroups.Count > 0;

    public string GroupNameInput { get => _groupNameInput; set => Set(ref _groupNameInput, value); }
    public string GroupNameTitle => _groupNameFor is null ? S.GroupCreate : S.GroupNameTitle;

    public RelayCommand SelectGroupChip { get; private set; } = null!;
    public RelayCommand OpenGroupActions { get; private set; } = null!;
    public RelayCommand GroupSend { get; private set; } = null!;
    public RelayCommand GroupRename { get; private set; } = null!;
    public RelayCommand GroupArchive { get; private set; } = null!;
    public RelayCommand GroupDelete { get; private set; } = null!;
    public RelayCommand SaveGroupName { get; private set; } = null!;

    private void SaveGroups(Func<IReadOnlyList<ContactGroup>?, List<ContactGroup>> change)
    {
        _agent.Settings.Update(s =>
        {
            var list = change(s.ContactGroups);
            s.ContactGroups = list.Count > 0 ? list : null;
        });
        SyncGroupChips();
        RefreshActionGroups();
    }

    private void SyncGroupChips()
    {
        var visible = Groups.Where(g => !g.Archived).ToList();
        if (_groupFilter is not null && visible.All(g => g.Id != _groupFilter))
            _groupFilter = null;
        int CountOf(ContactGroup g) => g.Members.Count(id => Peers.Any(p => p.DeviceId == id));
        var same = GroupChips.Count == visible.Count + 2 && GroupChips.Skip(1).Zip(visible).All(x => x.First.Id == x.Second.Id && x.First.Name == x.Second.Name);
        if (!same)
        {
            GroupChips.Clear();
            GroupChips.Add(new GroupChipViewModel(null, S.GroupAll, 0));
            foreach (var g in visible)
                GroupChips.Add(new GroupChipViewModel(g.Id, g.Name, CountOf(g)));
            GroupChips.Add(new GroupChipViewModel(null, S.GroupNew, 0, isNew: true));
        }
        else
        {
            for (int i = 0; i < visible.Count; i++)
                GroupChips[i + 1].Count = CountOf(visible[i]);
        }
        foreach (var c in GroupChips)
            c.IsSelected = !c.IsNew && c.Id == _groupFilter;
        ApplyGroupFilter();
    }

    private void FilterBy(GroupChipViewModel chip)
    {
        _groupFilter = chip.Id;
        foreach (var c in GroupChips)
            c.IsSelected = c == chip;
        ApplyGroupFilter();
    }

    private void ApplyGroupFilter()
    {
        var group = FilteredGroup;
        foreach (var p in Peers)
            p.InFilter = group is null || group.Members.Contains(p.DeviceId);
        Raise(nameof(ShowGroupChips));
        Raise(nameof(IsGroupFiltered));
        Raise(nameof(GroupActionsTitle));
        Raise(nameof(ShowGroupEmpty));
        Raise(nameof(GroupEmptyText));
    }

    private void AskGroupName(string? renameId, IReadOnlyList<string> members)
    {
        _groupNameFor = renameId;
        _groupNameMembers = members;
        GroupNameInput = renameId is null ? "" : Groups.FirstOrDefault(g => g.Id == renameId)?.Name ?? "";
        Raise(nameof(GroupNameTitle));
        Open(MobileSheet.GroupName);
    }

    private void RefreshArchived()
    {
        ArchivedGroups.Clear();
        foreach (var g in Groups.Where(g => g.Archived))
        {
            var id = g.Id;
            ArchivedGroups.Add(new ArchivedGroupViewModel(g.Name, new RelayCommand(() =>
            {
                SaveGroups(list => ContactGroups.SetArchived(list, id, false));
                RefreshArchived();
            })));
        }
        Raise(nameof(HasArchivedGroups));
    }

    private async Task SendToGroupAsync(ContactGroup group)
    {
        var members = group.Members.Where(m => _agent.Trust.Find(m) is not null).ToList();
        if (members.Count == 0)
            return;
        if (HasPending)
        {
            var (paths, copies) = (_pending, _pendingCopyDir);
            _pendingCopyDir = null;
            SetPending([], null);
            await SendToManyAsync(members, paths, copies, group.Name);
        }
        else if (await PickMaterializedAsync() is { } picked)
        {
            await SendToManyAsync(members, picked.Paths, picked.Dir, group.Name);
        }
    }

    // ── Elegir a varios y enviarles lo mismo ─────────────────────────────────────

    private bool _isSelecting;
    private readonly Dictionary<GroupSendViewModel, string> _groupCopies = [];

    public bool IsSelecting
    {
        get => _isSelecting;
        set
        {
            if (!Set(ref _isSelecting, value))
                return;
            if (!value)
            {
                foreach (var p in Peers)
                    p.IsSelected = false;
            }
            RaiseSelection();
        }
    }

    public bool ShowSelectLink => Peers.Count >= 2 && !IsSelecting;
    public int SelectedCount => Peers.Count(p => p.IsSelected);
    public string SelectedText => S.SelectedCount(SelectedCount);
    public string SendSelectedText => S.SendToSelected(SelectedCount);
    public IReadOnlyList<string> SelectedIds => Peers.Where(p => p.IsSelected).Select(p => p.DeviceId).ToList();

    public RelayCommand StartSelecting { get; private set; } = null!;
    public RelayCommand CancelSelecting { get; private set; } = null!;
    public RelayCommand SendSelected { get; private set; } = null!;

    private void ToggleSelected(PeerViewModel peer)
    {
        peer.IsSelected = !peer.IsSelected;
        RaiseSelection();
    }

    private void RaiseSelection()
    {
        Raise(nameof(ShowSelectLink));
        Raise(nameof(SelectedCount));
        Raise(nameof(SelectedText));
        Raise(nameof(SendSelectedText));
        SendSelected?.NotifyCanExecuteChanged();
    }

    public ObservableCollection<GroupSendViewModel> GroupSends { get; } = [];
    public bool HasGroupSends => GroupSends.Count > 0;

    /// <summary>Lo mismo a varios a la vez: un envío a cada uno y una sola tarjeta con el progreso de todos.</summary>
    private async Task SendToManyAsync(IReadOnlyList<string> peerIds, IReadOnlyList<string> paths, string? copies, string? groupName)
    {
        var ids = peerIds.Distinct(StringComparer.Ordinal).Where(id => _agent.Trust.Find(id) is not null).ToList();
        if (ids.Count == 0 || paths.Count == 0)
            return;
        if (ids.Count == 1)
        {
            await SendAsync(ids[0], NameOf(ids[0]), paths, copies);
            return;
        }
        var recipients = ids.Select(id => (id, NameOf(id), _agent.Peers.Find(id)?.Avatar ?? _agent.Trust.Find(id)?.Avatar)).ToList();
        var group = new GroupSendViewModel(recipients, paths, groupName, DismissGroupSend, _agent.CancelTransfer,
            (g, failed) =>
            {
                // Las copias siguen haciendo falta para el reintento.
                var kept = _groupCopies.Remove(g, out var dir) ? dir : null;
                GroupSends.Remove(g);
                Raise(nameof(HasGroupSends));
                _ = SendToManyAsync(failed, g.Paths, kept, groupName);
            });
        if (copies is not null)
            _groupCopies[group] = copies;
        GroupSends.Insert(0, group);
        Raise(nameof(HasGroupSends));
        _fast.Start();
        SetBusy(true);
        await Task.WhenAll(ids.Select(async id =>
        {
            var target = await _agent.LocateAsync(id, TimeSpan.FromSeconds(1.5));
            group.Attach(id, target is null ? null : _agent.BeginSend(target, paths));
        }));
    }

    private void DismissGroupSend(GroupSendViewModel group)
    {
        if (!GroupSends.Remove(group))
            return;
        Raise(nameof(HasGroupSends));
        if (!group.IsActive && _groupCopies.Remove(group, out var copies))
            OutgoingFiles.Delete(copies);
    }

    // ── Mantener conectado ───────────────────────────────────────────────────────

    public bool KeepConnected
    {
        get => _agent.Settings.Current.KeepConnected;
        set
        {
            if (value == KeepConnected)
                return;
            _agent.Settings.Update(s => s.KeepConnected = value);
            _platform.SetKeepAlive(value);
            Raise();
        }
    }

    public bool VisibleNearby
    {
        get => _agent.Settings.Current.VisibleNearby;
        set
        {
            if (value == VisibleNearby)
                return;
            _agent.Settings.Update(s => s.VisibleNearby = value);
            _agent.Discovery?.AnnounceSoon();
            Raise();
            RefreshMode();
        }
    }
}

/// <summary>Un grupo en el menú de un compañero: marcado si pertenece.</summary>
public sealed class GroupToggleViewModel(string name, bool isChecked, Action<bool> changed) : ViewModelBase
{
    private bool _isChecked = isChecked;

    public string Name { get; } = name;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value))
                changed(value);
        }
    }
}

/// <summary>Un grupo archivado, para mostrarlo de nuevo.</summary>
public sealed class ArchivedGroupViewModel(string name, RelayCommand restore)
{
    public string Name { get; } = name;
    public RelayCommand Restore { get; } = restore;
}

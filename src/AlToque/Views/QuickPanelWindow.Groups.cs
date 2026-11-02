using AlToque.Localization;
using AlToque.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AlToque.Views;

/// <summary>Grupos y envío a varios en el panel: Ctrl+clic para elegir, menús de clic derecho y arrastrar a un grupo.</summary>
public partial class QuickPanelWindow
{
    /// <summary>Un compañero arrastrado dentro del panel (solo entre controles de esta app).</summary>
    private static readonly DataFormat<string> PeerFormat = DataFormat.CreateInProcessFormat<string>("altoque-peer");

    private PointerPressedEventArgs? _cardPress;
    private PeerViewModel? _cardPeer;
    private Point _cardPressAt;
    private bool _draggingCard;

    private static AppController? Controller => (Application.Current as App)?.Controller;

    private void InitGroups()
    {
        var list = this.FindControl<ItemsControl>("PeerList")!;
        list.AddHandler(PointerPressedEvent, OnCardPressed, RoutingStrategies.Tunnel);
        list.AddHandler(PointerMovedEvent, OnCardMoved, RoutingStrategies.Tunnel);
        list.AddHandler(PointerReleasedEvent, (_, _) => _cardPress = null, RoutingStrategies.Tunnel);
        list.AddHandler(ContextRequestedEvent, OnCardContext);
        this.FindControl<ItemsControl>("GroupChipList")!.AddHandler(ContextRequestedEvent, OnChipContext);
    }

    private static T? DataUnder<T>(object? source, string buttonClass) where T : class =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.Classes.Contains(buttonClass))?.DataContext as T;

    private static GroupChipViewModel? ChipUnder(DragEventArgs e) => DataUnder<GroupChipViewModel>(e.Source, "groupchip");

    private void HighlightChip(GroupChipViewModel? target)
    {
        if (Vm is not { } vm)
            return;
        foreach (var c in vm.GroupChips)
            c.IsDropTarget = c == target && c.IsGroup;
    }

    private void OnCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        var peer = DataUnder<PeerViewModel>(e.Source, "peer");
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || peer is not { IsTrusted: true })
        {
            vm.NextClickSelects = false;
            return;
        }
        // Ctrl+clic (⌘ en macOS) elige al compañero para enviar a varios en lugar de abrir su chat.
        vm.NextClickSelects = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        _cardPress = e;
        _cardPeer = peer;
        _cardPressAt = e.GetPosition(this);
    }

    /// <summary>Arrastrar la tarjeta unos píxeles la convierte en algo que se suelta sobre la ficha de un grupo.</summary>
    private async void OnCardMoved(object? sender, PointerEventArgs e)
    {
        if (_cardPress is not { } press || _cardPeer is not { } peer || _draggingCard || Vm is not { } vm || !vm.GroupChips.Any(c => c.IsGroup))
            return;
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - _cardPressAt.X) < 8 && Math.Abs(at.Y - _cardPressAt.Y) < 8)
            return;
        _cardPress = null;
        _draggingCard = true;
        vm.NextClickSelects = false;
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(PeerFormat, peer.DeviceId));
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Link);
        }
        finally
        {
            _draggingCard = false;
            HighlightChip(null);
        }
    }

    private static MenuItem Item(string header, Action action) => new() { Header = header, Command = new RelayCommand(action) };

    /// <summary>Clic derecho en un compañero: chat, enviar, elegir y sus grupos.</summary>
    private void OnCardContext(object? sender, ContextRequestedEventArgs e)
    {
        if (Vm is not { } vm || Controller is not { } app || DataUnder<PeerViewModel>(e.Source, "peer") is not { IsTrusted: true } peer
            || (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("peer")) is not { } card)
            return;
        e.Handled = true;
        var groups = new List<object>();
        foreach (var g in app.Groups.Where(g => !g.Archived))
        {
            var id = g.Id;
            groups.Add(new MenuItem
            {
                Header = g.Name,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = g.Members.Contains(peer.DeviceId),
                Command = new RelayCommand(() => app.ToggleGroupMember(id, peer.DeviceId)),
            });
        }
        if (groups.Count > 0)
            groups.Add(new Separator());
        groups.Add(Item(S.PeerMenuNewGroup, () => _ = app.CreateGroupAsync([peer.DeviceId])));
        var menu = new ContextMenu
        {
            ItemsSource = new List<object>
            {
                Item(S.PeerMenuChat, () =>
                {
                    if (!app.FocusPinned(peer.DeviceId))
                        vm.OpenChat(peer.DeviceId);
                }),
                Item(S.PeerMenuSendFiles, () => app.OnPeerClicked(peer, null, null)),
                Item(peer.IsSelected ? S.PeerMenuUnselect : S.PeerMenuSelect, () => vm.ToggleSelected(peer)),
                new Separator(),
                new MenuItem { Header = S.PeerMenuGroups, ItemsSource = groups },
            },
        };
        menu.Open(card);
    }

    /// <summary>Clic derecho en una ficha: enviar al grupo, renombrar, archivar o eliminar; en «Todos», los archivados.</summary>
    private void OnChipContext(object? sender, ContextRequestedEventArgs e)
    {
        if (Controller is not { } app || DataUnder<GroupChipViewModel>(e.Source, "groupchip") is not { } chip
            || (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("groupchip")) is not { } button)
            return;
        e.Handled = true;
        var items = new List<object>();
        if (chip is { IsGroup: true, Id: { } id })
        {
            items.Add(Item(S.GroupSendTo(chip.Name), () => _ = app.SendToGroupAsync(id)));
            items.Add(Item(S.GroupRename, () => _ = app.RenameGroupAsync(id)));
            items.Add(Item(S.GroupArchive, () => app.SetGroupArchived(id, true)));
            items.Add(new Separator());
            items.Add(Item(S.GroupDelete, () => _ = app.DeleteGroupAsync(id)));
        }
        else
        {
            var archived = app.Groups.Where(g => g.Archived).ToList();
            if (archived.Count == 0)
                return;
            items.Add(new MenuItem
            {
                Header = S.GroupArchived,
                ItemsSource = archived.Select(g => (object)Item($"{g.Name} · {S.GroupUnarchive}", () => app.SetGroupArchived(g.Id, false))).ToList(),
            });
        }
        new ContextMenu { ItemsSource = items }.Open(button);
    }
}

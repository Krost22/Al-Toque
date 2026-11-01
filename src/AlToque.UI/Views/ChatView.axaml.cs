using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AlToque.Views;

public partial class ChatView : UserControl
{
    private ChatViewModel? _vm;

    public ChatView()
    {
        AvaloniaXamlLoader.Load(this);
        // Entrar envía; Mayús+Entrar hace un salto de línea (se intercepta antes que la caja de texto).
        this.FindControl<TextBox>("Composer")!.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _vm?.SendCommand.CanExecute(null) == true)
            {
                _vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.TryGetFiles() is { } ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            e.Handled = true;
            if (e.DataTransfer.TryGetFiles() is not { } items)
                return;
            var paths = items.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (paths.Count > 0)
                FilesDropped?.Invoke(paths);
        });
    }

    /// <summary>Soltaron archivos sobre la conversación: se le envían a este compañero.</summary>
    public event Action<IReadOnlyList<string>>? FilesDropped;

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
            _vm.MessageAdded -= ScrollToEnd;
        _vm = DataContext as ChatViewModel;
        if (_vm is not null)
        {
            _vm.MessageAdded += ScrollToEnd;
            ScrollToEnd();
        }
        base.OnDataContextChanged(e);
    }

    /// <summary>Pone el cursor en el cuadro para escribir.</summary>
    public void FocusComposer() => this.FindControl<TextBox>("Composer")?.Focus();

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() =>
    {
        if (_vm is { Items.Count: > 0 } vm)
            this.FindControl<ListBox>("Messages")?.ScrollIntoView(vm.Items.Count - 1);
    }, DispatcherPriority.Background);
}

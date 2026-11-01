using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AlToque.Views;

public partial class ChatView : UserControl
{
    private readonly TextBox _composer;
    private readonly ListBox _messages;
    private ScrollViewer? _scroller;
    private ChatViewModel? _vm;

    /// <summary>La vista está al final: lo que llega (o una foto que termina de cargar y crece) la mantiene abajo.</summary>
    private bool _stickToEnd = true;

    public ChatView()
    {
        AvaloniaXamlLoader.Load(this);
        _composer = this.FindControl<TextBox>("Composer")!;
        _messages = this.FindControl<ListBox>("Messages")!;
        _messages.TemplateApplied += (_, _) => Dispatcher.UIThread.Post(HookScroller, DispatcherPriority.Loaded);
        // Se intercepta antes que la caja de texto: Entrar envía (Mayús+Entrar hace un salto de línea) y Ctrl+V puede
        // traer archivos o una captura en vez de texto.
        _composer.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        this.FindControl<Grid>("Header")!.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && (e.Source as Avalonia.Visual)?.FindAncestorOfType<Button>(includeSelf: true) is null)
                HeaderPressed?.Invoke(e);
        };
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

    /// <summary>Soltaron (o pegaron) archivos sobre la conversación: se le envían a este compañero.</summary>
    public event Action<IReadOnlyList<string>>? FilesDropped;

    /// <summary>Pegaron una imagen (una captura de pantalla, una imagen copiada del navegador).</summary>
    public event Action<Bitmap>? ImagePasted;

    /// <summary>Clic en la cabecera: la ventana del chat fijado se arrastra desde ahí.</summary>
    public event Action<PointerPressedEventArgs>? HeaderPressed;

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
            _vm.MessageAdded -= ScrollToEnd;
        _vm = DataContext as ChatViewModel;
        if (_vm is not null)
        {
            // Otra conversación (o la misma vuelta a abrir): empieza al final.
            _stickToEnd = true;
            _vm.MessageAdded += ScrollToEnd;
            ScrollToEnd();
        }
        base.OnDataContextChanged(e);
    }

    /// <summary>Pone el cursor en el cuadro para escribir.</summary>
    public void FocusComposer() => _composer.Focus();

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _vm?.SendCommand.CanExecute(null) == true)
        {
            _vm.SendCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.V && e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            e.Handled = true;
            _ = PasteAsync();
        }
    }

    /// <summary>
    /// Archivos copiados → se envían. Texto → se pega como siempre (copiar de Word o Excel trae también una imagen y lo
    /// esperado es el texto). Solo una imagen → se envía como foto.
    /// </summary>
    private async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            _composer.Paste();
            return;
        }
        try
        {
            if (FilesDropped is not null && await clipboard.TryGetFilesAsync() is { Length: > 0 } items)
            {
                var paths = items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList();
                if (paths.Count > 0)
                {
                    FilesDropped.Invoke(paths);
                    return;
                }
            }
            if (!string.IsNullOrEmpty(await clipboard.TryGetTextAsync()))
            {
                _composer.Paste();
                return;
            }
            if (ImagePasted is not null && await clipboard.TryGetBitmapAsync() is { } bitmap)
            {
                ImagePasted.Invoke(bitmap);
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException
                                       or System.Runtime.InteropServices.COMException)
        {
            // El portapapeles ocupado por otra app o con un formato raro: se pega lo que la caja de texto entienda.
        }
        _composer.Paste();
    }

    private void HookScroller()
    {
        if (_scroller is not null || _messages.FindDescendantOfType<ScrollViewer>() is not { } scroller)
            return;
        _scroller = scroller;
        scroller.ScrollChanged += (_, e) =>
        {
            // Creció el contenido o cambió el alto de la ventana: si estaba al final, sigue al final.
            if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
            {
                if (_stickToEnd)
                    scroller.ScrollToEnd();
                return;
            }
            // Lo movió el usuario: queda pegado solo si llegó (casi) al final.
            _stickToEnd = scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 40;
        };
        ScrollToEnd();
    }

    /// <summary>Llegó o se escribió algo: se baja al final (al abrir, o si ya se estaba ahí).</summary>
    private void ScrollToEnd() => Dispatcher.UIThread.Post(() =>
    {
        if (_vm is not { Items.Count: > 0 } vm)
            return;
        var last = vm.Items[^1];
        // Lo que escribo yo siempre baja; lo que llega, solo si no estaba leyendo más arriba.
        if (last.IsMine)
            _stickToEnd = true;
        if (!_stickToEnd)
            return;
        _messages.ScrollIntoView(vm.Items.Count - 1);
        _scroller?.ScrollToEnd();
    }, DispatcherPriority.Background);
}

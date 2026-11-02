using AlToque.Controls;
using AlToque.Core.Agent;
using AlToque.Core.Chat;
using AlToque.Core.Discovery;
using AlToque.Localization;
using AlToque.Platform;
using AlToque.ViewModels;
using AlToque.Views;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AlToque;

/// <summary>
/// «Enviar lo copiado»: Ctrl+Alt+C manda lo que hay en el portapapeles (texto como mensaje del chat, imágenes y
/// archivos como envío) al compañero elegido en Ajustes, o pregunta a quién. Opcional: al copiar algo aparece unos
/// segundos un botón para enviarlo. Todo apagado de fábrica; se enciende y apaga en Ajustes o en la bandeja.
/// </summary>
public sealed partial class AppController
{
    /// <summary>Lo que había en el portapapeles al momento de ofrecerlo (se envía eso, aunque luego se copie otra cosa).</summary>
    private sealed record Copied(IReadOnlyList<string>? Files, string? Text, Bitmap? Image);

    // Marcas con las que los gestores de contraseñas piden que nadie mire lo que copiaron.
    private static readonly string[] ConcealedFormats =
    [
        "ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory", "Clipboard Viewer Ignore",
        "org.nspasteboard.ConcealedType", "org.nspasteboard.TransientType", "x-kde-passwordManagerHint",
    ];

    private CopyHooks? _copyHooks;
    private CopySendWindow? _copyWindow;
    private CopySendViewModel? _copyVm;
    private Copied? _copied;
    private NativeMenuItem? _copySendItem;
    private DispatcherTimer? _copyPoll;
    private string? _copyPollLast;
    private bool _copyReading;

    /// <summary>"Ctrl+Alt+C" o, en el Mac, "⌃⌥C".</summary>
    public static string CopySendShortcut => OperatingSystem.IsMacOS() ? "⌃⌥C" : "Ctrl+Alt+C";

    /// <summary>Por qué el atajo no funciona en este equipo (null si funciona o si está apagado).</summary>
    public string? CopySendProblem => _copyHooks?.HotkeyProblem;

    /// <summary>Aplica lo que diga Ajustes: registra o suelta el atajo y la vigilancia del portapapeles.</summary>
    public void ApplyCopySend()
    {
        var s = Agent.Settings.Current;
        var offer = s.CopySendEnabled && s.CopySendOffer;
        if (_copyHooks is null && !s.CopySendEnabled)
        {
            UpdateCopySendItem();
            return;
        }
        if (_copyHooks is null)
        {
            _copyHooks = CopyHooks.Create();
            _copyHooks.HotkeyPressed += OnCopyHotkey;
            _copyHooks.ClipboardChanged += OnClipboardChanged;
        }
        _copyHooks.Configure(s.CopySendEnabled, offer);

        // Sin aviso del sistema al copiar (Wayland): se mira el texto copiado cada segundo y medio.
        if (offer && _copyHooks.NeedsPolling)
        {
            if (_copyPoll is null)
            {
                _copyPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                _copyPoll.Tick += async (_, _) => await PollClipboardAsync();
                _copyPollLast = null;
                _copyPoll.Start();
            }
        }
        else if (_copyPoll is not null)
        {
            _copyPoll.Stop();
            _copyPoll = null;
        }
        if (!s.CopySendEnabled)
            CloseCopyWindow();
        UpdateCopySendItem();
    }

    /// <summary>Interruptor de la bandeja: todo «Enviar lo copiado» de una vez.</summary>
    private void ToggleCopySend()
    {
        Agent.Settings.Update(s => s.CopySendEnabled = !s.CopySendEnabled);
        ApplyCopySend();
        (_mainWindow?.DataContext as MainViewModel)?.RefreshCopySend();
    }

    private void UpdateCopySendItem()
    {
        if (_copySendItem is not null)
            _copySendItem.IsChecked = Agent.Settings.Current.CopySendEnabled;
    }

    private string? FixedTarget() =>
        Agent.Settings.Current.CopySendTarget is { } id && Agent.Trust.Find(id) is not null && !Agent.IsBlocked(id) ? id : null;

    private void OnCopyHotkey()
    {
        if (Agent.Settings.Current.CopySendEnabled)
            SendCopiedFromCommand();
    }

    /// <summary>El atajo, o «altoque clipboard» desde un atajo del sistema (Wayland): manda lo copiado.</summary>
    private async void SendCopiedFromCommand()
    {
        var copied = await ReadCopiedAsync(respectConcealed: false);
        if (copied is null)
        {
            ShowCopyDone(S.CopySendNothing, error: true);
            return;
        }
        if (FixedTarget() is { } id)
            await SendCopiedAsync(id, copied);
        else
            ShowCopyPicker(copied);
    }

    private async void OnClipboardChanged()
    {
        var s = Agent.Settings.Current;
        // Lo que se copia dentro de Al Toque (un mensaje del chat, por ejemplo) no se ofrece.
        if (!s.CopySendEnabled || !s.CopySendOffer || _desktop.Windows.Any(w => w.IsActive))
            return;
        if (await ReadCopiedAsync(respectConcealed: true) is { } copied)
            ShowCopyOffer(copied);
    }

    private async Task PollClipboardAsync()
    {
        try
        {
            if (Clipboard() is not { } clipboard)
                return;
            var text = await clipboard.TryGetTextAsync();
            var files = await clipboard.TryGetFilesAsync();
            var key = (text ?? "") + "|" + string.Join("|", files?.Select(f => f.Name) ?? []);
            if (_copyPollLast is null)
            {
                _copyPollLast = key; // Lo que ya estaba copiado al encender no se ofrece.
                return;
            }
            if (key == _copyPollLast)
                return;
            _copyPollLast = key;
            OnClipboardChanged();
        }
        catch (Exception ex) when (IsClipboardError(ex))
        {
        }
    }

    private IClipboard? Clipboard() =>
        (_desktop.Windows.FirstOrDefault(w => w.IsVisible) ?? EnsurePanelWindow()).Clipboard;

    /// <summary>Archivos copiados, si no texto (copiar de Word trae también una imagen: lo esperado es el texto), si no una imagen.</summary>
    private async Task<Copied?> ReadCopiedAsync(bool respectConcealed)
    {
        if (_copyReading || Clipboard() is not { } clipboard)
            return null;
        _copyReading = true;
        try
        {
            if (respectConcealed)
            {
                var formats = await clipboard.GetDataFormatsAsync();
                if (formats.Any(f => ConcealedFormats.Any(c => f.Identifier.Contains(c, StringComparison.OrdinalIgnoreCase))))
                    return null;
            }
            if (await clipboard.TryGetFilesAsync() is { Length: > 0 } items
                && items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList() is { Count: > 0 } paths)
                return new Copied(paths, null, null);
            if (await clipboard.TryGetTextAsync() is { } text && !string.IsNullOrWhiteSpace(text))
                return new Copied(null, text, null);
            if (await clipboard.TryGetBitmapAsync() is { } bitmap)
                return new Copied(null, null, bitmap);
            return null;
        }
        catch (Exception ex) when (IsClipboardError(ex))
        {
            AgentLog.Write("No se pudo leer el portapapeles: " + ex.Message);
            return null;
        }
        finally
        {
            _copyReading = false;
        }
    }

    private static bool IsClipboardError(Exception ex) => ex is InvalidOperationException or IOException or UnauthorizedAccessException
        or NotSupportedException or TimeoutException or System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException;

    private static (string Preview, Ic Icon) Describe(Copied copied)
    {
        if (copied.Files is { Count: > 0 } files)
            return (S.ItemsNamed(Path.GetFileName(files[0]), files.Count), files.Count == 1 && Directory.Exists(files[0]) ? Ic.Folder : Ic.File);
        if (copied.Text is { } text)
        {
            var line = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
            return (line.Length > 120 ? line[..120] + "…" : line, Ic.FileText);
        }
        return (S.CopySendImage, Ic.Image);
    }

    // ── La tarjeta ───────────────────────────────────────────────────────────────

    private CopySendViewModel EnsureCopyWindow()
    {
        if (_copyVm is not null && _copyWindow is not null)
            return _copyVm;
        _copyVm = new CopySendViewModel(
            send: () =>
            {
                if (_copied is not { } copied)
                    return;
                if (FixedTarget() is { } id)
                    _ = SendCopiedAsync(id, copied);
                else
                    ShowCopyPicker(copied);
            },
            choose: () =>
            {
                if (_copied is { } copied)
                    ShowCopyPicker(copied);
            },
            close: CloseCopyWindow);
        _copyWindow = new CopySendWindow { DataContext = _copyVm };
        _copyWindow.LayoutChanged += LayoutToasts;
        _copyWindow.Closed += (_, _) =>
        {
            _copyWindow = null;
            _copyVm = null;
            LayoutToasts();
        };
        return _copyVm;
    }

    private void ShowCopyWindow()
    {
        var first = _copyWindow is not { IsVisible: true };
        _copyWindow!.Show();
        LayoutToasts();
        if (first)
            _copyWindow.Pop();
    }

    private void ShowCopyOffer(Copied copied)
    {
        _copied = copied;
        var (preview, icon) = Describe(copied);
        var vm = EnsureCopyWindow();
        vm.ShowOffer(preview, icon, FixedTarget() is { } id ? NameOf(id) : null);
        ShowCopyWindow();
    }

    private void ShowCopyPicker(Copied copied)
    {
        _copied = copied;
        var choices = Agent.Peers.Snapshot()
            .Where(p => p.IsTrusted && !Agent.IsBlocked(p.DeviceId))
            .OrderByDescending(p => p.IsFavorite)
            .ThenByDescending(p => p.Presence == PeerPresence.Online)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new CopyChoiceViewModel(p.DeviceId, p.DisplayName, p.Avatar, p.Presence == PeerPresence.Online,
                id => _ = SendCopiedAsync(id, copied)))
            .ToList();
        var vm = EnsureCopyWindow();
        if (choices.Count == 0)
        {
            vm.ShowDone(S.CopySendNoPeers, error: true);
        }
        else
        {
            var (preview, icon) = Describe(copied);
            vm.ShowPick(preview, icon, choices);
        }
        ShowCopyWindow();
    }

    private void ShowCopyDone(string text, bool error = false)
    {
        EnsureCopyWindow().ShowDone(text, error);
        ShowCopyWindow();
    }

    private void CloseCopyWindow()
    {
        _copied = null;
        _copyWindow?.Close();
    }

    /// <summary>La tarjeta se retira sola: el botón a los 7 s, la lista a los 25 s y el «Enviado» a los 3 s.</summary>
    private void TickCopySend()
    {
        if (_copyVm is not { } vm || _copyWindow is not { IsVisible: true } window || window.IsHovered)
            return;
        var limit = vm.Mode switch
        {
            CopySendMode.Offer => TimeSpan.FromSeconds(7),
            CopySendMode.Pick => TimeSpan.FromSeconds(25),
            _ => TimeSpan.FromSeconds(3),
        };
        if (DateTimeOffset.Now - vm.ShownAt > limit)
            CloseCopyWindow();
    }

    // ── Enviar ───────────────────────────────────────────────────────────────────

    private async Task SendCopiedAsync(string peerId, Copied copied)
    {
        var name = NameOf(peerId);
        _copied = null;
        var online = Agent.Peers.Find(peerId)?.Presence == PeerPresence.Online;
        if (copied.Files is { Count: > 0 } files)
        {
            ShowCopyDone(S.CopySendSending(name));
            await SendToPeerAsync(peerId, name, files, null);
        }
        else if (copied.Text is { } text)
        {
            if (text.Length > ChatStore.MaxTextLength)
            {
                // Demasiado largo para un mensaje: va como archivo de texto.
                if (await SaveTextAsync(text) is not { } path)
                    return;
                ShowCopyDone(S.CopySendSending(name));
                await SendToPeerAsync(peerId, name, [path], null);
                return;
            }
            Agent.SendChat(peerId, text);
            ShowCopyDone(online ? S.CopySendSent(name) : S.CopySendQueued(name));
        }
        else if (copied.Image is { } image)
        {
            ShowCopyDone(S.CopySendSending(name));
            await SendPastedImageAsync(peerId, image);
        }
    }

    private async Task<string?> SaveTextAsync(string text)
    {
        try
        {
            var dir = Agent.Paths.PastedDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, S.CopiedTextName(DateTime.Now));
            for (var i = 2; File.Exists(path); i++)
                path = Path.Combine(dir, Path.GetFileNameWithoutExtension(S.CopiedTextName(DateTime.Now)) + $" ({i}).txt");
            await File.WriteAllTextAsync(path, text);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Write("No se pudo guardar el texto copiado: " + ex.Message);
            ShowCopyDone(S.CopySendFailed, error: true);
            return null;
        }
    }
}

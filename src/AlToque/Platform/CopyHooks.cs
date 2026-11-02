using System.Runtime.InteropServices;
using AlToque.Core.Agent;
using Avalonia.Threading;

namespace AlToque.Platform;

/// <summary>
/// Lo que «Enviar lo copiado» necesita del sistema: el atajo global Ctrl+Alt+C (en macOS, Control+Opción+C) y saber
/// cuándo se copió algo. Solo se activa lo que el usuario encendió; los avisos llegan en el hilo de la interfaz.
/// </summary>
public abstract class CopyHooks : IDisposable
{
    /// <summary>Se pulsó el atajo.</summary>
    public event Action? HotkeyPressed;

    /// <summary>Cambió el portapapeles (se copió algo, en cualquier app).</summary>
    public event Action? ClipboardChanged;

    /// <summary>Por qué el atajo no funciona (otro programa lo usa, Wayland…); null si funciona o está apagado.</summary>
    public string? HotkeyProblem { get; protected set; }

    /// <summary>Este sistema no avisa al copiar: quien usa la clase revisa el portapapeles cada tanto.</summary>
    public virtual bool NeedsPolling => false;

    public static CopyHooks Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsCopyHooks();
        if (OperatingSystem.IsMacOS())
            return new MacCopyHooks();
        return new X11CopyHooks();
    }

    /// <summary>Enciende o apaga el atajo y la vigilancia del portapapeles.</summary>
    public abstract void Configure(bool hotkey, bool watchClipboard);

    protected void RaiseHotkey() => Dispatcher.UIThread.Post(() => HotkeyPressed?.Invoke());

    protected void RaiseClipboard() => Dispatcher.UIThread.Post(() => ClipboardChanged?.Invoke());

    public void Dispose()
    {
        Configure(false, false);
        GC.SuppressFinalize(this);
    }
}

/// <summary>Windows: RegisterHotKey en un hilo con su propia cola de mensajes y el número de secuencia del portapapeles.</summary>
internal sealed partial class WindowsCopyHooks : CopyHooks
{
    private const int HotkeyId = 0xA170;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModNoRepeat = 0x4000, VkC = 0x43;
    private const uint WmHotkey = 0x0312, WmQuit = 0x0012;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private Thread? _thread;
    private uint _threadId;
    private DispatcherTimer? _watch;
    private uint _sequence;

    public override void Configure(bool hotkey, bool watchClipboard)
    {
        if (hotkey && _thread is null)
            StartHotkey();
        else if (!hotkey && _thread is not null)
            StopHotkey();
        if (!hotkey)
            HotkeyProblem = null;

        if (watchClipboard && _watch is null)
        {
            _sequence = Sequence();
            _watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _watch.Tick += (_, _) =>
            {
                var now = Sequence();
                if (now == _sequence)
                    return;
                _sequence = now;
                RaiseClipboard();
            };
            _watch.Start();
        }
        else if (!watchClipboard && _watch is not null)
        {
            _watch.Stop();
            _watch = null;
        }
    }

    private void StartHotkey()
    {
        // Sin «using»: si el hilo tarda más que la espera, todavía la va a marcar.
        var ready = new ManualResetEventSlim();
        var ok = false;
        var error = 0;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            ok = RegisterHotKey(0, HotkeyId, ModControl | ModAlt | ModNoRepeat, VkC);
            error = Marshal.GetLastPInvokeError();
            ready.Set();
            if (!ok)
                return;
            while (GetMessageW(out var msg, 0, 0, 0) > 0)
            {
                if (msg.Message == WmHotkey && msg.WParam == HotkeyId)
                    RaiseHotkey();
            }
            UnregisterHotKey(0, HotkeyId);
        })
        { IsBackground = true, Name = "AlToque atajo" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        if (!ok)
        {
            _thread = null;
            HotkeyProblem = error == ErrorHotkeyAlreadyRegistered ? Localization.S.CopySendHotkeyTaken : Localization.S.CopySendHotkeyFailed;
            AgentLog.Write("No se pudo registrar Ctrl+Alt+C (error " + error + ").");
        }
        else
        {
            HotkeyProblem = null;
        }
    }

    private void StopHotkey()
    {
        PostThreadMessageW(_threadId, WmQuit, 0, 0);
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    private static uint Sequence()
    {
        try
        {
            return GetClipboardSequenceNumber();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg msg, nint hWnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
}

/// <summary>
/// macOS: el atajo con RegisterEventHotKey (Carbon; no pide permisos de accesibilidad) y el portapapeles con el
/// contador de cambios de NSPasteboard.
/// </summary>
internal sealed unsafe partial class MacCopyHooks : CopyHooks
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const uint KeyC = 8, ControlKey = 0x1000, OptionKey = 0x0800;
    private const uint KeyboardClass = 0x6B657962; // 'keyb'
    private const uint HotKeyPressedKind = 5;

    private static MacCopyHooks? _current;
    private nint _hotkey;
    private nint _handler;
    private DispatcherTimer? _watch;
    private nint _changeCount;

    public override void Configure(bool hotkey, bool watchClipboard)
    {
        try
        {
            if (hotkey && _hotkey == 0)
                StartHotkey();
            else if (!hotkey && _hotkey != 0)
                StopHotkey();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            HotkeyProblem = Localization.S.CopySendHotkeyFailed;
        }
        if (!hotkey)
            HotkeyProblem = null;

        if (watchClipboard && _watch is null)
        {
            _changeCount = ChangeCount();
            _watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _watch.Tick += (_, _) =>
            {
                var now = ChangeCount();
                if (now == _changeCount)
                    return;
                _changeCount = now;
                RaiseClipboard();
            };
            _watch.Start();
        }
        else if (!watchClipboard && _watch is not null)
        {
            _watch.Stop();
            _watch = null;
        }
    }

    private void StartHotkey()
    {
        _current = this;
        var target = GetApplicationEventTarget();
        var spec = new EventTypeSpec { EventClass = KeyboardClass, EventKind = HotKeyPressedKind };
        var status = InstallEventHandler(target, &OnHotKey, 1, &spec, 0, out _handler);
        if (status == 0)
            status = RegisterEventHotKey(KeyC, ControlKey | OptionKey, new EventHotKeyId { Signature = 0x414C5451, Id = 1 }, target, 0, out _hotkey);
        if (status != 0)
        {
            AgentLog.Write("No se pudo registrar Control+Opción+C (" + status + ").");
            HotkeyProblem = Localization.S.CopySendHotkeyTaken;
            StopHotkey();
        }
        else
        {
            HotkeyProblem = null;
        }
    }

    private void StopHotkey()
    {
        if (_hotkey != 0)
            UnregisterEventHotKey(_hotkey);
        if (_handler != 0)
            RemoveEventHandler(_handler);
        _hotkey = 0;
        _handler = 0;
    }

    [UnmanagedCallersOnly]
    private static int OnHotKey(nint callRef, nint eventRef, nint userData)
    {
        _current?.RaiseHotkey();
        return 0;
    }

    private static nint ChangeCount()
    {
        try
        {
            var pasteboard = MsgSend(objc_getClass("NSPasteboard"), sel_registerName("generalPasteboard"));
            return pasteboard == 0 ? 0 : MsgSend(pasteboard, sel_registerName("changeCount"));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [LibraryImport(Carbon)]
    private static partial nint GetApplicationEventTarget();

    [LibraryImport(Carbon)]
    private static partial int InstallEventHandler(nint target, delegate* unmanaged<nint, nint, nint, int> handler, uint numTypes,
        EventTypeSpec* types, nint userData, out nint handlerRef);

    [LibraryImport(Carbon)]
    private static partial int RemoveEventHandler(nint handlerRef);

    [LibraryImport(Carbon)]
    private static partial int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, nint target, uint options, out nint hotKeyRef);

    [LibraryImport(Carbon)]
    private static partial int UnregisterEventHotKey(nint hotKeyRef);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint MsgSend(nint receiver, nint selector);
}

/// <summary>
/// Linux con X11: el atajo con XGrabKey y los cambios del portapapeles con XFixes, en una conexión propia y un hilo
/// propio (no se toca la conexión de Avalonia). En Wayland no hay atajos globales para las apps: se avisa en Ajustes,
/// y el portapapeles se revisa cada tanto.
/// </summary>
internal sealed unsafe partial class X11CopyHooks : CopyHooks
{
    private const string X11 = "libX11.so.6";
    private const string Xfixes = "libXfixes.so.3";
    private const uint ControlMask = 1 << 2, Mod1Mask = 1 << 3, LockMask = 1 << 1, Mod2Mask = 1 << 4;
    private const int KeyPress = 2, GrabModeAsync = 1;
    private const ulong SetSelectionOwnerNotifyMask = 1;

    private Thread? _thread;
    private volatile bool _stop;
    private bool _polling;

    public override bool NeedsPolling => _polling;

    private static bool IsWayland => string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    public override void Configure(bool hotkey, bool watchClipboard)
    {
        // Cada cambio rehace el hilo con lo pedido: la conexión X solo la usa ese hilo.
        if (_thread is not null)
        {
            _stop = true;
            _thread.Join(TimeSpan.FromSeconds(2));
            _thread = null;
        }
        _polling = false;
        HotkeyProblem = null;
        if (!hotkey && !watchClipboard)
            return;
        if (IsWayland)
        {
            if (hotkey)
                HotkeyProblem = Localization.S.CopySendHotkeyWayland;
            _polling = watchClipboard;
            return;
        }
        var ready = new ManualResetEventSlim();
        _stop = false;
        _thread = new Thread(() => Run(hotkey, watchClipboard, ready)) { IsBackground = true, Name = "AlToque atajo" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Run(bool hotkey, bool watchClipboard, ManualResetEventSlim ready)
    {
        nint display = 0;
        try
        {
            display = XOpenDisplay(0);
            if (display == 0)
            {
                HotkeyProblem = hotkey ? Localization.S.CopySendHotkeyFailed : null;
                _polling = watchClipboard;
                return;
            }
            var root = XDefaultRootWindow(display);
            int keycode = XKeysymToKeycode(display, 0x63); // XK_c
            uint[] variants = [0, LockMask, Mod2Mask, LockMask | Mod2Mask];
            if (hotkey)
            {
                foreach (var extra in variants)
                    XGrabKey(display, keycode, ControlMask | Mod1Mask | extra, root, 0, GrabModeAsync, GrabModeAsync);
            }
            var fixesEvent = -1;
            if (watchClipboard)
            {
                try
                {
                    if (XFixesQueryExtension(display, out var eventBase, out _) != 0)
                    {
                        XFixesSelectSelectionInput(display, root, XInternAtom(display, "CLIPBOARD", 0), SetSelectionOwnerNotifyMask);
                        fixesEvent = eventBase; // XFixesSelectionNotify = base + 0
                    }
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                {
                }
                _polling = fixesEvent < 0;
            }
            XSync(display, 0);
            ready.Set();

            var buffer = stackalloc byte[256];
            while (!_stop)
            {
                while (XPending(display) > 0)
                {
                    XNextEvent(display, buffer);
                    var type = *(int*)buffer;
                    if (type == KeyPress && hotkey)
                        RaiseHotkey();
                    else if (type == fixesEvent)
                        RaiseClipboard();
                }
                Thread.Sleep(40);
            }
            if (hotkey)
            {
                foreach (var extra in variants)
                    XUngrabKey(display, keycode, ControlMask | Mod1Mask | extra, root);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            HotkeyProblem = hotkey ? Localization.S.CopySendHotkeyFailed : null;
            _polling = watchClipboard;
            AgentLog.Write("Sin X11 para el atajo: " + ex.Message);
        }
        finally
        {
            if (display != 0)
                XCloseDisplay(display);
            ready.Set();
        }
    }

    [LibraryImport(X11)]
    private static partial nint XOpenDisplay(nint name);

    [LibraryImport(X11)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(X11)]
    private static partial nint XDefaultRootWindow(nint display);

    [LibraryImport(X11)]
    private static partial byte XKeysymToKeycode(nint display, nint keysym);

    [LibraryImport(X11)]
    private static partial int XGrabKey(nint display, int keycode, uint modifiers, nint window, int ownerEvents, int pointerMode, int keyboardMode);

    [LibraryImport(X11)]
    private static partial int XUngrabKey(nint display, int keycode, uint modifiers, nint window);

    [LibraryImport(X11)]
    private static partial int XPending(nint display);

    [LibraryImport(X11)]
    private static partial int XNextEvent(nint display, byte* ev);

    [LibraryImport(X11)]
    private static partial int XSync(nint display, int discard);

    [LibraryImport(X11, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XInternAtom(nint display, string name, int onlyIfExists);

    [LibraryImport(Xfixes)]
    private static partial int XFixesQueryExtension(nint display, out int eventBase, out int errorBase);

    [LibraryImport(Xfixes)]
    private static partial void XFixesSelectSelectionInput(nint display, nint window, nint selection, ulong eventMask);
}

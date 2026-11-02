using System.Collections.Concurrent;
using AlToque.Core.Agent;
using AlToque.Mobile;
using Android.App;
using Android.Content;
using Android.Media;
using Android.Net.Wifi;
using Android.OS;
using Android.Webkit;
using AndroidEnvironment = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;

namespace AlToque.Android;

/// <summary>Lo propio del teléfono: carpetas, Wi-Fi, galería, abrir archivos y el servicio de transferencias.</summary>
public sealed class AndroidPlatform : IMobilePlatform
{
    public const string LogTag = "AlToque";

    private readonly Context _context;
    private readonly ConcurrentDictionary<string, AndroidUri> _published = new(StringComparer.Ordinal);
    private WifiManager.MulticastLock? _multicast;

    public AndroidPlatform(Context context)
    {
        _context = context.ApplicationContext ?? context;
        DataDirectory = Path.Combine(_context.FilesDir!.AbsolutePath, "altoque");
        CacheDirectory = _context.CacheDir!.AbsolutePath;
        AcquireMulticast();
    }

    public string DataDirectory { get; }
    public string CacheDirectory { get; }

    // Desde Android 11 la app puede crear archivos en Descargas sin pedir permisos.
#pragma warning disable CA1422 // La ruta pública de Descargas sigue siendo válida para escribir los archivos propios.
    public string? DownloadsDirectory => AndroidEnvironment.GetExternalStoragePublicDirectory(AndroidEnvironment.DirectoryDownloads!)?.AbsolutePath;
#pragma warning restore CA1422

    public string DeviceName
    {
        get
        {
            try
            {
                if (global::Android.Provider.Settings.Global.GetString(_context.ContentResolver, "device_name") is { Length: > 0 } name)
                    return name;
            }
            catch (Java.Lang.Exception)
            {
            }
            return Build.Model is { Length: > 0 } model ? model : "Android";
        }
    }

    /// <summary>Los anuncios de los demás equipos llegan por multicast y difusión: Android los filtra sin este permiso activo.</summary>
    private void AcquireMulticast()
    {
        try
        {
            if (_context.GetSystemService(Context.WifiService) is WifiManager wifi)
            {
                _multicast = wifi.CreateMulticastLock("altoque-descubrimiento");
                _multicast?.SetReferenceCounted(false);
                _multicast?.Acquire();
            }
        }
        catch (Java.Lang.Exception ex)
        {
            AgentLog.Write("Sin bloqueo multicast: " + ex.Message);
        }
    }

    public void PublishReceived(IReadOnlyList<string> paths)
    {
        try
        {
            var files = paths.Where(File.Exists).ToArray();
            if (files.Length == 0)
                return;
            var mimes = files.Select(MimeFor).ToArray();
            MediaScannerConnection.ScanFile(_context, files, mimes, new ScanCallback(_published));
        }
        catch (Java.Lang.Exception ex)
        {
            AgentLog.Write("No se pudo avisar a la galería: " + ex.Message);
        }
    }

    public void OpenReceived(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path) && _published.TryGetValue(path, out var uri))
            {
                var view = new Intent(Intent.ActionView);
                view.SetDataAndType(uri, MimeFor(path));
                view.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
                _context.StartActivity(Intent.CreateChooser(view, (string?)null)!.AddFlags(ActivityFlags.NewTask));
                return;
            }
            var downloads = new Intent(DownloadManager.ActionViewDownloads);
            downloads.AddFlags(ActivityFlags.NewTask);
            _context.StartActivity(downloads);
        }
        catch (ActivityNotFoundException ex)
        {
            AgentLog.Write("Nada puede abrir lo recibido: " + ex.Message);
        }
    }

    public void OpenUrl(string url)
    {
        try
        {
            var intent = new Intent(Intent.ActionView, AndroidUri.Parse(url));
            intent.AddFlags(ActivityFlags.NewTask);
            _context.StartActivity(intent);
        }
        catch (ActivityNotFoundException ex)
        {
            AgentLog.Write("No hay navegador para abrir el enlace: " + ex.Message);
        }
    }

    private bool _busy;
    private bool _keepAlive;

    public void SetBusy(bool busy)
    {
        if (busy)
            MainActivity.AskForNotifications();
        MainActivity.KeepScreenOn(busy);
        _busy = busy;
        TransferService.SetRunning(_context, _busy || _keepAlive, _keepAlive);
    }

    public void SetKeepAlive(bool keep)
    {
        if (keep)
            MainActivity.AskForNotifications();
        _keepAlive = keep;
        TransferService.SetRunning(_context, _busy || _keepAlive, _keepAlive);
    }

    private const string MessagesChannel = "mensajes";

    public void NotifyMessage(string peerId, string title, string text)
    {
        try
        {
            var manager = (NotificationManager)_context.GetSystemService(Context.NotificationService)!;
            manager.CreateNotificationChannel(new NotificationChannel(MessagesChannel, Localization.S.MobileMessagesChannel, NotificationImportance.High));
            var open = new Intent(_context, typeof(MainActivity));
            open.PutExtra(MainActivity.ChatExtra, peerId);
            open.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
            var pending = PendingIntent.GetActivity(_context, NotificationIdFor(peerId), open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var notification = new Notification.Builder(_context, MessagesChannel)
                .SetContentTitle(title)!
                .SetContentText(text)!
                .SetStyle(new Notification.BigTextStyle().BigText(text))!
                .SetSmallIcon(Resource.Drawable.ic_notification)!
                .SetContentIntent(pending)!
                .SetAutoCancel(true)!
                .SetCategory(Notification.CategoryMessage)!
                .Build()!;
            manager.Notify(NotificationIdFor(peerId), notification);
        }
        catch (Java.Lang.Exception ex)
        {
            AgentLog.Write("No se pudo mostrar la notificación del mensaje: " + ex.Message);
        }
    }

    public void ClearMessages(string peerId)
    {
        try
        {
            ((NotificationManager)_context.GetSystemService(Context.NotificationService)!).Cancel(NotificationIdFor(peerId));
        }
        catch (Java.Lang.Exception)
        {
        }
    }

    /// <summary>Una notificación por compañero (los mensajes nuevos de esa persona la reemplazan).</summary>
    private static int NotificationIdFor(string peerId) => 10_000 + (int)((uint)StringComparer.Ordinal.GetHashCode(peerId) % 50_000);

    private static string MimeFor(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return MimeTypeMap.Singleton?.GetMimeTypeFromExtension(ext) ?? "application/octet-stream";
    }

    /// <summary>El escáner devuelve la dirección content:// con la que otras apps pueden abrir cada archivo.</summary>
    private sealed class ScanCallback(ConcurrentDictionary<string, AndroidUri> published) : Java.Lang.Object, MediaScannerConnection.IOnScanCompletedListener
    {
        public void OnScanCompleted(string? path, AndroidUri? uri)
        {
            if (path is not null && uri is not null)
                published[path] = uri;
        }
    }
}

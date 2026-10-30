using AlToque.Core.Agent;
using AlToque.Mobile;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Views;
using Avalonia.Android;
using Avalonia.Threading;
using AndroidUri = Android.Net.Uri;

namespace AlToque.Android;

[Activity(Name = "com.krostgames.altoque.MainActivity", Label = "Al Toque", Theme = "@style/AlToque.Theme", Icon = "@drawable/icon",
    MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTask, WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
// "Compartir" desde la galería, el gestor de archivos o cualquier app.
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
[IntentFilter([Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
public sealed class MainActivity : AvaloniaMainActivity
{
    /// <summary>Extra para la autoprueba del CI: adb shell am start -n …/.MainActivity --ez altoque_selftest true</summary>
    public const string SelfTestExtra = "altoque_selftest";

    private static WeakReference<MainActivity>? _current;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _current = new WeakReference<MainActivity>(this);
        AskForNotifications();
        HandleIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }

    /// <summary>Pantalla encendida mientras pasan archivos: si se apaga, Android puede cortar la Wi-Fi.</summary>
    public static void KeepScreenOn(bool on)
    {
        if (_current?.TryGetTarget(out var activity) != true || activity!.Window is not { } window)
            return;
        activity.RunOnUiThread(() =>
        {
            if (on)
                window.AddFlags(WindowManagerFlags.KeepScreenOn);
            else
                window.ClearFlags(WindowManagerFlags.KeepScreenOn);
        });
    }

    /// <summary>Android 13+: sin este permiso la notificación de "pasando archivos" queda oculta.</summary>
    private void AskForNotifications()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            RequestPermissions([Manifest.Permission.PostNotifications], 1);
    }

    private void HandleIntent(Intent? intent)
    {
        if (intent is null)
            return;
        if (intent.GetBooleanExtra(SelfTestExtra, false))
        {
            intent.RemoveExtra(SelfTestExtra);
            var cache = CacheDir!.AbsolutePath;
            var files = FilesDir!.AbsolutePath;
            _ = Task.Run(async () =>
            {
                var result = await SelfTest.RunAsync(cache);
                global::Android.Util.Log.Info(AndroidPlatform.LogTag, result);
                await File.WriteAllTextAsync(Path.Combine(files, "selftest.txt"), result);
            });
            return;
        }
        if (intent.Action is Intent.ActionSend or Intent.ActionSendMultiple)
        {
            var uris = SharedUris(intent);
            var text = intent.GetStringExtra(Intent.ExtraText);
            intent.SetAction(null); // Que una rotación o volver a la app no lo comparta otra vez.
            if (uris.Count > 0 || !string.IsNullOrWhiteSpace(text))
                _ = CopySharedAsync(uris, uris.Count == 0 ? text : null);
        }
    }

#pragma warning disable CA1422 // GetParcelableExtra(string) sigue funcionando en todas las versiones.
    private static List<AndroidUri> SharedUris(Intent intent)
    {
        var list = new List<AndroidUri>();
        if (intent.Action == Intent.ActionSend && intent.GetParcelableExtra(Intent.ExtraStream) is AndroidUri one)
            list.Add(one);
        else if (intent.GetParcelableArrayListExtra(Intent.ExtraStream) is { } many)
            list.AddRange(many.OfType<AndroidUri>());
        if (list.Count == 0 && intent.ClipData is { } clip)
        {
            for (int i = 0; i < clip.ItemCount; i++)
            {
                if (clip.GetItemAt(i)?.Uri is { } uri)
                    list.Add(uri);
            }
        }
        return list;
    }
#pragma warning restore CA1422

    /// <summary>Lo compartido llega como content://: se copia a la caché y queda listo para elegir a quién.</summary>
    private async Task CopySharedAsync(List<AndroidUri> uris, string? text)
    {
        var cache = CacheDir!.AbsolutePath;
        var resolver = ContentResolver!;
        try
        {
            var (paths, dir) = await Task.Run(async () =>
            {
                var batch = OutgoingFiles.NewBatch(cache);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var result = new List<string>();
                if (text is not null)
                {
                    // Un enlace o un texto compartido viaja como un .txt.
                    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
                    result.Add(await OutgoingFiles.CopyAsync(stream, "texto.txt", batch, used));
                }
                foreach (var uri in uris)
                {
                    await using var input = resolver.OpenInputStream(uri);
                    if (input is null)
                        continue;
                    result.Add(await OutgoingFiles.CopyAsync(input, DisplayName(resolver, uri), batch, used));
                }
                return (result, batch);
            });
            Dispatcher.UIThread.Post(() => MobileApp.Session?.ViewModel.Share(paths, dir));
        }
        catch (Exception ex) when (ex is IOException or Java.Lang.Exception or UnauthorizedAccessException)
        {
            AgentLog.Write("No se pudo leer lo compartido: " + ex.Message);
        }
    }

    private static string? DisplayName(ContentResolver resolver, AndroidUri uri)
    {
        try
        {
            using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
                return cursor.GetString(0);
        }
        catch (Java.Lang.Exception)
        {
        }
        return uri.LastPathSegment;
    }
}

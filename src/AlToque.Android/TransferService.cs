using AlToque.Core.Agent;
using AlToque.Localization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace AlToque.Android;

/// <summary>
/// Servicio en primer plano mientras hay transferencias: Android no congela la app aunque se cambie a otra, y una
/// notificación avisa que Al Toque está trabajando. Se detiene solo al terminar.
/// </summary>
[Service(Name = "com.krostgames.altoque.TransferService", Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class TransferService : Service
{
    private const string Channel = "transferencias";
    private const int NotificationId = 4583;

    public static void SetRunning(Context context, bool running)
    {
        try
        {
            var intent = new Intent(context, typeof(TransferService));
            if (running)
                context.StartForegroundService(intent);
            else
                context.StopService(intent);
        }
        catch (Java.Lang.Exception ex)
        {
            // Sin el servicio la transferencia sigue igual mientras la app esté a la vista.
            AgentLog.Write("No se pudo iniciar el servicio de transferencias: " + ex.Message);
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        try
        {
            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            manager.CreateNotificationChannel(new NotificationChannel(Channel, S.MobileServiceChannel, NotificationImportance.Low));
            var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var notification = new Notification.Builder(this, Channel)
                .SetContentTitle(S.MobileServiceTitle)!
                .SetContentText(S.MobileServiceBody)!
                .SetSmallIcon(Resource.Drawable.ic_notification)!
                .SetContentIntent(open)!
                .SetOngoing(true)!
                .Build()!;
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        }
        catch (Java.Lang.Exception ex)
        {
            AgentLog.Write("El servicio de transferencias no pudo mostrarse: " + ex.Message);
            StopSelf();
        }
        return StartCommandResult.NotSticky;
    }
}

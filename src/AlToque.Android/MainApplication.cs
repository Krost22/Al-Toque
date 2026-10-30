using Android.App;
using Android.Runtime;
using AlToque.Mobile;
using Avalonia;
using Avalonia.Android;

namespace AlToque.Android;

[Application]
public sealed class MainApplication : AvaloniaAndroidApplication<MobileApp>
{
    public MainApplication(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // La plataforma tiene que existir antes de que Avalonia arranque la app (y con ella el agente).
        AlToque.Core.Agent.AgentLog.Echo = line => global::Android.Util.Log.Info(AndroidPlatform.LogTag, line);
        MobileApp.Platform = new AndroidPlatform(this);
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont().LogToTrace();
}

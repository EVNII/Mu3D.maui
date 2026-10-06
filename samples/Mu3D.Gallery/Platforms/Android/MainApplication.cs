using Android.App;
using Android.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Mu3D.Gallery;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp()
    {
        // Keep startup consistent with the app's unconditional UseMaterial3 opt-in. The tested
        // Release APK contains that switch, but the device still instantiated legacy handlers.
        // Set it before building MAUI so both handler registration and the Activity theme see it.
        const string material3Switch = "Microsoft.Maui.RuntimeFeature.IsMaterial3Enabled";
        bool switchPresent = AppContext.TryGetSwitch(material3Switch, out bool material3Enabled);
        Android.Util.Log.Info("Mu3D.Material3",
            $"Before MAUI: switch present={switchPresent}, enabled={material3Enabled}; requested=True");
        AppContext.SetSwitch(material3Switch, true);

        MauiApp app = MauiProgram.CreateMauiApp();
        IMauiHandlersFactory handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();
        Android.Util.Log.Info("Mu3D.Material3",
            $"Registered handlers: Label={handlers.GetHandlerType(typeof(Label))?.Name}, " +
            $"SearchBar={handlers.GetHandlerType(typeof(SearchBar))?.Name}, " +
            $"Slider={handlers.GetHandlerType(typeof(Slider))?.Name}, " +
            $"Switch={handlers.GetHandlerType(typeof(Switch))?.Name}");
        return app;
    }
}

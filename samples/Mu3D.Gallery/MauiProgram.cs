using AdaptiveShell.Hosting;
using Microsoft.Extensions.Logging;
using Mu3D.Maui;

namespace Mu3D.Gallery;

/// <summary>
/// Configures the Mu3D Gallery application built on AdaptiveShell navigation.
/// </summary>
public static partial class MauiProgram
{
    static partial void ConfigurePlatformHandlers(MauiAppBuilder builder);

    /// <summary>Creates the configured MAUI application.</summary>
    /// <returns>The configured application.</returns>
    public static MauiApp CreateMauiApp()
    {
        AppContext.SetSwitch("Mu3D.Maui.Windows.ExperimentalSynchronizedTransparentHdrMask", true);
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .UseMu3D()
            .UseAdaptiveShell();

        ConfigurePlatformHandlers(builder);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

using AdaptiveShell.Hosting;
using Microsoft.Extensions.Logging;
using Mu3D.Maui;

namespace Mu3D.Gallery;

/// <summary>
/// Configures the Mu3D Gallery application built on AdaptiveShell navigation.
/// </summary>
public static class MauiProgram
{
    /// <summary>Creates the configured MAUI application.</summary>
    /// <returns>The configured application.</returns>
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMu3D()
            .UseAdaptiveShell();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

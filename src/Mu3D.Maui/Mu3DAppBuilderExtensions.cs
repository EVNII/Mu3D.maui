using Microsoft.Maui.Hosting;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Handlers;

namespace Mu3D.Maui;

/// <summary>Registers Mu3D controls and platform handlers with a MAUI application.</summary>
public static class Mu3DAppBuilderExtensions
{
    /// <summary>Adds the Mu3D platform surface handler.</summary>
    public static MauiAppBuilder UseMu3D(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Mu3DView, Mu3DViewHandler>());
        return builder;
    }
}

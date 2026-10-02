using Foundation;

namespace Mu3D.GalleryApp;

/// <summary>
/// Creates the Gallery MAUI application on iOS.
/// </summary>
[Register("AppDelegate")]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

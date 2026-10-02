using Android.App;
using Android.Runtime;

namespace Mu3D.GalleryApp;

/// <summary>
/// Creates the Gallery MAUI application on Android.
/// </summary>
[Application]
public sealed class MainApplication(nint handle, JniHandleOwnership ownership)
    : MauiApplication(handle, ownership)
{
    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

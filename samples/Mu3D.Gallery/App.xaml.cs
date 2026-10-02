namespace Mu3D.Gallery;

/// <summary>
/// Represents the Mu3D Gallery application.
/// </summary>
public partial class App : Application
{
    /// <summary>Initializes a new instance of the <see cref="App"/> class.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        AppShell shell = new();
        Window window = new(shell)
        {
            Title = "Mu3D Gallery",
        };
        window.Stopped += (_, _) => shell.SuspendGallery();
        window.Resumed += (_, _) => shell.ResumeGallery();
        window.Destroying += (_, _) => shell.CloseGallery();
#if MACCATALYST
        window.MinimumWidth = 420;
        window.MinimumHeight = 480;
#endif
        return window;
    }
}

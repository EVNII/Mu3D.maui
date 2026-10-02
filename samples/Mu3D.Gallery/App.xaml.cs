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
        Window window = new(new AppShell())
        {
            Title = "Mu3D Gallery",
        };
#if MACCATALYST
        window.MinimumWidth = 900;
        window.MinimumHeight = 600;
#endif
        return window;
    }
}

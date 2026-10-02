namespace Mu3D.GalleryApp;

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
    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());
}

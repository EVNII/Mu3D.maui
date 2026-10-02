namespace Mu3D.GalleryApp.WinUI;

/// <summary>
/// Creates the Gallery MAUI application on Windows.
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>Initializes a new instance of the <see cref="App"/> class.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

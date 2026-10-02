namespace Mu3D.GalleryApp.Pages;

/// <summary>Explains the Gallery application and links to its license acknowledgements.</summary>
public partial class AboutPage : ContentPage
{
    /// <summary>Initializes the Gallery about page.</summary>
    public AboutPage()
    {
        InitializeComponent();
    }

    private async void OnLicensesClicked(object? sender, EventArgs e)
    {
        _ = sender;
        await Shell.Current.GoToAsync(GalleryRoutes.Licenses);
    }
}

namespace Mu3D.GalleryApp.Pages;

/// <summary>Physically validates gain-map JPEG encoding and decoding through the pinned codec.</summary>
public partial class HdrJpegLabPage : ContentPage
{
    /// <summary>Initializes the HDR JPEG round-trip lab.</summary>
    public HdrJpegLabPage()
    {
        InitializeComponent();
    }

    private async void OnRunRoundTripClicked(object? sender, EventArgs e)
    {
        _ = sender;
        RunRoundTripButton.IsEnabled = false;
        ResultLabel.Text = "Encoding…";
        try
        {
            string result = await Task.Run(() => HdrJpegRoundTrip.Run().Report);
            ResultLabel.Text = result;
        }
        catch (Exception error)
        {
            ResultLabel.Text = error.ToString();
        }
        finally
        {
            RunRoundTripButton.IsEnabled = true;
        }
    }

}

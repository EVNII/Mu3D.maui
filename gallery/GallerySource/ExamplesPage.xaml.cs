namespace Mu3D.GalleryApp.Pages;

/// <summary>Lists the Gallery examples used by the adaptive navigation hub.</summary>
public partial class ExamplesPage : ContentPage
{
    /// <summary>Initializes the adaptive examples hub.</summary>
    public ExamplesPage()
    {
        InitializeComponent();
        Examples = GalleryNavigationCatalog.Examples;
        BindingContext = this;
    }

    /// <summary>Gets the ordered Gallery example entries.</summary>
    public IReadOnlyList<GalleryExample> Examples { get; }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not GalleryExample example)
        {
            return;
        }

        ((CollectionView)sender!).SelectedItem = null;
        await GalleryNavigationService.NavigateToExampleAsync(example.Route);
    }
}

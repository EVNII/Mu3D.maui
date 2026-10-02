namespace Mu3D.Gallery.Pages;

/// <summary>Finds real Gallery examples without changing the native navigation tree.</summary>
public partial class ExampleCatalogPage : ContentPage
{
    private readonly AppShell shell;
    private readonly string? sectionId;

    internal ExampleCatalogPage(AppShell shell, string? sectionId)
    {
        this.shell = shell;
        this.sectionId = sectionId;
        InitializeComponent();
        GallerySection? section = GalleryCatalog.Sections.SingleOrDefault(section => section.Id == sectionId);
        Title = Heading.Text = section?.Title ?? "Mu3D Examples";
        Description.Text = section?.Description ??
            "Choose one focused example. Native navigation adapts to the window; rendering stays HDR-first.";
        UpdateResults();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => UpdateResults();

    private void UpdateResults()
    {
        if (Examples is null)
        {
            return;
        }
        GalleryEntry[] results = GalleryCatalog.Search(Search.Text, sectionId).ToArray();
        Examples.ItemsSource = results;
        ResultCount.Text = $"{results.Length} examples";
    }

    private void OnExampleSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is GalleryEntry entry)
        {
            Examples.SelectedItem = null;
            shell.NavigateTo(entry.Id);
        }
    }
}

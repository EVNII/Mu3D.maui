using System.ComponentModel;
using AdaptiveShell.Controls;
using Mu3D.Gallery.Controls;
using Mu3D.GalleryApp;

namespace Mu3D.Gallery;

/// <summary>Hosts the complete Gallery in five platform-adaptive navigation groups.</summary>
public partial class AppShell : AShell
{
    private readonly Dictionary<string, AShellContent> destinations = [];
    private readonly Dictionary<AShellContent, Page> createdPages = [];
    private readonly GalleryPageActivation activation = new();
    private AShellContent? browserContent;
    private bool suspended;

    /// <summary>Creates the static navigation tree without instantiating rendering pages.</summary>
    public AppShell()
    {
        InitializeComponent();
        foreach (GallerySection section in GalleryCatalog.Sections)
        {
            AShellGroup group = new()
            {
                Title = section.Title,
                Icon = section.Icon,
                AutomationId = $"gallery-section-{section.Id}",
                LandingTemplate = new DataTemplate(() => CreateCatalogPage(section.Id)),
            };
            if (section.Id == "basics")
            {
                browserContent = new AShellContent
                {
                    Title = "All Examples",
                    Icon = "scene.svg",
                    AutomationId = "gallery-all-examples",
                    ContentTemplate = new DataTemplate(() => CreateCatalogPage(null)),
                };
                group.Items.Add(browserContent);
            }
            foreach (GalleryEntry entry in GalleryCatalog.Examples.Where(entry => entry.SectionId == section.Id))
            {
                AShellContent destination = new()
                {
                    Title = entry.Title,
                    Icon = section.Icon,
                    AutomationId = $"gallery-example-{entry.Id}",
                    FullBleed = false,
                };
                destination.ContentTemplate = new DataTemplate(() => CreateExamplePage(entry, destination));
                destinations.Add(entry.Id, destination);
                group.Items.Add(destination);
            }
            Items.Add(group);
        }
        PropertyChanged += OnShellPropertyChanged;
        CurrentItem = browserContent;
    }

    internal void NavigateTo(string id)
    {
        if (!destinations.TryGetValue(id, out AShellContent? destination))
        {
            throw new ArgumentException($"Unknown Gallery example '{id}'.", nameof(id));
        }
        Select(destination);
    }

    internal void BrowseExamples()
    {
        if (browserContent is not null)
        {
            Select(browserContent);
        }
    }

    internal void SuspendGallery()
    {
        suspended = true;
        activation.Deactivate();
    }

    internal void ResumeGallery()
    {
        suspended = false;
        ActivateCurrentPage();
    }

    internal void CloseGallery()
    {
        PropertyChanged -= OnShellPropertyChanged;
        activation.Dispose();
    }

    private Page CreateCatalogPage(string? sectionId)
    {
        Page page = new Pages.ExampleCatalogPage(this, sectionId);
        if (sectionId is null && browserContent is not null)
        {
            createdPages[browserContent] = page;
            activation.Register(page);
            if (!suspended && ReferenceEquals(CurrentItem, browserContent))
            {
                activation.Activate(page);
            }
        }
        else
        {
            activation.RegisterLanding(page);
        }
        return page;
    }

    private Page CreateExamplePage(GalleryEntry entry, AShellContent destination)
    {
        Page page = GalleryPageFactory.Create(entry.Id);
        page.AutomationId = $"gallery-page-{entry.Id}";
        if (entry.FeatureId is string featureId &&
            !page.ToolbarItems.OfType<GalleryDocumentationToolbarItem>().Any())
        {
            page.ToolbarItems.Add(new GalleryDocumentationToolbarItem { FeatureId = featureId });
        }
        ToolbarItem browse = new() { Text = "Examples", AutomationId = "gallery-browse-examples" };
        browse.Clicked += (_, _) => BrowseExamples();
        page.ToolbarItems.Add(browse);
        createdPages[destination] = page;
        activation.Register(page);
        if (!suspended && ReferenceEquals(CurrentItem, destination))
        {
            activation.Activate(page);
        }
        return page;
    }

    private void Select(AShellContent destination)
    {
        if (ReferenceEquals(CurrentItem, destination))
        {
            // Native back-to-group preserves CurrentItem; reselecting must still display its page.
            Handler?.UpdateValue(nameof(CurrentItem));
            ActivateCurrentPage();
        }
        else
        {
            CurrentItem = destination;
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CurrentItem))
        {
            ActivateCurrentPage();
        }
    }

    private void ActivateCurrentPage()
    {
        if (!suspended && CurrentItem is not null && createdPages.TryGetValue(CurrentItem, out Page? page))
        {
            activation.Activate(page);
        }
        else
        {
            activation.Deactivate();
        }
    }
}

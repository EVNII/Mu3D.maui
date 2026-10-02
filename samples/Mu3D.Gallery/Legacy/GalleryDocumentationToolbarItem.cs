namespace Mu3D.GalleryApp;

/// <summary>Opens one validated Gallery feature article in the system-preferred browser.</summary>
public sealed class GalleryDocumentationToolbarItem : ToolbarItem
{
    /// <summary>Identifies the <see cref="FeatureId"/> bindable property.</summary>
    public static readonly BindableProperty FeatureIdProperty = BindableProperty.Create(
        nameof(FeatureId),
        typeof(string),
        typeof(GalleryDocumentationToolbarItem),
        string.Empty,
        validateValue: static (_, value) =>
            value is string featureId && !string.IsNullOrWhiteSpace(featureId),
        propertyChanged: static (bindable, _, _) =>
            ((GalleryDocumentationToolbarItem)bindable).AttachSourceViewer());

    private bool isOpening;

    /// <summary>Initializes a standard Gallery documentation action.</summary>
    public GalleryDocumentationToolbarItem()
    {
        Text = "Docs";
        AutomationId = "GalleryDocs";
        Clicked += OnClicked;
    }

    /// <summary>Gets or sets the stable Gallery feature ID.</summary>
    public string FeatureId
    {
        get => (string)GetValue(FeatureIdProperty);
        set => SetValue(FeatureIdProperty, value);
    }

    /// <inheritdoc />
    protected override void OnParentSet()
    {
        base.OnParentSet();
        AttachSourceViewer();
    }

    private void AttachSourceViewer()
    {
        if (string.IsNullOrWhiteSpace(FeatureId))
        {
            return;
        }

        if (FindOwningPage() is ContentPage page)
        {
            GallerySourceCodePresenter.Attach(page);
        }
    }

    private async void OnClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (isOpening)
        {
            return;
        }

        isOpening = true;
        IsEnabled = false;
        try
        {
            if (!await GalleryDocumentation.OpenAsync(FeatureId))
            {
                await ShowFailureAsync("No system browser accepted the documentation link.");
            }
        }
        catch (Exception exception)
        {
            await ShowFailureAsync(exception.Message);
        }
        finally
        {
            IsEnabled = true;
            isOpening = false;
        }
    }

    private Page? FindOwningPage()
    {
        for (Element? ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is Page page) return page;
        }
        return null;
    }

    private Task ShowFailureAsync(string message)
    {
        Page? owner = FindOwningPage();
        // AdaptiveShell hosts the example page without a Shell.Current. Its window's root page
        // owns native dialogs even when this toolbar belongs to a nested ContentPage.
        Page? dialogPage = owner?.Window?.Page ?? owner;
        return dialogPage?.DisplayAlertAsync("Documentation unavailable", message, "OK") ??
            Task.CompletedTask;
    }
}

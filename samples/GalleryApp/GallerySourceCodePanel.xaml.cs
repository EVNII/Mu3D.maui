namespace Mu3D.GalleryApp;

/// <summary>Provides the desktop source drawer used by Gallery feature pages.</summary>
public partial class GallerySourceCodePanel : ContentView
{
    private readonly Type sourcePageType;
    private GalleryExampleSource? source;
    private bool isExpanded;

    /// <summary>Initializes a source drawer for one Gallery feature page type.</summary>
    /// <param name="sourcePageType">The page type whose packaged XAML and C# source is displayed.</param>
    public GallerySourceCodePanel(Type sourcePageType)
    {
        this.sourcePageType = sourcePageType ?? throw new ArgumentNullException(nameof(sourcePageType));
        InitializeComponent();
        SelectXaml();
    }

    private async void OnExpandClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        isExpanded = !isExpanded;
        ExpandedContent.IsVisible = isExpanded;
        ExpandButton.Text = isExpanded ? "Hide example code" : "Show example code";
        if (isExpanded && source is null)
        {
            await LoadSourceAsync();
        }
    }

    private void OnXamlClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        SelectXaml();
    }

    private void OnCSharpClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        XamlButton.IsEnabled = true;
        CSharpButton.IsEnabled = false;
        SourceEditor.Text = source?.CSharp ?? string.Empty;
    }

    private async void OnCopyClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (string.IsNullOrEmpty(SourceEditor.Text))
        {
            return;
        }

        try
        {
            await Clipboard.Default.SetTextAsync(SourceEditor.Text);
            CopyButton.Text = "Copied";
            await Task.Delay(1000);
            CopyButton.Text = "Copy";
        }
        catch (Exception exception)
        {
            CopyButton.Text = "Copy unavailable";
            SemanticScreenReader.Announce($"Copy failed: {exception.Message}");
        }
    }

    private async Task LoadSourceAsync()
    {
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        try
        {
            source = await GalleryExampleSourceLoader.LoadAsync(sourcePageType);
            SourceEditor.Text = XamlButton.IsEnabled ? source.CSharp : source.Xaml;
        }
        catch (Exception exception)
        {
            SourceEditor.Text = $"Example source is unavailable.\n\n{exception.Message}";
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private void SelectXaml()
    {
        XamlButton.IsEnabled = false;
        CSharpButton.IsEnabled = true;
        SourceEditor.Text = source?.Xaml ?? string.Empty;
    }
}

namespace Mu3D.Maui.Toolkit.Controls
{
    /// <summary>Supplies the Gallery coordinator's host switch without a graphics device.</summary>
    public sealed class SceneViewProxyHost : Grid
    {
        /// <summary>Gets or sets the fixture's application rendering intent.</summary>
        public bool IsRenderingEnabled { get; set; } = true;
    }
}

namespace Mu3D.Gallery.Pages
{
    // The coordinator excludes catalog pages from its GPU content gate.
    internal sealed class ExampleCatalogPage : ContentPage { }
}

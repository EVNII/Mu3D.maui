using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Hosting;
using Mu3D.GalleryApp.Pages;

namespace Mu3D.Gallery;

public static partial class MauiProgram
{
    static partial void ConfigurePlatformHandlers(MauiAppBuilder builder)
    {
        // The optimized Apple CollectionView handler uses an estimated-height compositional
        // layout even for this uniform feed. Upward realization then corrects earlier estimates,
        // moving the content anchor and scrollbar. Keep that behavior out of this one diagnostic
        // feed while leaving every ordinary Gallery CollectionView on the optimized handler.
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<ProductFeedCollectionView, CollectionViewHandler>());
    }
}

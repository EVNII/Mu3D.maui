using AndroidX.RecyclerView.Widget;

namespace Mu3D.GalleryApp.Pages;

public partial class ProductFeedPage
{
    partial void ConfigureProductsViewPlatform()
    {
        ProductsView.HandlerChanged += OnProductsViewHandlerChanged;
        ApplyFixedRecyclerViewPolicy();
    }

    private void OnProductsViewHandlerChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyFixedRecyclerViewPolicy();
    }

    private void ApplyFixedRecyclerViewPolicy()
    {
        if (ProductsView.Handler?.PlatformView is not RecyclerView recyclerView)
        {
            return;
        }

        // Product descriptors and card extents are fixed. Prevent content-state bindings from
        // invalidating the RecyclerView's own size, and avoid animations for non-structural state
        // changes such as Poster/Live while the user is scrolling.
        recyclerView.HasFixedSize = true;
        recyclerView.SetItemAnimator(null!);
    }
}

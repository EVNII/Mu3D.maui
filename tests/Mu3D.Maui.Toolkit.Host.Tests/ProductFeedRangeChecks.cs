using Mu3D.GalleryApp.Pages;

internal static class ProductFeedRangeChecks
{
    internal static void Verify()
    {
        ProductFeedVisibleRange range = new();
        // Screenshot regression: many cached/realized earlier products, actual cards #19–24.
        // Enumerate backwards to ensure visual-tree order is not used as model identity.
        for (int index = 39; index >= 6; index--)
        {
            int row = index / 2;
            range.Include(index, (index % 2) * 400, (row - 9) * 268 - 20,
                390, 260, 800, 750);
        }
        Expect(range.First == 18 && range.Last == 23, "global indexes after virtualization");

        ProductFeedVisibleRange edges = new();
        edges.Include(50, 0, -259, 390, 260, 800, 750);
        edges.Include(55, 400, 749, 390, 260, 800, 750);
        edges.Include(0, 0, -260, 390, 260, 800, 750);
        edges.Include(59, 0, 750, 390, 260, 800, 750);
        edges.Include(1, -390, 0, 390, 260, 800, 750);
        edges.Include(58, 800, 0, 390, 260, 800, 750);
        Expect(edges.First == 50 && edges.Last == 55, "partial cards included; touching edges excluded");

        ProductFeedVisibleRange empty = new();
        empty.Include(-1, 0, 0, 390, 260, 800, 750);
        empty.Include(5, 0, double.NaN, 390, 260, 800, 750);
        empty.Include(6, 0, 0, 390, 0, 800, 750);
        empty.Include(7, 0, 0, 390, 260, 800, 0);
        Expect(empty.First == -1 && empty.Last == -1, "unmeasured/detached containers provide no new range");

        ProductFeedVisibleRange reattached = new();
        // Live Windows trace: reattachment initially measures labels alone at height 44.
        // Publishing that temporary layout promotes 24 cards instead of the final six.
        for (int index = 0; index < 24; index++)
        {
            reattached.Include(index, (index % 2) * 400, (index / 2) * 60 + 8,
                390, 44, 800, 701, minimumMeasuredHeight: 260);
        }
        Expect(reattached.First == -1 && reattached.Last == -1,
            "reattached label-only measurement keeps range pending");
        for (int index = 0; index < 24; index++)
        {
            reattached.Include(index, (index % 2) * 400, (index / 2) * 276 + 8,
                390, 260, 800, 701, minimumMeasuredHeight: 260);
        }
        Expect(reattached.First == 0 && reattached.Last == 5,
            "completed reattachment promotes only the actual visible six cards");

        VerifyProceduralScrollLifecycle();
    }

    private static void VerifyProceduralScrollLifecycle()
    {
        var products = Enumerable.Range(0, Mu3D.Gallery.Pages.ProceduralFeedExample.ProductCount)
            .Select(index => new Mu3D.Gallery.Pages.FeedProduct(index)).ToArray();
        Mu3D.Gallery.Pages.ProceduralFeedExample.ApplyLifecycle(products, 0, 5);
        Expect(products[8].Scene is not null && !products[8].IsLive,
            "initially preloaded card #9 awaits viewport promotion");
        ProductFeedVisibleRange visible = new();
        for (int index = 23; index >= 0; index--)
        {
            visible.Include(index, (index % 2) * 400, ((index / 2) - 4) * 248,
                390, 240, 800, 490);
        }
        Expect(visible.First == 8 && visible.Last == 11, "procedural visible cards #9–12");
        Mu3D.Gallery.Pages.ProceduralFeedExample.ApplyLifecycle(products, visible.First, visible.Last);
        Expect(products.Skip(8).Take(4).All(product =>
            product.IsLive && product.Scene is not null && product.Camera is not null),
            "scrolling promotes every new visible procedural scene");
        Mu3D.Gallery.Pages.ProceduralFeedExample.ApplyLifecycle(products, 20, 23);
        Expect(products.Skip(20).All(product => product.IsLive && product.Scene is not null),
            "last rows load and render");
        Expect(products[0].Scene is null && !products[0].IsLive, "distant first rows release");
        Mu3D.Gallery.Pages.ProceduralFeedExample.ApplyLifecycle(products, 0, 3);
        Expect(products.Take(4).All(product => product.IsLive && product.Scene is not null),
            "returning to released rows recreates live scenes");
    }

    private static void Expect(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}

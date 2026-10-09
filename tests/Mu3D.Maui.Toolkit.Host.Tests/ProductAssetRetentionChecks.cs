using Mu3D.GalleryApp.Pages;

internal static class ProductAssetRetentionChecks
{
    internal static void Verify() => VerifyAsync().GetAwaiter().GetResult();

    private static async Task VerifyAsync()
    {
        using ProductAssetProvider provider = new(() => { }, () => { });
        for (int index = 0; index < 60; index++)
        {
            ProductModel model = ProductModels.All[index % ProductModels.All.Length];
            await provider.GetContentAsync(index, model.DisplayName, model, CancellationToken.None);
        }
        if (provider.DefinitionCacheCount != 6 || provider.WarmContentCount != 16)
            throw new InvalidOperationException("Model browsing must respect the bounded shared caches.");
        provider.Clear();
        provider.Clear();
        if (provider.DefinitionCacheCount != 0 || provider.WarmContentCount != 0)
            throw new InvalidOperationException("Hidden model pages must retain no cached definitions or instances.");
        ProductModel first = ProductModels.All[0];
        var rebuilt = await provider.GetContentAsync(0, first.DisplayName, first, CancellationToken.None);
        if (rebuilt.Scene is null || provider.DefinitionCacheCount != 1 || provider.WarmContentCount != 1)
            throw new InvalidOperationException("Re-entering a cached page must rebuild its model content.");
    }
}

namespace Mu3D.GalleryApp
{
    // The actual provider runs on the host against the Gallery's real bundled model files.
    internal static class GalleryAssets
    {
        internal static Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken)
            => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, path), cancellationToken);
    }
}

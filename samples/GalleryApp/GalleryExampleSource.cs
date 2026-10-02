using System.Collections.Concurrent;

namespace Mu3D.GalleryApp;

internal sealed record GalleryExampleSource(string Xaml, string CSharp);

internal static class GalleryExampleSourceLoader
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<GalleryExampleSource>>> Sources =
        new(StringComparer.Ordinal);

    internal static Task<GalleryExampleSource> LoadAsync(Type pageType)
    {
        ArgumentNullException.ThrowIfNull(pageType);
        return Sources.GetOrAdd(
            pageType.Name,
            static pageName => new Lazy<Task<GalleryExampleSource>>(
                () => LoadCoreAsync(pageName),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static async Task<GalleryExampleSource> LoadCoreAsync(string pageName)
    {
        string xaml = await ReadAssetAsync($"GallerySource/{pageName}.xaml");
        string csharp = await ReadAssetAsync($"GallerySource/{pageName}.xaml.cs");
        string[] helpers = GallerySourceCatalog.Helpers(pageName);
        foreach (string helper in helpers)
            csharp += $"\n\n// {helper} (shared example file)\n" + await ReadAssetAsync("GallerySource/" + helper);
        return new GalleryExampleSource(xaml, csharp);
    }

    private static async Task<string> ReadAssetAsync(string path)
    {
        await using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(path);
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync();
    }
}

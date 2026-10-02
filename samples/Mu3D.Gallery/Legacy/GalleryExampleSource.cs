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
            pageType.FullName ?? pageType.Name,
            _ => new Lazy<Task<GalleryExampleSource>>(
                () => LoadCoreAsync(pageType),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static async Task<GalleryExampleSource> LoadCoreAsync(Type pageType)
    {
        string pageName = pageType.Name;
        bool adaptivePage = pageType.Namespace == "Mu3D.Gallery.Pages";
        string prefix = adaptivePage ? "GallerySource/Adaptive/" : "GallerySource/";
        string xaml = await ReadAssetAsync($"{prefix}{pageName}.xaml");
        string csharp = await ReadAssetAsync($"{prefix}{pageName}.xaml.cs");
        string[] helpers = adaptivePage ? GallerySourceCatalog.AdaptiveHelpers(pageName) : GallerySourceCatalog.Helpers(pageName);
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

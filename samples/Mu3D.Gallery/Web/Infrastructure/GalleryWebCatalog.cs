using Mu3D.GalleryApp;
using Mu3D.Gallery;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// Native and Web share the same case identity, categories, order and search policy.
internal static class GalleryWebCatalog
{
    internal static string Href(GalleryEntry entry) => entry.Id == "licenses" ? "licenses" : $"examples/{entry.Id}";
    internal static string DocsHref(GalleryEntry entry)
        => $"https://evnii.github.io/Mu3D.maui/v0.1/articles/features/{entry.FeatureId}.html";

    internal static GalleryEntry? RouteEntry(string relative)
    {
        string path = relative.Split('?', '#')[0].Trim('/');
        string id = path.StartsWith("examples/", StringComparison.Ordinal) ? path[9..]
            : path.StartsWith("source/", StringComparison.Ordinal) ? path[7..] : path;
        if (id == "pbr-material") id = "pbr-material-textures";
        return GalleryCatalog.Examples.FirstOrDefault(entry => entry.Id == id);
    }

    internal static string SearchQuery(string relative)
    {
        string? query = relative.Split('#')[0].Split('?', 2).ElementAtOrDefault(1);
        if (query is null) return string.Empty;
        foreach (string field in query.Split('&'))
        {
            string[] pair = field.Split('=', 2);
            if (pair[0] == "search") return pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty;
        }
        return string.Empty;
    }

    internal static IEnumerable<string> SourceFiles(GalleryEntry entry)
    {
        string? adaptiveName = GallerySourceCatalog.AdaptivePageName(entry.Id);
        string? pageName = adaptiveName ?? (entry.LegacyRoute is string route ? GallerySourceCatalog.PageName(route) : null);
        if (pageName is null) return [];
        string prefix = adaptiveName is null ? "" : "Adaptive/";
        string[] helpers = adaptiveName is null ? GallerySourceCatalog.Helpers(pageName) : GallerySourceCatalog.AdaptiveHelpers(pageName);
        return new[] { prefix + pageName + ".xaml", prefix + pageName + ".xaml.cs" }.Concat(helpers);
    }

    internal static string FeatureId(GalleryExample entry) => GalleryFeatureCatalog.FeatureId(entry.Route);

    internal static IEnumerable<string> PageText(string pageName)
    {
        using Stream source = typeof(GalleryWebCatalog).Assembly.GetManifestResourceStream($"Mu3D.GalleryApp.NativePages.{pageName}.xaml")
            ?? throw new InvalidOperationException($"Gallery source missing: {pageName}.");
        return System.Xml.Linq.XDocument.Load(source).Descendants()
            .Where(element => element.Name.LocalName == "Label")
            .Select(element => (string?)element.Attribute("Text")).OfType<string>().ToArray();
    }
}

using Mu3D.Gallery;
using Mu3D.GalleryApp;
using Mu3D.GalleryApp.Web.Infrastructure;

internal static class GalleryCatalogChecks
{
    internal static void Validate(Action<bool, string> check)
    {
        IReadOnlyList<GalleryEntry> entries = GalleryCatalog.Examples;
        check(entries.Count == GalleryNavigationCatalog.Examples.Count + 5,
            "Adaptive Gallery must retain every focused example and its five additional entries.");
        check(entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() == entries.Count,
            "Navigation IDs must be unique.");
        check(GalleryCatalog.Sections.Count == 5 && GalleryCatalog.Sections.All(section =>
            entries.Any(entry => entry.SectionId == section.Id)), "All five native groups must have examples.");
        foreach (GalleryExample example in GalleryNavigationCatalog.Examples)
        {
            GalleryEntry entry = GalleryCatalog.Find(GalleryFeatureCatalog.FeatureId(example.Route));
            check(entry.LegacyRoute == example.Route && entry.Title == example.Title &&
                entry.Description == example.Description, "Migration must preserve each real example's identity.");
        }
        check(GalleryCatalog.Find("material-conformance").LegacyRoute == GalleryRoutes.ModelLab,
            "Advanced material conformance must remain available.");
        check(entries.All(entry => GalleryCatalog.Sections.Any(section =>
            section.Id == entry.SectionId && section.Title == entry.SectionTitle)),
            "Every entry must resolve to its declared category.");
        HashSet<string> features = GalleryNavigationCatalog.Examples
            .Select(example => GalleryFeatureCatalog.FeatureId(example.Route)).ToHashSet(StringComparer.Ordinal);
        features.Add(GalleryFeatureCatalog.FeatureId(GalleryRoutes.ModelLab));
        check(entries.All(entry => entry.FeatureId is null || features.Contains(entry.FeatureId)),
            "Additional examples must link to existing documented features.");
        check(GalleryCatalog.Search(null).SequenceEqual(entries) && GalleryCatalog.Search(" \t\n").SequenceEqual(entries),
            "Empty searches preserve the complete catalog and its stable order.");
        check(GalleryCatalog.Search("OpenPBR WHITE").Select(entry => entry.Id)
            .SequenceEqual(["openpbr-white-furnace"]), "Search combines case-insensitive terms.");
        check(GalleryCatalog.Search("gltf-product-feed").Any(entry => entry.Id == "gltf-product-feed"),
            "Stable feature IDs must be searchable.");
        check(!GalleryCatalog.Search("no-such-example-123").Any(), "Unknown queries have an empty result.");
        foreach (GallerySection section in GalleryCatalog.Sections)
        {
            check(GalleryCatalog.Search(null, section.Id).SequenceEqual(entries.Where(entry => entry.SectionId == section.Id)),
                "Category lists preserve catalog order and exclude other categories.");
        }
        check(!GalleryCatalog.Search("OpenPBR", "assets").Any(), "Search must respect its category scope.");
        ValidateWebNavigation(check);
#if MU3D_PRINTING
        check(entries.Count == 43 && entries.Any(entry => entry.Id == "cmyk-printing"), "Printing builds retain CMYK.");
#else
        check(entries.Count == 42 && !entries.Any(entry => entry.Id == "cmyk-printing"), "Printing-free builds omit CMYK only.");
#endif
    }

    private static void ValidateWebNavigation(Action<bool, string> check)
    {
        foreach (GalleryEntry entry in GalleryCatalog.Examples)
        {
            string href = GalleryWebCatalog.Href(entry);
            check(!href.StartsWith('/') && GalleryWebCatalog.RouteEntry(href + "?output=hdr#mu3d-canvas") == entry,
                "Every browser destination resolves under its deployment base, including deep links.");
            if (entry.FeatureId is null) continue;
            check(GalleryWebCatalog.RouteEntry("source/" + entry.Id) == entry &&
                GalleryWebCatalog.DocsHref(entry).EndsWith("/" + entry.FeatureId + ".html", StringComparison.Ordinal),
                "Source follows case identity while Docs follows the documented feature.");
            string[] files = GalleryWebCatalog.SourceFiles(entry).ToArray();
            check(files.Length >= 2 && files[0].EndsWith(".xaml", StringComparison.Ordinal) &&
                files[1] == files[0] + ".cs" && files.Distinct(StringComparer.Ordinal).Count() == files.Length,
                "Every documented case has one native authoring source pair and distinct dependencies.");
        }
        check(GalleryWebCatalog.RouteEntry("examples/pbr-material") == GalleryCatalog.Find("pbr-material-textures") &&
            GalleryWebCatalog.RouteEntry("sections/rendering") is null &&
            GalleryWebCatalog.RouteEntry("examples/does-not-exist") is null,
            "The previous PBR URL still resolves; category and unknown routes cannot select a case.");
        check(GalleryWebCatalog.SearchQuery("examples?search=OpenPBR+HDR&other=1#search=wrong") == "OpenPBR HDR" &&
            GalleryWebCatalog.SearchQuery("sections/assets?other=1&search=%E9%80%8F%E6%98%8E%26alpha%3D0.5") == "透明&alpha=0.5" &&
            GalleryWebCatalog.SearchQuery("examples#search=ignored") == "" &&
            GalleryWebCatalog.SearchQuery("examples?search") == "",
            "Search URLs preserve spaces, Unicode and escaped delimiters without reading fragments.");
        check(GalleryWebCatalog.SourceFiles(GalleryCatalog.Find("openpbr-materialx")).First() == "Adaptive/OpenPbrPage.xaml" &&
            GalleryWebCatalog.SourceFiles(GalleryCatalog.Find("procedural-feed")).Contains("ProceduralFeedExample.cs") &&
            GalleryWebCatalog.SourceFiles(GalleryCatalog.Find("gltf-product-feed")).First() == "ProductFeedPage.xaml",
            "Adaptive and retained cases show their actual distinct implementations, including both feeds.");
    }
}

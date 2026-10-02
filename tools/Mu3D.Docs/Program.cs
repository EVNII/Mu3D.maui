using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mu3D.Docs;

internal static partial class Program
{
    private const string PublicVersion = "v0.1";
    private static readonly string[] PublicAssemblyNames =
    [
        "Mu3D.Color",
        "Mu3D.Color.Printing",
        "Mu3D.Graphics",
        "Mu3D.Core",
        "Mu3D.Creative",
        "Mu3D.Formats.MaterialX",
        "Mu3D.Rendering.OpenPbr",
        "Mu3D.Toolkit",
        "Mu3D.Formats.Gltf",
        "Mu3D.Native.Wgpu",
        "Mu3D.Native.OpenColorIO",
        "Mu3D.Native.Ktx",
        "Mu3D.Native.UltraHdr",
        "Mu3D.Maui",
        "Mu3D.Maui.Toolkit",
    ];
    private static readonly string[] PublicXamlComponentNames =
    [
        "Mu3DView",
        "Mu3DSceneView",
        "ColorView3D",
        "Scene3D",
        "PerspectiveCamera3D",
        "DirectionalLight3D",
        "Sphere3D",
        "Cone3D",
        "PbrMaterial3D",
        "OpenPbrSurface3D",
        "OpenPbrPreviewMaterial3D",
        "OpenPbrMaterial3D",
        "UnlitMaterial3D",
        "ViewportTools",
        "OrbitTool",
        "MapTool",
        "FlyTool",
        "ViewportInput",
        "MouseInput",
        "TrackpadInput",
        "TouchscreenInput",
        "KeyboardInput",
        "SceneSelection",
        "SceneSelectionTool",
        "TransformGizmoTool",
        "ProgressTool",
        "NodeTransformProgress",
        "PlaybackToolbar",
        "AxesHelper",
        "GridHelper",
        "BoundsHelper",
        "OutlineHelper",
        "RenderOutputToolbar",
        "RenderOutputOption",
        "RenderOutputTool",
        "FrameStatisticsOverlay",
        "OrbitSceneViewFeature",
        "TransformGizmoSceneViewFeature",
        "ViewportNavigationBehavior",
        "TransformGizmoPointerBehavior",
        "FrameStatisticsBehavior",
        "ViewportOverlay",
        "SceneNodeAnchor",
        "SceneNodeAnchorLayer",
        "FrameStatisticsView",
        "SceneViewProxyHost",
        "SceneViewProxy",
        "VirtualizedSceneView",
        "SceneViewportHost",
    ];

    private static async Task<int> Main(string[] args)
    {
        try
        {
            string root = FindRepositoryRoot();
            string generatedConfig = Path.Combine(root, "docs-public", ".docfx.generated.json");
            if (File.Exists(generatedConfig))
            {
                File.Delete(generatedConfig);
            }
            string generatedDependencies = Path.Combine(root, "docs-public", ".docfx-dependencies");
            if (Directory.Exists(generatedDependencies))
            {
                Directory.Delete(generatedDependencies, recursive: true);
            }
            string command = args.FirstOrDefault() ?? "build";
            FeatureCatalog catalog = ValidatePublicSources(root);
            if (command.Equals("validate", StringComparison.Ordinal))
            {
                string site = Path.Combine(root, "artifacts", "docs", "_site");
                if (Directory.Exists(site))
                {
                    ValidateGeneratedSite(root, site, catalog);
                }
                Console.WriteLine("Validated the public documentation source boundary and feature catalog.");
                return 0;
            }
            if (!command.Equals("build", StringComparison.Ordinal))
            {
                throw new ArgumentException("Usage: Mu3D.Docs [build|validate]");
            }

            await BuildAsync(root, catalog).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task BuildAsync(string root, FeatureCatalog catalog)
    {
        string docsArtifacts = Path.Combine(root, "artifacts", "docs");
        string buildRoot = Path.Combine(docsArtifacts, "build");
        string referenceRoot = Path.Combine(docsArtifacts, "reference");
        string dependencyRoot = Path.Combine(docsArtifacts, "dependencies");
        string apiRoot = Path.Combine(docsArtifacts, "api");
        string siteRoot = Path.Combine(docsArtifacts, "_site");
        RecreateDirectory(root, buildRoot);
        RecreateDirectory(root, referenceRoot);
        RecreateDirectory(root, dependencyRoot);
        RecreateDirectory(root, apiRoot);
        RecreateDirectory(root, siteRoot);

        await RunAsync(root, "dotnet", "tool", "restore").ConfigureAwait(false);

        string referenceCollector = Path.Combine(buildRoot, "CollectDocumentationReferences.targets");
        File.WriteAllText(
            referenceCollector,
            """
            <Project>
              <Target Name="Mu3DCollectDocumentationReferences" DependsOnTargets="ResolveReferences">
                <WriteLinesToFile
                    File="$(Mu3DDocsReferenceList)"
                    Lines="@(ReferencePath->'%(FullPath)')"
                    Overwrite="true" />
              </Target>
            </Project>
            """);

        string hostTfm = OperatingSystem.IsWindows()
            ? "net10.0-windows10.0.19041.0"
            : OperatingSystem.IsMacOS()
                ? "net10.0-maccatalyst"
                : "net10.0-android";
        ProjectSpec[] projects =
        [
            new("src/Mu3D.Color/Mu3D.Color.csproj", "Mu3D.Color", "net10.0"),
            new("src/Mu3D.Color.Printing/Mu3D.Color.Printing.csproj", "Mu3D.Color.Printing", "net10.0"),
            new("src/Mu3D.Native.OpenColorIO/Mu3D.Native.OpenColorIO.csproj", "Mu3D.Native.OpenColorIO", "net10.0"),
            new("src/Mu3D.Graphics/Mu3D.Graphics.csproj", "Mu3D.Graphics", "net10.0"),
            new("src/Mu3D.Core/Mu3D.Core.csproj", "Mu3D.Core", "net10.0"),
            new("src/Mu3D.Creative/Mu3D.Creative.csproj", "Mu3D.Creative", "net10.0"),
            new("src/Mu3D.Formats.MaterialX/Mu3D.Formats.MaterialX.csproj", "Mu3D.Formats.MaterialX", "net10.0"),
            new("src/Mu3D.Rendering.OpenPbr/Mu3D.Rendering.OpenPbr.csproj", "Mu3D.Rendering.OpenPbr", "net10.0"),
            new("src/Mu3D.Toolkit/Mu3D.Toolkit.csproj", "Mu3D.Toolkit", "net10.0"),
            new("src/Mu3D.Formats.Gltf/Mu3D.Formats.Gltf.csproj", "Mu3D.Formats.Gltf", "net10.0"),
            new("src/Mu3D.Native.Wgpu/Mu3D.Native.Wgpu.csproj", "Mu3D.Native.Wgpu", "net10.0"),
            new("src/Mu3D.Native.Ktx/Mu3D.Native.Ktx.csproj", "Mu3D.Native.Ktx", "net10.0"),
            new("src/Mu3D.Native.UltraHdr/Mu3D.Native.UltraHdr.csproj", "Mu3D.Native.UltraHdr", "net10.0"),
            new("src/Mu3D.Maui/Mu3D.Maui.csproj", "Mu3D.Maui", hostTfm),
            new("src/Mu3D.Maui.Toolkit/Mu3D.Maui.Toolkit.csproj", "Mu3D.Maui.Toolkit", hostTfm),
        ];

        foreach (ProjectSpec project in projects)
        {
            string output = Path.Combine(buildRoot, project.AssemblyName);
            Directory.CreateDirectory(output);
            await RunAsync(
                root,
                "dotnet",
                "build",
                Path.Combine(root, project.ProjectPath),
                "-c",
                "Release",
                "-f",
                project.TargetFramework,
                $"-p:OutputPath={output}{Path.DirectorySeparatorChar}",
                "-p:CopyLocalLockFileAssemblies=true",
                "-m:1",
                "-nr:false").ConfigureAwait(false);
            string referenceList = Path.Combine(output, "documentation-references.txt");
            await RunAsync(
                root,
                "dotnet",
                "msbuild",
                Path.Combine(root, project.ProjectPath),
                "-t:Mu3DCollectDocumentationReferences",
                $"-p:TargetFramework={project.TargetFramework}",
                "-p:Configuration=Release",
                "-p:BuildProjectReferences=false",
                $"-p:CustomAfterMicrosoftCommonTargets={referenceCollector}",
                $"-p:Mu3DDocsReferenceList={referenceList}",
                "-m:1",
                "-nr:false",
                "-v:minimal").ConfigureAwait(false);
            StageResolvedReferences(referenceList, dependencyRoot);
            StageProjectOutput(output, project.AssemblyName, referenceRoot, dependencyRoot);
        }

        string config = CreateGeneratedDocfxConfig(root, dependencyRoot);
        try
        {
            await RunAsync(
                root,
                "dotnet",
                "docfx",
                "metadata",
                config,
                "--warningsAsErrors").ConfigureAwait(false);
            await RunAsync(
                root,
                "dotnet",
                "docfx",
                "build",
                config,
                "--warningsAsErrors",
                "--disableGitFeatures").ConfigureAwait(false);
        }
        finally
        {
            File.Delete(config);
            Directory.Delete(
                Path.Combine(root, "docs-public", ".docfx-dependencies"),
                recursive: true);
        }

        // DocFX's build-only manifest records the absolute local source root. The static site does
        // not consume it, so never include it in privacy-validated or publishable output.
        File.Delete(Path.Combine(siteRoot, "manifest.json"));
        File.WriteAllText(Path.Combine(siteRoot, ".nojekyll"), string.Empty);
        WriteSiteEntrypoint(siteRoot);
        WriteMissingNamespaceEntrypoints(siteRoot);
        ValidateStagedReference(referenceRoot);
        ValidateGeneratedSite(root, siteRoot, catalog);
        Console.WriteLine($"Built privacy-validated public documentation: {siteRoot}");
    }

    private static void WriteSiteEntrypoint(string siteRoot)
    {
        string versionPath = $"{PublicVersion}/index.html";
        File.WriteAllText(
            Path.Combine(siteRoot, "index.html"),
            $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta http-equiv="refresh" content="0; url={versionPath}">
              <title>Mu3D documentation</title>
            </head>
            <body>
              <p><a href="{versionPath}">Open the current Mu3D documentation.</a></p>
            </body>
            </html>
            """);
    }

    private static void WriteMissingNamespaceEntrypoints(string siteRoot)
    {
        string apiRoot = Path.Combine(siteRoot, PublicVersion, "api");
        string[] pageNames = Directory.GetFiles(apiRoot, "*.html", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        HashSet<string> existingPages = pageNames.ToHashSet(StringComparer.Ordinal);
        HashSet<string> missingParents = new(StringComparer.Ordinal);

        foreach (string pageName in pageNames)
        {
            string html = File.ReadAllText(Path.Combine(apiRoot, pageName));
            foreach (Match match in HtmlHrefRegex().Matches(html))
            {
                string href = match.Groups[1].Value;
                if (href.Contains('/') ||
                    !href.StartsWith("Mu3D.", StringComparison.Ordinal) ||
                    !href.EndsWith(".html", StringComparison.Ordinal) ||
                    existingPages.Contains(href))
                {
                    continue;
                }

                string namespacePrefix = Path.GetFileNameWithoutExtension(href) + ".";
                if (pageNames.Any(candidate =>
                    candidate.StartsWith(namespacePrefix, StringComparison.Ordinal)))
                {
                    missingParents.Add(href);
                }
            }
        }

        foreach (string parentPage in missingParents)
        {
            string parentNamespace = Path.GetFileNameWithoutExtension(parentPage);
            string prefix = parentNamespace + ".";
            string[] children = pageNames
                .Where(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal))
                .Select(Path.GetFileNameWithoutExtension)
                .Where(candidate =>
                {
                    string remainder = candidate![prefix.Length..];
                    return !remainder.Contains('.') &&
                        existingPages.Contains(candidate + ".html") &&
                        pageNames.Any(page => page.StartsWith(candidate + ".", StringComparison.Ordinal));
                })
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Cast<string>()
                .ToArray();
            if (children.Length == 0)
            {
                throw new InvalidDataException(
                    $"Cannot derive namespace children for generated link '{parentPage}'.");
            }

            string childLinks = string.Join(
                Environment.NewLine,
                children.Select(child =>
                    $"    <li><a href=\"{child}.html\">{child}</a></li>"));
            File.WriteAllText(
                Path.Combine(apiRoot, parentPage),
                $"""
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <title>{parentNamespace} namespace</title>
                </head>
                <body>
                  <nav><a href="../api.html">API reference</a></nav>
                  <h1>{parentNamespace} namespace</h1>
                  <ul>
                {childLinks}
                  </ul>
                </body>
                </html>
                """);
        }
    }

    private static string CreateGeneratedDocfxConfig(string root, string dependencyRoot)
    {
        const string referenceToken = "\"../artifacts/docs/dependencies/*.dll\"";
        string sourceConfig = File.ReadAllText(Path.Combine(root, "docs-public", "docfx.json"));
        string[] dependencies = Directory.GetFiles(dependencyRoot, "*.dll");
        if (dependencies.Length == 0 || !sourceConfig.Contains(referenceToken, StringComparison.Ordinal))
        {
            throw new InvalidDataException("DocFX dependency staging or reference template is incomplete.");
        }
        string temporaryDependencies = Path.Combine(root, "docs-public", ".docfx-dependencies");
        Directory.CreateDirectory(temporaryDependencies);
        foreach (string dependency in dependencies)
        {
            File.Copy(
                dependency,
                Path.Combine(temporaryDependencies, Path.GetFileName(dependency)),
                overwrite: true);
        }
        string generatedConfig = Path.Combine(root, "docs-public", ".docfx.generated.json");
        File.WriteAllText(
            generatedConfig,
            sourceConfig.Replace(
                referenceToken,
                "\".docfx-dependencies/*.dll\"",
                StringComparison.Ordinal));
        return generatedConfig;
    }

    private static FeatureCatalog ValidatePublicSources(string root)
    {
        string publicRoot = Path.GetFullPath(Path.Combine(root, "docs-public"));
        foreach (string path in Directory.EnumerateFileSystemEntries(
            publicRoot,
            "*",
            SearchOption.AllDirectories))
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Public documentation cannot contain links or reparse points: {path}");
            }
            if (File.Exists(path) &&
                Path.GetExtension(path) is not ".md" and not ".yml" and not ".json")
            {
                throw new InvalidDataException($"Unexpected public-documentation file type: {path}");
            }
        }

        string configText = File.ReadAllText(Path.Combine(publicRoot, "docfx.json"));
        if (configText.Contains("../docs", StringComparison.OrdinalIgnoreCase) ||
            configText.Contains("PROJECT_PLAN", StringComparison.OrdinalIgnoreCase) ||
            configText.Contains("STATUS.md", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("DocFX configuration crosses the public content allowlist.");
        }

        string articlesTocText = File.ReadAllText(Path.Combine(publicRoot, "articles", "toc.yml"));
        string xamlCatalogPath = Path.Combine(publicRoot, "articles", "xaml-components.md");
        if (!File.Exists(xamlCatalogPath) ||
            !articlesTocText.Contains("href: xaml-components.md", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The public XAML component catalog is missing or absent from the article TOC.");
        }
        string xamlCatalogCode = string.Join(
            Environment.NewLine,
            XamlCodeBlockRegex().Matches(File.ReadAllText(xamlCatalogPath))
                .Select(match => match.Groups["code"].Value));
        string[] undocumentedXamlComponents = PublicXamlComponentNames
            .Where(component =>
                !xamlCatalogCode.Contains($":{component}", StringComparison.Ordinal))
            .ToArray();
        if (undocumentedXamlComponents.Length != 0)
        {
            throw new InvalidDataException(
                "The XAML component catalog has no XAML example for: " +
                string.Join(", ", undocumentedXamlComponents));
        }

        FeatureCatalog catalog = JsonSerializer.Deserialize<FeatureCatalog>(
            File.ReadAllText(Path.Combine(publicRoot, "gallery-features.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
            throw new InvalidDataException("The Gallery feature catalog is empty.");
        if (catalog.SchemaVersion != 1 || catalog.Features.Count == 0)
        {
            throw new InvalidDataException("The Gallery feature catalog schema or feature list is invalid.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> routes = new(StringComparer.Ordinal);
        HashSet<string> articles = new(StringComparer.Ordinal);
        foreach (Feature feature in catalog.Features)
        {
            if (!FeatureIdRegex().IsMatch(feature.Id) ||
                !feature.Route.StartsWith("gallery-", StringComparison.Ordinal) ||
                !feature.Article.StartsWith("articles/features/", StringComparison.Ordinal) ||
                !feature.Article.EndsWith(".html", StringComparison.Ordinal) ||
                !ids.Add(feature.Id) || !routes.Add(feature.Route) || !articles.Add(feature.Article))
            {
                throw new InvalidDataException($"Invalid or duplicate Gallery documentation mapping: {feature.Id}");
            }

            string articleSource = Path.Combine(
                publicRoot,
                Path.ChangeExtension(feature.Article, ".md"));
            if (!File.Exists(articleSource))
            {
                throw new InvalidDataException($"Gallery feature '{feature.Id}' has no article source.");
            }
            if (!XamlCodeBlockRegex().IsMatch(File.ReadAllText(articleSource)))
            {
                throw new InvalidDataException(
                    $"Gallery feature '{feature.Id}' has no XAML example.");
            }
            IReadOnlyDictionary<string, string> frontMatter = ReadFrontMatter(articleSource);
            if (!frontMatter.TryGetValue("feature_id", out string? articleId) || articleId != feature.Id)
            {
                throw new InvalidDataException($"Article '{articleSource}' does not declare feature_id '{feature.Id}'.");
            }
        }

        string[] articleIds = Directory.EnumerateFiles(
                Path.Combine(publicRoot, "articles", "features"),
                "*.md")
            .Select(ReadFrontMatter)
            .Select(frontMatter => frontMatter.TryGetValue("feature_id", out string? id) ? id : string.Empty)
            .ToArray();
        if (articleIds.Length != ids.Count ||
            articleIds.Any(string.IsNullOrWhiteSpace) ||
            !articleIds.ToHashSet(StringComparer.Ordinal).SetEquals(ids))
        {
            throw new InvalidDataException("Gallery feature articles and catalog mappings are not bidirectional.");
        }

        HashSet<string> tocArticles = FeatureTocArticleRegex().Matches(articlesTocText)
            .Select(match => $"articles/features/{match.Groups[1].Value}.html")
            .ToHashSet(StringComparer.Ordinal);
        if (!tocArticles.SetEquals(articles))
        {
            throw new InvalidDataException("The public feature TOC and Gallery article catalog differ.");
        }

        string legacyRoot = Path.Combine(root, "samples", "Mu3D.Gallery", "Legacy");
        Mu3D.Gallery.GalleryEntry[] adaptiveCases = Mu3D.Gallery.GalleryCatalog.Examples.ToArray();
        if (adaptiveCases.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() != adaptiveCases.Length ||
            !adaptiveCases.Where(entry => entry.FeatureId is not null).Select(entry => entry.FeatureId!)
                .ToHashSet(StringComparer.Ordinal).SetEquals(ids))
        {
            throw new InvalidDataException("Adaptive Gallery cases must have unique IDs and valid public feature articles.");
        }
        string routeSource = File.ReadAllText(Path.Combine(legacyRoot, "GalleryRoutes.cs"));
        HashSet<string> declaredRoutes = GalleryRouteRegex().Matches(routeSource)
            .Select(match => match.Groups[2].Value)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> toolbarIds = new(StringComparer.Ordinal);
        foreach (Feature feature in catalog.Features)
        {
            if (!declaredRoutes.Contains(feature.Route) ||
                Mu3D.GalleryApp.GalleryFeatureCatalog.FeatureId(feature.Route) != feature.Id)
            {
                throw new InvalidDataException($"Gallery feature '{feature.Id}' has an invalid route mapping.");
            }
            string pageName = Mu3D.GalleryApp.GallerySourceCatalog.PageName(feature.Route);
            string pageSource = File.ReadAllText(Path.Combine(legacyRoot, "Pages", pageName + ".xaml"));
            Match[] toolbarMatches = GalleryToolbarFeatureRegex().Matches(pageSource).ToArray();
            if (toolbarMatches.Length != 1 ||
                toolbarMatches[0].Groups[1].Value != feature.Id ||
                !toolbarIds.Add(feature.Id))
            {
                throw new InvalidDataException(
                    $"Gallery page '{pageName}' must declare exactly one matching Docs action.");
            }
        }
        string[] allToolbarIds = Directory.EnumerateFiles(Path.Combine(legacyRoot, "Pages"), "*.xaml")
            .SelectMany(path => GalleryToolbarFeatureRegex().Matches(File.ReadAllText(path)))
            .Select(match => match.Groups[1].Value)
            .ToArray();
        if (allToolbarIds.Length != ids.Count ||
            !toolbarIds.SetEquals(ids) ||
            !allToolbarIds.ToHashSet(StringComparer.Ordinal).SetEquals(ids))
        {
            throw new InvalidDataException(
                "Every Gallery feature must have exactly one cataloged Docs action.");
        }

        string galleryMappings = File.ReadAllText(Path.Combine(
            root,
            "samples",
            "Mu3D.Gallery",
            "Legacy",
            "GalleryDocumentation.cs"));
        Match[] galleryMappingMatches = GalleryFeatureMappingRegex().Matches(galleryMappings).ToArray();
        Dictionary<string, string> galleryFeatures = galleryMappingMatches.ToDictionary(
            match => match.Groups[1].Value,
            match => match.Groups[2].Value,
            StringComparer.Ordinal);
        if (galleryMappingMatches.Length != ids.Count ||
            catalog.Features.Any(feature =>
                !galleryFeatures.TryGetValue(feature.Id, out string? article) ||
                article != feature.Article))
        {
            throw new InvalidDataException("Gallery runtime mappings and the public feature catalog differ.");
        }
        return catalog;
    }

    private static IReadOnlyDictionary<string, string> ReadFrontMatter(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 3 || lines[0] != "---")
        {
            throw new InvalidDataException($"Public article has no YAML front matter: {path}");
        }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < lines.Length && lines[index] != "---"; index++)
        {
            int separator = lines[index].IndexOf(':');
            if (separator > 0)
            {
                result[lines[index][..separator].Trim()] = lines[index][(separator + 1)..].Trim();
            }
        }
        return result;
    }

    private static void StageProjectOutput(
        string output,
        string assemblyName,
        string referenceRoot,
        string dependencyRoot)
    {
        string assembly = FindSingleOutput(output, assemblyName + ".dll");
        string xml = Path.ChangeExtension(assembly, ".xml");
        if (!File.Exists(xml))
        {
            throw new FileNotFoundException($"Public XML documentation was not generated for {assemblyName}.", xml);
        }
        File.Copy(assembly, Path.Combine(referenceRoot, Path.GetFileName(assembly)), overwrite: true);
        File.Copy(xml, Path.Combine(referenceRoot, Path.GetFileName(xml)), overwrite: true);

        foreach (string dependency in Directory.EnumerateFiles(output, "*.dll", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(dependency);
            if (PublicAssemblyNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }
            if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) ||
                !IsManagedAssembly(dependency))
            {
                continue;
            }
            string destination = Path.Combine(dependencyRoot, Path.GetFileName(dependency));
            if (!File.Exists(destination))
            {
                File.Copy(dependency, destination);
            }
        }
    }

    private static void StageResolvedReferences(string referenceList, string dependencyRoot)
    {
        if (!File.Exists(referenceList))
        {
            throw new FileNotFoundException(
                "The documentation reference collector did not produce an output list.",
                referenceList);
        }

        foreach (string reference in File.ReadLines(referenceList)
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(reference);
            if (!File.Exists(reference) ||
                !Path.GetExtension(reference).Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                PublicAssemblyNames.Contains(name, StringComparer.Ordinal) ||
                name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) ||
                !IsManagedAssembly(reference))
            {
                continue;
            }

            string destination = Path.Combine(dependencyRoot, Path.GetFileName(reference));
            if (!File.Exists(destination))
            {
                File.Copy(reference, destination);
            }
        }
    }

    private static bool IsManagedAssembly(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        return reader.HasMetadata;
    }

    private static string FindSingleOutput(string root, string fileName)
    {
        string[] matches = Directory.GetFiles(root, fileName, SearchOption.AllDirectories);
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException(
                $"Expected one '{fileName}' under '{root}', found {matches.Length}.");
    }

    private static void ValidateStagedReference(string referenceRoot)
    {
        string[] files = Directory.GetFiles(referenceRoot);
        foreach (string assemblyName in PublicAssemblyNames)
        {
            if (!files.Contains(Path.Combine(referenceRoot, assemblyName + ".dll"), StringComparer.OrdinalIgnoreCase) ||
                !files.Contains(Path.Combine(referenceRoot, assemblyName + ".xml"), StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The staged public API pair is incomplete for {assemblyName}.");
            }
        }
        if (files.Any(path => Path.GetExtension(path).Equals(".pdb", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("PDB files must not enter the public API staging directory.");
        }
    }

    private static void ValidateGeneratedSite(
        string root,
        string siteRoot,
        FeatureCatalog catalog)
    {
        string versionRoot = Path.Combine(siteRoot, PublicVersion);
        if (File.Exists(Path.Combine(siteRoot, "manifest.json")))
        {
            throw new InvalidDataException("The DocFX build manifest must not enter public output.");
        }
        if (!File.Exists(Path.Combine(siteRoot, ".nojekyll")) ||
            !File.Exists(Path.Combine(siteRoot, "index.html")) ||
            !File.Exists(Path.Combine(versionRoot, "index.html")) ||
            !File.Exists(Path.Combine(siteRoot, "xrefmap.yml")))
        {
            throw new InvalidDataException("The versioned generated site is incomplete.");
        }
        foreach (Feature feature in catalog.Features)
        {
            if (!File.Exists(Path.Combine(versionRoot, feature.Article.Replace('/', Path.DirectorySeparatorChar))))
            {
                throw new InvalidDataException($"Generated feature path is missing: {feature.Article}");
            }
        }

        string[] forbiddenText =
        [
            "docs/PROJECT_PLAN.md",
            "docs/STATUS.md",
            "docs/M5_HANDOFF.md",
            "docs/decisions/",
            Path.GetFullPath(root),
        ];
        foreach (string path in Directory.EnumerateFiles(siteRoot, "*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(path) is not ".html" and not ".yml" and not ".json" and not ".js" and not ".css")
            {
                continue;
            }
            string text = File.ReadAllText(path);
            if (forbiddenText.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"Generated public output contains private workspace text: {path}");
            }
        }
        ValidateGeneratedLinks(siteRoot);
    }

    private static void ValidateGeneratedLinks(string siteRoot)
    {
        foreach (string htmlPath in Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories))
        {
            string html = File.ReadAllText(htmlPath);
            foreach (Match match in HtmlHrefRegex().Matches(html))
            {
                string href = Uri.UnescapeDataString(match.Groups[1].Value);
                int fragment = href.IndexOfAny(['#', '?']);
                if (fragment >= 0)
                {
                    href = href[..fragment];
                }
                if (string.IsNullOrEmpty(href) ||
                    href.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string candidate = href.StartsWith('/')
                    ? Path.Combine(siteRoot, href.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))
                    : Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(htmlPath)!,
                        href.Replace('/', Path.DirectorySeparatorChar)));
                if (Directory.Exists(candidate))
                {
                    candidate = Path.Combine(candidate, "index.html");
                }
                if (!IsWithin(siteRoot, candidate) || !File.Exists(candidate))
                {
                    throw new InvalidDataException($"Broken generated link '{match.Groups[1].Value}' in '{htmlPath}'.");
                }
            }
        }
    }

    private static async Task RunAsync(string workingDirectory, string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Failed to start {fileName}.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(output))
        {
            Console.Write(output);
        }
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.Write(error);
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.");
        }
    }

    private static void RecreateDirectory(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(path);
        string expectedParent = Path.Combine(fullRoot, "artifacts", "docs") + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) ||
            fullPath.Equals(expectedParent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to recreate unexpected documentation path: {fullPath}");
        }
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
        Directory.CreateDirectory(fullPath);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Mu3D.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Mu3D.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Mu3D repository root was not found.");
    }

    private static bool IsWithin(string root, string path)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex FeatureIdRegex();

    [GeneratedRegex("public const string (\\w+) = \\\"([^\\\"]+)\\\";", RegexOptions.CultureInvariant)]
    private static partial Regex GalleryRouteRegex();

    [GeneratedRegex("GalleryDocumentationToolbarItem FeatureId=\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex GalleryToolbarFeatureRegex();

    [GeneratedRegex("href: features/([a-z0-9-]+)\\.md", RegexOptions.CultureInvariant)]
    private static partial Regex FeatureTocArticleRegex();

    [GeneratedRegex("new\\(\\\"([^\\\"]+)\\\", \\\"(articles/features/[^\\\"]+\\.html)\\\"\\)", RegexOptions.CultureInvariant)]
    private static partial Regex GalleryFeatureMappingRegex();

    [GeneratedRegex("href=[\\\"']([^\\\"']+)[\\\"']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlHrefRegex();

    [GeneratedRegex("```xaml\\s*\\r?\\n(?<code>.*?)```", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex XamlCodeBlockRegex();

    private sealed record ProjectSpec(string ProjectPath, string AssemblyName, string TargetFramework);

    private sealed record FeatureCatalog(int SchemaVersion, IReadOnlyList<Feature> Features);

    private sealed record Feature(string Id, string Route, string Article);
}

using System.Numerics;
using System.Xml.Linq;
using Mu3D.Color;
using Mu3D.GalleryApp;
using Mu3D.GalleryApp.Examples;
using Mu3D.GalleryApp.Web.Infrastructure;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Animation;
using static ViewerTestChecks;

internal static class GalleryPortChecks
{
    internal static void Validate(Action<bool, string> check)
    {
        ValidateSourceInventory(check);
        ValidateNativeDefinitions(check);
        ValidateNamedLookups(check);
        ValidateProgress(check);
        ValidateSceneAssets(check);
        ValidatePlayback(check);
    }

    private static void ValidateSourceInventory(Action<bool, string> check)
    {
#if MU3D_PRINTING
        const int expectedExamples = 38;
#else
        const int expectedExamples = 37;
#endif
        check(GalleryNavigationCatalog.Examples.Count == expectedExamples,
            "The original Gallery catalog retains every example for the selected printing configuration.");
        var features = GalleryNavigationCatalog.Examples
            .Select(entry => (Feature: GalleryWebCatalog.FeatureId(entry), entry.Route))
            .Append((Feature: "material-conformance", Route: GalleryRoutes.ModelLab)).ToArray();
        check(features.All(entry => !string.IsNullOrWhiteSpace(entry.Feature)) &&
            features.Select(entry => entry.Feature).Distinct(StringComparer.Ordinal).Count() == expectedExamples + 1,
            "All original Gallery examples and Advanced have distinct, nonempty Web feature IDs.");
        HashSet<string> resources = typeof(GalleryWebCatalog).Assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);
        foreach (var entry in features)
        {
            string page = GallerySourceCatalog.PageName(entry.Route);
            check(resources.Contains($"Mu3D.GalleryApp.NativePages.{page}.xaml"),
                $"{entry.Feature} maps to an actual embedded native XAML page.");
            foreach (string file in new[] { page + ".xaml", page + ".xaml.cs" }.Concat(GallerySourceCatalog.Helpers(page)))
                check(resources.Contains((file.EndsWith(".xaml", StringComparison.Ordinal) ? "Mu3D.GalleryApp.NativePages." : "Mu3D.GalleryApp.Sources.") + file),
                    $"{entry.Feature} source dependency {file} is packaged from the actual Gallery file.");
        }
    }

    private static void ValidateNativeDefinitions(Action<bool, string> check)
    {
        // Independent native authoring defaults and endpoints, not values obtained from the adapter.
        (string Page, int Meshes, float Fov, Vector3 Camera, float Far)[] pages =
        [
            ("OrbitControlsPage", 4, 50, new(5.2f,3.8f,7.6f), 80),
            ("MapControlsPage", 3, 48, new(7,7,7), 1000),
            ("FlyControlsPage", 3, 55, new(0,2,8), 100),
            ("AxesHelperPage", 3, 50, new(3.4f,2.5f,6.2f), 1000),
            ("GridHelperPage", 3, 50, new(5.8f,4.8f,8.2f), 1000),
            ("BoundsHelperPage", 2, 48, new(5.2f,3.5f,7.4f), 1000),
            ("DeclarativeScenePage", 2, 52, new(0,0,5.5f), 1000),
            ("DeclarativeToolsPage", 3, 52, new(3.2f,2.4f,6.2f), 1000),
            ("PointerPenInputPage", 4, 50, new(0,0,6.5f), 1000),
            ("ProgressBridgePage", 2, 50, new(0,0,5.4f), 1000),
            ("PlaybackToolbarPage", 2, 52, new(0,0,5.8f), 1000),
            ("ColorManagementPage", 3, 48, new(0,0,6), 1000),
            ("HdrCanvasPage", 1, 50, new(0,0,4), 1000),
        ];
        foreach (var item in pages)
        {
            NativeGalleryScene parsed = NativeGalleryScene.Read(item.Page);
            check(parsed.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().Count() == item.Meshes,
                $"{item.Page} must load its actual native primitives, not a shared preview scene.");
            check(Vector3.Distance(parsed.Camera.Transform.Position, item.Camera) < .00001f &&
                Math.Abs(parsed.Camera.FieldOfViewRadians - item.Fov * MathF.PI / 180) < .00001f &&
                parsed.Camera.NearClip == .1f && parsed.Camera.FarClip == item.Far,
                $"{item.Page} retains authored camera values and native clipping defaults.");
        }
        NativeGalleryScene declarative = NativeGalleryScene.Read("DeclarativeScenePage");
        Mesh sphere = declarative.Scene.Root.Children.OfType<Mesh>().First();
        Mesh cone = declarative.Scene.Root.Children.OfType<Mesh>().Last();
        check(sphere.Geometry.Positions.Count == 561 && sphere.Geometry.Indices.Count == 2880 &&
            cone.Geometry.Positions.Count == 100 && cone.Geometry.Indices.Count == 192,
            "Authored primitives use native Sphere3D 32×16 and Cone3D 32-segment defaults.");
        check(sphere.Material is PbrMaterial pbr && pbr.Name == "Blue PBR" && pbr.Metallic == .15f && pbr.Roughness == .22f &&
            pbr.BaseColor.ColorSpace == StandardColorSpaces.LinearSrgb && pbr.BaseColor.Blue == 1 && pbr.BaseColor.Red < .1f,
            "Native material identity, coefficients and decoded color reach the Core material.");
        DirectionalLight light = declarative.Scene.Root.Children.OfType<DirectionalLight>().Single();
        check(light.Intensity == 3.5f && !light.CastsShadows && light.Color.Red == 1 && light.Color.Green == 1 && light.Color.Blue == 1,
            "Native default white-light and shadow policy are preserved.");
        Mesh white = NativeGalleryScene.Read("ColorManagementPage").Scene.Root.Children.OfType<Mesh>().ElementAt(1);
        check(white.Material is PbrMaterial whiteMaterial && whiteMaterial.BaseColor.Red == 1 &&
            whiteMaterial.BaseColor.Green == 1 && whiteMaterial.BaseColor.Blue == 1 && whiteMaterial.Metallic == 0 && whiteMaterial.Roughness == .18f,
            "The actual highlight scene accepts the native White alias with dielectric defaults.");
        check(Throws<NotSupportedException>(() => NativeGalleryScene.Read("OpenPbrPage")) &&
            Throws<NotSupportedException>(() => NativeGalleryScene.Read("ColorLutPage")),
            "The bounded scene adapter rejects alternate draw/material contracts rather than substituting PBR.");
        check(Throws<InvalidOperationException>(() => NativeGalleryScene.Read("MissingGalleryPage")) &&
            Throws<NotSupportedException>(() => NativeGalleryScene.Color("UnknownColorAlias")) &&
            Throws<FormatException>(() => NativeGalleryScene.ApplyTransform(new SceneNode(), XElement.Parse("<Sphere3D X='{Binding Value}' />"))),
            "Unknown definitions, palette names and unevaluated bindings cannot silently produce default content.");
    }

    private static void ValidateNamedLookups(Action<bool, string> check)
    {
        NativeGalleryScene tools = NativeGalleryScene.Read("DeclarativeToolsPage");
        Mesh cone = (Mesh)tools.NamedNodes["TargetCone"], sphere = (Mesh)tools.NamedNodes["TargetSphere"];
        check(cone.Name == "Blue cone" && sphere.Name == "Orange sphere" &&
            tools.SelectionMasks[cone] == 1 && tools.SelectionMasks[sphere] == 2 && tools.SelectionMasks.Values.Contains(0u),
            "x:Name target lookup preserves separate Core names and real native selection masks.");
        NativeGalleryScene pointer = NativeGalleryScene.Read("PointerPenInputPage");
        Mesh marker = (Mesh)pointer.NamedNodes["HitMarker"];
        check(!marker.IsVisible && marker.ShadowCastingMode == MeshShadowCastingMode.Off &&
            marker.Geometry.Positions.Count == 187 && marker.Geometry.Indices.Count == 864 &&
            ReferenceEquals(marker.Material, pointer.NamedMaterials["HitMarkerMaterial"]),
            "Application-owned marker lookup preserves authored visibility, tessellation, shadow and material references.");
        NativeGalleryScene canvas = NativeGalleryScene.Read("HdrCanvasPage");
        check(canvas.NamedMaterials["CanvasMaterial"] is UnlitMaterial &&
            ReferenceEquals(canvas.NamedMaterials["CanvasMaterial"], canvas.Scene.Root.Children.OfType<Mesh>().Single().Material),
            "The HDR canvas snapshot targets the actual authored unlit Core material.");
    }

    private static void ValidateProgress(Action<bool, string> check)
    {
        int requested = 0;
        var example = new ProgressGalleryExample("ProgressBridgePage", () => requested++);
        SceneNode node = example.Definition.NamedNodes["AnimatedCone"];
        check(node.Transform.Position.Y == -.5f && NearRotation(node, -35) &&
            example.Definition.Camera.Transform.Position.X == -1.2f && NearRotation(example.Definition.Camera, -12) && requested == 1,
            "Native progress endpoints are applied before the first requested frame.");
        check(!example.Controller.SetProgress(0) && requested == 1, "Unchanged paused progress does not create a render loop.");
        example.Controller.SetProgress(.5);
        check(Math.Abs(node.Transform.Position.Y - .075f) < .00001f && NearRotation(node, 145) &&
            example.Definition.Camera.Transform.Position.X == 0 && NearRotation(example.Definition.Camera, 0) && requested == 2,
            "One paused progress edit updates all four native mappings and coalesces one request.");
        example.Controller.SetProgress(1);
        check(node.Transform.Position.Y == .65f && NearRotation(node, 325) &&
            example.Definition.Camera.Transform.Position.X == 1.2f && NearRotation(example.Definition.Camera, 12) && requested == 3,
            "The Web progress adapter reaches the original cone/camera end poses.");
        check(!example.Controller.SetProgress(2) && requested == 3, "Overscroll at the end cannot change authored endpoint state.");
        var playback = new ProgressGalleryExample("PlaybackToolbarPage", () => requested++);
        SceneNode played = playback.Definition.NamedNodes["AnimatedCone"];
        playback.Controller.SetProgress(.25);
        check(Math.Abs(played.Transform.Position.X + .7f) < .00001f && played.Transform.Position.Y == -.2f && NearRotation(played, 90),
            "Playback uses its distinct native X/rotation path and preserves unmapped Y.");
    }

    private static void ValidateSceneAssets(Action<bool, string> check)
    {
        var example = new SceneAssetExample();
        SceneNode blue = example.LeftScene.Root.Children.Single(), gold = example.RightScene.Root.Children.Single();
        Mesh blueMesh = blue.EnumerateDepthFirst().OfType<Mesh>().First(), goldMesh = gold.EnumerateDepthFirst().OfType<Mesh>().First();
        check(ReferenceEquals(blueMesh.Geometry, goldMesh.Geometry) && !ReferenceEquals(blueMesh.Material, goldMesh.Material) &&
            blueMesh.Material is UnlitMaterial blueMaterial && blueMaterial.Color.Blue == 1.4f &&
            goldMesh.Material is UnlitMaterial goldMaterial && goldMaterial.Color.Red == 1.5f,
            "The real lamp shares geometry, isolates materials and preserves original HDR finishes.");
        example.Move(); example.Rotate();
        check(example.LeftScene.Root.Children.Count == 0 && example.RightScene.Root.Children.Count == 2 &&
            blue.Transform.Position.X == -1.15f && gold.Transform.Position.X == 1.15f &&
            Math.Abs(Quaternion.Dot(blue.Transform.Rotation, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 6))) > .99999f &&
            gold.Transform.Rotation == Quaternion.Identity,
            "Room transfer and 30-degree rotation mutate only the selected native lamp instance.");
        example.Reset();
        check(ReferenceEquals(example.LeftScene.Root.Children.Single(), blue) && ReferenceEquals(example.RightScene.Root.Children.Single(), gold) &&
            blue.Transform.Position == Vector3.Zero && gold.Transform.Position == Vector3.Zero && blue.Transform.Rotation == Quaternion.Identity &&
            ReferenceEquals(blueMesh.Geometry, goldMesh.Geometry), "Reset restores original rooms without rebuilding shared geometry.");
    }

    private static void ValidatePlayback(Action<bool, string> check)
    {
        var player = new GalleryPlayable();
        check(player.Duration == TimeSpan.FromSeconds(6) && player.State == PlaybackState.Stopped && player.CanSeek,
            "The browser transport keeps the original six-second application-owned playback contract.");
        player.Play(); player.Advance(2); player.Pause(); player.Advance(1);
        check(player.Position == TimeSpan.FromSeconds(2), "Paused playback consumes no animation time.");
        player.Seek(TimeSpan.FromSeconds(4)); player.Play(); player.Advance(3);
        check(player.Position == player.Duration && player.State == PlaybackState.Paused, "Non-looping playback stops at the original end pose.");
        player.Play(); check(player.Position == TimeSpan.Zero, "Playing an ended clip starts again from zero.");
        player.IsLooping = true; player.Advance(6.25);
        check(Math.Abs(player.Position.TotalSeconds - .25) < .00001 && player.State == PlaybackState.Playing,
            "Looping playback preserves VSync overshoot without extending its declared duration.");
    }

    private static bool NearRotation(SceneNode node, float degrees) => Math.Abs(Quaternion.Dot(node.Transform.Rotation,
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * MathF.PI / 180))) > .99999f;
}

using System.Numerics;
using Mu3D.Color;
using Mu3D.Gallery.Pages;
using Mu3D.GalleryApp.Examples;
using Mu3D.GalleryApp.Web.Infrastructure;
using Mu3D.SceneGraph;

internal static class NewGalleryCaseChecks
{
    internal static void Validate(Action<bool, string> check)
    {
        ValidateNativeScenes(check);
        ValidateEmission(check);
        ValidateCanvas(check);
        ValidateFeed(check);
    }

    private static void ValidateNativeScenes(Action<bool, string> check)
    {
        NativeGalleryScene emissive = NativeGalleryScene.Read("EmissiveMaterialPage");
        CheckCamera(emissive.Camera, 60, new(0, 0, 7), check);
        Mesh[] spheres = emissive.Scene.Root.Children.OfType<Mesh>().ToArray();
        check(spheres.Length == 4 && emissive.NamedMaterials.Count == 4,
            "The emissive example must retain four separately addressable native spheres.");
        float[] positions = [-3.6f, -1.2f, 1.2f, 3.6f];
        for (int index = 0; index < spheres.Length; index++)
        {
            Mesh sphere = spheres[index];
            check(sphere.Transform.Position == new Vector3(positions[index], 0, 0) &&
                IsSphere(sphere.Geometry, .9f, 561, 2880),
                "Emissive spheres preserve native spacing, radius and 32×16 tessellation.");
            check(sphere.Material is PbrMaterial material && material.Metallic == 0 && material.Roughness == .4f &&
                material.BaseColor == White && material.AlphaMode == MaterialAlphaMode.Opaque,
                "Native emissive spheres retain white dielectric PBR defaults before emission is applied.");
        }
        DirectionalLight light = emissive.Scene.Root.Children.OfType<DirectionalLight>().Single();
        check(light.Intensity == .5f && light.Color == White && !light.CastsShadows,
            "The emissive scene retains its authored light strength and native shadow default.");

        NativeGalleryScene translucent = NativeGalleryScene.Read("TranslucentCanvasPage");
        CheckCamera(translucent.Camera, 50, new(0, 0, 4), check);
        Mesh canvas = translucent.Scene.Root.Children.OfType<Mesh>().Single();
        check(IsSphere(canvas.Geometry, 1.1f, 561, 2880) &&
            ReferenceEquals(canvas.Material, translucent.NamedMaterials["CanvasMaterial"]) &&
            canvas.Material is UnlitMaterial unlit && unlit.Color == White && unlit.BaseColorTexture is null,
            "The translucent example must use its actual native unlit sphere and named texture slot.");
    }

    private static void ValidateEmission(Action<bool, string> check)
    {
        float[] strengths = [0, 1, 4, 16];
        float[] expectedRed = [0, 4, 16, 64];
        for (int index = 0; index < strengths.Length; index++)
        {
            LinearRgba authoredColor = new(.2f, .3f, .4f, 1, StandardColorSpaces.LinearSrgb);
            PbrMaterial material = new(authoredColor, metallic: .35f, roughness: .72f);
            EmissiveMaterialExample.Apply(material, strengths[index]);
            check(material.EmissiveColor == new LinearRgba(4, 1.2f, .5f, 1, StandardColorSpaces.LinearSrgb) &&
                material.EmissiveStrength == strengths[index] &&
                material.EmissiveColor.Red * material.EmissiveStrength == expectedRed[index],
                "Emission must preserve FP32 tagged HDR components and independent 0/1/4/16 strengths.");
            check(material.BaseColor == authoredColor && material.Roughness == .72f && material.Metallic == .35f,
                "Applying emission must not replace the authored PBR closure.");
        }
    }

    private static void ValidateCanvas(Action<bool, string> check)
    {
        HdrCanvasExample opaque = new();
        HdrCanvasExample translucent = new(translucent: true);
        opaque.Reset(); translucent.Reset();
        LinearRgbaImage originalOpaque = opaque.Publish(out _);
        LinearRgbaImage originalTranslucent = translucent.Publish(out _);
        check(originalOpaque.Width == 256 && originalOpaque.Height == 256 &&
            originalOpaque.ColorSpace == StandardColorSpaces.AcesCg &&
            originalTranslucent.Width == 256 && originalTranslucent.Height == 256 &&
            originalTranslucent.ColorSpace == StandardColorSpaces.AcesCg,
            "Both documents retain the bounded 256×256 ACEScg snapshot contract.");
        CheckPixel(originalOpaque.Pixels[0], new(.025f, .08f, .2f, 1), .001f, check);
        CheckPixel(originalTranslucent.Pixels[0], new(.05f, .1f, .35f, .2f), .001f, check);

        opaque.AddDab(); translucent.AddDab();
        LinearRgbaImage opaqueDab = opaque.Publish(out _);
        LinearRgbaImage translucentDab = translucent.Publish(out _);
        // Full brush coverage at the first center. These independent source-over endpoints include
        // an opaque destination and a 0.2-alpha destination, with FP16 storage rounding tolerance.
        const int center = 48 * 256 + 32;
        CheckPixel(opaqueDab.Pixels[center], new(3.00625f, .47f, .1625f, 1), .003f, check);
        CheckPixel(translucentDab.Pixels[center], new(3.3416667f, .5166667f, .1833333f, .6f), .003f, check);
        check(opaqueDab.Pixels.All(pixel => pixel.W == 1) &&
            translucentDab.Pixels.All(pixel => pixel.W > 0 && pixel.W < 1) &&
            opaqueDab.Pixels[center].X > 1 && translucentDab.Pixels[center].X > 1,
            "Dabs preserve HDR RGB while only the translucent document retains partial coverage.");
        check(originalOpaque.Pixels[center] == originalOpaque.Pixels[0] &&
            originalTranslucent.Pixels[center] == originalTranslucent.Pixels[0],
            "Published snapshots remain independent of later canvas edits.");
        opaque.Reset(); translucent.Reset(); opaque.AddDab(); translucent.AddDab();
        check(opaque.Publish(out _).Pixels.SequenceEqual(opaqueDab.Pixels) &&
            translucent.Publish(out _).Pixels.SequenceEqual(translucentDab.Pixels),
            "Reset restores the document and deterministic first dab for both alpha modes.");
    }

    private static void ValidateFeed(Action<bool, string> check)
    {
        check(ProceduralFeedExample.ProductCount == 24 && ProceduralFeedExample.GridSpan == 2,
            "The procedural native/Web feed retains 24 products in two columns.");
        FeedProduct[] products = Enumerable.Range(0, 24).Select(index => new FeedProduct(index)).ToArray();
        check(products.All(product => product.Scene is null && product.Camera is null && !product.IsLive),
            "Cold feed records must not create scenes, cameras or presentation work.");
        (int live, int warm) = ProceduralFeedExample.ApplyLifecycle(products, 4, 7);
        check(live == 8 && warm == 6 &&
            Enumerable.Range(0, 24).Where(index => products[index].IsLive).SequenceEqual(Enumerable.Range(2, 8)) &&
            Enumerable.Range(0, 24).Where(index => products[index].Scene is not null).SequenceEqual(Enumerable.Range(0, 14)),
            "Two visible middle rows keep one adjacent row live, three adjacent rows warm, and distant records cold.");
        Vector3[] colors = [new(1.6f, .5f, .3f), new(.35f, 1.1f, 2.2f), new(.9f, .8f, .2f), new(1.8f, .4f, 1.2f)];
        for (int index = 0; index < 4; index++)
        {
            Mesh mesh = products[index].Scene!.Root.Children.OfType<Mesh>().Single();
            Vector3 color = colors[index];
            check(mesh.Material is UnlitMaterial material &&
                material.Color == new LinearRgba(color.X, color.Y, color.Z, 1, StandardColorSpaces.LinearSrgb),
                "Procedural products retain their actual FP32 HDR color variants.");
        }
        Mesh sphere = products[0].Scene!.Root.Children.OfType<Mesh>().Single();
        Mesh cone = products[1].Scene!.Root.Children.OfType<Mesh>().Single();
        check(IsSphere(sphere.Geometry, .85f, 1025, 5520) &&
            cone.Geometry.Positions.Count == 124 && cone.Geometry.Indices.Count == 240 &&
            Math.Abs(cone.Geometry.Positions.Min(position => position.Y) + .85f) < .00001f &&
            Math.Abs(cone.Geometry.Positions.Max(position => position.Y) - .85f) < .00001f &&
            Math.Abs(cone.Geometry.Positions.Max(position => new Vector2(position.X, position.Z).Length()) - .75f) < .00001f,
            "Feed variants retain the native 0.85 sphere and 0.75×1.7 cone with their authored tessellation.");
        PerspectiveCamera camera = (PerspectiveCamera)products[0].Camera!;
        CheckCamera(camera, 60, new(2.2f, 1.6f, 2.6f), check);
        Vector3 target = Vector3.Transform(Vector3.Zero, camera.ViewMatrix);
        check(Math.Abs(target.X) < .00001f && Math.Abs(target.Y) < .00001f && target.Z < 0,
            "Feed cameras point at the product center rather than retaining an unrotated native pose.");

        Scene warmScene = products[0].Scene!;
        ProceduralFeedExample.ApplyLifecycle(products, 0, 1);
        check(products[0].IsLive && ReferenceEquals(warmScene, products[0].Scene),
            "Promoting a preloaded row to live must reuse its content.");
        ProceduralFeedExample.ApplyLifecycle(products, 4, 7);
        check(!products[0].IsLive && ReferenceEquals(warmScene, products[0].Scene),
            "Suspending a nearby row preserves its preloaded content.");
        Scene retained = products[4].Scene!;
        Camera retainedCamera = products[4].Camera!;
        products[4].EnsureContent();
        ProceduralFeedExample.ApplyLifecycle(products, 6, 9);
        check(ReferenceEquals(retained, products[4].Scene) && ReferenceEquals(retainedCamera, products[4].Camera),
            "Nearby row changes and repeated preparation reuse the same scene and camera.");
        (live, warm) = ProceduralFeedExample.ApplyLifecycle(products, 22, 23);
        check(live == 4 && warm == 4 && products.Take(16).All(product => product.Scene is null && product.Camera is null && !product.IsLive),
            "The final row clips retention at the list boundary and releases distant content.");
        ProceduralFeedExample.ApplyLifecycle(products, 4, 7);
        check(products[4].Scene is not null && !ReferenceEquals(retained, products[4].Scene),
            "A cold product reconstructs its content when it returns to the visible neighborhood.");
        ProceduralFeedExample.ApplyLifecycle(products, -1, -1);
        ProceduralFeedExample.ApplyLifecycle(products, -1, -1);
        check(products.All(product => product.Scene is null && product.Camera is null && !product.IsLive),
            "An absent viewport releases all feed content; repeated suspension is idempotent.");
    }

    private static LinearRgba White => new(1, 1, 1, 1, StandardColorSpaces.LinearSrgb);

    private static bool IsSphere(MeshGeometry geometry, float radius, int vertices, int indices) =>
        geometry.Positions.Count == vertices && geometry.Indices.Count == indices &&
        geometry.Positions.All(position => Math.Abs(position.Length() - radius) < .00001f);

    private static void CheckCamera(PerspectiveCamera camera, float degrees, Vector3 position, Action<bool, string> check) =>
        check(Vector3.Distance(camera.Transform.Position, position) < .00001f &&
            Math.Abs(camera.FieldOfViewRadians - degrees * MathF.PI / 180) < .00001f &&
            camera.NearClip == .1f && camera.FarClip == 1000,
            "Native camera field of view, eye position and clipping defaults must survive the Web adapter.");

    private static void CheckPixel(Vector4 actual, Vector4 expected, float tolerance, Action<bool, string> check) =>
        check(Vector4.Distance(actual, expected) < tolerance,
            "The real canvas snapshot must match the independent linear-light RGBA endpoint within FP16 storage tolerance.");
}

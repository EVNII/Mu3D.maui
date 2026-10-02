using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

internal static class SceneRendererCacheChecks
{
    internal static void Run(ICollection<string> failures)
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) failures.Add($"FAILED: renderer cache {name}");
        }

        CheckAllMaterialSlots(Check);
        CheckSemanticSharing(Check);
        CheckRetainedScenes(Check);
        CheckRejectedInput(Check);
        CheckMaterialIdentity(Check);
        CheckDisposalReentry(Check);
        CheckBorrowedReferences(Check);
        Console.WriteLine($"Validated {checks} renderer cache retention, lifetime, slot, semantic and recovery checks.");
    }

    private static void CheckAllMaterialSlots(Action<bool, string> check)
    {
        using Fixture fixture = new();
        ColorSlot[] colors =
        [
            new("base color", (m, i) => m.BaseColorTexture = i, (m, i) => m.CompressedBaseColorTexture = i),
            new("emissive", (m, i) => m.EmissiveTexture = i, (m, i) => m.CompressedEmissiveTexture = i),
            new("sheen color", (m, i) => m.SheenColorTexture = i, (m, i) => m.CompressedSheenColorTexture = i),
            new("specular color", (m, i) => m.SpecularColorTexture = i, (m, i) => m.CompressedSpecularColorTexture = i),
            new("diffuse transmission color", (m, i) => m.DiffuseTransmissionColorTexture = i, (m, i) => m.CompressedDiffuseTransmissionColorTexture = i),
        ];
        DataSlot[] data =
        [
            new("normal", (m, i) => m.NormalTexture = i, (m, i) => m.CompressedNormalTexture = i),
            new("ORM", (m, i) => m.OcclusionRoughnessMetallicTexture = i, (m, i) => m.CompressedOcclusionRoughnessMetallicTexture = i),
            new("clearcoat", (m, i) => m.ClearcoatTexture = i, (m, i) => m.CompressedClearcoatTexture = i),
            new("clearcoat roughness", (m, i) => m.ClearcoatRoughnessTexture = i, (m, i) => m.CompressedClearcoatRoughnessTexture = i),
            new("clearcoat normal", (m, i) => m.ClearcoatNormalTexture = i, (m, i) => m.CompressedClearcoatNormalTexture = i),
            new("anisotropy", (m, i) => m.AnisotropyTexture = i, (m, i) => m.CompressedAnisotropyTexture = i),
            new("transmission", (m, i) => m.TransmissionTexture = i, (m, i) => m.CompressedTransmissionTexture = i),
            new("volume thickness", (m, i) => m.VolumeThicknessTexture = i, (m, i) => m.CompressedVolumeThicknessTexture = i),
            new("sheen roughness", (m, i) => m.SheenRoughnessTexture = i, (m, i) => m.CompressedSheenRoughnessTexture = i),
            new("iridescence", (m, i) => m.IridescenceTexture = i, (m, i) => m.CompressedIridescenceTexture = i),
            new("iridescence thickness", (m, i) => m.IridescenceThicknessTexture = i, (m, i) => m.CompressedIridescenceThicknessTexture = i),
            new("specular", (m, i) => m.SpecularTexture = i, (m, i) => m.CompressedSpecularTexture = i),
            new("diffuse transmission", (m, i) => m.DiffuseTransmissionTexture = i, (m, i) => m.CompressedDiffuseTransmissionTexture = i),
        ];
        foreach (ColorSlot slot in colors)
        {
            CheckSlot(slot.Name + " decoded", true, m => slot.Decoded(m, ColorImage(slot.Name)),
                m => slot.Decoded(m, null));
            CheckSlot(slot.Name + " compressed", true, m => slot.Compressed(m, CompressedImage(true, slot.Name)),
                m => slot.Compressed(m, null));
        }
        foreach (DataSlot slot in data)
        {
            CheckSlot(slot.Name + " decoded", false, m => slot.Decoded(m, DataImage(slot.Name)),
                m => slot.Decoded(m, null));
            CheckSlot(slot.Name + " compressed", false, m => slot.Compressed(m, CompressedImage(false, slot.Name)),
                m => slot.Compressed(m, null));
        }

        void CheckSlot(string name, bool isColor, Action<PbrMaterial> assign, Action<PbrMaterial> remove)
        {
            Scene scene = fixture.CreateScene(name);
            Mesh mesh = scene.Root.EnumerateDepthFirst().OfType<Mesh>().Single();
            PbrMaterial material = (PbrMaterial)mesh.Material;
            ActivateExtensions(material);
            assign(material);
            int beforeWrites = fixture.Device.TextureWrites.Count;
            fixture.Render(scene);
            string imageLabel = name.Replace(" decoded", "").Replace(" compressed", "");
            GraphicsTexture[] uploaded = fixture.Device.TextureWrites.Skip(beforeWrites)
                .Where(write => write.Destination.Descriptor.Label == imageLabel)
                .Select(write => write.Destination).Distinct().ToArray();
            int count = isColor ? fixture.Renderer.CachedMaterialTextureCount : fixture.Renderer.CachedMaterialDataTextureCount;
            check(count == 1 && uploaded.Length == 1 && !uploaded[0].IsDisposed, name + " retained after render");
            int writes = fixture.Device.TextureWrites.Count;
            fixture.Render(scene);
            check(fixture.Device.TextureWrites.Count == writes, name + " reused without reupload");
            mesh.Parent!.IsVisible = false;
            fixture.Renderer.TrimCaches(scene);
            check(uploaded.All(texture => !texture.IsDisposed) && fixture.Renderer.CachedMeshCount == 1,
                name + " hidden hierarchy stays warm");
            remove(material);
            fixture.Renderer.TrimCaches(scene);
            check((isColor ? fixture.Renderer.CachedMaterialTextureCount : fixture.Renderer.CachedMaterialDataTextureCount) == 0 &&
                uploaded.All(texture => texture.IsDisposed), name + " removed slot disposes upload");
        }
    }

    private static void CheckSemanticSharing(Action<bool, string> check)
    {
        foreach (bool compressed in new[] { false, true })
        {
            using Fixture fixture = new();
            Scene scene = fixture.CreateScene("semantic sharing");
            Mesh mesh = scene.Root.EnumerateDepthFirst().OfType<Mesh>().Single();
            PbrMaterial material = (PbrMaterial)mesh.Material;
            NormalizedRgbaDataImage decoded = DataImage("shared semantic image");
            CompressedMaterialTexture encoded = CompressedImage(false, "shared semantic image");
            material.ClearcoatFactor = 0.5f;
            if (compressed)
            {
                material.CompressedNormalTexture = encoded;
                material.CompressedClearcoatNormalTexture = encoded;
                material.CompressedOcclusionRoughnessMetallicTexture = encoded;
            }
            else
            {
                material.NormalTexture = decoded;
                material.ClearcoatNormalTexture = decoded;
                material.OcclusionRoughnessMetallicTexture = decoded;
            }
            fixture.Render(scene);
            GraphicsTexture[] textures = fixture.Device.TextureWrites
                .Where(write => write.Destination.Descriptor.Label == "shared semantic image")
                .Select(write => write.Destination).Distinct().ToArray();
            string name = compressed ? "compressed" : "decoded";
            check(fixture.Renderer.CachedMaterialDataTextureCount == 2 && textures.Length == 2,
                name + " shares normal uploads while separating normal and channel semantics");
            int writes = fixture.Device.TextureWrites.Count;
            if (compressed)
            {
                material.CompressedNormalTexture = null;
                material.CompressedClearcoatNormalTexture = null;
            }
            else
            {
                material.NormalTexture = null;
                material.ClearcoatNormalTexture = null;
            }
            fixture.Render(scene);
            check(fixture.Renderer.CachedMaterialDataTextureCount == 1 &&
                textures.Count(texture => texture.IsDisposed) == 1 && fixture.Device.TextureWrites.Count == writes,
                name + " semantic removal disposes only the unused normal upload");
            mesh.Material = new PbrMaterial(new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearSrgb));
            fixture.Render(scene);
            check(fixture.Renderer.CachedMaterialDataTextureCount == 0 && textures.All(texture => texture.IsDisposed),
                name + " material replacement releases former image uploads");
        }
    }

    private static void CheckRetainedScenes(Action<bool, string> check)
    {
        using Fixture fixture = new();
        Scene first = fixture.CreateScene("first warm scene");
        Scene second = fixture.CreateScene("second warm scene");
        Mesh firstMesh = first.Root.EnumerateDepthFirst().OfType<Mesh>().Single();
        Mesh secondMesh = second.Root.EnumerateDepthFirst().OfType<Mesh>().Single();
        ((PbrMaterial)firstMesh.Material).BaseColorTexture = ColorImage("first warm image");
        ((PbrMaterial)secondMesh.Material).BaseColorTexture = ColorImage("second warm image");
        SceneRenderViewport[] viewports =
        [
            new(first, fixture.Camera, 0, 0, 480, 576),
            new(second, new PerspectiveCamera { Transform = { Position = new Vector3(0, 0, 5.6f) } }, 480, 0, 480, 576),
        ];
        fixture.Renderer.RenderViewports(viewports, fixture.Color, fixture.Depth,
            new LinearRgba(0, 0, 0, 1, StandardColorSpaces.LinearSrgb));
        check(fixture.Renderer.CachedMeshCount == 2 && fixture.Renderer.CachedGeometryCount == 1 &&
            fixture.Renderer.CachedMaterialTextureCount == 2, "multi-scene union shares geometry and retains independent textures");
        GraphicsTexture firstTexture = fixture.Device.TextureWrites.First(write => write.Destination.Label == "first warm image").Destination;
        GraphicsTexture secondTexture = fixture.Device.TextureWrites.First(write => write.Destination.Label == "second warm image").Destination;
        second.Root.IsVisible = false;
        fixture.Renderer.TrimCaches(EnumerateOnce(first, second, first));
        check(fixture.Renderer.CachedMeshCount == 2 && !firstTexture.IsDisposed && !secondTexture.IsDisposed,
            "single-pass enumerable, duplicate scenes and hidden scene retain their union");
        fixture.Renderer.TrimCaches(first);
        check(fixture.Renderer.CachedMeshCount == 1 && fixture.Renderer.CachedGeometryCount == 1 &&
            !firstTexture.IsDisposed && secondTexture.IsDisposed, "single-scene retention drops only unretained scene resources");
        firstMesh.Geometry = new MeshGeometry([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]);
        fixture.Renderer.TrimCaches(first);
        check(fixture.Renderer.CachedGeometryCount == 0 && fixture.Renderer.CachedMeshCount == 1,
            "geometry reassignment removes former geometry without removing retained mesh identity");
        fixture.Renderer.TrimCaches(Array.Empty<Scene>());
        check(fixture.Renderer.CachedGeometryCount == 0 && fixture.Renderer.CachedMeshCount == 0 &&
            fixture.Renderer.CachedMaterialTextureCount == 0 && firstTexture.IsDisposed,
            "empty retention releases all authored scene caches");

        Scene environmentScene = fixture.CreateScene("hidden IBL");
        ImageBasedLight light = new(new EquirectangularHdrEnvironment(2, 1,
            [new Vector3(2), new Vector3(0.5f)], StandardColorSpaces.LinearSrgb));
        environmentScene.Add(light);
        fixture.Render(environmentScene);
        light.IsVisible = false;
        fixture.Renderer.TrimCaches(environmentScene);
        check(fixture.Renderer.CachedEnvironmentCount == 1, "hidden environment remains retained");
        environmentScene.Remove(light);
        fixture.Renderer.TrimCaches(environmentScene);
        check(fixture.Renderer.CachedEnvironmentCount == 0, "removed environment cache is released");
    }

    private static void CheckRejectedInput(Action<bool, string> check)
    {
        using Fixture fixture = new();
        Scene retained = fixture.CreateScene("invalid-input recovery");
        ((PbrMaterial)retained.Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material).BaseColorTexture = ColorImage("atomic retention image");
        fixture.Render(retained);
        GraphicsTexture texture = fixture.Device.TextureWrites.First(write => write.Destination.Label == "atomic retention image").Destination;
        Scene unrelated = fixture.CreateScene("unrelated input");
        check(Throws<ArgumentException>(() => fixture.Renderer.TrimCaches(new Scene[] { unrelated, null! })) &&
            fixture.Renderer.CachedMeshCount == 1 && !texture.IsDisposed,
            "null element is rejected before cached resource disposal");
        check(Throws<InvalidOperationException>(() => fixture.Renderer.TrimCaches(ThrowingEnumeration(unrelated))) &&
            fixture.Renderer.CachedMeshCount == 1 && !texture.IsDisposed,
            "enumeration failure preserves existing resources");
        check(Throws<ArgumentNullException>(() => fixture.Renderer.TrimCaches((Scene)null!)) &&
            Throws<ArgumentNullException>(() => fixture.Renderer.TrimCaches((IEnumerable<Scene>)null!)) && !texture.IsDisposed,
            "null single and multi-scene arguments preserve resources");
        fixture.Renderer.TrimCaches(retained);
        check(fixture.Renderer.CachedMeshCount == 1 && fixture.Renderer.CachedMaterialTextureCount == 1 && !texture.IsDisposed,
            "valid call after rejected enumeration does not use stale scratch contents");
        fixture.Renderer.TrimCaches(NestedEnumeration(retained, unrelated, fixture.Renderer));
        check(fixture.Renderer.CachedMeshCount == 1 && !texture.IsDisposed,
            "iterator may invoke a nested trim before the outer retention set is applied");
        PbrMaterial material = (PbrMaterial)retained.Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material;
        LinearRgba originalColor = material.BaseColor;
        material.BaseColor = new LinearRgba(1, 1, 1, 1, new UnsupportedLinearColorSpace());
        check(Throws<NotSupportedException>(() => fixture.Renderer.TrimCaches(new[] { unrelated, retained })) &&
            fixture.Renderer.CachedMeshCount == 1 && !texture.IsDisposed,
            "invalid material is rejected before disposing existing mesh and texture resources");
        material.BaseColor = originalColor;
        fixture.Renderer.TrimCaches(retained);
        check(fixture.Renderer.CachedMeshCount == 1 && !texture.IsDisposed,
            "valid trim recovers after material preparation fails");
        fixture.Renderer.Dispose();
        check(Throws<ObjectDisposedException>(() => fixture.Renderer.TrimCaches(retained)) &&
            Throws<ObjectDisposedException>(() => fixture.Renderer.TrimCaches(new[] { retained })),
            "both overloads reject disposed renderer");
    }

    private static void CheckMaterialIdentity(Action<bool, string> check)
    {
        using Fixture fixture = new();
        Scene scene = fixture.CreateScene("material identity", 2);
        Mesh[] meshes = scene.Root.EnumerateDepthFirst().OfType<Mesh>().ToArray();
        meshes[0].Material = new EqualPbrMaterial { BaseColorTexture = ColorImage("equal material first texture") };
        meshes[1].Material = new EqualPbrMaterial { BaseColorTexture = ColorImage("equal material second texture") };
        fixture.Render(scene);
        check(meshes[0].Material.Equals(meshes[1].Material) &&
            fixture.Renderer.CachedMaterialTextureCount == 2,
            "equal-valued material subclasses keep distinct image references");
        int writes = fixture.Device.TextureWrites.Count;
        fixture.Render(scene);
        check(fixture.Device.TextureWrites.Count == writes,
            "equal-valued material identities reuse both uploaded textures");
    }

    private static void CheckDisposalReentry(Action<bool, string> check)
    {
        using Fixture fixture = new();
        Scene scene = fixture.CreateScene("disposal reentry");
        fixture.Render(scene);
        bool callbackRan = false, singleRejected = false, manyRejected = false;
        fixture.Device.OnBufferDisposing = () =>
        {
            fixture.Device.OnBufferDisposing = null;
            callbackRan = true;
            singleRejected = Throws<InvalidOperationException>(() => fixture.Renderer.TrimCaches(scene));
            manyRejected = Throws<InvalidOperationException>(() => fixture.Renderer.TrimCaches(Array.Empty<Scene>()));
        };
        fixture.Renderer.TrimCaches(Array.Empty<Scene>());
        check(callbackRan && singleRejected && manyRejected,
            "backend resource disposal cannot reenter either trim overload");
        check(fixture.Renderer.CachedMeshCount == 0 && fixture.Renderer.CachedGeometryCount == 0,
            "rejected disposal reentry does not corrupt outer removal");
        fixture.Render(scene);
        check(fixture.Renderer.CachedMeshCount == 1 && fixture.Renderer.CachedGeometryCount == 1,
            "normal rendering resumes after rejected disposal reentry");

        using Fixture disposedDuringTrim = new();
        Scene disposedScene = disposedDuringTrim.CreateScene("dispose during trim");
        disposedDuringTrim.Render(disposedScene);
        disposedDuringTrim.Device.OnBufferDisposing = () =>
        {
            disposedDuringTrim.Device.OnBufferDisposing = null;
            disposedDuringTrim.Renderer.Dispose();
        };
        disposedDuringTrim.Renderer.TrimCaches(Array.Empty<Scene>());
        check(disposedDuringTrim.Renderer.CachedMeshCount == 0 &&
            disposedDuringTrim.Renderer.CachedGeometryCount == 0 &&
            disposedDuringTrim.Device.Buffers.All(buffer => buffer.IsDisposed),
            "renderer disposal from a backend callback completes outer resource cleanup");
    }

    private static void CheckBorrowedReferences(Action<bool, string> check)
    {
        using Fixture fixture = new();
        Scene cached = fixture.CreateScene("scratch texture setup");
        ((PbrMaterial)cached.Root.EnumerateDepthFirst().OfType<Mesh>().Single().Material).BaseColorTexture = ColorImage("scratch texture setup");
        fixture.Render(cached);
        WeakReference[] success = TrimBorrowedScene(fixture.Renderer, false);
        WeakReference[] failure = TrimBorrowedScene(fixture.Renderer, true);
        fixture.Render(cached);
        WeakReference[] materialFailure = TrimBorrowedScene(fixture.Renderer, false, invalidMaterial: true);
        // The renderer stays live across GC: only scratch ownership could retain these uncached inputs.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        check(success.All(reference => !reference.IsAlive), "successful trim retains no borrowed scene graph or environment references");
        check(failure.All(reference => !reference.IsAlive), "failed enumeration retains no borrowed scene graph or environment references");
        check(materialFailure.All(reference => !reference.IsAlive), "failed material preparation retains no borrowed scene graph, image or traversal references");
        GC.KeepAlive(fixture);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] TrimBorrowedScene(SceneRenderer renderer, bool fail, bool invalidMaterial = false)
    {
        Scene scene = new("borrowed input");
        SceneNode parent = new("borrowed parent");
        MeshGeometry geometry = new([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
        PbrMaterial material = new(new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearSrgb));
        LinearRgbaImage image = ColorImage("uncached borrowed image");
        material.BaseColorTexture = image;
        if (invalidMaterial) material.BaseColor = new LinearRgba(1, 1, 1, 1, new UnsupportedLinearColorSpace());
        Mesh mesh = new(geometry, material);
        EquirectangularHdrEnvironment environment = new(1, 1, [Vector3.One], StandardColorSpaces.LinearSrgb);
        parent.AddChild(mesh);
        parent.AddChild(new ImageBasedLight(environment));
        scene.Add(parent);
        if (fail) _ = Throws<InvalidOperationException>(() => renderer.TrimCaches(ThrowingEnumeration(scene)));
        else if (invalidMaterial) _ = Throws<NotSupportedException>(() => renderer.TrimCaches(scene));
        else renderer.TrimCaches(scene);
        return [new(scene), new(parent), new(mesh), new(geometry), new(material), new(image), new(environment)];
    }

    private static IEnumerable<Scene> ThrowingEnumeration(Scene scene)
    {
        yield return scene;
        throw new InvalidOperationException("Injected retained-scene enumeration failure.");
    }

    private static IEnumerable<Scene> EnumerateOnce(params Scene[] scenes)
    {
        // Iterator inputs have no Count/indexer and must remain supported.
        foreach (Scene scene in scenes) yield return scene;
    }

    private static IEnumerable<Scene> NestedEnumeration(Scene first, Scene second, SceneRenderer renderer)
    {
        yield return first;
        renderer.TrimCaches(first);
        yield return second;
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    private static LinearRgbaImage ColorImage(string name) => new(2, 2,
        [Vector4.One, Vector4.One, Vector4.One, Vector4.One], StandardColorSpaces.LinearSrgb, name);

    private static NormalizedRgbaDataImage DataImage(string name) => new(2, 2,
        [new Vector4(0.5f, 0.5f, 1, 1), new Vector4(1, 0.5f, 0.5f, 1),
         new Vector4(0.5f, 1, 0.5f, 1), new Vector4(0.5f, 0.5f, 1, 1)], name);

    private static CompressedMaterialTexture CompressedImage(bool color, string name) => new(
        color ? GraphicsTextureFormat.Bc7RgbaUnormSrgb : GraphicsTextureFormat.Astc4x4Unorm,
        color ? CompressedMaterialTextureContent.Color : CompressedMaterialTextureContent.Data,
        [new CompressedTextureMipLevel(4, 4, new byte[16])], color ? StandardColorSpaces.LinearSrgb : null, name);

    private static void ActivateExtensions(PbrMaterial material)
    {
        material.EmissiveColor = new LinearRgba(0.1f, 0.1f, 0.1f, 1, StandardColorSpaces.LinearSrgb);
        material.ClearcoatFactor = 0.5f;
        material.AnisotropyStrength = 0.5f;
        material.TransmissionFactor = 0.5f;
        material.VolumeThicknessFactor = 0.5f;
        material.SheenColor = new LinearRgba(0.1f, 0.1f, 0.1f, 1, StandardColorSpaces.LinearSrgb);
        material.IridescenceFactor = 0.5f;
        material.SpecularFactor = 0.5f;
        material.DiffuseTransmissionFactor = 0.5f;
    }

    private sealed record ColorSlot(string Name, Action<PbrMaterial, LinearRgbaImage?> Decoded,
        Action<PbrMaterial, CompressedMaterialTexture?> Compressed);
    private sealed record DataSlot(string Name, Action<PbrMaterial, NormalizedRgbaDataImage?> Decoded,
        Action<PbrMaterial, CompressedMaterialTexture?> Compressed);

    private sealed class EqualPbrMaterial() : PbrMaterial(new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearSrgb))
    {
        public override bool Equals(object? obj) => obj is EqualPbrMaterial;
        public override int GetHashCode() => 1;
    }

    private sealed record UnsupportedLinearColorSpace() : ColorSpaceReference("unsupported test space", isLinear: true);

    internal static void Measure(string? outputPath)
    {
        using Fixture fixture = new();
        Scene scene = fixture.CreateScene("allocation fixture", 3);
        fixture.Render(scene);
        Scene[] scenes = [scene];
        object report = new
        {
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            backend = "RecordingGraphicsDevice (headless; render allocation includes recording commands)",
            configuration = "Release; FP16 960x576 color, Depth32Float, three shared-geometry PBR meshes, directional light",
            measurement = "GC.GetAllocatedBytesForCurrentThread and Stopwatch.GetTimestamp; 120 warmup calls then 300 measured calls; no forced GC",
            trimSingleScene = MeasureCalls(() => fixture.Renderer.TrimCaches(scene)),
            trimSceneArray = MeasureCalls(() => fixture.Renderer.TrimCaches(scenes)),
            render = MeasureCalls(() => fixture.Render(scene)),
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (outputPath is not null)
        {
            string fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json + Environment.NewLine);
        }
        Console.WriteLine(json);
    }

    private static object MeasureCalls(Action action)
    {
        const int warmup = 120, measured = 300;
        for (int i = 0; i < warmup; i++) action();
        long[] allocations = new long[measured];
        double[] elapsed = new double[measured];
        for (int i = 0; i < measured; i++)
        {
            long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
            long beforeTime = Stopwatch.GetTimestamp();
            action();
            elapsed[i] = Stopwatch.GetElapsedTime(beforeTime).TotalMilliseconds;
            allocations[i] = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
        }
        Array.Sort(elapsed);
        return new
        {
            calls = measured,
            totalAllocatedBytes = allocations.Sum(),
            meanAllocatedBytes = allocations.Average(),
            minimumAllocatedBytes = allocations.Min(),
            maximumAllocatedBytes = allocations.Max(),
            medianMilliseconds = elapsed[(measured - 1) / 2],
            p95Milliseconds = elapsed[(int)Math.Ceiling(measured * 0.95) - 1],
        };
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RecordingGraphicsDevice Device = new();
        internal readonly SceneRenderer Renderer;
        internal readonly RecordingGraphicsTexture Color;
        internal readonly GraphicsTexture Depth;
        internal readonly PerspectiveCamera Camera = new(aspectRatio: 960f / 576f);
        internal readonly MeshGeometry Geometry = new(
            [new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(0, 1, 0)],
            [0, 1, 2],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]);

        internal Fixture()
        {
            Renderer = new SceneRenderer(Device, GraphicsTextureFormat.Rgba16Float);
            Color = (RecordingGraphicsTexture)Device.CreateTexture(new GraphicsTextureDescriptor(
                new GraphicsExtent3D(960, 576), GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.RenderAttachment));
            Depth = Device.CreateTexture(new GraphicsTextureDescriptor(
                new GraphicsExtent3D(960, 576), GraphicsTextureFormat.Depth32Float,
                GraphicsTextureUsage.RenderAttachment));
            Camera.Transform.Position = new Vector3(0, 0, 5.6f);
        }

        internal Scene CreateScene(string name, int meshCount = 1)
        {
            Scene scene = new(name);
            SceneNode parent = new("nested PBR group");
            scene.Add(parent);
            for (int i = 0; i < meshCount; i++)
                parent.AddChild(new Mesh(Geometry, new PbrMaterial(
                    new LinearRgba(0.8f, 0.2f, 0.1f, 1, StandardColorSpaces.LinearSrgb),
                    metallic: 0.7f, roughness: 0.25f), $"mesh {i}"));
            scene.Add(new DirectionalLight(
                new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearSrgb)));
            return scene;
        }

        internal void Render(Scene scene) => Renderer.Render(scene, Camera, Color, Depth);

        public void Dispose()
        {
            Renderer.Dispose();
            Depth.Dispose();
            Color.Dispose();
            Device.Dispose();
        }
    }
}

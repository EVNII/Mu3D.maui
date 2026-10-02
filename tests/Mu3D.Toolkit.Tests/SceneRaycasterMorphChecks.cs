using System.Numerics;
using System.Text.Json;
using Mu3D.Color;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Selection;

internal static class SceneRaycasterMorphChecks
{
    private const uint ViewportSize = 400;
    private const int AllocationSamples = 64;
    private const float PositionTolerance = .0002f;
    private static readonly UnlitMaterial Material = new(
        new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb));
    private static GapEvidence? gapEvidence;

    internal static void Validate(ICollection<string> failures)
    {
        VerifyLegacyGap(failures);
        VerifyCurrentWeightsAndPose(failures);
        VerifyClosestOrdering(failures);
        VerifyEligibility(failures);
        VerifyClipInterval(failures);
        VerifyOverflowRecovery(failures);
        VerifyCollapsedAndGeneralTargets(failures);
        VerifyAllocation(failures);
    }

    // Root compiles this same public-API fixture against retained .9. The evidence is written
    // before current-behavior assertions so the expected legacy failure still records both pixels.
    internal static void VerifyLegacyGap(ICollection<string> failures)
    {
        Fixture fixture = new("displaced picking triangle");
        Mesh mesh = Triangle(Vector3.Zero, [new MorphTarget(UniformDelta(new(1.5f, 0f, 0f)))]);
        mesh.MorphWeights = [1f];
        fixture.Scene.Add(mesh);
        Vector2 oldPixel = Project(fixture.Camera, Vector3.Zero);
        Vector2 newPixel = Project(fixture.Camera, new(1.5f, 0f, 0f));
        int oldHits = fixture.Raycaster.HitTest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, oldPixel).Count;
        int newHits = fixture.Raycaster.HitTest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, newPixel).Count;
        bool oldClosest = fixture.Raycaster.TryHitClosest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, oldPixel, out _);
        bool newClosest = fixture.Raycaster.TryHitClosest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, newPixel, out _);
        gapEvidence = new(oldPixel, newPixel, oldHits, newHits, oldClosest, newClosest);
        WriteEvidence(null);
        Expect(oldHits == 0 && newHits == 1 && !oldClosest && newClosest,
            "both scene raycast APIs miss the old base pixel and hit the current displaced triangle", failures);
    }

    private static void VerifyCurrentWeightsAndPose(ICollection<string> failures)
    {
        Fixture fixture = new("current morph picking");
        Vector3 firstDelta = new(1.5f, 0f, .25f);
        Vector3 secondDelta = new(0f, .5f, -.5f);
        Mesh mesh = Triangle(Vector3.Zero,
            [new MorphTarget(UniformDelta(firstDelta)), new MorphTarget(UniformDelta(secondDelta))]);
        fixture.Scene.Add(mesh);
        float[][] cases = [[0f, 0f], [1f, 0f], [-1f, 0f], [1.5f, 0f], [.5f, 1f], [0f, 0f]];
        foreach (float[] weights in cases)
        {
            mesh.MorphWeights = weights;
            Vector3 local = firstDelta * weights[0] + secondDelta * weights[1];
            VerifyKnownHit(fixture, mesh, local, 0,
                "current zero/positive/negative/overshoot/multiple morph weights", failures);
            if (MathF.Abs(local.X) >= .75f)
                VerifyMiss(fixture, Project(fixture.Camera, Vector3.Zero),
                    "displaced morph geometry no longer hits the original base pixel", failures);
        }

        mesh.MorphWeights = [.5f, 1f];
        SceneNode parent = new("rigid transformed parent");
        parent.Transform.Position = new(.25f, -.15f, -.2f);
        parent.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .2f);
        parent.Transform.Scale = new(1.1f, .9f, 1f);
        parent.AddChild(mesh);
        fixture.Scene.Add(parent);
        mesh.Transform.Position = new(-.25f, .2f, .1f);
        mesh.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .3f);
        mesh.Transform.Scale = new(2f, .75f, 1.25f);
        Vector3 deformedLocal = firstDelta * .5f + secondDelta;
        VerifyKnownHit(fixture, mesh, deformedLocal, 0,
            "morph picking follows current parent and nonuniform local transforms", failures);
        mesh.Transform.Scale = new(-2f, .75f, 1.25f);
        VerifyKnownHit(fixture, mesh, deformedLocal, 0,
            "reflected morph triangles remain double-sided with world-distance parameterization", failures);
        parent.Transform.Position = new(.35f, -.3f, -.1f);
        fixture.Camera.Transform.Position = new(.1f, -.1f, 5f);
        VerifyKnownHit(fixture, mesh, deformedLocal, 0,
            "morph picking reads live camera and ancestor pose", failures);

        parent.RemoveChild(mesh);
        fixture.Scene.Add(mesh);
        fixture.Camera.Transform.Position = new(0f, 0f, 5f);
        mesh.Transform.Position = Vector3.Zero;
        mesh.Transform.Rotation = Quaternion.Identity;
        mesh.Transform.Scale = Vector3.One;
        mesh.Geometry = new MeshGeometry(TrianglePositions(new(-.75f, 0f, 0f)), [2u, 1u, 0u]);
        VerifyKnownHit(fixture, mesh, new(-.75f, 0f, 0f), 0,
            "geometry replacement resets morph data and preserves reversed-winding hits", failures);
        Expect(mesh.MorphWeights.Count == 0, "raycasting preserves geometry-owned morph weight reset", failures);

        // Preserve the renderer's authored target order: moving the small term before this
        // cancellation would lose .25 at FP32 precision and return the old plane's distance.
        Mesh ordered = Triangle(Vector3.Zero,
            [new MorphTarget(UniformDelta(new(0f, 0f, 16_777_216f))),
             new MorphTarget(UniformDelta(new(0f, 0f, -16_777_216f))),
             new MorphTarget(UniformDelta(new(0f, 0f, .25f)))]);
        ordered.MorphWeights = [1f, 1f, 1f];
        fixture.Scene.Remove(mesh);
        fixture.Scene.Add(ordered);
        VerifyKnownHit(fixture, ordered, new(0f, 0f, .25f), 0,
            "FP32 morph sums retain authored target order", failures);
    }

    private static void VerifyClosestOrdering(ICollection<string> failures)
    {
        Fixture fixture = new("morphed closest triangles");
        Vector3[] positions = [.. TrianglePositions(new(0f, 0f, 1f)), .. TrianglePositions(new(0f, 0f, -1f))];
        Vector3[] deltas = [new(0f, 0f, -2f), new(0f, 0f, -2f), new(0f, 0f, -2f),
            new(0f, 0f, 3f), new(0f, 0f, 3f), new(0f, 0f, 3f)];
        Mesh changed = new(new MeshGeometry(positions, [0u, 1u, 2u, 3u, 4u, 5u],
            morphTargets: [new MorphTarget(deltas)]), Material);
        changed.MorphWeights = [1f];
        Mesh other = Triangle(new(0f, 0f, 1f));
        fixture.Scene.Add(other);
        fixture.Scene.Add(changed);
        Vector2 pixel = Project(fixture.Camera, new(0f, 0f, 2f));
        IReadOnlyList<SceneRaycastHit> hits = fixture.Raycaster.HitTest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, pixel);
        Expect(hits.Count == 2 && ReferenceEquals(hits[0].Mesh, changed) && hits[0].TriangleIndex == 1 &&
            MathF.Abs(hits[0].Distance - 3f) < PositionTolerance && ReferenceEquals(hits[1].Mesh, other) &&
            MathF.Abs(hits[1].Distance - 4f) < PositionTolerance,
            "ordered raycast returns the current closest triangle per mesh in camera-distance order", failures);
        VerifyKnownHit(fixture, changed, new(0f, 0f, 2f), 1,
            "closest and ordered raycast agree after morph changes triangle ranking", failures, expectedHitCount: 2);
        Predicate<Mesh> otherOnly = candidate => ReferenceEquals(candidate, other);
        Expect(fixture.Raycaster.TryHitClosest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, pixel, out SceneRaycastIntersection filtered, otherOnly) &&
            ReferenceEquals(filtered.Mesh, other) && filtered.TriangleIndex == 0 &&
            MathF.Abs(filtered.Distance - 4f) < PositionTolerance,
            "cached application filter excludes the closer morphed mesh before intersection", failures);
    }

    private static void VerifyEligibility(ICollection<string> failures)
    {
        Fixture fixture = new("morph eligibility");
        SceneNode parent = new("masked ancestor") { VisibilityMask = SceneVisibilityMask.FromLayer(1) };
        Mesh mesh = Triangle(Vector3.Zero, [new MorphTarget(UniformDelta(new(1f, 0f, .25f)))]);
        mesh.MorphWeights = [1f];
        parent.AddChild(mesh);
        fixture.Scene.Add(parent);
        Vector3 local = new(1f, 0f, .25f);
        Vector2 pixel = Project(fixture.Camera, local);
        VerifyKnownHit(fixture, mesh, local, 0, "matching morph mesh is reachable below a masked parent", failures);
        fixture.Scene.Root.IsVisible = false;
        fixture.Scene.Root.VisibilityMask = SceneVisibilityMask.None;
        VerifyKnownHit(fixture, mesh, local, 0, "morph raycasts ignore permanent-root flags", failures);
        parent.IsVisible = false;
        VerifyMiss(fixture, pixel, "hidden ancestor suppresses morph picking", failures);
        parent.IsVisible = true;
        mesh.IsVisible = false;
        VerifyMiss(fixture, pixel, "hidden mesh suppresses morph picking", failures);
        mesh.IsVisible = true;
        mesh.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        VerifyMiss(fixture, pixel, "own mesh layer mismatch suppresses morph picking", failures);
        fixture.Camera.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        parent.VisibilityMask = SceneVisibilityMask.Default;
        VerifyKnownHit(fixture, mesh, local, 0, "camera mask restores morph picking independently of parent masks", failures);
        parent.RemoveChild(mesh);
        VerifyMiss(fixture, pixel, "detached morphed mesh is outside scene picking", failures);
        Scene foreign = new("foreign morph scene");
        foreign.Add(mesh);
        VerifyMiss(fixture, pixel, "foreign-scene morphed mesh is outside scene picking", failures);
        parent.AddChild(mesh);
        VerifyKnownHit(fixture, mesh, local, 0, "reattachment restores current morphed mesh hits", failures);
        fixture.Camera.VisibilityMask = SceneVisibilityMask.None;
        VerifyMiss(fixture, pixel, "empty camera mask suppresses morph picking", failures);
        fixture.Camera.VisibilityMask = SceneVisibilityMask.FromLayer(1);

        int filterCalls = 0;
        Predicate<Mesh> reject = candidate => { filterCalls++; return false; };
        Expect(!fixture.Raycaster.TryHitClosest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, pixel, out _, reject) && filterCalls == 1,
            "application filters can refuse an otherwise eligible morph target", failures);
        mesh.IsVisible = false;
        _ = fixture.Raycaster.TryHitClosest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, pixel, out _, reject);
        mesh.IsVisible = true;
        mesh.VisibilityMask = SceneVisibilityMask.None;
        _ = fixture.Raycaster.TryHitClosest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, pixel, out _, reject);
        Expect(filterCalls == 1, "visibility and camera masks run before application filtering and morph evaluation", failures);
    }

    private static void VerifyClipInterval(ICollection<string> failures)
    {
        Fixture fixture = new("morphed clip interval");
        fixture.Camera.NearClip = 1f;
        fixture.Camera.FarClip = 2f;
        // Central projection uses exactly representable depth coefficients for this 1..2 range.
        Mesh mesh = Triangle(Vector3.Zero, [new MorphTarget(UniformDelta(Vector3.UnitZ))]);
        fixture.Scene.Add(mesh);
        foreach (float z in new[] { 4f, 3.5f, 3f })
        {
            mesh.MorphWeights = [z];
            VerifyKnownHit(fixture, mesh, new(0f, 0f, z), 0,
                "current morphed triangles hit at inclusive near/far interval boundaries", failures);
        }
        mesh.MorphWeights = [4.001f];
        VerifyMiss(fixture, new(ViewportSize / 2f), "morphed triangle before near clip is excluded", failures);
        mesh.MorphWeights = [2.999f];
        VerifyMiss(fixture, new(ViewportSize / 2f), "morphed triangle beyond far clip is excluded", failures);
        mesh.MorphWeights = [3.5f];
        VerifyKnownHit(fixture, mesh, new(0f, 0f, 3.5f), 0,
            "morph picking recovers after clip-interval misses", failures);
    }

    private static void VerifyOverflowRecovery(ICollection<string> failures)
    {
        Fixture fixture = new("non-finite morph triangle recovery");
        Vector3[] positions = [.. TrianglePositions(new(0f, 0f, 1f)), .. TrianglePositions(Vector3.Zero)];
        Vector3[] deltas = [new(float.MaxValue, 0f, 0f), new(float.MaxValue, 0f, 0f), new(float.MaxValue, 0f, 0f),
            Vector3.Zero, Vector3.Zero, Vector3.Zero];
        Mesh mesh = new(new MeshGeometry(positions, [0u, 1u, 2u, 3u, 4u, 5u],
            morphTargets: [new MorphTarget(deltas)]), Material);
        mesh.MorphWeights = [2f];
        fixture.Scene.Add(mesh);
        VerifyKnownHit(fixture, mesh, Vector3.Zero, 1,
            "overflowed morph triangle is skipped while finite source triangle retains its index", failures);
        mesh.MorphWeights = [0f];
        VerifyKnownHit(fixture, mesh, new(0f, 0f, 1f), 0,
            "zero weights restore finite base geometry after morph overflow", failures);
        mesh.MorphWeights = [2f];
        mesh.Geometry = new MeshGeometry(TrianglePositions(new(.75f, 0f, 0f)), [0u, 1u, 2u]);
        VerifyKnownHit(fixture, mesh, new(.75f, 0f, 0f), 0,
            "geometry replacement recovers without retaining invalid morph positions", failures);
    }

    private static void VerifyAllocation(ICollection<string> failures)
    {
        Fixture fixture = new("warmed active morph picking");
        uint[] indices = new uint[128 * 3];
        for (int index = 0; index < indices.Length; index++) indices[index] = (uint)(index % 3);
        MeshGeometry geometry = new(TrianglePositions(Vector3.Zero), indices,
            morphTargets: [new MorphTarget(UniformDelta(new(.75f, 0f, .25f))),
                new MorphTarget(UniformDelta(new(0f, .25f, -.25f)))]);
        Mesh mesh = new(geometry, Material);
        mesh.MorphWeights = [.5f, 1f];
        mesh.Transform.Scale = new(-1.25f, .75f, 1.1f);
        SceneNode parent = new("warmed transformed ancestor");
        parent.Transform.Position = new(.2f, -.1f, 0f);
        parent.AddChild(mesh);
        fixture.Scene.Add(parent);
        Mesh excluded = new(geometry, Material);
        excluded.MorphWeights = [1f, 1f];
        fixture.Scene.Add(excluded);
        Vector3 local = new(.375f, .25f, -.125f);
        Vector2 pixel = Project(fixture.Camera, Vector3.Transform(local, mesh.WorldMatrix));
        Predicate<Mesh> filter = candidate => ReferenceEquals(candidate, mesh);
        for (int sample = 0; sample < 16; sample++)
            _ = fixture.Raycaster.TryHitClosest(fixture.Scene, fixture.Camera,
                ViewportSize, ViewportSize, pixel, out _, filter);
        bool allHit = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int sample = 0; sample < AllocationSamples; sample++)
        {
            allHit &= fixture.Raycaster.TryHitClosest(fixture.Scene, fixture.Camera,
                ViewportSize, ViewportSize, pixel, out SceneRaycastIntersection hit, filter) &&
                ReferenceEquals(hit.Mesh, mesh) && hit.TriangleIndex == 0;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        WriteEvidence(allocated);
        Expect(allHit && allocated == 0,
            $"warmed active-morph closest picking allocates zero managed bytes ({allocated} for {AllocationSamples} samples)", failures);
        VerifyKnownHit(fixture, mesh, local, 0,
            "warmed morph picking preserves live local/world/distance evidence", failures);
    }

    private static void VerifyCollapsedAndGeneralTargets(ICollection<string> failures)
    {
        Fixture fixture = new("collapsed and general morph target picking");
        Vector3[] positions = TrianglePositions(Vector3.Zero);
        Mesh mesh = new(new MeshGeometry(positions, [0u, 1u, 2u],
            morphTargets: [new MorphTarget(positions.Select(position => -position))]), Material);
        fixture.Scene.Add(mesh);
        mesh.MorphWeights = [1f];
        VerifyMiss(fixture, new(ViewportSize / 2f), "morph-collapsed triangles have no geometric hit", failures);
        mesh.MorphWeights = [0f];
        VerifyKnownHit(fixture, mesh, Vector3.Zero, 0, "zero weights recover collapsed base triangles", failures);

        MorphTarget[] targets = new MorphTarget[9];
        for (int index = 0; index < targets.Length; index++)
            targets[index] = new MorphTarget(UniformDelta(index == 8 ? Vector3.UnitX : Vector3.Zero));
        mesh.Geometry = new MeshGeometry(positions, [0u, 1u, 2u], morphTargets: targets);
        mesh.MorphWeights = Enumerable.Repeat(1f, targets.Length).ToArray();
        VerifyKnownHit(fixture, mesh, Vector3.UnitX, 0,
            "general CPU picking evaluates targets beyond the raster renderer's separate eight-target cap", failures);
        VerifyMiss(fixture, new(ViewportSize / 2f), "ninth morph target is not silently clamped out of picking", failures);
    }

    private static void VerifyKnownHit(Fixture fixture, Mesh mesh, Vector3 currentLocal, int triangleIndex,
        string description, ICollection<string> failures, int expectedHitCount = 1)
    {
        MeshGeometry geometry = mesh.Geometry;
        IReadOnlyList<float> weights = mesh.MorphWeights;
        Vector3 expectedWorld = Vector3.Transform(currentLocal, mesh.WorldMatrix);
        float expectedDistance = Vector3.Distance(Vector3.Transform(Vector3.Zero, fixture.Camera.WorldMatrix), expectedWorld);
        Vector2 pixel = Project(fixture.Camera, expectedWorld);
        IReadOnlyList<SceneRaycastHit> hits = fixture.Raycaster.HitTest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, pixel);
        Expect(hits.Count == expectedHitCount, description + " returns the expected mesh candidate count", failures);
        if (hits.Count > 0)
        {
            SceneRaycastHit ordered = hits[0];
            Expect(ReferenceEquals(ordered.Mesh, mesh) && ordered.TriangleIndex == triangleIndex &&
                Near(ordered.LocalPosition, currentLocal) && Near(ordered.WorldPosition, expectedWorld) &&
                MathF.Abs(ordered.Distance - expectedDistance) < PositionTolerance,
                description + " records ordered triangle/world/current-local/distance values", failures);
        }
        bool found = fixture.Raycaster.TryHitClosest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, pixel, out SceneRaycastIntersection closest);
        Expect(found && ReferenceEquals(closest.Mesh, mesh) && closest.TriangleIndex == triangleIndex &&
            Near(closest.LocalPosition, currentLocal) && Near(closest.WorldPosition, expectedWorld) &&
            MathF.Abs(closest.Distance - expectedDistance) < PositionTolerance,
            description + " records matching closest triangle/world/current-local/distance values", failures);
        Expect(ReferenceEquals(mesh.Geometry, geometry) && ReferenceEquals(mesh.MorphWeights, weights),
            description + " preserves borrowed geometry and current weight ownership", failures);
    }

    private static void VerifyMiss(Fixture fixture, Vector2 pixel, string description, ICollection<string> failures)
    {
        Expect(fixture.Raycaster.HitTest(fixture.Scene, fixture.Camera,
            ViewportSize, ViewportSize, pixel).Count == 0 &&
            !fixture.Raycaster.TryHitClosest(fixture.Scene, fixture.Camera,
                ViewportSize, ViewportSize, pixel, out _), description + " in both raycast APIs", failures);
    }

    private static Vector2 Project(PerspectiveCamera camera, Vector3 world)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), camera.ViewProjectionMatrix);
        return new((clip.X / clip.W + 1f) * ViewportSize * .5f,
            (1f - clip.Y / clip.W) * ViewportSize * .5f);
    }

    private static Mesh Triangle(Vector3 center, MorphTarget[]? morphTargets = null) => new(
        new MeshGeometry(TrianglePositions(center), [0u, 1u, 2u], morphTargets: morphTargets), Material);

    private static Vector3[] TrianglePositions(Vector3 center) =>
        [center + new Vector3(-.5f, -.5f, 0f), center + new Vector3(.5f, -.5f, 0f), center + new Vector3(0f, .5f, 0f)];

    private static Vector3[] UniformDelta(Vector3 delta) => [delta, delta, delta];

    private static bool Near(Vector3 actual, Vector3 expected) => Vector3.Distance(actual, expected) < PositionTolerance;

    private static void WriteEvidence(long? allocatedBytes)
    {
        string? path = Environment.GetEnvironmentVariable("MU3D_MORPH_RAYCAST_EVIDENCE");
        if (string.IsNullOrEmpty(path) || gapEvidence is not GapEvidence gap) return;
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Runtime = Environment.Version.ToString(),
            Backend = "portable SceneRaycaster; no browser input or GPU execution",
            Displacement = new[] { 1.5f, 0f, 0f },
            OldPixel = new[] { gap.OldPixel.X, gap.OldPixel.Y },
            NewPixel = new[] { gap.NewPixel.X, gap.NewPixel.Y },
            OldPixelOrderedHitCount = gap.OldHits,
            NewPixelOrderedHitCount = gap.NewHits,
            OldPixelClosestHit = gap.OldClosest,
            NewPixelClosestHit = gap.NewClosest,
            AllocationSamples,
            SourceMeshes = 2,
            SourceTrianglesPerMesh = 128,
            ActiveMorphTargetsPerMesh = 2,
            AllocationIncludesCachedFilter = true,
            WarmClosestTotalBytes = allocatedBytes,
            WarmClosestBytesPerSample = allocatedBytes / (double?)AllocationSamples,
            AllocationExcludesWarmupAndEvidenceSerialization = true,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Expect(bool condition, string description, ICollection<string> failures)
    {
        if (!condition) failures.Add(description);
    }

    private readonly record struct GapEvidence(Vector2 OldPixel, Vector2 NewPixel,
        int OldHits, int NewHits, bool OldClosest, bool NewClosest);

    private sealed class Fixture
    {
        internal Fixture(string name)
        {
            Scene = new(name);
            Camera.Transform.Position = new(0f, 0f, 5f);
        }

        internal Scene Scene { get; }
        internal PerspectiveCamera Camera { get; } = new(aspectRatio: 1f);
        internal SceneRaycaster Raycaster { get; } = new();
    }
}

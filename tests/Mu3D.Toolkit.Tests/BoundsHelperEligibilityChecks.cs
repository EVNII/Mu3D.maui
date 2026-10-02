using System.Numerics;
using System.Text.Json;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Helpers;
using Mu3D.Toolkit.Rendering;
using static HelperRenderChecks;
using Checks = HelperRenderChecks;

internal static class BoundsHelperEligibilityChecks
{
    private const int VertexStride = 28;
    private const int MaximumVertices = 72;
    private const int FrameCount = 64;
    private const int AllocationBudget = 2048;
    private static readonly UnlitMaterial Material = new(
        new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb));
    private static readonly BoundsHelperRenderStyle Overlay = new(
        BoundsHelperRenderStyle.Default.Color, depthMode: BoundsHelperDepthMode.Overlay);

    internal static void Validate(ICollection<string> failures)
    {
        foreach (bool precise in new[] { true, false })
        {
            VerifyEligibility(precise, failures);
            VerifyCurrentGeometryAndRecovery(precise, failures);
            VerifyLooseQuery(precise, failures);
        }
        VerifyValidationBeforeEmpty(failures);
        VerifyAllocation(failures);
    }

    private static void VerifyEligibility(bool precise, ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("bounds eligibility", 256, 256, hasDepth: true);
        SceneNode parent = new("ordinary ancestor") { VisibilityMask = SceneVisibilityMask.FromLayer(1) };
        SceneNode group = new("selected group") { VisibilityMask = SceneVisibilityMask.FromLayer(1) };
        Mesh matching = Box(new(-.25f), new(.25f));
        // The masked mesh's own positions are distant; its independently layered child remains
        // near the origin. Filtering must affect its contribution without pruning the child.
        Mesh excluded = Box(new(99.75f, -.25f, -.25f), new(100.25f, .25f, .25f));
        excluded.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        Mesh independent = Box(new(-.25f), new(.25f));
        independent.Transform.Position = new(1f, 0f, 0f);
        excluded.AddChild(independent);
        Mesh hidden = Box(new(-.25f), new(.25f));
        hidden.Transform.Position = new(-1.5f, 0f, 0f);
        hidden.IsVisible = false;
        group.AddChild(matching);
        group.AddChild(excluded);
        group.AddChild(hidden);
        parent.AddChild(group);
        fixture.Scene.Add(parent);
        BoundsHelper helper = new() { Target = group, Precise = precise };
        using BoundsHelperRenderPass pass = new(helper, Overlay, StandardColorSpaces.LinearSrgb);
        Bounds3D visible = new(new(-.25f), new(1.25f, .25f, .25f));
        Bounds3D withHidden = new(new(-1.75f, -.25f, -.25f), visible.Maximum);

        DrawExpected(fixture, pass, visible, "masked mesh extrema are excluded while its matching child contributes", failures);
        Checks.Expect(helper.TryGetWorldBounds(out Bounds3D loose) && loose.Maximum.X == 100.25f,
            "public bounds query remains independent of the rendering camera mask", failures);
        parent.IsVisible = false;
        DrawExpected(fixture, pass, null, "hidden ancestor suppresses selected bounds", failures);
        helper.IncludeInvisible = true;
        DrawExpected(fixture, pass, withHidden, "invisible inclusion bypasses ancestor and child hiding only", failures);
        Checks.Expect(!parent.IsVisible && !hidden.IsVisible, "bounds never mutate borrowed visibility flags", failures);
        parent.IsVisible = true;
        helper.IncludeInvisible = false;
        group.IsVisible = false;
        DrawExpected(fixture, pass, null, "hidden selected group suppresses bounds", failures);
        helper.IncludeInvisible = true;
        DrawExpected(fixture, pass, withHidden, "invisible inclusion restores a hidden target with filtered extrema", failures);
        group.IsVisible = true;
        helper.IncludeInvisible = false;

        fixture.Scene.Root.IsVisible = false;
        fixture.Scene.Root.VisibilityMask = SceneVisibilityMask.None;
        DrawExpected(fixture, pass, visible, "ordinary bounds ignore permanent-root flags", failures);
        parent.RemoveChild(group);
        DrawExpected(fixture, pass, null, "detached selected bounds are suppressed", failures);
        helper.IncludeInvisible = true;
        DrawExpected(fixture, pass, null, "invisible inclusion never bypasses scene membership", failures);
        Scene foreign = new("foreign scene");
        foreign.Add(group);
        DrawExpected(fixture, pass, null, "foreign-scene selected bounds are suppressed", failures);
        parent.AddChild(group);
        helper.IncludeInvisible = false;
        DrawExpected(fixture, pass, visible, "reattachment restores the retained bounds selection", failures);

        helper.Target = matching;
        matching.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        helper.IncludeInvisible = true;
        DrawExpected(fixture, pass, null, "invisible inclusion never bypasses a selected mesh mask", failures);
        fixture.Camera.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        parent.VisibilityMask = SceneVisibilityMask.Default;
        DrawExpected(fixture, pass, new Bounds3D(new(-.25f), new(.25f)),
            "camera layer changes restore the selected mesh independently of parent layers", failures);
        matching.VisibilityMask = SceneVisibilityMask.Default;
        fixture.Camera.VisibilityMask = SceneVisibilityMask.Default;
        helper.IncludeInvisible = false;
        helper.Target = fixture.Scene.Root;
        DrawExpected(fixture, pass, visible, "permanent root anchors bounds despite its visibility and mask flags", failures);
        helper.IncludeDescendants = false;
        DrawExpected(fixture, pass, null, "root bounds without descendants have no contributing geometry", failures);
        helper.Target = matching;
        DrawExpected(fixture, pass, new Bounds3D(new(-.25f), new(.25f)),
            "a mesh still contributes when descendant inclusion is disabled", failures);
        helper.Target = group;
        DrawExpected(fixture, pass, null, "a geometry-free group without descendants produces no bounds", failures);
        helper.IncludeDescendants = true;
        fixture.Camera.VisibilityMask = SceneVisibilityMask.None;
        DrawExpected(fixture, pass, null, "empty camera mask suppresses every bounds contribution", failures);
        fixture.Camera.VisibilityMask = SceneVisibilityMask.Default;
        helper.Target = null;
        DrawExpected(fixture, pass, null, "null bounds target submits no retained vertices", failures);

        // A masked mesh must be skipped before evaluating world bounds. Its finite local data
        // and transform produce non-finite world positions, so evaluating it would throw.
        Mesh invalidMasked = Box(new(-2f), new(2f));
        invalidMasked.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        invalidMasked.Transform.Scale = new(float.MaxValue);
        group.AddChild(invalidMasked);
        helper.Target = group;
        DrawExpected(fixture, pass, visible, "masked non-finite world geometry is filtered before bounds evaluation", failures);
    }

    private static void VerifyLooseQuery(bool precise, ICollection<string> failures)
    {
        Scene scene = new("explicit loose bounds");
        SceneNode parent = new("hidden ancestor") { IsVisible = false };
        Mesh target = Box(new(-.25f), new(.25f));
        target.VisibilityMask = SceneVisibilityMask.None;
        parent.AddChild(target);
        scene.Add(parent);
        BoundsHelper helper = new() { Target = target, Precise = precise };
        Bounds3D expected = new(new(-.25f), new(.25f));
        Checks.Expect(helper.TryGetWorldBounds(out Bounds3D attached) && attached == expected,
            "explicit bounds ignore hidden ancestors outside the selected subtree and all masks", failures);
        parent.RemoveChild(target);
        Checks.Expect(helper.TryGetWorldBounds(out Bounds3D detached) && detached == expected,
            "explicit bounds remain valid for a detached target", failures);
        Scene foreign = new("explicit foreign bounds");
        foreign.Add(target);
        Checks.Expect(helper.TryGetWorldBounds(out Bounds3D moved) && moved == expected,
            "explicit bounds remain valid after moving the target to another scene", failures);
        target.IsVisible = false;
        Checks.Expect(!helper.TryGetWorldBounds(out _), "explicit bounds still evaluate visibility inside the target subtree", failures);
        helper.IncludeInvisible = true;
        Checks.Expect(helper.TryGetWorldBounds(out Bounds3D invisible) && invisible == expected && ReferenceEquals(helper.Target, target),
            "explicit invisible bounds preserve the scene-independent borrowed target", failures);
    }

    private static void VerifyCurrentGeometryAndRecovery(bool precise, ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("bounds staging recovery", 256, 256, hasDepth: true);
        Vector3[] positions = [new(-.75f, -.25f, -.5f), new(.75f, -.25f, .5f), new(0f, .75f, 0f)];
        Vector3[] deltas = [new(.1f, 0f, .2f), new(-.2f, .15f, -.1f), new(0f, .2f, .3f)];
        Mesh target = new(new MeshGeometry(positions, [0u, 1u, 2u],
            morphTargets: [new MorphTarget(deltas)]), Material);
        Mesh flat = Box(new(-.25f, -.25f, 0f), new(.25f, .25f, 0f));
        Mesh invalid = Box(new(-2f), new(2f));
        invalid.Transform.Scale = new(float.MaxValue);
        fixture.Scene.Add(target);
        fixture.Scene.Add(flat);
        fixture.Scene.Add(invalid);
        BoundsHelper helper = new() { Target = target, Precise = precise };
        using BoundsHelperRenderPass pass = new(helper, Overlay, StandardColorSpaces.LinearSrgb);
        DrawExpected(fixture, pass, CurrentBounds(target, precise, 0f), "bounds stage original rigid geometry", failures);
        byte[] original = ReadGeometry(fixture.Device);
        target.MorphWeights = [.5f];
        target.Transform.Position = new(.3f, -.2f, .1f);
        target.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * .25f);
        target.Transform.Scale = new(1.2f, .8f, 1.1f);
        helper.Padding = .1f;
        DrawExpected(fixture, pass, CurrentBounds(target, precise, helper.Padding),
            "bounds upload current morph, rigid pose and padding extrema", failures);
        byte[] deformed = ReadGeometry(fixture.Device);
        Checks.Expect(!deformed.AsSpan().SequenceEqual(original), "current bounds mutations replace the original upload", failures);
        fixture.Camera.Transform.Position = new(.4f, .1f, 5f);
        DrawExpected(fixture, pass, CurrentBounds(target, precise, helper.Padding),
            "bounds projection reads the current camera pose", failures);
        Checks.Expect(!ReadGeometry(fixture.Device).AsSpan().SequenceEqual(deformed),
            "moving the camera changes bounds clip bytes", failures);
        fixture.Camera.Transform.Position = new(0f, 0f, 5f);
        target.MorphWeights = [0f];
        target.Transform.Position = Vector3.Zero;
        target.Transform.Rotation = Quaternion.Identity;
        target.Transform.Scale = Vector3.One;
        helper.Padding = 0f;
        DrawExpected(fixture, pass, CurrentBounds(target, precise, 0f),
            "zero morph weights restore cached base bounds", failures);
        Checks.Expect(ReadGeometry(fixture.Device).AsSpan().SequenceEqual(original),
            "restoring morph, pose, camera and padding restores original bounds bytes", failures);

        target.Geometry = new MeshGeometry([new Vector3(-.1f), new Vector3(.1f), new Vector3(0f)], [0u, 1u, 2u]);
        DrawExpected(fixture, pass, new Bounds3D(new(-.1f), new(.1f)),
            "geometry replacement refreshes cached conservative and precise bounds", failures);
        helper.Target = flat;
        Bounds3D flatBounds = new(new(-.25f, -.25f, 0f), new(.25f, .25f, 0f));
        DrawExpected(fixture, pass, flatBounds, "retargeting to a flat box excludes previous larger staging", failures);
        byte[] flatBytes = ReadGeometry(fixture.Device);
        Checks.Expect(original.Length == MaximumVertices * VertexStride && flatBytes.Length == 48 * VertexStride,
            "bounds staging shrinks from twelve projected edges to eight planar edges", failures);
        helper.Target = null;
        DrawExpected(fixture, pass, null, "targetless frame submits no retained box", failures);
        helper.Target = flat;
        flat.IsVisible = false;
        DrawExpected(fixture, pass, null, "frame with no visible bounds submits no retained box", failures);
        flat.IsVisible = true;
        flat.Transform.Position = new(0f, 0f, 20f);
        DrawExpected(fixture, pass, null, "clip-empty bounds behind the camera submit no retained box", failures);
        flat.Transform.Position = Vector3.Zero;
        DrawExpected(fixture, pass, flatBounds, "bounds recover from targetless, invisible and clipped frames", failures);
        Checks.Expect(ReadGeometry(fixture.Device).AsSpan().SequenceEqual(flatBytes), "empty frames never contaminate recovered bounds bytes", failures);

        helper.Target = target;
        VerifyUploadFailure(pass, fixture.Context, fixture.Device, "bounds upload failure", failures);
        helper.Target = flat;
        DrawExpected(fixture, pass, flatBounds, "bounds recover after a failed upload", failures);
        Checks.Expect(ReadGeometry(fixture.Device).AsSpan().SequenceEqual(flatBytes), "upload recovery excludes retained larger vertices", failures);
        helper.Target = invalid;
        int before = fixture.Device.SubmittedPipelineLabels.Count;
        Checks.ExpectThrows(() => pass.Execute(fixture.Context), error => precise
                ? error is InvalidOperationException
                : error is ArgumentOutOfRangeException,
            "non-finite world bounds fail explicitly", failures);
        Checks.Expect(fixture.Device.SubmittedPipelineLabels.Count == before, "invalid world bounds submit no partial commands", failures);
        helper.Target = flat;
        DrawExpected(fixture, pass, flatBounds, "bounds recover after non-finite geometry evaluation", failures);
        Checks.Expect(ReadGeometry(fixture.Device).AsSpan().SequenceEqual(flatBytes) && fixture.Device.PipelineDescriptors.Count == 1,
            "bounds recovery preserves current bytes and reuses the existing pipeline", failures);
    }

    private static void VerifyValidationBeforeEmpty(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("empty bounds validation", 256, 256, hasDepth: true);
        BoundsHelper helper = new();
        using BoundsHelperRenderPass overlay = new(helper, Overlay, StandardColorSpaces.LinearSrgb);
        using BoundsHelperRenderPass depth = new(helper);
        RenderPassContext noColor = new(fixture.Scene, fixture.Camera, fixture.Color, fixture.Depth,
            StandardColorSpaces.LinearSrgb, depthTargetInitialized: true);
        RenderPassContext noDepth = new(fixture.Scene, fixture.Camera, fixture.Color, fixture.Depth,
            StandardColorSpaces.LinearSrgb, colorTargetInitialized: true);
        RenderPassContext absentDepth = new(fixture.Scene, fixture.Camera, fixture.Color, null,
            StandardColorSpaces.LinearSrgb, colorTargetInitialized: true);
        RenderPassContext nonPerspective = new(fixture.Scene, new NonPerspectiveCamera(), fixture.Color, null,
            StandardColorSpaces.LinearSrgb, colorTargetInitialized: true);
        Checks.ExpectThrows(() => overlay.Execute(noColor), error => error is InvalidOperationException,
            "empty bounds do not bypass initialized-color validation", failures);
        Checks.ExpectThrows(() => depth.Execute(noDepth), error => error is InvalidOperationException,
            "empty bounds do not bypass initialized-depth validation", failures);
        Checks.ExpectThrows(() => depth.Execute(absentDepth), error => error is InvalidOperationException,
            "empty bounds do not bypass required-depth-attachment validation", failures);
        Checks.ExpectThrows(() => overlay.Execute(nonPerspective), error => error is InvalidOperationException,
            "empty bounds do not bypass perspective-camera validation", failures);
        Checks.Expect(fixture.Device.SubmittedPipelineLabels.Count == 0 && fixture.Device.PipelineDescriptors.Count == 0,
            "invalid empty bounds frames create no render resources", failures);
    }

    // This entry point is also compiled against the retained pre-change package. Keeping it
    // independent from eligibility checks makes allocation comparisons use exactly one fixture.
    internal static void VerifyAllocation(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("bounds warmed allocation", 256, 256, hasDepth: true);
        MeshGeometry geometry = BoxGeometry(new(-.25f), new(.25f));
        SceneNode dense = new("sixty-four actual source meshes");
        for (int index = 0; index < 64; index++)
        {
            Mesh mesh = new(geometry, Material);
            mesh.Transform.Position = new((index % 8 - 3.5f) * .04f, (index / 8 - 3.5f) * .04f, 0f);
            dense.AddChild(mesh);
        }
        Mesh single = new(geometry, Material);
        fixture.Scene.Add(dense);
        fixture.Scene.Add(single);
        BoundsHelper helper = new() { Target = dense };
        using BoundsHelperRenderPass pass = new(helper, Overlay, StandardColorSpaces.LinearSrgb);
        RenderPassContext context = fixture.Context;
        pass.Execute(context);
        byte[] denseOriginal = ReadGeometry(fixture.Device);
        // Exclude readback copies and history capacity growth; recording command wrappers and
        // closures stay included, so this is not a claim about browser/native GPU allocation.
        fixture.Device.SubmittedPipelineLabels.EnsureCapacity(4096);
        long densePrecise = Measure(pass, context, FrameCount);
        bool preciseBytesMatch = ReadGeometry(fixture.Device).AsSpan().SequenceEqual(denseOriginal);
        helper.Precise = false;
        long denseConservative = Measure(pass, context, FrameCount);
        bool conservativeBytesMatch = ReadGeometry(fixture.Device).AsSpan().SequenceEqual(denseOriginal);
        helper.Precise = true;
        helper.Target = single;
        pass.Execute(context);
        byte[] singleOriginal = ReadGeometry(fixture.Device);
        long singlePrecise = Measure(pass, context, FrameCount);
        bool singleBytesMatch = ReadGeometry(fixture.Device).AsSpan().SequenceEqual(singleOriginal);

        // Write comparable measurements before asserting the new budget: the retained package
        // is expected to fail that assertion but still supplies the same-fixture baseline JSON.
        string? evidencePath = Environment.GetEnvironmentVariable("MU3D_BOUNDS_ALLOCATION_EVIDENCE");
        if (!string.IsNullOrEmpty(evidencePath))
        {
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Runtime = Environment.Version.ToString(),
                Backend = "portable RecordingGraphicsDevice; not browser/native GPU",
                FramesPerCase = FrameCount,
                DenseSourceMeshes = 64,
                SingleSourceMeshes = 1,
                SourceVerticesPerMesh = 8,
                MaximumOutputVertices = MaximumVertices,
                DensePreciseTotalBytes = densePrecise,
                DensePreciseBytesPerFrame = densePrecise / (double)FrameCount,
                DenseConservativeTotalBytes = denseConservative,
                DenseConservativeBytesPerFrame = denseConservative / (double)FrameCount,
                SinglePreciseTotalBytes = singlePrecise,
                SinglePreciseBytesPerFrame = singlePrecise / (double)FrameCount,
                DenseVertexBytes = denseOriginal.Length,
                SingleVertexBytes = singleOriginal.Length,
                BudgetBytesPerFrame = AllocationBudget,
                IncludesRecordingCommands = true,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Checks.Expect(preciseBytesMatch && conservativeBytesMatch && singleBytesMatch &&
            denseOriginal.Length == MaximumVertices * VertexStride && singleOriginal.Length == MaximumVertices * VertexStride &&
            fixture.Device.PipelineDescriptors.Count == 1,
            "warmed bounds keep current geometry, bounded uploads and one pipeline across both algorithms", failures);
        Checks.Expect(densePrecise < FrameCount * AllocationBudget && denseConservative < FrameCount * AllocationBudget &&
            singlePrecise < FrameCount * AllocationBudget,
            "warmed bounds allocation stays below 2 KiB/frame with recording commands included " +
            $"(dense precise {densePrecise / FrameCount}, conservative {denseConservative / FrameCount}, single {singlePrecise / FrameCount})",
            failures);
    }

    private static Bounds3D CurrentBounds(Mesh mesh, bool precise, float padding)
    {
        Vector3[] current = new Vector3[mesh.Geometry.Positions.Count];
        for (int index = 0; index < current.Length; index++)
        {
            current[index] = mesh.Geometry.Positions[index];
            for (int morph = 0; morph < mesh.Geometry.MorphTargets.Count; morph++)
                current[index] += mesh.Geometry.MorphTargets[morph].PositionDeltas[index] * mesh.MorphWeights[morph];
        }
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        if (precise)
        {
            foreach (Vector3 point in current)
            {
                Vector3 world = Vector3.Transform(point, mesh.WorldMatrix);
                minimum = Vector3.Min(minimum, world);
                maximum = Vector3.Max(maximum, world);
            }
        }
        else
        {
            Vector3 localMin = current.Aggregate(Vector3.Min);
            Vector3 localMax = current.Aggregate(Vector3.Max);
            foreach (Vector3 corner in Corners(localMin, localMax))
            {
                Vector3 world = Vector3.Transform(corner, mesh.WorldMatrix);
                minimum = Vector3.Min(minimum, world);
                maximum = Vector3.Max(maximum, world);
            }
        }
        return new Bounds3D(minimum - new Vector3(padding), maximum + new Vector3(padding));
    }

    private static void DrawExpected(HelperRenderFixture fixture, BoundsHelperRenderPass pass, Bounds3D? expected,
        string description, ICollection<string> failures)
    {
        SceneNode? target = pass.Helper.Target;
        int before = fixture.Device.SubmittedPipelineLabels.Count;
        pass.Execute(fixture.Context);
        Checks.Expect(fixture.Device.SubmittedPipelineLabels.Count == before + (expected.HasValue ? 1 : 0), description, failures);
        Checks.Expect(ReferenceEquals(pass.Helper.Target, target), description + " retains the application selection", failures);
        if (expected is not Bounds3D bounds) return;
        if (fixture.Device.SubmittedPipelineLabels.Count != before + 1 || fixture.Device.Buffers.Count == 0) return;
        // The reference box contains independently known extrema, not the selected geometry.
        // Comparing complete public-pass uploads detects a wrongly expanded filtered AABB.
        using HelperRenderFixture reference = new("known extrema reference", 256, 256, hasDepth: true);
        Mesh box = Box(bounds.Minimum, bounds.Maximum);
        box.VisibilityMask = fixture.Camera.VisibilityMask;
        reference.Scene.Add(box);
        BoundsHelper helper = new() { Target = box };
        using BoundsHelperRenderPass golden = new(helper, Overlay, StandardColorSpaces.LinearSrgb);
        RenderPassContext context = new(reference.Scene, fixture.Camera, reference.Color, null,
            StandardColorSpaces.LinearSrgb, colorTargetInitialized: true);
        golden.Execute(context);
        byte[] actual = ReadGeometry(fixture.Device);
        Checks.Expect(actual.AsSpan().SequenceEqual(ReadGeometry(reference.Device)),
            description + " uploads exactly the expected filtered world extrema", failures);
        Checks.Expect(actual.Length <= MaximumVertices * VertexStride && BitConverter.ToSingle(actual, 12) > 1f,
            description + " keeps bounded FP32 HDR-linear vertices", failures);
    }

    private static Mesh Box(Vector3 minimum, Vector3 maximum) => new(BoxGeometry(minimum, maximum), Material);

    private static MeshGeometry BoxGeometry(Vector3 minimum, Vector3 maximum) => new(Corners(minimum, maximum), [0u, 1u, 2u]);

    private static Vector3[] Corners(Vector3 minimum, Vector3 maximum)
    {
        Vector3[] corners = new Vector3[8];
        for (int index = 0; index < corners.Length; index++)
            corners[index] = new((index & 1) == 0 ? minimum.X : maximum.X,
                (index & 2) == 0 ? minimum.Y : maximum.Y, (index & 4) == 0 ? minimum.Z : maximum.Z);
        return corners;
    }
}

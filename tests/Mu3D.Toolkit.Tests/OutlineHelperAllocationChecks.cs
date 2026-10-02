using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Helpers;
using Mu3D.Toolkit.Rendering;
using static HelperRenderChecks;
using Checks = HelperRenderChecks;

internal static class OutlineHelperAllocationChecks
{
    private const int DenseTriangleCount = 4096;
    private const int FrameCount = 64;

    internal static void Validate(ICollection<string> failures)
    {
        Vector3[] positions = [new(-1f, -1f, 0f), new(1f, -1f, 0f), new(0f, 1f, 0f)];
        Vector3[] deltas = [new(.2f, 0f, 0f), new(0f, .3f, 0f), new(0f, 0f, .4f)];
        uint[] denseIndices = new uint[DenseTriangleCount * 3];
        for (int index = 0; index < denseIndices.Length; index++) denseIndices[index] = (uint)(index % 3);
        UnlitMaterial material = new(new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb));
        Mesh small = new(new MeshGeometry(positions, [0u, 1u, 2u]), material, "small outline target");
        Mesh dense = new(new MeshGeometry(positions, denseIndices,
            morphTargets: [new MorphTarget(deltas)]), material, "dense morph outline target");
        using HelperRenderFixture fixture = new("outline staging regression", 256, 256);
        Scene scene = fixture.Scene;
        scene.Add(small);
        scene.Add(dense);
        PerspectiveCamera camera = fixture.Camera;
        OutlineHelper helper = new() { Target = dense };
        RecordingGraphicsDevice device = fixture.Device;
        RenderPassContext context = fixture.Context;
        using OutlineHelperRenderPass pass = new(helper,
            new OutlineHelperRenderStyle(OutlineHelperRenderStyle.Default.Color,
                depthMode: OutlineHelperDepthMode.Overlay), StandardColorSpaces.LinearSrgb);

        pass.Execute(context);
        byte[] denseOriginal = ReadGeometry(device);
        Checks.Expect(denseOriginal.Length == DenseTriangleCount * 3 * 16 &&
            MatchesClipGeometry(denseOriginal, dense, camera),
            "dense outline staging preserves every rigid FP32 clip vertex", failures);

        // Retain CPU/GPU capacity and pipeline resources before measuring. Recording command
        // wrappers/closures remain included; history growth and geometry readback copies do not.
        device.SubmittedPipelineLabels.EnsureCapacity(4096);
        long denseBytes = Measure(pass, context, FrameCount);
        Checks.Expect(ReadGeometry(device).AsSpan().SequenceEqual(denseOriginal),
            "warmed dense outline submissions preserve staged geometry", failures);
        helper.Target = small;
        pass.Execute(context);
        byte[] smallOriginal = ReadGeometry(device);
        long smallBytes = Measure(pass, context, FrameCount);
        Checks.Expect(smallOriginal.Length == 3 * 16 &&
            ReadGeometry(device).AsSpan().SequenceEqual(smallOriginal) &&
            MatchesClipGeometry(smallOriginal, small, camera),
            "retargeting a large outline to one triangle excludes prior retained vertices", failures);
        Checks.Expect(denseBytes / FrameCount < 4096 && smallBytes / FrameCount < 4096 &&
            Math.Abs(denseBytes - smallBytes) <= FrameCount * 256,
            "warmed outline allocation stays below 4 KiB/frame and is independent of triangle count " +
            $"with recording commands included (dense {denseBytes / FrameCount}, small {smallBytes / FrameCount})",
            failures);

        helper.Target = dense;
        dense.MorphWeights = [.5f];
        dense.Transform.Position = new(.3f, -.2f, .1f);
        pass.Execute(context);
        byte[] deformed = ReadGeometry(device);
        Checks.Expect(!deformed.AsSpan().SequenceEqual(denseOriginal) &&
            MatchesClipGeometry(deformed, dense, camera),
            "outline staging reevaluates current morph weights and rigid transforms", failures);
        dense.MorphWeights = [0f];
        dense.Transform.Position = Vector3.Zero;
        pass.Execute(context);
        Checks.Expect(ReadGeometry(device).AsSpan().SequenceEqual(denseOriginal),
            "restoring morph and transform state restores all original outline clips", failures);

        int submissions = device.SubmittedPipelineLabels.Count;
        helper.Target = null;
        pass.Execute(context);
        helper.Target = dense;
        camera.VisibilityMask = SceneVisibilityMask.None;
        pass.Execute(context);
        Checks.Expect(device.SubmittedPipelineLabels.Count == submissions,
            "targetless and camera-masked outline frames submit no retained geometry", failures);
        camera.VisibilityMask = SceneVisibilityMask.All;
        helper.Target = small;
        pass.Execute(context);
        Checks.Expect(ReadGeometry(device).AsSpan().SequenceEqual(smallOriginal),
            "outline staging recovers after targetless and masked frames", failures);

        helper.Target = dense;
        VerifyUploadFailure(pass, context, device, "outline upload failure", failures);
        helper.Target = small;
        pass.Execute(context);
        Checks.Expect(ReadGeometry(device).AsSpan().SequenceEqual(smallOriginal),
            "outline staging recovers from a failed large upload without retaining vertices", failures);

        // The first child appends vertices before the second child exceeds the frame limit.
        // The next valid frame must not retain that partial triangle batch.
        helper.Target = scene.Root;
        helper.TriangleLimit = 1;
        submissions = device.SubmittedPipelineLabels.Count;
        Checks.ExpectThrows(() => pass.Execute(context),
            error => error is InvalidOperationException && error.Message.Contains("triangle frame limit", StringComparison.Ordinal),
            "outline staging exercises a partially built triangle-limit failure", failures);
        Checks.Expect(device.SubmittedPipelineLabels.Count == submissions,
            "a partially built over-limit outline submits no geometry", failures);
        helper.TriangleLimit = OutlineHelper.DefaultTriangleLimit;
        helper.Target = small;
        pass.Execute(context);
        Checks.Expect(ReadGeometry(device).AsSpan().SequenceEqual(smallOriginal) && device.PipelineDescriptors.Count == 2,
            "outline staging recovers from triangle-limit failure and reuses both pipelines", failures);

        string? evidencePath = Environment.GetEnvironmentVariable("MU3D_OUTLINE_ALLOCATION_EVIDENCE");
        if (!string.IsNullOrEmpty(evidencePath))
        {
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Runtime = Environment.Version.ToString(),
                Backend = "portable RecordingGraphicsDevice; not browser/native GPU",
                FramesPerTarget = FrameCount,
                DenseTriangles = DenseTriangleCount,
                SmallTriangles = 1,
                DenseTotalBytes = denseBytes,
                DenseBytesPerFrame = denseBytes / (double)FrameCount,
                SmallTotalBytes = smallBytes,
                SmallBytesPerFrame = smallBytes / (double)FrameCount,
                DenseVertexBytes = denseOriginal.Length,
                SmallVertexBytes = smallOriginal.Length,
                BudgetBytesPerFrame = 4096,
                IncludesRecordingCommands = true,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static bool MatchesClipGeometry(byte[] bytes, Mesh mesh, PerspectiveCamera camera)
    {
        ReadOnlySpan<Vector4> actual = MemoryMarshal.Cast<byte, Vector4>(bytes);
        if (actual.Length != mesh.Geometry.Indices.Count) return false;
        Matrix4x4 matrix = mesh.WorldMatrix * camera.ViewProjectionMatrix;
        for (int index = 0; index < actual.Length; index++)
        {
            int sourceIndex = checked((int)mesh.Geometry.Indices[index]);
            Vector3 position = mesh.Geometry.Positions[sourceIndex];
            for (int target = 0; target < mesh.Geometry.MorphTargets.Count; target++)
                position += mesh.Geometry.MorphTargets[target].PositionDeltas[sourceIndex] * mesh.MorphWeights[target];
            if (actual[index] != Vector4.Transform(new Vector4(position, 1f), matrix)) return false;
        }
        return true;
    }
}

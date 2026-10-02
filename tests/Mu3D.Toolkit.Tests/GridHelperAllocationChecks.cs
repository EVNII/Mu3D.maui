using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Helpers;
using Mu3D.Toolkit.Rendering;
using static HelperRenderChecks;
using Checks = HelperRenderChecks;

internal static class GridHelperAllocationChecks
{
    private const int VertexStride = 28;
    private const int VerticesPerLine = 6;
    private const int FrameCount = 64;
    private const int AllocationBudget = 2048;
    private const float PixelTolerance = .003f;

    internal static void Validate(ICollection<string> failures)
    {
        VerifyCurrentGeometry(failures);
        VerifyFlagsAndRecovery(failures);
        VerifyResourceChangesAndDisposal(failures);
        VerifyValidationBeforeEmpty(failures);
        VerifyAllocation(failures);
    }

    private static void VerifyCurrentGeometry(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("current grid geometry", hasDepth: true, cameraZ: 20f);
        fixture.Camera.Transform.Position = new(3f, 4f, 8f);
        GridHelper helper = new()
        {
            Origin = new(2.5f, 3.5f, 0f), Size = 2f,
            ShowMinorLines = false, ShowMajorLines = false, ShowCenterAxes = true,
        };
        using GridHelperRenderPass pass = new(helper);
        byte[]? firstPlane = null;
        foreach (GridHelperPlane plane in new[] { GridHelperPlane.XY, GridHelperPlane.XZ, GridHelperPlane.YZ })
        {
            helper.Plane = plane;
            byte[] bytes = Draw(fixture, pass, 2, "each current grid plane produces two center axes", failures);
            VerifyAxes(bytes, helper, pass.Style, fixture.Context, "current plane axes", failures);
            if (firstPlane is null) firstPlane = bytes;
            else Checks.Expect(!bytes.AsSpan().SequenceEqual(firstPlane), "changing the grid plane replaces uploaded axis geometry", failures);
        }

        helper.Plane = GridHelperPlane.XY;
        byte[] original = Draw(fixture, pass, 2, "original axis grid", failures);
        helper.Origin = new(2.25f, 3.2f, -.2f);
        helper.Size = 3f;
        byte[] moved = Draw(fixture, pass, 2, "current origin and world size", failures);
        VerifyAxes(moved, helper, pass.Style, fixture.Context, "current origin and size", failures);
        Checks.Expect(!moved.AsSpan().SequenceEqual(original), "grid origin/size changes replace staged vertices", failures);
        fixture.Camera.Transform.Position = new(3.2f, 4.1f, 8.5f);
        byte[] cameraMoved = Draw(fixture, pass, 2, "current camera grid", failures);
        VerifyAxes(cameraMoved, helper, pass.Style, fixture.Context, "current camera projection", failures);
        Checks.Expect(!cameraMoved.AsSpan().SequenceEqual(moved), "grid projection reads the current camera pose", failures);

        using GraphicsTexture resizedColor = fixture.Device.CreateTexture(new(new(768, 384),
            GraphicsTextureFormat.Rgba16Float, GraphicsTextureUsage.RenderAttachment));
        using GraphicsTexture resizedDepth = fixture.Device.CreateTexture(new(new(768, 384),
            GraphicsTextureFormat.Depth32Float, GraphicsTextureUsage.RenderAttachment));
        fixture.Camera.AspectRatio = 2f;
        RenderPassContext resized = new(fixture.Scene, fixture.Camera, resizedColor, resizedDepth,
            StandardColorSpaces.LinearSrgb, colorTargetInitialized: true, depthTargetInitialized: true);
        pass.Execute(resized);
        byte[] resizedBytes = ReadGeometry(fixture.Device);
        VerifyAxes(resizedBytes, helper, pass.Style, resized, "current physical viewport", failures);
        Checks.Expect(!resizedBytes.AsSpan().SequenceEqual(cameraMoved) && fixture.Device.PipelineDescriptors.Count == 1,
            "grid viewport/aspect changes preserve physical line widths without rebuilding the pipeline", failures);
        helper.Origin = new(2.5f, 3.5f, 0f);
        helper.Size = 2f;
        fixture.Camera.Transform.Position = new(3f, 4f, 8f);
        fixture.Camera.AspectRatio = 1f;
        byte[] restored = Draw(fixture, pass, 2, "restored grid geometry", failures);
        Checks.Expect(restored.AsSpan().SequenceEqual(original), "restoring grid/camera/viewport state restores exact upload bytes", failures);

        fixture.Scene.Root.IsVisible = false;
        fixture.Scene.Root.VisibilityMask = SceneVisibilityMask.None;
        fixture.Camera.VisibilityMask = SceneVisibilityMask.None;
        byte[] referenceGrid = Draw(fixture, pass, 2, "grid remains a scene-independent reference feature", failures);
        Checks.Expect(referenceGrid.AsSpan().SequenceEqual(original) && fixture.Scene.Root.Children.Count == 0,
            "empty scene, hidden/root mask flags and empty camera mask do not alter reference-grid geometry", failures);
        Checks.Expect(fixture.Device.PipelineDescriptors.Single().CullMode == GraphicsCullMode.None,
            "grid line quads remain visible from either face", failures);
    }

    private static void VerifyFlagsAndRecovery(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("grid staging recovery", hasDepth: true, cameraZ: 20f);
        GridHelper helper = new() { Plane = GridHelperPlane.XY, Divisions = 4, MajorLineEvery = 2 };
        using GridHelperRenderPass pass = new(helper);
        byte[] all = Draw(fixture, pass, 10, "four divisions include ten grid lines", failures);
        helper.ShowMinorLines = false;
        helper.ShowCenterAxes = false;
        byte[] majorOnly = Draw(fixture, pass, 6, "major-only rows at indices zero/two/four", failures);
        Checks.Expect(AllColors(majorOnly, Premultiply(pass.Style.MajorLineColor)), "major-only uploads contain only emphasized line color", failures);
        helper.ShowMajorLines = false;
        helper.ShowMinorLines = true;
        byte[] minorOnly = Draw(fixture, pass, 10, "disabling emphasis renders every row as a minor line", failures);
        Checks.Expect(AllColors(minorOnly, Premultiply(pass.Style.MinorLineColor)), "minor-only uploads contain only minor line color", failures);
        helper.ShowMinorLines = false;
        helper.ShowMajorLines = true;
        helper.ShowCenterAxes = true;
        byte[] majorAxes = Draw(fixture, pass, 6, "center axes replace the central emphasized grid rows", failures);
        Checks.Expect(!majorAxes.AsSpan().SequenceEqual(majorOnly), "center-axis flags update color and physical width despite equal line counts", failures);
        helper.MajorLineEvery = 3;
        byte[] interval = Draw(fixture, pass, 6, "major interval moves emphasized rows to indices zero/three", failures);
        Checks.Expect(!interval.AsSpan().SequenceEqual(majorAxes), "major-line interval changes replace current grid rows", failures);
        helper.ShowMinorLines = true;
        helper.Divisions = 3;
        byte[] odd = Draw(fixture, pass, 10, "odd divisions retain four rows plus two separate center axes", failures);
        Checks.Expect(!odd.AsSpan().SequenceEqual(all), "division changes replace grid spacing even when upload lengths match", failures);

        helper.Divisions = GridHelper.MaximumDivisions;
        byte[] large = Draw(fixture, pass, 2 * (GridHelper.MaximumDivisions + 1), "maximum grid staging", failures);
        helper.ShowMinorLines = false;
        helper.ShowMajorLines = false;
        byte[] small = Draw(fixture, pass, 2, "maximum-to-center-axis-only shrink", failures);
        VerifyAxes(small, helper, pass.Style, fixture.Context, "shrunken grid axis endpoints", failures);
        Checks.Expect(large.Length > small.Length && small.Length == 12 * VertexStride,
            "shrinking grid uploads exclude all previously staged rows", failures);
        helper.ShowCenterAxes = false;
        Draw(fixture, pass, 0, "all disabled grid flags produce an empty frame", failures);
        helper.ShowCenterAxes = true;
        helper.Origin = new(0f, 0f, 30f);
        Draw(fixture, pass, 0, "grid fully behind the camera produces a clip-empty frame", failures);
        helper.Origin = Vector3.Zero;
        byte[] recovered = Draw(fixture, pass, 2, "grid recovery after empty/clipped frames", failures);
        Checks.Expect(recovered.AsSpan().SequenceEqual(small), "empty/clipped frames never contaminate recovered grid bytes", failures);

        helper.Plane = GridHelperPlane.XZ;
        helper.Size = 4f;
        fixture.Camera.Transform.Position = new(0f, .02f, 0f);
        byte[] clipped = Draw(fixture, pass, 1, "near-plane-crossing grid axis is clipped rather than discarded", failures);
        if (clipped.Length == VerticesPerLine * VertexStride)
        {
            VerifySegment(clipped, 0, new(0f, 0f, -2f), new(0f, 0f, -.1f),
                Premultiply(pass.Style.ZAxisColor), pass.Style.CenterAxisWidthPixels, fixture.Context,
                "clipped Z-axis keeps the visible world endpoints", failures);
            Checks.Expect(ReadPosition(clipped, 2).Z < .0001f && ReadPosition(clipped, 5).Z < .0001f,
                "near-plane grid endpoint has finite zero-to-one clipped depth", failures);
        }
        helper.Plane = GridHelperPlane.XY;
        helper.Size = 10f;
        fixture.Camera.Transform.Position = new(0f, 0f, 20f);
        byte[] afterClip = Draw(fixture, pass, 2, "grid returns from partial clipping to prior axis geometry", failures);
        Checks.Expect(afterClip.AsSpan().SequenceEqual(small), "partial clipping does not retain extra grid vertices", failures);

        helper.ShowMinorLines = true;
        helper.ShowMajorLines = true;
        VerifyUploadFailure(pass, fixture.Context, fixture.Device, "grid upload failure", failures);
        helper.ShowMinorLines = false;
        helper.ShowMajorLines = false;
        byte[] afterUpload = Draw(fixture, pass, 2, "grid recovers from a failed large upload", failures);
        Checks.Expect(afterUpload.AsSpan().SequenceEqual(small), "upload failure recovery excludes retained maximum-grid rows", failures);
        helper.Plane = (GridHelperPlane)int.MaxValue;
        int before = fixture.Device.SubmittedPipelineLabels.Count;
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(fixture.Context), "invalid grid plane fails explicitly", failures);
        Checks.Expect(fixture.Device.SubmittedPipelineLabels.Count == before, "invalid grid build submits no stale geometry", failures);
        helper.Plane = GridHelperPlane.XY;
        byte[] afterInvalid = Draw(fixture, pass, 2, "grid recovers from an invalid geometry build", failures);
        Checks.Expect(afterInvalid.AsSpan().SequenceEqual(small) && fixture.Device.PipelineDescriptors.Count == 1,
            "grid failure recovery retains correct bytes and the existing pipeline", failures);
    }

    private static void VerifyResourceChangesAndDisposal(ICollection<string> failures)
    {
        using HelperRenderFixture first = new("grid resource changes", hasDepth: true, cameraZ: 20f);
        GridHelper helper = new() { Plane = GridHelperPlane.XY };
        using GridHelperRenderPass pass = new(helper);
        byte[] original = Draw(first, pass, 42, "original FP16 grid resource", failures);
        RecordingGraphicsBuffer originalBuffer = first.Device.Buffers[^1];
        using GraphicsTexture referenceColor = first.Device.CreateTexture(new(new(512, 512),
            GraphicsTextureFormat.Rgba32Float, GraphicsTextureUsage.RenderAttachment));
        RenderPassContext reference = new(first.Scene, first.Camera, referenceColor, first.Depth,
            StandardColorSpaces.LinearSrgb, colorTargetInitialized: true, depthTargetInitialized: true);
        pass.Execute(reference);
        RecordingGraphicsBuffer referenceBuffer = first.Device.Buffers[^1];
        Checks.Expect(originalBuffer.IsDisposed && ReadGeometry(first.Device).AsSpan().SequenceEqual(original) &&
            first.Device.PipelineDescriptors.Count == 2 && first.Device.PipelineDescriptors[^1].ColorFormat == GraphicsTextureFormat.Rgba32Float,
            "grid color-format resource replacement preserves current CPU geometry and releases prior buffer", failures);
        byte[] restored = Draw(first, pass, 42, "grid restores its original FP16 color format", failures);
        Checks.Expect(referenceBuffer.IsDisposed && restored.AsSpan().SequenceEqual(original) && first.Device.PipelineDescriptors.Count == 3,
            "returning to FP16 rebuilds only graphics resources with identical grid bytes", failures);
        RecordingGraphicsBuffer beforeDevice = first.Device.Buffers[^1];
        using HelperRenderFixture second = new("replacement grid device", hasDepth: true, cameraZ: 20f);
        byte[] replacement = Draw(second, pass, 42, "replacement device evaluates current grid", failures);
        Checks.Expect(beforeDevice.IsDisposed && replacement.AsSpan().SequenceEqual(original) && second.Device.PipelineDescriptors.Count == 1,
            "grid device replacement releases old resources and retains current numeric staging", failures);
        RecordingGraphicsBuffer finalBuffer = second.Device.Buffers[^1];
        pass.Dispose();
        pass.Dispose();
        Checks.Expect(finalBuffer.IsDisposed && ReferenceEquals(pass.Helper, helper) && helper.Divisions == 20 && helper.Size == 10f,
            "grid disposal releases owned buffer while preserving the borrowed helper", failures);
        Checks.ExpectThrows<ObjectDisposedException>(() => pass.Execute(second.Context), "disposed grid rejects further execution", failures);
    }

    private static void VerifyValidationBeforeEmpty(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("empty grid validation", hasDepth: true, cameraZ: 20f);
        GridHelper helper = new() { ShowMinorLines = false, ShowMajorLines = false, ShowCenterAxes = false };
        using GridHelperRenderPass pass = new(helper);
        RenderPassContext noColor = new(fixture.Scene, fixture.Camera, fixture.Color, fixture.Depth,
            StandardColorSpaces.LinearSrgb, depthTargetInitialized: true);
        RenderPassContext noDepth = new(fixture.Scene, fixture.Camera, fixture.Color, fixture.Depth,
            StandardColorSpaces.LinearSrgb, colorTargetInitialized: true);
        RenderPassContext missingDepth = new(fixture.Scene, fixture.Camera, fixture.Color, null,
            StandardColorSpaces.LinearSrgb, colorTargetInitialized: true);
        RenderPassContext nonPerspective = new(fixture.Scene, new NonPerspectiveCamera(), fixture.Color, fixture.Depth,
            StandardColorSpaces.LinearSrgb, colorTargetInitialized: true, depthTargetInitialized: true);
        RenderPassContext wrongSpace = new(fixture.Scene, fixture.Camera, fixture.Color, fixture.Depth,
            StandardColorSpaces.LinearDisplayP3, colorTargetInitialized: true, depthTargetInitialized: true);
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(noColor), "empty grid still validates initialized color", failures);
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(noDepth), "empty grid still validates initialized depth", failures);
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(missingDepth), "empty grid still validates required depth", failures);
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(nonPerspective), "empty grid still validates perspective camera", failures);
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(wrongSpace), "empty grid still validates working color space", failures);
        Draw(fixture, pass, 0, "a valid empty grid submits no commands", failures);
        Checks.Expect(fixture.Device.PipelineDescriptors.Count == 0 && fixture.Device.Buffers.Count == 0,
            "empty and initially invalid grid frames create no graphics resources", failures);
    }

    // Standalone public-API fixture: root source-links it against retained .10 as the control.
    internal static void VerifyAllocation(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("warmed default and maximum grid", hasDepth: true, cameraZ: 20f);
        GridHelper helper = new() { Plane = GridHelperPlane.XY };
        using GridHelperRenderPass pass = new(helper);
        RenderPassContext context = fixture.Context;
        pass.Execute(context);
        byte[] defaultBytes = ReadGeometry(fixture.Device);
        fixture.Device.SubmittedPipelineLabels.EnsureCapacity(4096);
        long defaultAllocated = Measure(pass, context, FrameCount);
        bool defaultStable = ReadGeometry(fixture.Device).AsSpan().SequenceEqual(defaultBytes);
        helper.Divisions = GridHelper.MaximumDivisions;
        pass.Execute(context);
        byte[] maximumBytes = ReadGeometry(fixture.Device);
        long maximumAllocated = Measure(pass, context, FrameCount);
        bool maximumStable = ReadGeometry(fixture.Device).AsSpan().SequenceEqual(maximumBytes);
        helper.Divisions = 20;
        pass.Execute(context);
        bool shrunkStable = ReadGeometry(fixture.Device).AsSpan().SequenceEqual(defaultBytes);
        int maximumLines = 2 * (GridHelper.MaximumDivisions + 1);
        string defaultHash = Convert.ToHexString(SHA256.HashData(defaultBytes));
        string maximumHash = Convert.ToHexString(SHA256.HashData(maximumBytes));

        // Retained .10 is expected to fail only the new allocation budget. Emit identical-fixture
        // counts and byte hashes first, while excluding warmup, readbacks and serialization.
        string? path = Environment.GetEnvironmentVariable("MU3D_GRID_ALLOCATION_EVIDENCE");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Runtime = Environment.Version.ToString(),
                Backend = "portable RecordingGraphicsDevice; not browser allocation or GPU execution",
                FramesPerCase = FrameCount,
                Plane = "XY", Size = 10f, CameraWorldPosition = new[] { 0f, 0f, 20f },
                ViewportWidthPixels = 512, ViewportHeightPixels = 512,
                DefaultDivisions = 20, MaximumDivisions = GridHelper.MaximumDivisions,
                DefaultLines = 42, MaximumLines = maximumLines,
                DefaultVertices = defaultBytes.Length / VertexStride,
                MaximumVertices = maximumBytes.Length / VertexStride,
                DefaultVertexBytes = defaultBytes.Length, MaximumVertexBytes = maximumBytes.Length,
                DefaultGeometrySha256 = defaultHash, MaximumGeometrySha256 = maximumHash,
                DefaultTotalAllocatedBytes = defaultAllocated,
                DefaultAllocatedBytesPerFrame = defaultAllocated / (double)FrameCount,
                MaximumTotalAllocatedBytes = maximumAllocated,
                MaximumAllocatedBytesPerFrame = maximumAllocated / (double)FrameCount,
                BudgetBytesPerFrame = AllocationBudget,
                IncludesRecordingCommands = true,
                ExcludesWarmupHistoryGrowthReadbackAndEvidence = true,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Checks.Expect(defaultBytes.Length == 42 * VerticesPerLine * VertexStride &&
            maximumBytes.Length == maximumLines * VerticesPerLine * VertexStride &&
            defaultStable && maximumStable && shrunkStable && fixture.Device.PipelineDescriptors.Count == 1,
            "warmed default/maximum grid uploads retain exact geometry, current draw length and one pipeline", failures);
        Checks.Expect(defaultAllocated < FrameCount * AllocationBudget && maximumAllocated < FrameCount * AllocationBudget,
            "warmed grid allocation stays below 2 KiB/frame with recording commands included " +
            $"(default {defaultAllocated / FrameCount}, maximum {maximumAllocated / FrameCount})", failures);
    }

    private static byte[] Draw(HelperRenderFixture fixture, GridHelperRenderPass pass, int lines,
        string description, ICollection<string> failures)
    {
        int before = fixture.Device.SubmittedPipelineLabels.Count;
        pass.Execute(fixture.Context);
        Checks.Expect(fixture.Device.SubmittedPipelineLabels.Count == before + (lines > 0 ? 1 : 0), description, failures);
        if (lines == 0 || fixture.Device.Buffers.Count == 0) return [];
        byte[] bytes = ReadGeometry(fixture.Device);
        Checks.Expect(bytes.Length == lines * VerticesPerLine * VertexStride, description + " uploads exactly the current line count", failures);
        return bytes;
    }

    private static void VerifyAxes(byte[] bytes, GridHelper helper, GridHelperRenderStyle style,
        RenderPassContext context, string description, ICollection<string> failures)
    {
        Checks.Expect(bytes.Length == 2 * VerticesPerLine * VertexStride, description + " has two numeric axis quads", failures);
        if (bytes.Length != 2 * VerticesPerLine * VertexStride) return;
        (Vector3 first, Vector3 second, LinearRgba firstColor, LinearRgba secondColor) = helper.Plane switch
        {
            GridHelperPlane.XY => (Vector3.UnitX, Vector3.UnitY, style.XAxisColor, style.YAxisColor),
            GridHelperPlane.XZ => (Vector3.UnitX, Vector3.UnitZ, style.XAxisColor, style.ZAxisColor),
            GridHelperPlane.YZ => (Vector3.UnitY, Vector3.UnitZ, style.YAxisColor, style.ZAxisColor),
            _ => throw new InvalidOperationException("Unexpected test grid plane."),
        };
        VerifySegment(bytes, 0, helper.Origin - first * (helper.Size * .5f), helper.Origin + first * (helper.Size * .5f),
            Premultiply(firstColor), style.CenterAxisWidthPixels, context, description + " first axis", failures);
        VerifySegment(bytes, VerticesPerLine, helper.Origin - second * (helper.Size * .5f), helper.Origin + second * (helper.Size * .5f),
            Premultiply(secondColor), style.CenterAxisWidthPixels, context, description + " second axis", failures);
    }

    private static void VerifySegment(byte[] bytes, int vertex, Vector3 worldStart, Vector3 worldEnd,
        Vector4 color, float lineWidth, RenderPassContext context, string description, ICollection<string> failures)
    {
        // Check the semantic endpoints and width from quad midpoints; this does not reconstruct
        // grid enumeration, clipping or all emitted triangles from the production algorithm.
        Vector3 expectedStart = Project(context, worldStart);
        Vector3 expectedEnd = Project(context, worldEnd);
        Vector3 startA = ToPixels(ReadPosition(bytes, vertex), context);
        Vector3 startB = ToPixels(ReadPosition(bytes, vertex + 1), context);
        Vector3 endA = ToPixels(ReadPosition(bytes, vertex + 2), context);
        Vector3 endB = ToPixels(ReadPosition(bytes, vertex + 5), context);
        Checks.Expect(Vector3.Distance((startA + startB) * .5f, expectedStart) < PixelTolerance &&
            Vector3.Distance((endA + endB) * .5f, expectedEnd) < PixelTolerance &&
            MathF.Abs(Vector2.Distance(new(startA.X, startA.Y), new(startB.X, startB.Y)) - lineWidth) < PixelTolerance,
            description + " matches projected world endpoints and physical-pixel line width", failures);
        for (int index = vertex; index < vertex + VerticesPerLine; index++)
        {
            Vector3 point = ReadPosition(bytes, index);
            Checks.Expect(float.IsFinite(point.X) && float.IsFinite(point.Y) && point.Z >= 0f && point.Z <= 1f &&
                Vector4.Distance(ReadColor(bytes, index), color) < .000001f,
                description + " preserves finite FP32 depth and premultiplied HDR-linear color", failures);
        }
    }

    private static Vector3 Project(RenderPassContext context, Vector3 world)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), context.Camera.ViewProjectionMatrix);
        return new((clip.X / clip.W + 1f) * .5f * context.OutputExtent.Width,
            (1f - clip.Y / clip.W) * .5f * context.OutputExtent.Height, Math.Clamp(clip.Z / clip.W, 0f, 1f));
    }

    private static Vector3 ToPixels(Vector3 ndc, RenderPassContext context) => new(
        (ndc.X + 1f) * .5f * context.OutputExtent.Width, (1f - ndc.Y) * .5f * context.OutputExtent.Height, ndc.Z);

    private static bool AllColors(byte[] bytes, Vector4 expected)
    {
        for (int index = 0; index < bytes.Length / VertexStride; index++)
            if (ReadColor(bytes, index) != expected) return false;
        return bytes.Length > 0;
    }
}

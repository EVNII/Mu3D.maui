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

internal static class AxesHelperAllocationChecks
{
    private const int Stride = 28;
    private const int CircleVertices = 72;
    private const int Frames = 64;
    private const int Budget = 2048;
    private const float PixelTolerance = .003f;

    internal static void Validate(ICollection<string> failures)
    {
        VerifyAuthoredOrdering(failures);
        VerifyCurrentOrientationAndPlacement(failures);
        VerifyHitRules(failures);
        VerifyFailureAndResources(failures);
        VerifyValidation(failures);
        VerifyAllocation(failures);
    }

    private static void VerifyAuthoredOrdering(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("authored axes painter order");
        AxesHelper helper = new();
        using AxesHelperRenderPass pass = new(helper);
        byte[] bytes = Draw(fixture.Context, fixture.Device, pass, failures);
        Vector2 center = new(442f, 70f);
        float axis = 112f * .34f;
        float diagonal = axis * .78f * MathF.Sqrt(.5f);
        // Known identity-camera ordering: equal ±zero depths retain authored cardinal/diagonal
        // order, with negative Z first and positive Z last. This table is independent of sorting.
        Marker[] markers =
        [
            new(AxesHelperAxis.Z, false, center),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(diagonal, 0)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(-diagonal, 0)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(0, -diagonal)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(0, diagonal)),
            new(AxesHelperAxis.X, true, center + new Vector2(axis, 0)),
            new(AxesHelperAxis.X, false, center + new Vector2(-axis, 0)),
            new(AxesHelperAxis.Y, true, center + new Vector2(0, -axis)),
            new(AxesHelperAxis.Y, false, center + new Vector2(0, axis)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(diagonal, -diagonal)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(diagonal, diagonal)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(-diagonal, -diagonal)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(-diagonal, diagonal)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(diagonal, 0)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(-diagonal, 0)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(0, -diagonal)),
            new(AxesHelperAxis.Diagonal, true, center + new Vector2(0, diagonal)),
            new(AxesHelperAxis.Z, true, center),
        ];
        Checks.Expect(bytes.Length == 1518 * Stride, "full identity axes emit 1518 vertices with two degenerate Z shafts omitted", failures);
        if (bytes.Length != 1518 * Stride) return;
        VerifyCircle(bytes, 0, center, 56f, Premultiply(pass.Style.BackgroundColor), fixture.Context,
            "background precedes every handle", failures);
        for (int index = 0; index < 4; index++)
        {
            Marker marker = markers[5 + index];
            VerifyShaft(bytes, CircleVertices + index * 6, center, marker.Center,
                HandleColor(marker, pass.Style), pass.Style.LineWidthPixels, fixture.Context, failures);
        }
        const int fillStart = CircleVertices + 4 * 6;
        for (int index = 0; index < markers.Length; index++)
        {
            Marker marker = markers[index];
            VerifyCircle(bytes, fillStart + index * CircleVertices, marker.Center,
                marker.Axis == AxesHelperAxis.Diagonal ? pass.Style.DiagonalMarkerRadiusPixels : pass.Style.AxisMarkerRadiusPixels,
                HandleColor(marker, pass.Style), fixture.Context,
                "stable authored-depth marker fill " + index, failures);
        }
        int signStart = fillStart + 18 * CircleVertices;
        foreach (Marker marker in markers.Where(marker => marker.Axis != AxesHelperAxis.Diagonal))
        {
            int count = marker.Positive ? 12 : 6;
            Vector4 sign = marker.Axis == AxesHelperAxis.Y
                ? new(pass.Style.BackgroundColor.Red, pass.Style.BackgroundColor.Green, pass.Style.BackgroundColor.Blue, 1f)
                : Premultiply(pass.Style.CenterColor);
            Checks.Expect(AllColor(bytes, signStart, count, sign) &&
                Vector2.Distance((Pixels(bytes, signStart, fixture.Context) + Pixels(bytes, signStart + 2, fixture.Context)) * .5f,
                    marker.Center) < PixelTolerance,
                "direction signs follow all fills in stable cardinal order", failures);
            signStart += count;
        }
        VerifyCircle(bytes, signStart, center, 3.5f, Premultiply(pass.Style.CenterColor), fixture.Context,
            "center marker is the final painter stage", failures);
        Checks.Expect(signStart + CircleVertices == bytes.Length / Stride,
            "background/shafts/fills/signs/center occupy the exact active upload", failures);
    }

    private static void VerifyCurrentOrientationAndPlacement(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("current axes orientation");
        AxesHelper helper = new() { ShowNegativeAxes = false, ShowDiagonalViews = false };
        using AxesHelperRenderPass pass = new(helper);
        byte[] original = VerifyMinimal(fixture.Context, fixture.Device, pass, new(442, 70), "world axes", failures);
        SceneNode parent = new("orientation parent");
        SceneNode target = new("borrowed orientation target");
        parent.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .3f);
        parent.Transform.Scale = new(1.25f, .9f, 1.1f);
        target.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -.4f);
        target.Transform.Scale = new(-2f, .7f, 1.3f);
        parent.AddChild(target);
        fixture.Scene.Add(parent);
        helper.OrientationTarget = target;
        byte[] worldWithTarget = VerifyMinimal(fixture.Context, fixture.Device, pass, new(442, 70), "world ignores borrowed target orientation", failures);
        Checks.Expect(worldWithTarget.AsSpan().SequenceEqual(original), "world-space axes do not inherit the optional target basis", failures);
        helper.Space = AxesHelperSpace.TargetLocal;
        byte[] local = VerifyMinimal(fixture.Context, fixture.Device, pass, new(442, 70), "current signed nonuniform target and parent basis", failures);
        Checks.Expect(!local.AsSpan().SequenceEqual(original), "target-local orientation replaces prior world marker positions", failures);
        fixture.Camera.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(.2f, -.25f, .1f);
        byte[] cameraChanged = VerifyMinimal(fixture.Context, fixture.Device, pass, new(442, 70), "current camera rotation", failures);
        Checks.Expect(!cameraChanged.AsSpan().SequenceEqual(local), "axes projection reads live camera orientation", failures);
        fixture.Camera.Transform.Position = new(20f, -30f, 50f);
        byte[] translatedCamera = VerifyMinimal(fixture.Context, fixture.Device, pass, new(442, 70), "camera translation does not alter orientation", failures);
        Checks.Expect(translatedCamera.AsSpan().SequenceEqual(cameraChanged), "orientation axes use camera direction rather than camera position", failures);
        parent.IsVisible = false;
        parent.VisibilityMask = SceneVisibilityMask.None;
        target.IsVisible = false;
        target.VisibilityMask = SceneVisibilityMask.None;
        fixture.Camera.VisibilityMask = SceneVisibilityMask.None;
        fixture.Scene.Remove(parent);
        byte[] detached = VerifyMinimal(fixture.Context, fixture.Device, pass, new(442, 70), "detached hidden orientation remains a loose query", failures);
        Scene foreign = new("foreign orientation scene");
        foreign.Add(parent);
        byte[] moved = VerifyMinimal(fixture.Context, fixture.Device, pass, new(442, 70), "foreign-scene orientation remains a loose query", failures);
        Checks.Expect(detached.AsSpan().SequenceEqual(cameraChanged) && moved.AsSpan().SequenceEqual(cameraChanged) &&
            ReferenceEquals(helper.OrientationTarget, target), "membership/visibility/masks never filter or clear the borrowed orientation target", failures);

        helper.Space = AxesHelperSpace.World;
        fixture.Camera.Transform.Rotation = Quaternion.Identity;
        (ViewportOverlayPlacement Placement, Vector2 Center)[] placements =
        [
            (ViewportOverlayPlacement.TopLeft, new(70, 70)), (ViewportOverlayPlacement.TopCenter, new(256, 70)),
            (ViewportOverlayPlacement.TopRight, new(442, 70)), (ViewportOverlayPlacement.CenterLeft, new(70, 256)),
            (ViewportOverlayPlacement.Center, new(256, 256)), (ViewportOverlayPlacement.CenterRight, new(442, 256)),
            (ViewportOverlayPlacement.BottomLeft, new(70, 442)), (ViewportOverlayPlacement.BottomCenter, new(256, 442)),
            (ViewportOverlayPlacement.BottomRight, new(442, 442)),
        ];
        foreach ((ViewportOverlayPlacement placement, Vector2 center) in placements)
        {
            helper.Placement = placement;
            VerifyMinimal(fixture.Context, fixture.Device, pass, center, "current corner/edge/center placement", failures);
        }
        helper.Placement = ViewportOverlayPlacement.TopRight;
        helper.ScreenSizePixels = 128f;
        helper.MarginPixels = 10f;
        VerifyMinimal(fixture.Context, fixture.Device, pass, new(438, 74), "current diameter and margin", failures);
        helper.ScreenSizePixels = 112f;
        helper.MarginPixels = 14f;
        using GraphicsTexture wide = fixture.Device.CreateTexture(new(new(640, 360), GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment));
        RenderPassContext wideContext = new(fixture.Scene, fixture.Camera, wide, null, StandardColorSpaces.LinearSrgb, colorTargetInitialized: true);
        VerifyMinimal(wideContext, fixture.Device, pass, new(570, 70), "current rectangular physical viewport", failures);
        using GraphicsTexture tiny = fixture.Device.CreateTexture(new(new(16, 32), GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment));
        RenderPassContext tinyContext = new(fixture.Scene, fixture.Camera, tiny, null, StandardColorSpaces.LinearSrgb, colorTargetInitialized: true);
        VerifyMinimal(tinyContext, fixture.Device, pass, new(8, 16), "small viewport clamps center while retaining fixed-pixel size", failures);
        helper.ShowNegativeAxes = true;
        helper.ShowDiagonalViews = false;
        Checks.Expect(Draw(fixture.Context, fixture.Device, pass, failures).Length == 654 * Stride,
            "six cardinal handles remain independent of diagonal visibility", failures);
        helper.ShowNegativeAxes = false;
        helper.ShowDiagonalViews = true;
        Checks.Expect(Draw(fixture.Context, fixture.Device, pass, failures).Length == 1272 * Stride,
            "twelve diagonal handles remain independent of negative cardinal visibility", failures);
        helper.ShowNegativeAxes = true;
        fixture.Camera.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(.2f, .3f, .1f);
        Checks.Expect(Draw(fixture.Context, fixture.Device, pass, failures).Length == 1530 * Stride,
            "tilted full axes fit the 1530-vertex bound with all six shafts", failures);
    }

    private static void VerifyHitRules(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("axes hit ordering");
        AxesHelper helper = new();
        AxesHelperHitTester tester = new();
        Vector2 center = new(442, 70);
        Checks.Expect(tester.HitTest(helper, fixture.Camera, 512, 512, center)?.Preset == AxesViewPreset.PositiveZ,
            "overlapping positive/negative Z picks the larger view depth", failures);
        Checks.Expect(tester.HitTest(helper, fixture.Camera, 512, 512, center + new Vector2(112f * .34f, 0))?.Preset == AxesViewPreset.PositiveX,
            "cardinal marker center returns its known authored preset", failures);
        float diagonal = 112f * .34f * .78f * MathF.Sqrt(.5f);
        Checks.Expect(tester.HitTest(helper, fixture.Camera, 512, 512, center + new Vector2(diagonal, 0))?.Preset == AxesViewPreset.PositiveXPositiveZ,
            "overlapping diagonal handles use larger depth after equal distance", failures);
        // The midpoint of +X and the overlapping +X±Z diagonals ties their ~8.54-pixel
        // distances within both default tolerances. +X wins despite +X+Z's larger depth.
        Vector2 tiedPointer = center + new Vector2((112f * .34f + diagonal) * .5f, 0f);
        AxesHelperHit? crowded = tester.HitTest(helper, fixture.Camera, 512, 512, tiedPointer);
        Checks.Expect(crowded is not null && crowded.Preset == AxesViewPreset.PositiveX && crowded.Axis == AxesHelperAxis.X,
            "equal-distance cardinal/diagonal overlap retains cardinal priority before depth", failures);
        fixture.Camera.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        Checks.Expect(tester.HitTest(helper, fixture.Camera, 512, 512, center)?.Preset == AxesViewPreset.NegativeZ,
            "current camera reversal changes the frontmost overlapping cardinal", failures);
        fixture.Camera.Transform.Rotation = Quaternion.Identity;
        Checks.Expect(tester.HitTest(helper, fixture.Camera, 512, 512, new(0, 512)) is null,
            "known pointer outside the helper misses all visible handles", failures);
    }

    private static void VerifyFailureAndResources(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("axes staging/resource recovery");
        AxesHelper helper = new();
        using AxesHelperRenderPass pass = new(helper);
        byte[] full = Draw(fixture.Context, fixture.Device, pass, failures);
        VerifyUploadFailure(pass, fixture.Context, fixture.Device, "axes upload failure", failures);
        helper.ShowNegativeAxes = false;
        helper.ShowDiagonalViews = false;
        byte[] small = Draw(fixture.Context, fixture.Device, pass, failures);
        Checks.Expect(small.Length == 408 * Stride && small.Length < full.Length, "full-to-minimal and failure-to-minimal axes discard retained vertices", failures);
        helper.Placement = (ViewportOverlayPlacement)int.MaxValue;
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(fixture.Context), "invalid axes placement", failures);
        helper.Placement = ViewportOverlayPlacement.TopRight;
        Checks.Expect(Draw(fixture.Context, fixture.Device, pass, failures).AsSpan().SequenceEqual(small), "axes recover current bytes after invalid layout", failures);
        RecordingGraphicsBuffer originalBuffer = fixture.Device.Buffers[^1];
        using GraphicsTexture color32 = fixture.Device.CreateTexture(new(new(512, 512), GraphicsTextureFormat.Rgba32Float,
            GraphicsTextureUsage.RenderAttachment));
        RenderPassContext context32 = new(fixture.Scene, fixture.Camera, color32, null, StandardColorSpaces.LinearSrgb, colorTargetInitialized: true);
        Checks.Expect(Draw(context32, fixture.Device, pass, failures).AsSpan().SequenceEqual(small) && originalBuffer.IsDisposed &&
            fixture.Device.PipelineDescriptors.Count == 2, "axes color-format replacement preserves staging and releases old resources", failures);
        RecordingGraphicsBuffer beforeDevice = fixture.Device.Buffers[^1];
        using HelperRenderFixture other = new("axes replacement device");
        Checks.Expect(Draw(other.Context, other.Device, pass, failures).AsSpan().SequenceEqual(small) && beforeDevice.IsDisposed,
            "axes device replacement preserves current numeric geometry", failures);
        GraphicsRenderPipelineDescriptor pipeline = other.Device.PipelineDescriptors.Single();
        Checks.Expect(pipeline.ColorFormat == GraphicsTextureFormat.Rgba16Float && pipeline.DepthStencil is null &&
            pipeline.Blend == GraphicsBlendState.PremultipliedAlpha && pass.Descriptor.DepthAttachment is null &&
            ((RecordingGraphicsTexture)other.Color).LastColorLoadOperation == GraphicsLoadOperation.Load,
            "axes load FP16 HDR color as a true premultiplied overlay without depth", failures);
        using GraphicsTexture unusedDepth = other.Device.CreateTexture(new(new(512, 512), GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment));
        RenderPassContext withDepth = new(other.Scene, other.Camera, other.Color, unusedDepth, StandardColorSpaces.LinearSrgb,
            colorTargetInitialized: true, depthTargetInitialized: true);
        Checks.Expect(Draw(withDepth, other.Device, pass, failures).AsSpan().SequenceEqual(small) &&
            ((RecordingGraphicsTexture)unusedDepth).LastDepthLoadOperation is null,
            "axes overlay neither attaches nor loads supplied scene depth", failures);
        RecordingGraphicsBuffer finalBuffer = other.Device.Buffers[^1];
        pass.Dispose();
        pass.Dispose();
        Checks.Expect(finalBuffer.IsDisposed && ReferenceEquals(pass.Helper, helper), "axes disposal releases owned graphics and retains borrowed definition", failures);
        Checks.ExpectThrows<ObjectDisposedException>(() => pass.Execute(other.Context), "disposed axes execution", failures);
    }

    private static void VerifyValidation(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("axes initial validation");
        AxesHelper helper = new();
        using AxesHelperRenderPass pass = new(helper);
        RenderPassContext noColor = new(fixture.Scene, fixture.Camera, fixture.Color, null, StandardColorSpaces.LinearSrgb);
        RenderPassContext wrongSpace = new(fixture.Scene, fixture.Camera, fixture.Color, null, StandardColorSpaces.LinearDisplayP3, colorTargetInitialized: true);
        RenderPassContext wrongCamera = new(fixture.Scene, new NonPerspectiveCamera(), fixture.Color, null, StandardColorSpaces.LinearSrgb, colorTargetInitialized: true);
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(noColor), "axes require initialized color", failures);
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(wrongSpace), "axes working color-space validation", failures);
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(wrongCamera), "axes perspective validation", failures);
        helper.Space = (AxesHelperSpace)int.MaxValue;
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(fixture.Context), "invalid axes space", failures);
        helper.Space = AxesHelperSpace.TargetLocal;
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(fixture.Context), "target-local axes require target", failures);
        SceneNode target = new();
        target.Transform.Scale = new(0f, 1f, 1f);
        helper.OrientationTarget = target;
        Checks.ExpectThrows<InvalidOperationException>(() => pass.Execute(fixture.Context), "degenerate target orientation", failures);
        Checks.Expect(fixture.Device.Buffers.Count == 0 && fixture.Device.PipelineDescriptors.Count == 0 && ReferenceEquals(helper.OrientationTarget, target),
            "initial axes validation creates no resources or clears target ownership", failures);
        helper.Space = AxesHelperSpace.World;
        Checks.Expect(Draw(fixture.Context, fixture.Device, pass, failures).Length == 1518 * Stride,
            "world axes recover independently of an unused degenerate target", failures);
    }

    internal static void VerifyAllocation(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("warmed identity full/minimal axes");
        AxesHelper helper = new();
        using AxesHelperRenderPass pass = new(helper);
        RenderPassContext context = fixture.Context;
        pass.Execute(context);
        byte[] full = ReadGeometry(fixture.Device);
        fixture.Device.SubmittedPipelineLabels.EnsureCapacity(4096);
        long fullAllocation = Measure(pass, context, Frames);
        bool fullStable = ReadGeometry(fixture.Device).AsSpan().SequenceEqual(full);
        helper.ShowNegativeAxes = false;
        helper.ShowDiagonalViews = false;
        pass.Execute(context);
        byte[] small = ReadGeometry(fixture.Device);
        long smallAllocation = Measure(pass, context, Frames);
        bool smallStable = ReadGeometry(fixture.Device).AsSpan().SequenceEqual(small);
        helper.ShowNegativeAxes = true;
        helper.ShowDiagonalViews = true;
        AxesHelperHitTester tester = new();
        for (int frame = 0; frame < 8; frame++) _ = tester.HitTest(helper, fixture.Camera, 512, 512, new(0, 512));
        bool allMiss = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < Frames; frame++) allMiss &= tester.HitTest(helper, fixture.Camera, 512, 512, new(0, 512)) is null;
        long missAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
        pass.Execute(context);
        bool restored = ReadGeometry(fixture.Device).AsSpan().SequenceEqual(full);
        string? path = Environment.GetEnvironmentVariable("MU3D_AXES_ALLOCATION_EVIDENCE");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Runtime = Environment.Version.ToString(), Backend = "portable RecordingGraphicsDevice/HitTest; not browser or GPU allocation",
                FramesPerCase = Frames, CameraRotation = "identity", ViewportWidthPixels = 512, ViewportHeightPixels = 512,
                ScreenSizePixels = 112, MarginPixels = 14, Placement = "TopRight", FullHandles = 18, MinimalHandles = 3,
                FullVisibleShafts = 4, MinimalVisibleShafts = 2, FullVertices = full.Length / Stride, MinimalVertices = small.Length / Stride,
                FullVertexBytes = full.Length, MinimalVertexBytes = small.Length,
                FullGeometrySha256 = Convert.ToHexString(SHA256.HashData(full)), MinimalGeometrySha256 = Convert.ToHexString(SHA256.HashData(small)),
                FullTotalAllocatedBytes = fullAllocation, FullAllocatedBytesPerFrame = fullAllocation / (double)Frames,
                MinimalTotalAllocatedBytes = smallAllocation, MinimalAllocatedBytesPerFrame = smallAllocation / (double)Frames,
                FullLayoutHitMissSamples = Frames, HitMissTotalAllocatedBytes = missAllocation,
                HitMissAllocatedBytesPerSample = missAllocation / (double)Frames, BudgetBytesPerFrame = Budget,
                IncludesRecordingCommands = true, ExcludesWarmupHistoryGrowthReadbackAndEvidence = true,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Checks.Expect(full.Length == 1518 * Stride && small.Length == 408 * Stride && fullStable && smallStable && restored && allMiss &&
            fixture.Device.PipelineDescriptors.Count == 1, "warmed axes preserve exact full/minimal bytes, missed hits and one pipeline", failures);
        Checks.Expect(fullAllocation < Frames * Budget && smallAllocation < Frames * Budget && missAllocation == 0,
            $"warmed axes stay below 2 KiB/render and zero bytes/hit miss (full {fullAllocation / Frames}, minimal {smallAllocation / Frames}, miss {missAllocation / Frames})", failures);
    }

    private static byte[] VerifyMinimal(RenderPassContext context, RecordingGraphicsDevice device, AxesHelperRenderPass pass,
        Vector2 center, string description, ICollection<string> failures)
    {
        byte[] bytes = Draw(context, device, pass, failures);
        Checks.Expect(bytes.Length == 408 * Stride || bytes.Length == 414 * Stride, description + " retains three marker layout", failures);
        VerifyCircle(bytes, 0, center, pass.Helper.ScreenSizePixels * .5f, Premultiply(pass.Style.BackgroundColor), context,
            description + " background bounds", failures);
        AxesHelperHitTester tester = new(.1f);
        (AxesViewPreset Preset, Vector3 Axis, LinearRgba Color)[] definitions =
        [(AxesViewPreset.PositiveX, Vector3.UnitX, pass.Style.XAxisColor), (AxesViewPreset.PositiveY, Vector3.UnitY, pass.Style.YAxisColor),
            (AxesViewPreset.PositiveZ, Vector3.UnitZ, pass.Style.ZAxisColor)];
        foreach ((AxesViewPreset preset, Vector3 axis, LinearRgba color) in definitions)
        {
            Vector3 world = pass.Helper.Space == AxesHelperSpace.World ? axis
                : Vector3.Normalize(Vector3.TransformNormal(axis, pass.Helper.OrientationTarget!.WorldMatrix));
            Vector3 view = Vector3.Normalize(Vector3.TransformNormal(world, context.Camera.ViewMatrix));
            Vector2 expected = center + new Vector2(view.X, -view.Y) * (pass.Helper.ScreenSizePixels * .34f);
            Checks.Expect(FindCircle(bytes, expected, pass.Style.AxisMarkerRadiusPixels, Premultiply(color), context),
                description + " uploads independently projected " + preset + " marker", failures);
            if (expected.X >= 0 && expected.Y >= 0 && expected.X <= context.OutputExtent.Width && expected.Y <= context.OutputExtent.Height)
            {
                AxesHelperHit? hit = tester.HitTest(pass.Helper, (PerspectiveCamera)context.Camera,
                    context.OutputExtent.Width, context.OutputExtent.Height, expected);
                Checks.Expect(hit is not null && hit.Preset == preset && Vector3.Distance(hit.CameraDirection, world) < .0001f &&
                    Vector2.Distance(hit.ScreenPositionPixels, expected) < PixelTolerance,
                    description + " hit geometry agrees with marker projection/current world direction", failures);
            }
        }
        return bytes;
    }

    private static byte[] Draw(RenderPassContext context, RecordingGraphicsDevice device, AxesHelperRenderPass pass, ICollection<string> failures)
    {
        int before = device.SubmittedPipelineLabels.Count;
        pass.Execute(context);
        Checks.Expect(device.SubmittedPipelineLabels.Count == before + 1, "axes public Execute submits one current overlay", failures);
        return ReadGeometry(device);
    }

    private static bool FindCircle(byte[] bytes, Vector2 center, float radius, Vector4 color, RenderPassContext context)
    {
        for (int start = 0; start + CircleVertices <= bytes.Length / Stride; start += 3)
        {
            if (ReadColor(bytes, start) != color || Vector2.Distance(Pixels(bytes, start, context), center) > PixelTolerance) continue;
            bool match = true;
            for (int triangle = 0; triangle < 24; triangle++)
                match &= Vector2.Distance(Pixels(bytes, start + triangle * 3, context), center) < PixelTolerance &&
                    MathF.Abs(Vector2.Distance(Pixels(bytes, start + triangle * 3 + 1, context), center) - radius) < PixelTolerance;
            if (match) return true;
        }
        return false;
    }

    private static void VerifyCircle(byte[] bytes, int start, Vector2 center, float radius, Vector4 color,
        RenderPassContext context, string description, ICollection<string> failures)
    {
        if (start + CircleVertices > bytes.Length / Stride) { failures.Add(description + ": missing circle batch"); return; }
        Vector2 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        bool centers = true;
        for (int index = 0; index < CircleVertices; index++)
        {
            Vector2 point = Pixels(bytes, start + index, context);
            minimum = Vector2.Min(minimum, point); maximum = Vector2.Max(maximum, point);
            if (index % 3 == 0) centers &= Vector2.Distance(point, center) < PixelTolerance;
        }
        Checks.Expect(centers && AllColor(bytes, start, CircleVertices, color) &&
            Vector2.Distance(minimum, center - new Vector2(radius)) < PixelTolerance &&
            Vector2.Distance(maximum, center + new Vector2(radius)) < PixelTolerance,
            description + " retains known center/radius/bounds/premultiplied color", failures);
    }

    private static void VerifyShaft(byte[] bytes, int start, Vector2 first, Vector2 last, Vector4 color, float width,
        RenderPassContext context, ICollection<string> failures)
    {
        Vector2 a = Pixels(bytes, start, context), b = Pixels(bytes, start + 1, context);
        Vector2 c = Pixels(bytes, start + 2, context), d = Pixels(bytes, start + 5, context);
        Checks.Expect(Vector2.Distance((a + b) * .5f, first) < PixelTolerance && Vector2.Distance((c + d) * .5f, last) < PixelTolerance &&
            MathF.Abs(Vector2.Distance(a, b) - width) < PixelTolerance && AllColor(bytes, start, 6, color),
            "stable cardinal shafts precede fills with known endpoints/physical width/HDR color", failures);
    }

    private static Vector4 HandleColor(Marker marker, AxesHelperRenderStyle style)
    {
        Vector4 color = Premultiply(marker.Axis switch
        { AxesHelperAxis.X => style.XAxisColor, AxesHelperAxis.Y => style.YAxisColor, AxesHelperAxis.Z => style.ZAxisColor, _ => style.DiagonalColor });
        return marker.Positive ? color : color * .48f;
    }

    private static bool AllColor(byte[] bytes, int start, int count, Vector4 color)
    {
        for (int index = start; index < start + count; index++)
            if (ReadColor(bytes, index) != color || BitConverter.ToSingle(bytes, index * Stride + 8) != 0f) return false;
        return true;
    }

    private static Vector2 Pixels(byte[] bytes, int index, RenderPassContext context) => new(
        (BitConverter.ToSingle(bytes, index * Stride) + 1f) * .5f * context.OutputExtent.Width,
        (1f - BitConverter.ToSingle(bytes, index * Stride + 4)) * .5f * context.OutputExtent.Height);
    private readonly record struct Marker(AxesHelperAxis Axis, bool Positive, Vector2 Center);
}

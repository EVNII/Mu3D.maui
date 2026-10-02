using System.Text.Json;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Rendering;
using static HelperRenderChecks;
using Checks = HelperRenderChecks;

internal static class TransformGizmoAllocationChecks
{
    internal static void Validate(ICollection<string> failures)
    {
        using HelperRenderFixture fixture = new("gizmo staging regression", 1000, 1000, cameraZ: 10f);
        SceneNode target = new("gizmo target");
        fixture.Scene.Add(target);
        using TransformGizmo gizmo = new(target) { ScreenSizePixels = 100f };
        RecordingGraphicsDevice device = fixture.Device;
        RenderPassContext context = fixture.Context;
        using TransformGizmoRenderPass pass = new(gizmo);

        gizmo.Mode = TransformGizmoMode.Translate;
        pass.Execute(context);
        byte[] translate = ReadGeometry(device);
        gizmo.Mode = TransformGizmoMode.Rotate;
        pass.Execute(context);
        byte[] rotate = ReadGeometry(device);
        Checks.Expect(rotate.Length > translate.Length,
            "rotation exercises a larger gizmo staging buffer than translation", failures);

        // Includes the recording backend's command wrappers/closures, but excludes initial
        // geometry capacity growth, GPU-resource creation and submission-history growth.
        device.SubmittedPipelineLabels.EnsureCapacity(1024);
        long rotateBytes = Measure(pass, context, 64);
        Checks.Expect(ReadGeometry(device).AsSpan().SequenceEqual(rotate),
            "repeated rotation preserves every staged vertex", failures);
        gizmo.Mode = TransformGizmoMode.Translate;
        long translateBytes = Measure(pass, context, 64);
        Checks.Expect(ReadGeometry(device).AsSpan().SequenceEqual(translate),
            "shrinking gizmo geometry excludes previous rotation vertices", failures);
        Checks.Expect(rotateBytes / 64 < 4096 && translateBytes / 64 < 4096,
            "warmed gizmo submissions stay below 4 KiB managed allocation per frame " +
            $"with the recording backend (rotate {rotateBytes / 64}, translate {translateBytes / 64})",
            failures);

        int submissions = device.SubmittedPipelineLabels.Count;
        gizmo.Target = null;
        pass.Execute(context);
        gizmo.Target = target;
        gizmo.IsTranslateEnabled = false;
        pass.Execute(context);
        Checks.Expect(device.SubmittedPipelineLabels.Count == submissions,
            "targetless and empty gizmo frames submit no retained geometry", failures);
        gizmo.Mode = TransformGizmoMode.Translate;
        pass.Execute(context);
        Checks.Expect(ReadGeometry(device).AsSpan().SequenceEqual(translate),
            "gizmo geometry recovers after targetless and empty frames", failures);

        gizmo.Mode = TransformGizmoMode.Rotate;
        VerifyUploadFailure(pass, context, device, "gizmo upload failure", failures);
        gizmo.Mode = TransformGizmoMode.Translate;
        pass.Execute(context);
        Checks.Expect(ReadGeometry(device).AsSpan().SequenceEqual(translate),
            "gizmo geometry recovers from an upload failure without retaining larger-frame vertices", failures);

        string? evidencePath = Environment.GetEnvironmentVariable("MU3D_GIZMO_ALLOCATION_EVIDENCE");
        if (!string.IsNullOrEmpty(evidencePath))
        {
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
            {
                Runtime = Environment.Version.ToString(),
                Backend = "portable RecordingGraphicsDevice; not browser/native GPU",
                FramesPerMode = 64,
                RotateTotalBytes = rotateBytes,
                RotateBytesPerFrame = rotateBytes / 64d,
                TranslateTotalBytes = translateBytes,
                TranslateBytesPerFrame = translateBytes / 64d,
                RotateVertexBytes = rotate.Length,
                TranslateVertexBytes = translate.Length,
                BudgetBytesPerFrame = 4096,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

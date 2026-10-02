using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

// Shared recording mechanics only; each scenario retains its own expected geometry and state.
internal static class HelperRenderChecks
{
    internal static long Measure(IRenderPass pass, RenderPassContext context, int frames = 64)
    {
        for (int frame = 0; frame < 8; frame++) pass.Execute(context);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < frames; frame++) pass.Execute(context);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    internal static byte[] ReadGeometry(RecordingGraphicsDevice device)
    {
        RecordingGraphicsBuffer buffer = device.Buffers[^1];
        return buffer.Data.AsSpan(0, buffer.LastWriteLength).ToArray();
    }

    internal static void VerifyUploadFailure(IRenderPass pass, RenderPassContext context,
        RecordingGraphicsDevice device, string description, ICollection<string> failures)
    {
        device.FailNextBufferWrite = true;
        int before = device.SubmittedPipelineLabels.Count;
        ExpectThrows<InvalidOperationException>(() => pass.Execute(context), description, failures,
            "Injected buffer upload failure.");
        Expect(device.SubmittedPipelineLabels.Count == before, description + " submits no incomplete commands", failures);
    }

    internal static Vector3 ReadPosition(ReadOnlySpan<byte> bytes, int vertex) => new(
        BitConverter.ToSingle(bytes.Slice(vertex * 28, 4)),
        BitConverter.ToSingle(bytes.Slice(vertex * 28 + 4, 4)),
        BitConverter.ToSingle(bytes.Slice(vertex * 28 + 8, 4)));

    internal static Vector4 ReadColor(ReadOnlySpan<byte> bytes, int vertex) => new(
        BitConverter.ToSingle(bytes.Slice(vertex * 28 + 12, 4)),
        BitConverter.ToSingle(bytes.Slice(vertex * 28 + 16, 4)),
        BitConverter.ToSingle(bytes.Slice(vertex * 28 + 20, 4)),
        BitConverter.ToSingle(bytes.Slice(vertex * 28 + 24, 4)));

    internal static Vector4 Premultiply(LinearRgba color) => new(
        color.Red * color.Alpha, color.Green * color.Alpha, color.Blue * color.Alpha, color.Alpha);

    internal static void Expect(bool condition, string description, ICollection<string> failures)
    {
        if (!condition) failures.Add(description);
    }

    internal static void ExpectThrows<TException>(Action action, string description,
        ICollection<string> failures, string? message = null) where TException : Exception =>
        ExpectThrows(action, error => error is TException && (message is null || error.Message == message),
            description, failures);

    internal static void ExpectThrows(Action action, Func<Exception, bool> expected,
        string description, ICollection<string> failures)
    {
        try { action(); failures.Add(description + ": expected exception"); }
        catch (Exception error)
        {
            if (!expected(error)) failures.Add(description + ": unexpected " + error.GetType().Name + ": " + error.Message);
        }
    }
}

internal sealed class NonPerspectiveCamera : Camera
{
    public override Matrix4x4 ProjectionMatrix => Matrix4x4.Identity;
}

internal sealed class HelperRenderFixture : IDisposable
{
    private readonly GraphicsTexture? depth;

    internal HelperRenderFixture(string name, uint width = 512, uint height = 512,
        bool hasDepth = false, float cameraZ = 5f)
    {
        Scene = new(name);
        Camera.Transform.Position = new(0f, 0f, cameraZ);
        Color = Device.CreateTexture(new(new(width, height), GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment, label: name + " HDR color"));
        if (hasDepth)
            depth = Device.CreateTexture(new(new(width, height), GraphicsTextureFormat.Depth32Float,
                GraphicsTextureUsage.RenderAttachment));
    }

    internal Scene Scene { get; }
    internal PerspectiveCamera Camera { get; } = new(aspectRatio: 1f);
    internal RecordingGraphicsDevice Device { get; } = new();
    internal GraphicsTexture Color { get; }
    internal GraphicsTexture Depth => depth ?? throw new InvalidOperationException("This fixture has no depth attachment.");
    internal RenderPassContext Context => new(Scene, Camera, Color, depth, StandardColorSpaces.LinearSrgb,
        colorTargetInitialized: true, depthTargetInitialized: depth is not null);

    public void Dispose()
    {
        depth?.Dispose();
        Color.Dispose();
        Device.Dispose();
    }
}

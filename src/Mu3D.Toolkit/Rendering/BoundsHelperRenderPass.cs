using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Helpers;

namespace Mu3D.Toolkit.Rendering;

/// <summary>Identifies how bounds-helper lines interact with scene depth.</summary>
public enum BoundsHelperDepthMode
{
    /// <summary>Loads scene depth and hides line fragments behind rendered geometry.</summary>
    SceneDepthTested,

    /// <summary>Renders every line over scene color without a depth attachment.</summary>
    Overlay,
}

/// <summary>Defines an immutable HDR-linear bounds-helper appearance.</summary>
public sealed class BoundsHelperRenderStyle
{
    /// <summary>Initializes a bounds-helper rendering style.</summary>
    /// <param name="color">HDR-linear source-over line color.</param>
    /// <param name="lineWidthPixels">Positive line width in physical pixels.</param>
    /// <param name="depthMode">How lines interact with initialized scene depth.</param>
    public BoundsHelperRenderStyle(
        LinearRgba color,
        float lineWidthPixels = 2.4f,
        BoundsHelperDepthMode depthMode = BoundsHelperDepthMode.SceneDepthTested)
    {
        if (color.ColorSpace is not StandardRgbColorSpaceReference)
        {
            throw new NotSupportedException(
                $"{nameof(color)} must use one of Mu3D's built-in standard linear RGB spaces.");
        }
        if (!float.IsFinite(lineWidthPixels) || lineWidthPixels <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(lineWidthPixels));
        }
        if (!Enum.IsDefined(depthMode))
        {
            throw new ArgumentOutOfRangeException(nameof(depthMode));
        }
        Color = color;
        LineWidthPixels = lineWidthPixels;
        DepthMode = depthMode;
    }

    /// <summary>Gets the default high-visibility HDR-linear style.</summary>
    public static BoundsHelperRenderStyle Default { get; } = new(
        new LinearRgba(1.45f, 0.52f, 0.035f, 1f, StandardColorSpaces.LinearSrgb));

    /// <summary>Gets the HDR-linear line color.</summary>
    public LinearRgba Color { get; }

    /// <summary>Gets the line width in physical pixels.</summary>
    public float LineWidthPixels { get; }

    /// <summary>Gets how lines interact with scene depth.</summary>
    public BoundsHelperDepthMode DepthMode { get; }
}

/// <summary>Renders one target's current world-axis-aligned bounds over initialized scene color.</summary>
/// <remarks>
/// Each execution restricts the borrowed target subtree to the context scene and each mesh's
/// camera layers. Detached or foreign-scene targets contribute nothing. The helper's public
/// geometry query remains independent of scene membership and camera context.
/// </remarks>
public sealed class BoundsHelperRenderPass : IRenderPass, IDisposable
{
    private const int VertexStride = 28;
    private const int MaximumVertexCount = 12 * 6;
    private const float ProjectionEpsilon = 0.000001f;
    private const string ShaderCode = """
        struct VertexInput {
            @location(0) position: vec3f,
            @location(1) color: vec4f,
        };

        struct VertexOutput {
            @builtin(position) position: vec4f,
            @location(0) color: vec4f,
        };

        @vertex fn vs_main(input: VertexInput) -> VertexOutput {
            var output: VertexOutput;
            output.position = vec4f(input.position, 1.0);
            output.color = input.color;
            return output;
        }

        @fragment fn fs_main(input: VertexOutput) -> @location(0) vec4f {
            return input.color;
        }
        """;

    private static readonly (int Start, int End)[] Edges =
    [
        (0, 1), (2, 3), (4, 5), (6, 7),
        (0, 2), (1, 3), (4, 6), (5, 7),
        (0, 4), (1, 5), (2, 6), (3, 7),
    ];

    private readonly StandardRgbColorSpaceReference workingColorSpace;
    private readonly Vector4 color;
    private readonly List<BoundsVertex> vertices = new(MaximumVertexCount);
    private readonly string encoderLabel;
    private readonly string commandsLabel;
    private GraphicsDevice? resourceDevice;
    private GraphicsTextureFormat resourceColorFormat;
    private GraphicsShaderModule? shader;
    private GraphicsRenderPipeline? pipeline;
    private GraphicsBuffer? vertexBuffer;
    private ulong vertexBufferCapacity;
    private bool disposed;

    /// <summary>Initializes a pass with the default style and linear-sRGB working space.</summary>
    public BoundsHelperRenderPass(BoundsHelper helper, string name = "Bounds Helper")
        : this(helper, BoundsHelperRenderStyle.Default, StandardColorSpaces.LinearSrgb, name)
    {
    }

    /// <summary>Initializes a pass with an explicit style and standard linear working space.</summary>
    public BoundsHelperRenderPass(
        BoundsHelper helper,
        BoundsHelperRenderStyle style,
        StandardRgbColorSpaceReference workingColorSpace,
        string name = "Bounds Helper")
    {
        ArgumentNullException.ThrowIfNull(helper);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(workingColorSpace);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!workingColorSpace.IsLinear)
        {
            throw new ArgumentException(
                "The bounds-helper working space must be linear-light.",
                nameof(workingColorSpace));
        }
        Helper = helper;
        Style = style;
        this.workingColorSpace = workingColorSpace;
        color = ConvertAndPremultiply(style.Color, workingColorSpace);
        encoderLabel = $"{name} encoder";
        commandsLabel = $"{name} commands";
        Descriptor = new RenderPassDescriptor(
            name,
            workingColorSpace,
            new RenderPassColorAttachmentPolicy(
                GraphicsLoadOperation.Load,
                new LinearRgba(0f, 0f, 0f, 0f, workingColorSpace)),
            style.DepthMode == BoundsHelperDepthMode.SceneDepthTested
                ? new RenderPassDepthAttachmentPolicy(GraphicsLoadOperation.Load)
                : null);
    }

    /// <summary>Gets the borrowed bounds definition evaluated for each frame.</summary>
    public BoundsHelper Helper { get; }

    /// <summary>Gets the immutable rendering style.</summary>
    public BoundsHelperRenderStyle Style { get; }

    /// <inheritdoc />
    public RenderPassDescriptor Descriptor { get; }

    /// <inheritdoc />
    public void Execute(RenderPassContext context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (context.WorkingColorSpace != workingColorSpace)
        {
            throw new InvalidOperationException(
                $"The bounds pass uses {workingColorSpace.Name}, but the context uses {context.WorkingColorSpace.Name}.");
        }
        if (!context.ColorTargetInitialized)
        {
            throw new InvalidOperationException(
                "The bounds-helper pass must follow a pass that initializes color.");
        }
        GraphicsTexture? depthTarget = null;
        if (Style.DepthMode == BoundsHelperDepthMode.SceneDepthTested)
        {
            if (!context.DepthTargetInitialized)
            {
                throw new InvalidOperationException(
                    "The scene-depth-tested bounds helper requires initialized depth.");
            }
            depthTarget = context.DepthTarget ?? throw new InvalidOperationException(
                "The scene-depth-tested bounds helper requires a Depth32Float attachment.");
            if (depthTarget.Descriptor.Format != GraphicsTextureFormat.Depth32Float)
            {
                throw new InvalidOperationException(
                    "The scene-depth-tested bounds helper requires a Depth32Float attachment.");
            }
        }
        if (context.Camera is not PerspectiveCamera camera)
        {
            throw new InvalidOperationException("The bounds helper currently requires a perspective camera.");
        }
        // Twelve box edges emit at most seventy-two numeric vertices. Reuse that bounded storage
        // across frames/resource changes; Queue.WriteBuffer consumes the span before returning.
        vertices.Clear();
        try
        {
            if (!Helper.TryGetWorldBounds(context.Scene, camera.VisibilityMask, out Bounds3D bounds))
            {
                return;
            }
            BuildGeometry(
                vertices,
                bounds,
                camera.ViewProjectionMatrix,
                context.OutputExtent.Width,
                context.OutputExtent.Height);
            if (vertices.Count == 0)
            {
                return;
            }

            EnsurePipeline(context.Device, context.ColorTarget.Descriptor.Format);
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(vertices));
            EnsureVertexBuffer(context.Device, checked((ulong)bytes.Length));
            context.Device.Queue.WriteBuffer(vertexBuffer!, 0, bytes);
            using GraphicsCommandEncoder encoder = context.Device.CreateCommandEncoder(encoderLabel);
            GraphicsRenderPassDepthAttachment? depthAttachment = depthTarget is null
                ? null
                : new GraphicsRenderPassDepthAttachment(
                    depthTarget,
                    GraphicsLoadOperation.Load,
                    GraphicsStoreOperation.Store);
            using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassColorAttachment(
                        context.ColorTarget,
                        GraphicsLoadOperation.Load,
                        GraphicsStoreOperation.Store),
                    Descriptor.Name,
                    depthAttachment)))
            {
                pass.SetPipeline(pipeline!);
                pass.SetVertexBuffer(0, vertexBuffer!, 0, checked((ulong)bytes.Length));
                pass.Draw(checked((uint)vertices.Count));
            }
            using GraphicsCommandBuffer commands = encoder.Finish(commandsLabel);
            context.Device.Queue.Submit(commands);
        }
        finally
        {
            vertices.Clear();
        }
    }

    /// <summary>Releases graphics resources without disposing the borrowed helper or target.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        vertices.Clear();
        vertices.TrimExcess();
        ReleaseResources();
        GC.SuppressFinalize(this);
    }

    private void BuildGeometry(
        List<BoundsVertex> vertices,
        Bounds3D bounds,
        Matrix4x4 viewProjection,
        uint width,
        uint height)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        for (int index = 0; index < corners.Length; index++)
        {
            corners[index] = new Vector3(
                (index & 1) == 0 ? bounds.Minimum.X : bounds.Maximum.X,
                (index & 2) == 0 ? bounds.Minimum.Y : bounds.Maximum.Y,
                (index & 4) == 0 ? bounds.Minimum.Z : bounds.Maximum.Z);
        }
        foreach ((int start, int end) in Edges)
        {
            AddWorldSegment(
                vertices,
                corners[start],
                corners[end],
                viewProjection,
                width,
                height);
        }
    }

    private void AddWorldSegment(
        List<BoundsVertex> vertices,
        Vector3 worldStart,
        Vector3 worldEnd,
        Matrix4x4 viewProjection,
        uint width,
        uint height)
    {
        if (!TryProjectSegment(
            worldStart,
            worldEnd,
            viewProjection,
            width,
            height,
            out ProjectedPoint start,
            out ProjectedPoint end))
        {
            return;
        }
        Vector2 delta = end.Screen - start.Screen;
        float lengthSquared = delta.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
        {
            return;
        }
        Vector2 normal = new(-delta.Y, delta.X);
        normal *= (Style.LineWidthPixels * 0.5f) / MathF.Sqrt(lengthSquared);
        AddQuad(
            vertices,
            ToNdc(start.Screen + normal, start.Depth, width, height),
            ToNdc(start.Screen - normal, start.Depth, width, height),
            ToNdc(end.Screen - normal, end.Depth, width, height),
            ToNdc(end.Screen + normal, end.Depth, width, height));
    }

    private static bool TryProjectSegment(
        Vector3 worldStart,
        Vector3 worldEnd,
        Matrix4x4 viewProjection,
        uint width,
        uint height,
        out ProjectedPoint projectedStart,
        out ProjectedPoint projectedEnd)
    {
        Vector4 clipStart = Vector4.Transform(new Vector4(worldStart, 1f), viewProjection);
        Vector4 clipEnd = Vector4.Transform(new Vector4(worldEnd, 1f), viewProjection);
        if (!IsFinite(clipStart) || !IsFinite(clipEnd))
        {
            projectedStart = default;
            projectedEnd = default;
            return false;
        }
        float minimum = 0f;
        float maximum = 1f;
        if (!ClipHalfSpace(clipStart.W - ProjectionEpsilon, clipEnd.W - ProjectionEpsilon, ref minimum, ref maximum) ||
            !ClipHalfSpace(clipStart.X + clipStart.W, clipEnd.X + clipEnd.W, ref minimum, ref maximum) ||
            !ClipHalfSpace(clipStart.W - clipStart.X, clipEnd.W - clipEnd.X, ref minimum, ref maximum) ||
            !ClipHalfSpace(clipStart.Y + clipStart.W, clipEnd.Y + clipEnd.W, ref minimum, ref maximum) ||
            !ClipHalfSpace(clipStart.W - clipStart.Y, clipEnd.W - clipEnd.Y, ref minimum, ref maximum) ||
            !ClipHalfSpace(clipStart.Z, clipEnd.Z, ref minimum, ref maximum) ||
            !ClipHalfSpace(clipStart.W - clipStart.Z, clipEnd.W - clipEnd.Z, ref minimum, ref maximum))
        {
            projectedStart = default;
            projectedEnd = default;
            return false;
        }
        Vector4 difference = clipEnd - clipStart;
        bool startProjected = TryProjectClip(
            clipStart + difference * minimum,
            width,
            height,
            out projectedStart);
        bool endProjected = TryProjectClip(
            clipStart + difference * maximum,
            width,
            height,
            out projectedEnd);
        return startProjected && endProjected;
    }

    private static bool ClipHalfSpace(
        float startValue,
        float endValue,
        ref float minimum,
        ref float maximum)
    {
        if (startValue < 0f && endValue < 0f)
        {
            return false;
        }
        if (startValue >= 0f && endValue >= 0f)
        {
            return true;
        }
        float intersection = startValue / (startValue - endValue);
        if (startValue < 0f)
        {
            minimum = MathF.Max(minimum, intersection);
        }
        else
        {
            maximum = MathF.Min(maximum, intersection);
        }
        return minimum <= maximum;
    }

    private static bool TryProjectClip(
        Vector4 clip,
        uint width,
        uint height,
        out ProjectedPoint projected)
    {
        if (!IsFinite(clip) || clip.W < ProjectionEpsilon)
        {
            projected = default;
            return false;
        }
        float inverseW = 1f / clip.W;
        Vector2 screen = new(
            (clip.X * inverseW + 1f) * 0.5f * width,
            (1f - clip.Y * inverseW) * 0.5f * height);
        if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y))
        {
            projected = default;
            return false;
        }
        projected = new ProjectedPoint(screen, Math.Clamp(clip.Z * inverseW, 0f, 1f));
        return true;
    }

    private void AddQuad(
        List<BoundsVertex> vertices,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector3 fourth)
    {
        vertices.Add(new BoundsVertex(first, color));
        vertices.Add(new BoundsVertex(second, color));
        vertices.Add(new BoundsVertex(third, color));
        vertices.Add(new BoundsVertex(first, color));
        vertices.Add(new BoundsVertex(third, color));
        vertices.Add(new BoundsVertex(fourth, color));
    }

    private static Vector3 ToNdc(Vector2 screen, float depth, uint width, uint height) => new(
        screen.X * (2f / width) - 1f,
        1f - screen.Y * (2f / height),
        depth);

    private void EnsurePipeline(GraphicsDevice device, GraphicsTextureFormat colorFormat)
    {
        if (ReferenceEquals(resourceDevice, device) && resourceColorFormat == colorFormat && pipeline is not null)
        {
            return;
        }
        ReleaseResources();
        resourceDevice = device;
        resourceColorFormat = colorFormat;
        shader = device.CreateShaderModule(
            new GraphicsShaderModuleDescriptor(ShaderCode, $"{Descriptor.Name} shader"));
        pipeline = device.CreateRenderPipeline(
            new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_main",
                shader,
                "fs_main",
                colorFormat,
                label: $"{Descriptor.Name} pipeline",
                vertexBuffers:
                [
                    new GraphicsVertexBufferLayout(
                        VertexStride,
                        [
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 0, 0),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 12, 1),
                        ])
                ],
                depthStencil: Style.DepthMode == BoundsHelperDepthMode.SceneDepthTested
                    ? new GraphicsDepthStencilState(
                        GraphicsTextureFormat.Depth32Float,
                        depthWriteEnabled: false,
                        depthCompare: GraphicsCompareFunction.LessEqual)
                    : null,
                cullMode: GraphicsCullMode.None,
                blend: GraphicsBlendState.PremultipliedAlpha));
    }

    private void EnsureVertexBuffer(GraphicsDevice device, ulong requiredBytes)
    {
        if (vertexBuffer is not null && vertexBufferCapacity >= requiredBytes)
        {
            return;
        }
        vertexBuffer?.Dispose();
        vertexBufferCapacity = 4096;
        while (vertexBufferCapacity < requiredBytes)
        {
            vertexBufferCapacity = checked(vertexBufferCapacity * 2);
        }
        vertexBuffer = device.CreateBuffer(
            new GraphicsBufferDescriptor(
                vertexBufferCapacity,
                GraphicsBufferUsage.Vertex | GraphicsBufferUsage.CopyDestination,
                $"{Descriptor.Name} vertices"));
    }

    private void ReleaseResources()
    {
        vertexBuffer?.Dispose();
        vertexBuffer = null;
        vertexBufferCapacity = 0;
        pipeline?.Dispose();
        pipeline = null;
        shader?.Dispose();
        shader = null;
        resourceDevice = null;
    }

    private static Vector4 ConvertAndPremultiply(
        LinearRgba source,
        StandardRgbColorSpaceReference destination)
    {
        LinearRgba converted = StandardLinearRgbConverter.Convert(source, destination);
        return new Vector4(
            converted.Red * converted.Alpha,
            converted.Green * converted.Alpha,
            converted.Blue * converted.Alpha,
            converted.Alpha);
    }

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct BoundsVertex(Vector3 Position, Vector4 Color);

    private readonly record struct ProjectedPoint(Vector2 Screen, float Depth);
}

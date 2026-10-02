using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Helpers;

namespace Mu3D.Toolkit.Rendering;

/// <summary>Defines HDR-linear colors and physical-pixel line widths for a grid helper.</summary>
public sealed class GridHelperRenderStyle
{
    /// <summary>Initializes an immutable grid-helper render style.</summary>
    /// <param name="minorLineColor">HDR-linear source-over color for minor lines.</param>
    /// <param name="majorLineColor">HDR-linear source-over color for emphasized lines.</param>
    /// <param name="xAxisColor">HDR-linear source-over X-axis color.</param>
    /// <param name="yAxisColor">HDR-linear source-over Y-axis color.</param>
    /// <param name="zAxisColor">HDR-linear source-over Z-axis color.</param>
    /// <param name="minorLineWidthPixels">Positive minor-line width in physical pixels.</param>
    /// <param name="majorLineWidthPixels">Positive major-line width in physical pixels.</param>
    /// <param name="centerAxisWidthPixels">Positive center-axis width in physical pixels.</param>
    public GridHelperRenderStyle(
        LinearRgba minorLineColor,
        LinearRgba majorLineColor,
        LinearRgba xAxisColor,
        LinearRgba yAxisColor,
        LinearRgba zAxisColor,
        float minorLineWidthPixels = 1f,
        float majorLineWidthPixels = 1.6f,
        float centerAxisWidthPixels = 2.2f)
    {
        ValidateColor(minorLineColor, nameof(minorLineColor));
        ValidateColor(majorLineColor, nameof(majorLineColor));
        ValidateColor(xAxisColor, nameof(xAxisColor));
        ValidateColor(yAxisColor, nameof(yAxisColor));
        ValidateColor(zAxisColor, nameof(zAxisColor));
        ValidatePositiveFinite(minorLineWidthPixels, nameof(minorLineWidthPixels));
        ValidatePositiveFinite(majorLineWidthPixels, nameof(majorLineWidthPixels));
        ValidatePositiveFinite(centerAxisWidthPixels, nameof(centerAxisWidthPixels));
        MinorLineColor = minorLineColor;
        MajorLineColor = majorLineColor;
        XAxisColor = xAxisColor;
        YAxisColor = yAxisColor;
        ZAxisColor = zAxisColor;
        MinorLineWidthPixels = minorLineWidthPixels;
        MajorLineWidthPixels = majorLineWidthPixels;
        CenterAxisWidthPixels = centerAxisWidthPixels;
    }

    /// <summary>Gets the default translucent HDR-linear grid style.</summary>
    public static GridHelperRenderStyle Default { get; } = new(
        new LinearRgba(0.23f, 0.28f, 0.38f, 0.24f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(0.42f, 0.5f, 0.68f, 0.42f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(1.2f, 0.05f, 0.05f, 0.85f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(0.05f, 1.2f, 0.05f, 0.85f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(0.08f, 0.28f, 1.2f, 0.85f, StandardColorSpaces.LinearSrgb));

    /// <summary>Gets the minor-line color.</summary>
    public LinearRgba MinorLineColor { get; }

    /// <summary>Gets the emphasized major-line color.</summary>
    public LinearRgba MajorLineColor { get; }

    /// <summary>Gets the X-axis color.</summary>
    public LinearRgba XAxisColor { get; }

    /// <summary>Gets the Y-axis color.</summary>
    public LinearRgba YAxisColor { get; }

    /// <summary>Gets the Z-axis color.</summary>
    public LinearRgba ZAxisColor { get; }

    /// <summary>Gets the minor-line width in physical pixels.</summary>
    public float MinorLineWidthPixels { get; }

    /// <summary>Gets the major-line width in physical pixels.</summary>
    public float MajorLineWidthPixels { get; }

    /// <summary>Gets the center-axis width in physical pixels.</summary>
    public float CenterAxisWidthPixels { get; }

    private static void ValidateColor(LinearRgba color, string parameterName)
    {
        if (color.ColorSpace is not StandardRgbColorSpaceReference)
        {
            throw new NotSupportedException(
                $"{parameterName} must use one of Mu3D's built-in standard linear RGB spaces.");
        }
    }

    private static void ValidatePositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>Renders one finite scene-space grid against initialized scene depth.</summary>
/// <remarks>
/// The pass loads HDR-linear scene color and depth, writes no depth, and uses premultiplied-alpha
/// blending. The borrowed helper remains independent of scene bounds, selection, and export.
/// The pass reuses bounded, value-only vertex staging while reevaluating the current helper,
/// camera, and physical output extent on every execution. Disposal releases the staging capacity
/// together with the graphics resources.
/// </remarks>
public sealed class GridHelperRenderPass : IRenderPass, IDisposable
{
    private const int VertexStride = 28;
    private const int MaximumVertexCount = 12 * (GridHelper.MaximumDivisions + 2);
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

    private readonly StandardRgbColorSpaceReference workingColorSpace;
    private readonly Vector4 minorColor;
    private readonly Vector4 majorColor;
    private readonly Vector4 xColor;
    private readonly Vector4 yColor;
    private readonly Vector4 zColor;
    private readonly List<GridVertex> vertices = [];
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
    /// <param name="helper">Borrowed grid definition evaluated for each frame.</param>
    /// <param name="name">Non-empty diagnostic pass name.</param>
    public GridHelperRenderPass(GridHelper helper, string name = "Grid Helper")
        : this(helper, GridHelperRenderStyle.Default, StandardColorSpaces.LinearSrgb, name)
    {
    }

    /// <summary>Initializes a pass with an explicit style and standard linear working space.</summary>
    /// <param name="helper">Borrowed grid definition evaluated for each frame.</param>
    /// <param name="style">Immutable HDR-linear rendering style.</param>
    /// <param name="workingColorSpace">Standard linear RGB working space used by the renderer.</param>
    /// <param name="name">Non-empty diagnostic pass name.</param>
    public GridHelperRenderPass(
        GridHelper helper,
        GridHelperRenderStyle style,
        StandardRgbColorSpaceReference workingColorSpace,
        string name = "Grid Helper")
    {
        ArgumentNullException.ThrowIfNull(helper);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(workingColorSpace);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!workingColorSpace.IsLinear)
        {
            throw new ArgumentException(
                "The grid-helper working space must be linear-light.",
                nameof(workingColorSpace));
        }
        Helper = helper;
        Style = style;
        this.workingColorSpace = workingColorSpace;
        minorColor = ConvertAndPremultiply(style.MinorLineColor, workingColorSpace);
        majorColor = ConvertAndPremultiply(style.MajorLineColor, workingColorSpace);
        xColor = ConvertAndPremultiply(style.XAxisColor, workingColorSpace);
        yColor = ConvertAndPremultiply(style.YAxisColor, workingColorSpace);
        zColor = ConvertAndPremultiply(style.ZAxisColor, workingColorSpace);
        encoderLabel = $"{name} encoder";
        commandsLabel = $"{name} commands";
        Descriptor = new RenderPassDescriptor(
            name,
            workingColorSpace,
            new RenderPassColorAttachmentPolicy(
                GraphicsLoadOperation.Load,
                new LinearRgba(0f, 0f, 0f, 0f, workingColorSpace)),
            new RenderPassDepthAttachmentPolicy(GraphicsLoadOperation.Load));
    }

    /// <summary>Gets the borrowed grid definition.</summary>
    public GridHelper Helper { get; }

    /// <summary>Gets the immutable render style.</summary>
    public GridHelperRenderStyle Style { get; }

    /// <inheritdoc />
    public RenderPassDescriptor Descriptor { get; }

    /// <inheritdoc />
    public void Execute(RenderPassContext context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (context.WorkingColorSpace != workingColorSpace)
        {
            throw new InvalidOperationException(
                $"The grid pass uses {workingColorSpace.Name}, but the context uses {context.WorkingColorSpace.Name}.");
        }
        if (!context.ColorTargetInitialized || !context.DepthTargetInitialized)
        {
            throw new InvalidOperationException(
                "The grid-helper pass requires initialized scene color and depth.");
        }
        GraphicsTexture depth = context.DepthTarget ??
            throw new InvalidOperationException(
                "The grid-helper pass requires a Depth32Float attachment.");
        if (depth.Descriptor.Format != GraphicsTextureFormat.Depth32Float)
        {
            throw new InvalidOperationException(
                "The grid-helper pass requires a Depth32Float attachment.");
        }
        if (context.Camera is not PerspectiveCamera camera)
        {
            throw new InvalidOperationException("The grid helper currently requires a perspective camera.");
        }

        // Queue.WriteBuffer consumes the numeric staging span before returning. Retain bounded
        // capacity across frames/resource changes, but discard logical contents on every exit.
        vertices.Clear();
        try
        {
            EnsureStagingCapacity();
            BuildGeometry(
                vertices,
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
            using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassColorAttachment(
                        context.ColorTarget,
                        GraphicsLoadOperation.Load,
                        GraphicsStoreOperation.Store),
                    Descriptor.Name,
                    new GraphicsRenderPassDepthAttachment(
                        depth,
                        GraphicsLoadOperation.Load,
                        GraphicsStoreOperation.Store))))
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

    /// <summary>Releases graphics resources without disposing the borrowed helper.</summary>
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

    private void EnsureStagingCapacity()
    {
        // Two segments per division sample plus two optional center axes, six vertices each.
        // Reserve for current divisions rather than allocating the maximum at construction.
        int requiredVertices = checked(12 * (Helper.Divisions + 2));
        if (vertices.Capacity < requiredVertices)
        {
            vertices.Capacity = Math.Min(MaximumVertexCount,
                Math.Max(requiredVertices, vertices.Capacity * 2));
        }
    }

    private void BuildGeometry(
        List<GridVertex> vertices,
        Matrix4x4 viewProjection,
        uint width,
        uint height)
    {
        (Vector3 firstAxis, Vector3 secondAxis) = Helper.GetPlaneAxes();
        float halfSize = Helper.Size * 0.5f;
        float step = Helper.Size / Helper.Divisions;
        for (int index = 0; index <= Helper.Divisions; index++)
        {
            float coordinate = -halfSize + step * index;
            bool isCenter = MathF.Abs(coordinate) <= Helper.Size * 0.000001f;
            if (isCenter && Helper.ShowCenterAxes)
            {
                continue;
            }
            bool isMajor = Helper.ShowMajorLines && index % Helper.MajorLineEvery == 0;
            if (!isMajor && !Helper.ShowMinorLines)
            {
                continue;
            }
            Vector4 color = isMajor ? majorColor : minorColor;
            float widthPixels = isMajor
                ? Style.MajorLineWidthPixels
                : Style.MinorLineWidthPixels;
            AddWorldSegment(
                vertices,
                Helper.Origin + secondAxis * coordinate - firstAxis * halfSize,
                Helper.Origin + secondAxis * coordinate + firstAxis * halfSize,
                viewProjection,
                width,
                height,
                widthPixels,
                color);
            AddWorldSegment(
                vertices,
                Helper.Origin + firstAxis * coordinate - secondAxis * halfSize,
                Helper.Origin + firstAxis * coordinate + secondAxis * halfSize,
                viewProjection,
                width,
                height,
                widthPixels,
                color);
        }
        if (!Helper.ShowCenterAxes)
        {
            return;
        }
        AddWorldSegment(
            vertices,
            Helper.Origin - firstAxis * halfSize,
            Helper.Origin + firstAxis * halfSize,
            viewProjection,
            width,
            height,
            Style.CenterAxisWidthPixels,
            GetAxisColor(firstAxis));
        AddWorldSegment(
            vertices,
            Helper.Origin - secondAxis * halfSize,
            Helper.Origin + secondAxis * halfSize,
            viewProjection,
            width,
            height,
            Style.CenterAxisWidthPixels,
            GetAxisColor(secondAxis));
    }

    private void AddWorldSegment(
        List<GridVertex> vertices,
        Vector3 worldStart,
        Vector3 worldEnd,
        Matrix4x4 viewProjection,
        uint width,
        uint height,
        float widthPixels,
        Vector4 color)
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
        normal *= (widthPixels * 0.5f) / MathF.Sqrt(lengthSquared);
        AddQuad(
            vertices,
            ToNdc(start.Screen + normal, start.Depth, width, height),
            ToNdc(start.Screen - normal, start.Depth, width, height),
            ToNdc(end.Screen - normal, end.Depth, width, height),
            ToNdc(end.Screen + normal, end.Depth, width, height),
            color);
    }

    private Vector4 GetAxisColor(Vector3 axis) => axis switch
    {
        { X: 1f, Y: 0f, Z: 0f } => xColor,
        { X: 0f, Y: 1f, Z: 0f } => yColor,
        { X: 0f, Y: 0f, Z: 1f } => zColor,
        _ => throw new InvalidOperationException("The grid-helper axis is not cardinal."),
    };

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
        if (!ClipHalfSpace(
                clipStart.W - ProjectionEpsilon,
                clipEnd.W - ProjectionEpsilon,
                ref minimum,
                ref maximum) ||
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
        Vector4 clippedStart = clipStart + difference * minimum;
        Vector4 clippedEnd = clipStart + difference * maximum;
        bool startProjected = TryProjectClip(clippedStart, width, height, out projectedStart);
        bool endProjected = TryProjectClip(clippedEnd, width, height, out projectedEnd);
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
        float depth = Math.Clamp(clip.Z * inverseW, 0f, 1f);
        Vector2 screen = new(
            (clip.X * inverseW + 1f) * 0.5f * width,
            (1f - clip.Y * inverseW) * 0.5f * height);
        if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y))
        {
            projected = default;
            return false;
        }
        projected = new ProjectedPoint(screen, depth);
        return true;
    }

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static Vector3 ToNdc(Vector2 screen, float depth, uint width, uint height) => new(
        screen.X * (2f / width) - 1f,
        1f - screen.Y * (2f / height),
        depth);

    private static void AddQuad(
        List<GridVertex> vertices,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector3 fourth,
        Vector4 color)
    {
        vertices.Add(new GridVertex(first, color));
        vertices.Add(new GridVertex(second, color));
        vertices.Add(new GridVertex(third, color));
        vertices.Add(new GridVertex(first, color));
        vertices.Add(new GridVertex(third, color));
        vertices.Add(new GridVertex(fourth, color));
    }

    private void EnsurePipeline(GraphicsDevice device, GraphicsTextureFormat colorFormat)
    {
        if (ReferenceEquals(resourceDevice, device) &&
            resourceColorFormat == colorFormat &&
            pipeline is not null)
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
                depthStencil: new GraphicsDepthStencilState(
                    GraphicsTextureFormat.Depth32Float,
                    depthWriteEnabled: false,
                    depthCompare: GraphicsCompareFunction.LessEqual),
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
        LinearRgba color,
        StandardRgbColorSpaceReference destination)
    {
        LinearRgba converted = StandardLinearRgbConverter.Convert(color, destination);
        return new Vector4(
            converted.Red * converted.Alpha,
            converted.Green * converted.Alpha,
            converted.Blue * converted.Alpha,
            converted.Alpha);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct GridVertex(Vector3 Position, Vector4 Color);

    private readonly record struct ProjectedPoint(Vector2 Screen, float Depth);
}

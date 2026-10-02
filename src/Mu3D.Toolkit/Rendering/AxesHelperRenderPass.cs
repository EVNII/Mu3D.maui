using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Helpers;

namespace Mu3D.Toolkit.Rendering;

/// <summary>Defines HDR-linear colors and physical-pixel geometry for an axes helper.</summary>
public sealed class AxesHelperRenderStyle
{
    /// <summary>Initializes an immutable axes-helper render style.</summary>
    /// <param name="xAxisColor">HDR-linear positive-X color.</param>
    /// <param name="yAxisColor">HDR-linear positive-Y color.</param>
    /// <param name="zAxisColor">HDR-linear positive-Z color.</param>
    /// <param name="diagonalColor">HDR-linear two-axis handle color.</param>
    /// <param name="backgroundColor">HDR-linear premultiplied-overlay background color.</param>
    /// <param name="centerColor">HDR-linear center-marker and direction-sign color.</param>
    /// <param name="lineWidthPixels">Positive cardinal-line width in physical pixels.</param>
    /// <param name="axisMarkerRadiusPixels">Positive cardinal-marker radius in physical pixels.</param>
    /// <param name="diagonalMarkerRadiusPixels">Positive diagonal-marker radius in physical pixels.</param>
    public AxesHelperRenderStyle(
        LinearRgba xAxisColor,
        LinearRgba yAxisColor,
        LinearRgba zAxisColor,
        LinearRgba diagonalColor,
        LinearRgba backgroundColor,
        LinearRgba centerColor,
        float lineWidthPixels = 3f,
        float axisMarkerRadiusPixels = 8f,
        float diagonalMarkerRadiusPixels = 4.5f)
    {
        ValidateColor(xAxisColor, nameof(xAxisColor));
        ValidateColor(yAxisColor, nameof(yAxisColor));
        ValidateColor(zAxisColor, nameof(zAxisColor));
        ValidateColor(diagonalColor, nameof(diagonalColor));
        ValidateColor(backgroundColor, nameof(backgroundColor));
        ValidateColor(centerColor, nameof(centerColor));
        ValidatePositiveFinite(lineWidthPixels, nameof(lineWidthPixels));
        ValidatePositiveFinite(axisMarkerRadiusPixels, nameof(axisMarkerRadiusPixels));
        ValidatePositiveFinite(diagonalMarkerRadiusPixels, nameof(diagonalMarkerRadiusPixels));
        XAxisColor = xAxisColor;
        YAxisColor = yAxisColor;
        ZAxisColor = zAxisColor;
        DiagonalColor = diagonalColor;
        BackgroundColor = backgroundColor;
        CenterColor = centerColor;
        LineWidthPixels = lineWidthPixels;
        AxisMarkerRadiusPixels = axisMarkerRadiusPixels;
        DiagonalMarkerRadiusPixels = diagonalMarkerRadiusPixels;
    }

    /// <summary>Gets the default high-visibility scene-linear style.</summary>
    public static AxesHelperRenderStyle Default { get; } = new(
        new LinearRgba(1.5f, 0.04f, 0.04f, 1f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(0.04f, 1.5f, 0.04f, 1f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(0.08f, 0.25f, 1.5f, 1f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(1.2f, 1.2f, 1.2f, 0.85f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(0.015f, 0.02f, 0.035f, 0.72f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(1.35f, 1.35f, 1.35f, 1f, StandardColorSpaces.LinearSrgb));

    /// <summary>Gets the positive-X axis color.</summary>
    public LinearRgba XAxisColor { get; }

    /// <summary>Gets the positive-Y axis color.</summary>
    public LinearRgba YAxisColor { get; }

    /// <summary>Gets the positive-Z axis color.</summary>
    public LinearRgba ZAxisColor { get; }

    /// <summary>Gets the two-axis 45-degree handle color.</summary>
    public LinearRgba DiagonalColor { get; }

    /// <summary>Gets the translucent overlay background color.</summary>
    public LinearRgba BackgroundColor { get; }

    /// <summary>Gets the center-marker and cardinal direction-sign color.</summary>
    public LinearRgba CenterColor { get; }

    /// <summary>Gets the cardinal line width in physical pixels.</summary>
    public float LineWidthPixels { get; }

    /// <summary>Gets the cardinal endpoint radius in physical pixels.</summary>
    public float AxisMarkerRadiusPixels { get; }

    /// <summary>Gets the 45-degree endpoint radius in physical pixels.</summary>
    public float DiagonalMarkerRadiusPixels { get; }

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

/// <summary>Renders one fixed-pixel orientation axes helper over initialized scene color.</summary>
/// <remarks>
/// This pass is always an overlay: it loads prior HDR-linear color and uses no depth attachment.
/// It owns graphics resources and numeric staging, and performs no input acquisition or camera mutation.
/// Bounded value-only handle and vertex staging is reused while reevaluating the current helper,
/// camera, orientation target and physical output extent. Disposal releases that staging capacity.
/// </remarks>
public sealed class AxesHelperRenderPass : IRenderPass, IDisposable
{
    private const int VertexStride = 28;
    private const int CircleSegments = 24;
    // Background, up to eighteen markers and center; six shafts; three plus and three minus signs.
    private const int MaximumVertexCount =
        (AxesHelper.MaximumHandleCount + 2) * CircleSegments * 3 + 6 * 6 + 3 * 12 + 3 * 6;
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
    private readonly Vector4 xColor;
    private readonly Vector4 yColor;
    private readonly Vector4 zColor;
    private readonly Vector4 diagonalColor;
    private readonly Vector4 backgroundColor;
    private readonly Vector4 centerColor;
    private readonly Vector4 darkSignColor;
    private readonly List<AxesVertex> vertices = new(MaximumVertexCount);
    private readonly string encoderLabel;
    private readonly string commandsLabel;
    private AxesHelperHandleLayout[] handles = new AxesHelperHandleLayout[AxesHelper.MaximumHandleCount];
    private GraphicsDevice? resourceDevice;
    private GraphicsTextureFormat resourceColorFormat;
    private GraphicsShaderModule? shader;
    private GraphicsRenderPipeline? pipeline;
    private GraphicsBuffer? vertexBuffer;
    private ulong vertexBufferCapacity;
    private bool disposed;

    /// <summary>Initializes a pass with the default style and linear-sRGB working space.</summary>
    /// <param name="helper">Borrowed helper definition evaluated for each frame.</param>
    /// <param name="name">Non-empty diagnostic pass name.</param>
    public AxesHelperRenderPass(AxesHelper helper, string name = "Axes Helper")
        : this(helper, AxesHelperRenderStyle.Default, StandardColorSpaces.LinearSrgb, name)
    {
    }

    /// <summary>Initializes a pass with an explicit style and standard linear working space.</summary>
    /// <param name="helper">Borrowed helper definition evaluated for each frame.</param>
    /// <param name="style">Immutable HDR-linear rendering style.</param>
    /// <param name="workingColorSpace">Standard linear RGB working space used by the renderer.</param>
    /// <param name="name">Non-empty diagnostic pass name.</param>
    public AxesHelperRenderPass(
        AxesHelper helper,
        AxesHelperRenderStyle style,
        StandardRgbColorSpaceReference workingColorSpace,
        string name = "Axes Helper")
    {
        ArgumentNullException.ThrowIfNull(helper);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(workingColorSpace);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!workingColorSpace.IsLinear)
        {
            throw new ArgumentException(
                "The axes-helper working space must be linear-light.",
                nameof(workingColorSpace));
        }
        Helper = helper;
        Style = style;
        this.workingColorSpace = workingColorSpace;
        xColor = ConvertAndPremultiply(style.XAxisColor, workingColorSpace);
        yColor = ConvertAndPremultiply(style.YAxisColor, workingColorSpace);
        zColor = ConvertAndPremultiply(style.ZAxisColor, workingColorSpace);
        diagonalColor = ConvertAndPremultiply(style.DiagonalColor, workingColorSpace);
        backgroundColor = ConvertAndPremultiply(style.BackgroundColor, workingColorSpace);
        centerColor = ConvertAndPremultiply(style.CenterColor, workingColorSpace);
        darkSignColor = ConvertOpaque(style.BackgroundColor, workingColorSpace);
        encoderLabel = $"{name} encoder";
        commandsLabel = $"{name} commands";
        Descriptor = new RenderPassDescriptor(
            name,
            workingColorSpace,
            new RenderPassColorAttachmentPolicy(
                GraphicsLoadOperation.Load,
                new LinearRgba(0f, 0f, 0f, 0f, workingColorSpace)),
            null);
    }

    /// <summary>Gets the borrowed helper definition.</summary>
    public AxesHelper Helper { get; }

    /// <summary>Gets the immutable render style.</summary>
    public AxesHelperRenderStyle Style { get; }

    /// <inheritdoc />
    public RenderPassDescriptor Descriptor { get; }

    /// <inheritdoc />
    public void Execute(RenderPassContext context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (context.WorkingColorSpace != workingColorSpace)
        {
            throw new InvalidOperationException(
                $"The axes pass uses {workingColorSpace.Name}, but the context uses {context.WorkingColorSpace.Name}.");
        }
        if (!context.ColorTargetInitialized)
        {
            throw new InvalidOperationException(
                "The axes pass must follow a pass that initializes color.");
        }
        if (context.Camera is not PerspectiveCamera camera)
        {
            throw new InvalidOperationException("The axes helper currently requires a perspective camera.");
        }

        // Queue.WriteBuffer consumes the numeric span before returning. Retain capacity across
        // graphics resource changes, but discard all layout/vertex contents on every exit.
        vertices.Clear();
        Array.Clear(handles);
        try
        {
            uint width = context.OutputExtent.Width;
            uint height = context.OutputExtent.Height;
            AxesHelperLayout layout = Helper.CalculateLayout(camera, width, height, handles);
            AddCircle(
                vertices,
                layout.CenterPixels,
                layout.BackgroundRadiusPixels,
                backgroundColor,
                width,
                height);

            Span<AxesHelperHandleLayout> ordered = handles.AsSpan(0, layout.HandleCount);
            SortByViewDepth(ordered);
            foreach (AxesHelperHandleLayout handle in ordered)
            {
                if (handle.Axis == AxesHelperAxis.Diagonal)
                {
                    continue;
                }
                AddSegment(
                    vertices,
                    layout.CenterPixels,
                    handle.ScreenPositionPixels,
                    Style.LineWidthPixels,
                    GetHandleColor(handle),
                    width,
                    height);
            }
            foreach (AxesHelperHandleLayout handle in ordered)
            {
                float radius = handle.Axis == AxesHelperAxis.Diagonal
                    ? Style.DiagonalMarkerRadiusPixels
                    : Style.AxisMarkerRadiusPixels;
                AddCircle(
                    vertices,
                    handle.ScreenPositionPixels,
                    radius,
                    GetHandleColor(handle),
                    width,
                    height);
            }
            foreach (AxesHelperHandleLayout handle in ordered)
            {
                if (handle.Axis != AxesHelperAxis.Diagonal)
                {
                    AddDirectionSign(
                        vertices,
                        handle.ScreenPositionPixels,
                        Style.AxisMarkerRadiusPixels,
                        handle.IsPositive,
                        GetDirectionSignColor(handle),
                        width,
                        height);
                }
            }
            AddCircle(vertices, layout.CenterPixels, 3.5f, centerColor, width, height);
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
                    Descriptor.Name)))
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
            Array.Clear(handles);
        }
    }

    /// <summary>Releases owned graphics resources and staging without disposing the borrowed helper.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        vertices.Clear();
        vertices.TrimExcess();
        Array.Clear(handles);
        handles = [];
        ReleaseResources();
        GC.SuppressFinalize(this);
    }

    private static void SortByViewDepth(Span<AxesHelperHandleLayout> handles)
    {
        // Insertion sort is bounded to eighteen finite-depth values. Equal depths retain the
        // authored handle order, matching stable OrderBy and its premultiplied overlay ordering.
        for (int index = 1; index < handles.Length; index++)
        {
            AxesHelperHandleLayout handle = handles[index];
            int previous = index - 1;
            while (previous >= 0 && handles[previous].ViewDepth > handle.ViewDepth)
            {
                handles[previous + 1] = handles[previous];
                previous--;
            }
            handles[previous + 1] = handle;
        }
    }

    private Vector4 GetHandleColor(AxesHelperHandleLayout handle)
    {
        Vector4 color = handle.Axis switch
        {
            AxesHelperAxis.X => xColor,
            AxesHelperAxis.Y => yColor,
            AxesHelperAxis.Z => zColor,
            AxesHelperAxis.Diagonal => diagonalColor,
            _ => throw new ArgumentOutOfRangeException(nameof(handle)),
        };
        return handle.IsPositive || handle.Axis == AxesHelperAxis.Diagonal
            ? color
            : color * 0.48f;
    }

    private static void AddSegment(
        List<AxesVertex> vertices,
        Vector2 start,
        Vector2 end,
        float widthPixels,
        Vector4 color,
        uint width,
        uint height)
    {
        Vector2 delta = end - start;
        float lengthSquared = delta.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
        {
            return;
        }
        Vector2 normal = new(-delta.Y, delta.X);
        normal *= (widthPixels * 0.5f) / MathF.Sqrt(lengthSquared);
        AddQuad(
            vertices,
            ToNdc(start + normal, width, height),
            ToNdc(start - normal, width, height),
            ToNdc(end - normal, width, height),
            ToNdc(end + normal, width, height),
            color);
    }

    private static void AddCircle(
        List<AxesVertex> vertices,
        Vector2 center,
        float radius,
        Vector4 color,
        uint width,
        uint height)
    {
        Vector3 centerNdc = ToNdc(center, width, height);
        Vector2 previous = center + new Vector2(radius, 0f);
        for (int index = 1; index <= CircleSegments; index++)
        {
            float angle = index * (MathF.PI * 2f / CircleSegments);
            Vector2 current = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            AddTriangle(
                vertices,
                centerNdc,
                ToNdc(previous, width, height),
                ToNdc(current, width, height),
                color);
            previous = current;
        }
    }

    private void AddDirectionSign(
        List<AxesVertex> vertices,
        Vector2 center,
        float markerRadius,
        bool isPositive,
        Vector4 color,
        uint width,
        uint height)
    {
        float halfLength = markerRadius * 0.52f;
        float strokeWidth = MathF.Max(1.75f, markerRadius * 0.27f);
        AddSegment(
            vertices,
            center - new Vector2(halfLength, 0f),
            center + new Vector2(halfLength, 0f),
            strokeWidth,
            color,
            width,
            height);
        if (isPositive)
        {
            AddSegment(
                vertices,
                center - new Vector2(0f, halfLength),
                center + new Vector2(0f, halfLength),
                strokeWidth,
                color,
                width,
                height);
        }
    }

    private Vector4 GetDirectionSignColor(AxesHelperHandleLayout handle)
    {
        Vector4 marker = GetHandleColor(handle);
        float luminance = marker.X * 0.2126f + marker.Y * 0.7152f + marker.Z * 0.0722f;
        return luminance >= 0.45f ? darkSignColor : centerColor;
    }

    private static void AddQuad(
        List<AxesVertex> vertices,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector3 fourth,
        Vector4 color)
    {
        AddTriangle(vertices, first, second, third, color);
        AddTriangle(vertices, first, third, fourth, color);
    }

    private static void AddTriangle(
        List<AxesVertex> vertices,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector4 color)
    {
        vertices.Add(new AxesVertex(first, color));
        vertices.Add(new AxesVertex(second, color));
        vertices.Add(new AxesVertex(third, color));
    }

    private static Vector3 ToNdc(Vector2 screen, uint width, uint height) => new(
        screen.X * (2f / width) - 1f,
        1f - screen.Y * (2f / height),
        0f);

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

    private static Vector4 ConvertOpaque(
        LinearRgba color,
        StandardRgbColorSpaceReference destination)
    {
        LinearRgba converted = StandardLinearRgbConverter.Convert(color, destination);
        return new Vector4(converted.Red, converted.Green, converted.Blue, 1f);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct AxesVertex(Vector3 Position, Vector4 Color);
}

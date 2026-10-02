using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Helpers;

namespace Mu3D.Toolkit.Rendering;

/// <summary>Controls whether transform-gizmo handles participate in scene depth testing.</summary>
public enum TransformGizmoDepthMode
{
    /// <summary>Draws every handle over scene geometry without a depth attachment.</summary>
    Overlay,

    /// <summary>Tests handles against the initialized scene depth without writing depth.</summary>
    SceneDepthTested,
}

/// <summary>Defines explicitly tagged scene-linear colors and physical-pixel sizing for a transform gizmo.</summary>
public sealed class TransformGizmoRenderStyle
{
    /// <summary>Initializes an immutable transform-gizmo render style.</summary>
    /// <param name="xAxisColor">The unpremultiplied scene-linear X-axis color.</param>
    /// <param name="yAxisColor">The unpremultiplied scene-linear Y-axis color.</param>
    /// <param name="zAxisColor">The unpremultiplied scene-linear Z-axis color.</param>
    /// <param name="uniformColor">The unpremultiplied scene-linear uniform-scale color.</param>
    /// <param name="activeColor">The unpremultiplied scene-linear active-handle color.</param>
    /// <param name="lineWidthPixels">The positive line width in physical pixels.</param>
    /// <param name="markerSizePixels">The positive arrowhead and scale-marker size in physical pixels.</param>
    public TransformGizmoRenderStyle(
        LinearRgba xAxisColor,
        LinearRgba yAxisColor,
        LinearRgba zAxisColor,
        LinearRgba uniformColor,
        LinearRgba activeColor,
        float lineWidthPixels = 3f,
        float markerSizePixels = 11f)
        : this(
            xAxisColor,
            yAxisColor,
            zAxisColor,
            uniformColor,
            activeColor,
            TransformGizmoDepthMode.Overlay,
            lineWidthPixels,
            markerSizePixels)
    {
    }

    /// <summary>Initializes an immutable transform-gizmo render style with explicit depth behavior.</summary>
    /// <param name="xAxisColor">The unpremultiplied scene-linear X-axis color.</param>
    /// <param name="yAxisColor">The unpremultiplied scene-linear Y-axis color.</param>
    /// <param name="zAxisColor">The unpremultiplied scene-linear Z-axis color.</param>
    /// <param name="uniformColor">The unpremultiplied scene-linear uniform-scale color.</param>
    /// <param name="activeColor">The unpremultiplied scene-linear active-handle color.</param>
    /// <param name="depthMode">Whether handles overlay or test against scene depth.</param>
    /// <param name="lineWidthPixels">The positive line width in physical pixels.</param>
    /// <param name="markerSizePixels">The positive arrowhead and scale-marker size in physical pixels.</param>
    public TransformGizmoRenderStyle(
        LinearRgba xAxisColor,
        LinearRgba yAxisColor,
        LinearRgba zAxisColor,
        LinearRgba uniformColor,
        LinearRgba activeColor,
        TransformGizmoDepthMode depthMode,
        float lineWidthPixels = 3f,
        float markerSizePixels = 11f)
    {
        ValidateColor(xAxisColor, nameof(xAxisColor));
        ValidateColor(yAxisColor, nameof(yAxisColor));
        ValidateColor(zAxisColor, nameof(zAxisColor));
        ValidateColor(uniformColor, nameof(uniformColor));
        ValidateColor(activeColor, nameof(activeColor));
        ValidatePositiveFinite(lineWidthPixels, nameof(lineWidthPixels));
        ValidatePositiveFinite(markerSizePixels, nameof(markerSizePixels));
        if (!Enum.IsDefined(depthMode))
        {
            throw new ArgumentOutOfRangeException(nameof(depthMode));
        }

        XAxisColor = xAxisColor;
        YAxisColor = yAxisColor;
        ZAxisColor = zAxisColor;
        UniformColor = uniformColor;
        ActiveColor = activeColor;
        DepthMode = depthMode;
        LineWidthPixels = lineWidthPixels;
        MarkerSizePixels = markerSizePixels;
    }

    /// <summary>Gets the default high-visibility HDR-linear style authored in linear sRGB.</summary>
    public static TransformGizmoRenderStyle Default { get; } = new(
        new LinearRgba(1.5f, 0.04f, 0.04f, 1f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(0.04f, 1.5f, 0.04f, 1f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(0.08f, 0.25f, 1.5f, 1f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(1.5f, 1.5f, 1.5f, 1f, StandardColorSpaces.LinearSrgb),
        new LinearRgba(2f, 1.25f, 0.03f, 1f, StandardColorSpaces.LinearSrgb));

    /// <summary>Gets the unpremultiplied scene-linear X-axis color.</summary>
    public LinearRgba XAxisColor { get; }

    /// <summary>Gets the unpremultiplied scene-linear Y-axis color.</summary>
    public LinearRgba YAxisColor { get; }

    /// <summary>Gets the unpremultiplied scene-linear Z-axis color.</summary>
    public LinearRgba ZAxisColor { get; }

    /// <summary>Gets the unpremultiplied scene-linear uniform-scale color.</summary>
    public LinearRgba UniformColor { get; }

    /// <summary>Gets the unpremultiplied scene-linear active-handle color.</summary>
    public LinearRgba ActiveColor { get; }

    /// <summary>Gets whether handles overlay or test against scene depth.</summary>
    public TransformGizmoDepthMode DepthMode { get; }

    /// <summary>Gets the line width in physical pixels.</summary>
    public float LineWidthPixels { get; }

    /// <summary>Gets the arrowhead and scale-marker size in physical pixels.</summary>
    public float MarkerSizePixels { get; }

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
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The physical-pixel size must be positive and finite.");
        }
    }
}

/// <summary>Renders one transform gizmo over an initialized HDR-linear scene.</summary>
/// <remarks>
/// The pass borrows the gizmo and its target, owns only its graphics resources, and performs no
/// selection, input acquisition, undo recording, display encoding, gamut mapping, tone mapping, or
/// clipping. Handle RGB is converted into the declared standard linear working space and remains
/// HDR-capable. The default style is a true overlay and loads only prior color; a scene-depth-tested
/// style additionally requires initialized depth.
/// The target must be an ordinary visible member of the context scene on a camera-enabled layer.
/// Hidden ancestors suppress it; differently layered ancestors and permanent-root flags do not.
/// Ineligible targets submit no geometry without changing the borrowed gizmo or its target.
/// </remarks>
public sealed class TransformGizmoRenderPass : IRenderPass, IDisposable
{
    private const int VertexStride = 28;
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
    private readonly Vector4 xColor;
    private readonly Vector4 yColor;
    private readonly Vector4 zColor;
    private readonly Vector4 uniformColor;
    private readonly Vector4 activeColor;
    private readonly List<GizmoVertex> vertices = [];
    private readonly string encoderLabel;
    private readonly string commandsLabel;
    private GraphicsDevice? resourceDevice;
    private GraphicsTextureFormat resourceColorFormat;
    private GraphicsShaderModule? shader;
    private GraphicsRenderPipeline? pipeline;
    private GraphicsBuffer? vertexBuffer;
    private ulong vertexBufferCapacity;
    private bool disposed;

    /// <summary>Initializes a render pass using the default style and linear-sRGB working space.</summary>
    /// <param name="gizmo">The borrowed transform gizmo whose current handles are rendered.</param>
    /// <param name="name">The stable non-empty diagnostic pass name.</param>
    public TransformGizmoRenderPass(TransformGizmo gizmo, string name = "Transform Gizmo")
        : this(gizmo, TransformGizmoRenderStyle.Default, StandardColorSpaces.LinearSrgb, name)
    {
    }

    /// <summary>Initializes a render pass with an explicit style and standard linear working space.</summary>
    /// <param name="gizmo">The borrowed transform gizmo whose current handles are rendered.</param>
    /// <param name="style">The immutable physical-pixel and scene-linear render style.</param>
    /// <param name="workingColorSpace">The standard linear RGB space retained by the enclosing pipeline.</param>
    /// <param name="name">The stable non-empty diagnostic pass name.</param>
    public TransformGizmoRenderPass(
        TransformGizmo gizmo,
        TransformGizmoRenderStyle style,
        StandardRgbColorSpaceReference workingColorSpace,
        string name = "Transform Gizmo")
    {
        ArgumentNullException.ThrowIfNull(gizmo);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(workingColorSpace);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!workingColorSpace.IsLinear)
        {
            throw new ArgumentException("The gizmo working space must be linear-light.", nameof(workingColorSpace));
        }

        Gizmo = gizmo;
        Style = style;
        this.workingColorSpace = workingColorSpace;
        xColor = ConvertAndPremultiply(style.XAxisColor, workingColorSpace);
        yColor = ConvertAndPremultiply(style.YAxisColor, workingColorSpace);
        zColor = ConvertAndPremultiply(style.ZAxisColor, workingColorSpace);
        uniformColor = ConvertAndPremultiply(style.UniformColor, workingColorSpace);
        activeColor = ConvertAndPremultiply(style.ActiveColor, workingColorSpace);
        encoderLabel = $"{name} encoder";
        commandsLabel = $"{name} commands";
        Descriptor = new RenderPassDescriptor(
            name,
            workingColorSpace,
            new RenderPassColorAttachmentPolicy(
                GraphicsLoadOperation.Load,
                new LinearRgba(0f, 0f, 0f, 0f, workingColorSpace)),
            style.DepthMode == TransformGizmoDepthMode.SceneDepthTested
                ? new RenderPassDepthAttachmentPolicy(GraphicsLoadOperation.Load)
                : null);
    }

    /// <summary>Gets the borrowed transform gizmo rendered by this pass.</summary>
    public TransformGizmo Gizmo { get; }

    /// <summary>Gets the immutable render style.</summary>
    public TransformGizmoRenderStyle Style { get; }

    /// <inheritdoc />
    public RenderPassDescriptor Descriptor { get; }

    /// <inheritdoc />
    public void Execute(RenderPassContext context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (context.WorkingColorSpace != workingColorSpace)
        {
            throw new InvalidOperationException(
                $"The gizmo pass uses {workingColorSpace.Name}, but the context uses {context.WorkingColorSpace.Name}.");
        }
        if (!context.ColorTargetInitialized)
        {
            throw new InvalidOperationException(
                "The gizmo pass must follow a pass that initializes color.");
        }
        GraphicsTexture? depthTarget = null;
        if (Style.DepthMode == TransformGizmoDepthMode.SceneDepthTested)
        {
            if (!context.DepthTargetInitialized)
            {
                throw new InvalidOperationException(
                    "The scene-depth-tested gizmo pass requires initialized depth.");
            }
            depthTarget = context.DepthTarget ??
                throw new InvalidOperationException(
                    "The scene-depth-tested gizmo pass requires a Depth32Float attachment.");
        }
        if (context.Camera is not PerspectiveCamera camera)
        {
            throw new InvalidOperationException("The transform gizmo currently requires a perspective camera.");
        }
        SceneNode? target = Gizmo.Target;
        if (target is null || !SceneTargetEligibility.IsVisible(context.Scene, target, camera.VisibilityMask))
        {
            return;
        }

        uint width = context.OutputExtent.Width;
        uint height = context.OutputExtent.Height;
        float worldSize = Gizmo.CalculateWorldSize(camera, height);
        if (worldSize == 0f)
        {
            return;
        }

        // The queue consumes this value-only staging span before returning. Retain capacity,
        // but clear the logical contents after every attempt, including upload failures.
        vertices.Clear();
        try
        {
            BuildGeometry(vertices, target, camera, width, height, worldSize);
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

    /// <summary>Releases graphics resources without disposing the borrowed gizmo or target.</summary>
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
        List<GizmoVertex> vertices,
        SceneNode target,
        PerspectiveCamera camera,
        uint width,
        uint height,
        float worldSize)
    {
        Matrix4x4 targetWorld = target.WorldMatrix;
        Vector3 origin = Vector3.Transform(Vector3.Zero, targetWorld);
        Matrix4x4 viewProjection = camera.ViewProjectionMatrix;
        if (Gizmo.IsScaleEnabled &&
            TryProject(origin, viewProjection, width, height, out ProjectedPoint projectedOrigin))
        {
            AddDiamond(
                vertices,
                projectedOrigin,
                Style.MarkerSizePixels * 0.55f,
                GetColor(TransformGizmoMode.Scale, TransformGizmoAxis.Uniform));
        }

        AddModeGeometry(
            vertices,
            TransformGizmoMode.Rotate,
            targetWorld,
            origin,
            worldSize,
            viewProjection,
            width,
            height);
        AddModeGeometry(
            vertices,
            TransformGizmoMode.Scale,
            targetWorld,
            origin,
            worldSize,
            viewProjection,
            width,
            height);
        AddModeGeometry(
            vertices,
            TransformGizmoMode.Translate,
            targetWorld,
            origin,
            worldSize,
            viewProjection,
            width,
            height);
    }

    private void AddModeGeometry(
        List<GizmoVertex> vertices,
        TransformGizmoMode mode,
        Matrix4x4 targetWorld,
        Vector3 origin,
        float worldSize,
        Matrix4x4 viewProjection,
        uint width,
        uint height)
    {
        if (!Gizmo.IsModeEnabled(mode))
        {
            return;
        }
        TransformGizmoSpace handleSpace = mode == TransformGizmoMode.Scale
            ? TransformGizmoSpace.Local
            : Gizmo.Space;
        AddAxisGeometry(
            vertices,
            TransformGizmoAxis.X,
            GetAxisDirection(handleSpace, targetWorld, TransformGizmoAxis.X),
            origin,
            worldSize,
            mode,
            viewProjection,
            width,
            height);
        AddAxisGeometry(
            vertices,
            TransformGizmoAxis.Y,
            GetAxisDirection(handleSpace, targetWorld, TransformGizmoAxis.Y),
            origin,
            worldSize,
            mode,
            viewProjection,
            width,
            height);
        AddAxisGeometry(
            vertices,
            TransformGizmoAxis.Z,
            GetAxisDirection(handleSpace, targetWorld, TransformGizmoAxis.Z),
            origin,
            worldSize,
            mode,
            viewProjection,
            width,
            height);
    }

    private void AddAxisGeometry(
        List<GizmoVertex> vertices,
        TransformGizmoAxis axis,
        Vector3 direction,
        Vector3 origin,
        float worldSize,
        TransformGizmoMode mode,
        Matrix4x4 viewProjection,
        uint width,
        uint height)
    {
        Vector4 color = GetColor(mode, axis);
        if (mode == TransformGizmoMode.Rotate)
        {
            AddRotationRing(
                vertices,
                direction,
                origin,
                worldSize * TransformGizmoHitTester.RotationRadiusFraction,
                color,
                viewProjection,
                width,
                height);
            return;
        }

        float endFraction = mode == TransformGizmoMode.Scale
            ? TransformGizmoHitTester.ScaleAxisEndFraction
            : TransformGizmoHitTester.TranslationAxisEndFraction;
        Vector3 worldStart = origin + direction * (worldSize * TransformGizmoHitTester.AxisStartFraction);
        Vector3 worldEnd = origin + direction * (worldSize * endFraction);
        if (!TryProject(worldStart, viewProjection, width, height, out ProjectedPoint start) ||
            !TryProject(worldEnd, viewProjection, width, height, out ProjectedPoint end))
        {
            return;
        }
        AddSegment(vertices, start, end, Style.LineWidthPixels, color, width, height);
        if (mode == TransformGizmoMode.Translate)
        {
            AddArrowhead(vertices, start, end, Style.MarkerSizePixels, color, width, height);
        }
        else
        {
            AddDiamond(vertices, end, Style.MarkerSizePixels * 0.55f, color);
        }
    }

    private void AddRotationRing(
        List<GizmoVertex> vertices,
        Vector3 normal,
        Vector3 origin,
        float radius,
        Vector4 color,
        Matrix4x4 viewProjection,
        uint width,
        uint height)
    {
        Vector3 seed = MathF.Abs(Vector3.Dot(normal, Vector3.UnitZ)) < 0.9f
            ? Vector3.UnitZ
            : Vector3.UnitY;
        Vector3 firstBasis = Vector3.Normalize(Vector3.Cross(normal, seed));
        Vector3 secondBasis = Vector3.Normalize(Vector3.Cross(normal, firstBasis));
        Vector3 previousWorld = origin + firstBasis * radius;
        bool previousVisible = TryProject(
            previousWorld,
            viewProjection,
            width,
            height,
            out ProjectedPoint previous);
        for (int index = 1; index <= TransformGizmoHitTester.RotationSegmentCount; index++)
        {
            float angle = index * (MathF.PI * 2f / TransformGizmoHitTester.RotationSegmentCount);
            Vector3 currentWorld = origin +
                (firstBasis * MathF.Cos(angle) + secondBasis * MathF.Sin(angle)) * radius;
            bool currentVisible = TryProject(
                currentWorld,
                viewProjection,
                width,
                height,
                out ProjectedPoint current);
            if (previousVisible && currentVisible)
            {
                AddSegment(vertices, previous, current, Style.LineWidthPixels, color, width, height);
            }
            previous = current;
            previousVisible = currentVisible;
        }
    }

    private static void AddSegment(
        List<GizmoVertex> vertices,
        ProjectedPoint start,
        ProjectedPoint end,
        float widthPixels,
        Vector4 color,
        uint width,
        uint height)
    {
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

    private static void AddArrowhead(
        List<GizmoVertex> vertices,
        ProjectedPoint start,
        ProjectedPoint end,
        float sizePixels,
        Vector4 color,
        uint width,
        uint height)
    {
        Vector2 delta = end.Screen - start.Screen;
        float lengthSquared = delta.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
        {
            return;
        }
        Vector2 direction = delta / MathF.Sqrt(lengthSquared);
        Vector2 normal = new(-direction.Y, direction.X);
        Vector2 baseCenter = end.Screen - direction * sizePixels;
        AddTriangle(
            vertices,
            ToNdc(end.Screen, end.Depth, width, height),
            ToNdc(baseCenter + normal * (sizePixels * 0.55f), end.Depth, width, height),
            ToNdc(baseCenter - normal * (sizePixels * 0.55f), end.Depth, width, height),
            color);
    }

    private static void AddDiamond(
        List<GizmoVertex> vertices,
        ProjectedPoint center,
        float radiusPixels,
        Vector4 color)
    {
        uint width = center.ViewportWidth;
        uint height = center.ViewportHeight;
        Vector3 top = ToNdc(center.Screen + new Vector2(0f, -radiusPixels), center.Depth, width, height);
        Vector3 right = ToNdc(center.Screen + new Vector2(radiusPixels, 0f), center.Depth, width, height);
        Vector3 bottom = ToNdc(center.Screen + new Vector2(0f, radiusPixels), center.Depth, width, height);
        Vector3 left = ToNdc(center.Screen + new Vector2(-radiusPixels, 0f), center.Depth, width, height);
        AddQuad(vertices, top, right, bottom, left, color);
    }

    private static void AddQuad(
        List<GizmoVertex> vertices,
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
        List<GizmoVertex> vertices,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector4 color)
    {
        vertices.Add(new GizmoVertex(first, color));
        vertices.Add(new GizmoVertex(second, color));
        vertices.Add(new GizmoVertex(third, color));
    }

    private Vector4 GetColor(TransformGizmoMode mode, TransformGizmoAxis axis)
    {
        if (Gizmo.ActiveMode == mode && Gizmo.ActiveAxis == axis)
        {
            return activeColor;
        }
        return axis switch
        {
            TransformGizmoAxis.X => xColor,
            TransformGizmoAxis.Y => yColor,
            TransformGizmoAxis.Z => zColor,
            TransformGizmoAxis.Uniform => uniformColor,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };
    }

    private static Vector3 GetAxisDirection(
        TransformGizmoSpace space,
        Matrix4x4 targetWorld,
        TransformGizmoAxis axis)
    {
        Vector3 direction = axis switch
        {
            TransformGizmoAxis.X => Vector3.UnitX,
            TransformGizmoAxis.Y => Vector3.UnitY,
            TransformGizmoAxis.Z => Vector3.UnitZ,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };
        if (space == TransformGizmoSpace.World)
        {
            return direction;
        }

        direction = Vector3.TransformNormal(direction, targetWorld);
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) ||
            !float.IsFinite(direction.Z) || direction.LengthSquared() <= float.Epsilon)
        {
            throw new InvalidOperationException("A local transform-gizmo axis is degenerate.");
        }
        return Vector3.Normalize(direction);
    }

    private static bool TryProject(
        Vector3 world,
        Matrix4x4 viewProjection,
        uint width,
        uint height,
        out ProjectedPoint projected)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        if (!float.IsFinite(clip.X) || !float.IsFinite(clip.Y) ||
            !float.IsFinite(clip.Z) || !float.IsFinite(clip.W) ||
            clip.W <= ProjectionEpsilon)
        {
            projected = default;
            return false;
        }
        float inverseW = 1f / clip.W;
        float depth = clip.Z * inverseW;
        if (!float.IsFinite(depth) || depth < 0f || depth > 1f)
        {
            projected = default;
            return false;
        }
        Vector2 screen = new(
            (clip.X * inverseW + 1f) * 0.5f * width,
            (1f - clip.Y * inverseW) * 0.5f * height);
        if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y))
        {
            projected = default;
            return false;
        }
        projected = new ProjectedPoint(screen, depth, width, height);
        return true;
    }

    private static Vector3 ToNdc(Vector2 screen, float depth, uint width, uint height) => new(
        screen.X * (2f / width) - 1f,
        1f - screen.Y * (2f / height),
        depth);

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
                depthStencil: Style.DepthMode == TransformGizmoDepthMode.SceneDepthTested
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
    private readonly record struct GizmoVertex(Vector3 Position, Vector4 Color);

    private readonly record struct ProjectedPoint(
        Vector2 Screen,
        float Depth,
        uint ViewportWidth,
        uint ViewportHeight);
}

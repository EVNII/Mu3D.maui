using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Helpers;

namespace Mu3D.Toolkit.Rendering;

/// <summary>Identifies how a model outline interacts with scene depth.</summary>
public enum OutlineHelperDepthMode
{
    /// <summary>Hides outline pixels whose target is behind other rendered scene geometry.</summary>
    SceneDepthTested,

    /// <summary>Composites the complete target outline over scene color.</summary>
    Overlay,
}

/// <summary>Defines an immutable HDR-linear model-outline appearance.</summary>
public sealed class OutlineHelperRenderStyle
{
    /// <summary>Maximum supported outer-outline width in physical pixels.</summary>
    public const float MaximumLineWidthPixels = 16f;

    /// <summary>Initializes a model-outline rendering style.</summary>
    /// <param name="color">HDR-linear source-over outline color.</param>
    /// <param name="lineWidthPixels">Positive outer-outline width in physical pixels.</param>
    /// <param name="depthMode">How the target outline interacts with initialized scene depth.</param>
    public OutlineHelperRenderStyle(
        LinearRgba color,
        float lineWidthPixels = 3f,
        OutlineHelperDepthMode depthMode = OutlineHelperDepthMode.SceneDepthTested)
    {
        if (color.ColorSpace is not StandardRgbColorSpaceReference)
        {
            throw new NotSupportedException(
                $"{nameof(color)} must use one of Mu3D's built-in standard linear RGB spaces.");
        }
        if (!float.IsFinite(lineWidthPixels) ||
            lineWidthPixels <= 0f ||
            lineWidthPixels > MaximumLineWidthPixels)
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

    /// <summary>Gets the default high-visibility HDR-linear outline style.</summary>
    public static OutlineHelperRenderStyle Default { get; } = new(
        new LinearRgba(0.08f, 1.55f, 1.75f, 1f, StandardColorSpaces.LinearSrgb));

    /// <summary>Gets the HDR-linear outline color.</summary>
    public LinearRgba Color { get; }

    /// <summary>Gets the outer-outline width in physical pixels.</summary>
    public float LineWidthPixels { get; }

    /// <summary>Gets how the target outline interacts with scene depth.</summary>
    public OutlineHelperDepthMode DepthMode { get; }
}

/// <summary>Renders one target's current screen-space silhouette over initialized scene color.</summary>
/// <remarks>
/// The pass rasterizes only the target into private mask/depth attachments, then composites the
/// outer mask edge into the existing HDR color target. It does not add duplicate scene geometry,
/// perform image encoding, or transfer pixels through the CPU. Targets outside the context scene
/// contribute nothing, and each target mesh follows the context camera's visibility layers.
/// </remarks>
public sealed class OutlineHelperRenderPass : IRenderPass, IDisposable
{
    private const int VertexStride = 16;
    private const string MaskShaderCode = """
        struct VertexInput {
            @location(0) clip_position: vec4f,
        };

        @vertex fn vs_mask(input: VertexInput) -> @builtin(position) vec4f {
            return input.clip_position;
        }

        @fragment fn fs_mask() -> @location(0) vec4f {
            return vec4f(1.0);
        }
        """;

    private readonly StandardRgbColorSpaceReference workingColorSpace;
    private readonly Vector4 color;
    private readonly int radiusPixels;
    private readonly List<Vector4> vertices = [];
    private readonly string encoderLabel;
    private readonly string maskPassLabel;
    private readonly string compositePassLabel;
    private readonly string commandsLabel;
    private GraphicsDevice? resourceDevice;
    private GraphicsTextureFormat resourceColorFormat;
    private GraphicsExtent3D resourceExtent;
    private GraphicsTexture? resourceSceneDepth;
    private GraphicsShaderModule? maskShader;
    private GraphicsShaderModule? compositeShader;
    private GraphicsBindGroupLayout? bindGroupLayout;
    private GraphicsPipelineLayout? pipelineLayout;
    private GraphicsRenderPipeline? maskPipeline;
    private GraphicsRenderPipeline? compositePipeline;
    private GraphicsTexture? maskTexture;
    private GraphicsTextureView? maskView;
    private GraphicsTexture? targetDepthTexture;
    private GraphicsTextureView? targetDepthView;
    private GraphicsTextureView? sceneDepthView;
    private GraphicsBindGroup? bindGroup;
    private GraphicsBuffer? vertexBuffer;
    private ulong vertexBufferCapacity;
    private bool disposed;

    /// <summary>Initializes a pass with the default style and linear-sRGB working space.</summary>
    public OutlineHelperRenderPass(OutlineHelper helper, string name = "Outline Helper")
        : this(helper, OutlineHelperRenderStyle.Default, StandardColorSpaces.LinearSrgb, name)
    {
    }

    /// <summary>Initializes a pass with an explicit style and standard linear working space.</summary>
    public OutlineHelperRenderPass(
        OutlineHelper helper,
        OutlineHelperRenderStyle style,
        StandardRgbColorSpaceReference workingColorSpace,
        string name = "Outline Helper")
    {
        ArgumentNullException.ThrowIfNull(helper);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(workingColorSpace);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!workingColorSpace.IsLinear)
        {
            throw new ArgumentException(
                "The outline-helper working space must be linear-light.",
                nameof(workingColorSpace));
        }
        Helper = helper;
        Style = style;
        this.workingColorSpace = workingColorSpace;
        color = ConvertAndPremultiply(style.Color, workingColorSpace);
        radiusPixels = checked((int)MathF.Ceiling(style.LineWidthPixels));
        encoderLabel = $"{name} encoder";
        maskPassLabel = $"{name} mask pass";
        compositePassLabel = $"{name} composite pass";
        commandsLabel = $"{name} commands";
        Descriptor = new RenderPassDescriptor(
            name,
            workingColorSpace,
            new RenderPassColorAttachmentPolicy(
                GraphicsLoadOperation.Load,
                new LinearRgba(0f, 0f, 0f, 0f, workingColorSpace)),
            style.DepthMode == OutlineHelperDepthMode.SceneDepthTested
                ? new RenderPassDepthAttachmentPolicy(GraphicsLoadOperation.Load)
                : null);
    }

    /// <summary>Gets the borrowed outline definition evaluated for each frame.</summary>
    public OutlineHelper Helper { get; }

    /// <summary>Gets the immutable rendering style.</summary>
    public OutlineHelperRenderStyle Style { get; }

    /// <inheritdoc />
    public RenderPassDescriptor Descriptor { get; }

    /// <inheritdoc />
    public void Execute(RenderPassContext context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (context.WorkingColorSpace != workingColorSpace)
        {
            throw new InvalidOperationException(
                $"The outline pass uses {workingColorSpace.Name}, but the context uses {context.WorkingColorSpace.Name}.");
        }
        if (!context.ColorTargetInitialized)
        {
            throw new InvalidOperationException(
                "The outline-helper pass must follow a pass that initializes color.");
        }
        GraphicsTexture? sceneDepth = ValidateSceneDepth(context);
        if (context.Camera is not PerspectiveCamera camera)
        {
            throw new InvalidOperationException("The outline helper currently requires a perspective camera.");
        }

        // Queue.WriteBuffer consumes the value-only staging span before returning. Keep its
        // capacity across frames/resources changes, but discard logical contents on every exit.
        vertices.Clear();
        try
        {
            Helper.BuildClipTriangles(context.Scene, camera.VisibilityMask, camera.ViewProjectionMatrix, vertices);
            if (vertices.Count == 0)
            {
                return;
            }

            EnsureResources(
                context.Device,
                context.ColorTarget.Descriptor.Format,
                context.OutputExtent,
                sceneDepth);
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(vertices));
            EnsureVertexBuffer(context.Device, checked((ulong)bytes.Length));
            context.Device.Queue.WriteBuffer(vertexBuffer!, 0, bytes);

            using GraphicsCommandEncoder encoder = context.Device.CreateCommandEncoder(encoderLabel);
            using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassColorAttachment(
                        maskTexture!,
                        GraphicsLoadOperation.Clear,
                        GraphicsStoreOperation.Store,
                        new GraphicsClearColor(0f, 0f, 0f, 0f)),
                    maskPassLabel,
                    new GraphicsRenderPassDepthAttachment(
                        targetDepthTexture!,
                        GraphicsLoadOperation.Clear,
                        GraphicsStoreOperation.Store,
                        1f))))
            {
                pass.SetPipeline(maskPipeline!);
                pass.SetVertexBuffer(0, vertexBuffer!, 0, checked((ulong)bytes.Length));
                pass.Draw(checked((uint)vertices.Count));
            }
            using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassColorAttachment(
                        context.ColorTarget,
                        GraphicsLoadOperation.Load,
                        GraphicsStoreOperation.Store),
                    compositePassLabel)))
            {
                pass.SetPipeline(compositePipeline!);
                pass.SetBindGroup(0, bindGroup!);
                pass.Draw(3);
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

    private GraphicsTexture? ValidateSceneDepth(RenderPassContext context)
    {
        if (Style.DepthMode == OutlineHelperDepthMode.Overlay)
        {
            return null;
        }
        if (!context.DepthTargetInitialized)
        {
            throw new InvalidOperationException(
                "The scene-depth-tested outline helper requires initialized depth.");
        }
        GraphicsTexture depth = context.DepthTarget ?? throw new InvalidOperationException(
            "The scene-depth-tested outline helper requires a Depth32Float attachment.");
        if (depth.Descriptor.Format != GraphicsTextureFormat.Depth32Float)
        {
            throw new InvalidOperationException(
                "The scene-depth-tested outline helper requires a Depth32Float attachment.");
        }
        if ((depth.Descriptor.Usage & GraphicsTextureUsage.TextureBinding) == 0)
        {
            throw new InvalidOperationException(
                "The scene-depth-tested outline helper requires depth created with TextureBinding usage.");
        }
        return depth;
    }

    private void EnsureResources(
        GraphicsDevice device,
        GraphicsTextureFormat colorFormat,
        GraphicsExtent3D extent,
        GraphicsTexture? sceneDepth)
    {
        if (!ReferenceEquals(resourceDevice, device) ||
            resourceColorFormat != colorFormat ||
            maskPipeline is null ||
            compositePipeline is null)
        {
            ReleaseResources();
            resourceDevice = device;
            resourceColorFormat = colorFormat;
            CreatePipelineResources(device, colorFormat);
        }
        if (bindGroup is not null &&
            maskTexture is not null &&
            targetDepthTexture is not null &&
            resourceExtent == extent &&
            ReferenceEquals(resourceSceneDepth, sceneDepth))
        {
            return;
        }
        ReleaseTargetResources();
        resourceExtent = extent;
        resourceSceneDepth = sceneDepth;
        maskTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Rgba8Unorm,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: $"{Descriptor.Name} mask"));
        maskView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
            maskTexture,
            label: $"{Descriptor.Name} mask view"));
        targetDepthTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: $"{Descriptor.Name} target depth"));
        targetDepthView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
            targetDepthTexture,
            label: $"{Descriptor.Name} target depth view"));
        List<GraphicsBindGroupEntry> entries = [new GraphicsBindGroupEntry(0, maskView)];
        if (sceneDepth is not null)
        {
            sceneDepthView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                sceneDepth,
                label: $"{Descriptor.Name} scene depth view"));
            entries.Add(new GraphicsBindGroupEntry(1, targetDepthView));
            entries.Add(new GraphicsBindGroupEntry(2, sceneDepthView));
        }
        bindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
            bindGroupLayout!,
            entries,
            $"{Descriptor.Name} bind group"));
    }

    private void CreatePipelineResources(GraphicsDevice device, GraphicsTextureFormat colorFormat)
    {
        maskShader = device.CreateShaderModule(new GraphicsShaderModuleDescriptor(
            MaskShaderCode,
            $"{Descriptor.Name} mask shader"));
        compositeShader = device.CreateShaderModule(new GraphicsShaderModuleDescriptor(
            CreateCompositeShaderCode(),
            $"{Descriptor.Name} composite shader"));
        List<GraphicsBindGroupLayoutEntry> entries =
        [
            new GraphicsBindGroupLayoutEntry(
                0,
                GraphicsShaderStage.Fragment,
                GraphicsTextureSampleType.Float,
                GraphicsTextureViewDimension.TwoD),
        ];
        if (Style.DepthMode == OutlineHelperDepthMode.SceneDepthTested)
        {
            entries.Add(new GraphicsBindGroupLayoutEntry(
                1,
                GraphicsShaderStage.Fragment,
                GraphicsTextureSampleType.Depth,
                GraphicsTextureViewDimension.TwoD));
            entries.Add(new GraphicsBindGroupLayoutEntry(
                2,
                GraphicsShaderStage.Fragment,
                GraphicsTextureSampleType.Depth,
                GraphicsTextureViewDimension.TwoD));
        }
        bindGroupLayout = device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
            entries,
            $"{Descriptor.Name} bind-group layout"));
        pipelineLayout = device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor(
            [bindGroupLayout],
            $"{Descriptor.Name} pipeline layout"));
        maskPipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
            maskShader,
            "vs_mask",
            maskShader,
            "fs_mask",
            GraphicsTextureFormat.Rgba8Unorm,
            label: $"{Descriptor.Name} mask pipeline",
            vertexBuffers:
            [
                new GraphicsVertexBufferLayout(
                    VertexStride,
                    [new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 0, 0)])
            ],
            depthStencil: new GraphicsDepthStencilState(
                GraphicsTextureFormat.Depth32Float,
                depthWriteEnabled: true,
                depthCompare: GraphicsCompareFunction.Less),
            cullMode: GraphicsCullMode.None));
        compositePipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
            compositeShader,
            "vs_fullscreen",
            compositeShader,
            "fs_outline",
            colorFormat,
            label: $"{Descriptor.Name} composite pipeline",
            layout: pipelineLayout,
            blend: GraphicsBlendState.PremultipliedAlpha));
    }

    private string CreateCompositeShaderCode()
    {
        string colorLiteral = string.Create(
            CultureInfo.InvariantCulture,
            $"vec4f({color.X:R}, {color.Y:R}, {color.Z:R}, {color.W:R})");
        string depthBindings = Style.DepthMode == OutlineHelperDepthMode.SceneDepthTested
            ? """
              @group(0) @binding(1) var target_depth: texture_depth_2d;
              @group(0) @binding(2) var scene_depth: texture_depth_2d;
              """
            : string.Empty;
        string nearestDeclaration = Style.DepthMode == OutlineHelperDepthMode.SceneDepthTested
            ? "var nearest_target_depth = 1.0;"
            : string.Empty;
        string nearestUpdate = Style.DepthMode == OutlineHelperDepthMode.SceneDepthTested
            ? "nearest_target_depth = min(nearest_target_depth, textureLoad(target_depth, sample_position, 0));"
            : string.Empty;
        string depthTest = Style.DepthMode == OutlineHelperDepthMode.SceneDepthTested
            ? """
              let scene_value = textureLoad(scene_depth, position, 0);
              if (nearest_target_depth > scene_value + 0.0005) {
                  discard;
              }
              """
            : string.Empty;
        return $$"""
            @group(0) @binding(0) var mask_texture: texture_2d<f32>;
            {{depthBindings}}

            @vertex fn vs_fullscreen(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
                var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
                return vec4f(positions[index], 0.0, 1.0);
            }

            @fragment fn fs_outline(@builtin(position) fragment_position: vec4f) -> @location(0) vec4f {
                let position = vec2i(fragment_position.xy);
                let dimensions = vec2i(textureDimensions(mask_texture));
                if (textureLoad(mask_texture, position, 0).r > 0.5) {
                    discard;
                }
                var found = false;
                {{nearestDeclaration}}
                for (var y = -{{radiusPixels}}; y <= {{radiusPixels}}; y = y + 1) {
                    for (var x = -{{radiusPixels}}; x <= {{radiusPixels}}; x = x + 1) {
                        if (x * x + y * y > {{radiusPixels * radiusPixels}}) {
                            continue;
                        }
                        let sample_position = position + vec2i(x, y);
                        if (any(sample_position < vec2i(0)) || any(sample_position >= dimensions)) {
                            continue;
                        }
                        if (textureLoad(mask_texture, sample_position, 0).r > 0.5) {
                            found = true;
                            {{nearestUpdate}}
                        }
                    }
                }
                if (!found) {
                    discard;
                }
                {{depthTest}}
                return {{colorLiteral}};
            }
            """;
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
        vertexBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
            vertexBufferCapacity,
            GraphicsBufferUsage.Vertex | GraphicsBufferUsage.CopyDestination,
            $"{Descriptor.Name} vertices"));
    }

    private void ReleaseTargetResources()
    {
        bindGroup?.Dispose();
        bindGroup = null;
        sceneDepthView?.Dispose();
        sceneDepthView = null;
        targetDepthView?.Dispose();
        targetDepthView = null;
        targetDepthTexture?.Dispose();
        targetDepthTexture = null;
        maskView?.Dispose();
        maskView = null;
        maskTexture?.Dispose();
        maskTexture = null;
        resourceSceneDepth = null;
        resourceExtent = default;
    }

    private void ReleaseResources()
    {
        ReleaseTargetResources();
        vertexBuffer?.Dispose();
        vertexBuffer = null;
        vertexBufferCapacity = 0;
        compositePipeline?.Dispose();
        compositePipeline = null;
        maskPipeline?.Dispose();
        maskPipeline = null;
        pipelineLayout?.Dispose();
        pipelineLayout = null;
        bindGroupLayout?.Dispose();
        bindGroupLayout = null;
        compositeShader?.Dispose();
        compositeShader = null;
        maskShader?.Dispose();
        maskShader = null;
        resourceDevice = null;
        resourceColorFormat = default;
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
}

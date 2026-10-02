using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Mutable inputs consumed by the application-owned display-normal shadow.</summary>
internal sealed class PenTipDisplayNormalShadowState
{
    internal bool IsVisible { get; set; }

    internal Vector3 CasterWorldPosition { get; set; }

    internal Vector2 LandingUv { get; set; }

    internal float TipDisplayDistanceMillimeters { get; set; }

    internal float WorldUnitsPerMillimeter { get; set; }

    internal float ShadowCoefficient { get; set; } = 0.78f;

    internal float BlurDecayCoefficientPixelsPerMillimeter { get; set; } = 0.55f;

    internal float ShadowDecayCoefficientPerMillimeter { get; set; } = 1f / 42f;

    internal float MaximumHoverDistanceMillimeters { get; set; } = 12f;

    internal float NormalWarpStrength { get; set; } = 0.55f;
}

/// <summary>
/// Flattens the real hidden Pencil geometry at its anchored screen depth, then bends and composites
/// the always-on-top mask using a private SurfaceNormal/depth output. This remains an application
/// visual, not a pen-input policy.
/// </summary>
internal sealed class PenTipDisplayNormalShadowPass : IRenderPass, IDisposable
{
    private const int VertexStride = 24;
    private const int SettingsSize = 176;
    private const float MinimumBlurSigmaPixels = 0.65f;
    private const float MaximumBlurSigmaPixels = 16f;
    private const string MaskShaderCode = """
        struct VertexInput {
            @location(0) clip_position: vec4f,
            @location(1) opacity_depth: vec2f,
        };

        struct MaskVertexOutput {
            @builtin(position) position: vec4f,
            @location(0) opacity_depth: vec2f,
        };

        @vertex fn vs_mask(input: VertexInput) -> MaskVertexOutput {
            var output: MaskVertexOutput;
            output.position = input.clip_position;
            output.opacity_depth = input.opacity_depth;
            return output;
        }

        @fragment fn fs_mask(input: MaskVertexOutput) -> @location(0) vec4f {
            return vec4f(input.opacity_depth, 1.0, 1.0);
        }
        """;
    private const string CompositeShaderCode = """
        struct ProjectionSettings {
            inverse_view_projection: mat4x4f,
            distance_parameters: vec4f,
            composite_parameters: vec4f,
            camera_position: vec4f,
            camera_right: vec4f,
            camera_up: vec4f,
            camera_forward: vec4f,
            receiver_uv: vec4f,
        };

        @group(0) @binding(0) var caster_mask: texture_2d<f32>;
        @group(0) @binding(1) var caster_mask_sampler: sampler;
        @group(0) @binding(2) var surface_normal: texture_2d<f32>;
        @group(0) @binding(3) var surface_depth: texture_depth_2d;
        @group(0) @binding(4) var<uniform> settings: ProjectionSettings;

        struct FullscreenVertex {
            @builtin(position) position: vec4f,
            @location(0) uv: vec2f,
        };

        @vertex fn vs_fullscreen(@builtin(vertex_index) index: u32) -> FullscreenVertex {
            var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
            let position = positions[index];
            var output: FullscreenVertex;
            output.position = vec4f(position, 0.0, 1.0);
            output.uv = vec2f(position.x * 0.5 + 0.5, 0.5 - position.y * 0.5);
            return output;
        }

        fn reconstruct_world(uv: vec2f, depth: f32) -> vec3f {
            let clip = vec4f(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
            let world = settings.inverse_view_projection * clip;
            return world.xyz / max(abs(world.w), 0.000001);
        }

        fn mask_at(uv: vec2f, offset_pixels: vec2f) -> vec3f {
            let dimensions = vec2f(textureDimensions(caster_mask));
            let sample_uv = uv + offset_pixels / dimensions;
            if (any(sample_uv < vec2f(0.0)) || any(sample_uv > vec2f(1.0))) {
                return vec3f(0.0);
            }
            return textureSampleLevel(caster_mask, caster_mask_sampler, sample_uv, 0.0).rgb;
        }

        fn nearest_mask_metrics(uv: vec2f) -> vec3f {
            var nearest = mask_at(uv, vec2f(0.0));
            let diagonal = 0.70710678;
            let directions = array<vec2f, 8>(
                vec2f(1.0, 0.0),
                vec2f(-1.0, 0.0),
                vec2f(0.0, 1.0),
                vec2f(0.0, -1.0),
                vec2f(diagonal, diagonal),
                vec2f(-diagonal, diagonal),
                vec2f(diagonal, -diagonal),
                vec2f(-diagonal, -diagonal));
            let radii = array<f32, 4>(2.0, 6.0, 14.0, 30.0);
            for (var ring = 0u; ring < 4u; ring += 1u) {
                if (nearest.z <= 0.0001) {
                    var ring_best = vec3f(0.0);
                    for (var direction = 0u; direction < 8u; direction += 1u) {
                        let candidate = mask_at(
                            uv,
                            directions[direction] * radii[ring]);
                        if (candidate.z > ring_best.z) {
                            ring_best = candidate;
                        }
                    }
                    if (ring_best.z > 0.0001) {
                        nearest = ring_best;
                    }
                }
            }
            return nearest;
        }

        fn blur_sigma(receiver_view_depth: f32, caster_view_depth: f32) -> f32 {
            let world_units_per_millimetre = max(settings.distance_parameters.x, 0.000001);
            let separation_millimetres =
                abs(receiver_view_depth - caster_view_depth) /
                world_units_per_millimetre;
            return clamp(
                settings.distance_parameters.y +
                    separation_millimetres * settings.distance_parameters.z,
                settings.distance_parameters.y,
                settings.distance_parameters.w);
        }

        fn gaussian_mask(uv: vec2f, receiver_view_depth: f32) -> f32 {
            let nearest = nearest_mask_metrics(uv);
            let nearest_coverage = max(nearest.z, 0.000001);
            let caster_view_depth = select(
                settings.composite_parameters.y,
                nearest.y / nearest_coverage,
                nearest.z > 0.0001);
            let sigma = blur_sigma(receiver_view_depth, caster_view_depth);
            let sample_step = max(1.0, sigma * 0.72);
            let inverse_two_variance = 0.5 / max(sigma * sigma, 0.000001);
            var weighted_mask = 0.0;
            var total_weight = 0.0;
            for (var y = -3i; y <= 3i; y += 1i) {
                for (var x = -3i; x <= 3i; x += 1i) {
                    let offset = vec2f(f32(x), f32(y)) * sample_step;
                    let weight = exp(-dot(offset, offset) * inverse_two_variance);
                    let source = mask_at(uv, offset);
                    weighted_mask += source.x * weight;
                    total_weight += weight;
                }
            }
            return weighted_mask / max(total_weight, 0.000001);
        }

        @fragment fn fs_composite(input: FullscreenVertex) -> @location(0) vec4f {
            let dimensions = vec2i(textureDimensions(surface_normal));
            let coordinate = clamp(
                vec2i(input.position.xy),
                vec2i(0),
                dimensions - vec2i(1));
            let depth = textureLoad(surface_depth, coordinate, 0);
            if (depth >= 0.999999) {
                discard;
            }

            let receiver_world = reconstruct_world(input.uv, depth);
            let receiver_view_depth = dot(
                receiver_world - settings.camera_position.xyz,
                settings.camera_forward.xyz);
            let encoded_normal = textureLoad(surface_normal, coordinate, 0).xyz;
            let normal = normalize(encoded_normal * 2.0 - vec3f(1.0));
            let normal_dot_view = abs(dot(normal, settings.camera_forward.xyz));
            let slope = vec2f(
                dot(normal, settings.camera_right.xyz),
                -dot(normal, settings.camera_up.xyz));
            let slope_length = length(slope);
            var warped_uv = input.uv;
            if (slope_length > 0.0001) {
                let slope_axis = slope / slope_length;
                let delta = input.uv - settings.receiver_uv.xy;
                let along_slope = dot(delta, slope_axis);
                let warp =
                    (1.0 - normal_dot_view) * settings.composite_parameters.x;
                warped_uv += slope_axis * along_slope * warp;
            }

            let mask = gaussian_mask(warped_uv, receiver_view_depth);
            let normal_gate = mix(1.0, smoothstep(0.08, 0.42, normal_dot_view), 0.55);
            let alpha = clamp(mask * normal_gate, 0.0, 1.0);
            if (alpha <= 0.0001) {
                discard;
            }
            return vec4f(0.0, 0.0, 0.0, alpha);
        }
        """;
    private static readonly GraphicsBlendState MultiplyPreserveAlpha = new(
        new GraphicsBlendComponent(
            GraphicsBlendOperation.Add,
            GraphicsBlendFactor.One,
            GraphicsBlendFactor.OneMinusSourceAlpha),
        new GraphicsBlendComponent(
            GraphicsBlendOperation.Add,
            GraphicsBlendFactor.Zero,
            GraphicsBlendFactor.One));

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MaskVertex
    {
        internal MaskVertex(Vector4 clipPosition, Vector2 opacityDepth)
        {
            ClipPosition = clipPosition;
            OpacityDepth = opacityDepth;
        }

        internal readonly Vector4 ClipPosition;

        internal readonly Vector2 OpacityDepth;
    }

    private readonly SceneRenderer renderer;
    private readonly IReadOnlyList<Mesh> casters;
    private readonly PenTipDisplayNormalShadowState state;
    private readonly StandardRgbColorSpaceReference workingColorSpace;
    private readonly List<MaskVertex> maskVertices = [];
    private GraphicsDevice? resourceDevice;
    private GraphicsTextureFormat resourceColorFormat;
    private GraphicsExtent3D resourceExtent;
    private GraphicsShaderModule? maskShader;
    private GraphicsShaderModule? compositeShader;
    private GraphicsBindGroupLayout? bindGroupLayout;
    private GraphicsPipelineLayout? pipelineLayout;
    private GraphicsRenderPipeline? maskPipeline;
    private GraphicsRenderPipeline? compositePipeline;
    private GraphicsTexture? maskTexture;
    private GraphicsTextureView? maskView;
    private GraphicsSampler? maskSampler;
    private GraphicsTexture? normalTexture;
    private GraphicsTextureView? normalView;
    private GraphicsTexture? normalDepthTexture;
    private GraphicsTextureView? normalDepthView;
    private GraphicsBuffer? settingsBuffer;
    private GraphicsBuffer? vertexBuffer;
    private ulong vertexBufferCapacity;
    private GraphicsBindGroup? bindGroup;
    private bool disposed;

    internal PenTipDisplayNormalShadowPass(
        SceneRenderer renderer,
        IReadOnlyList<Mesh> casters,
        PenTipDisplayNormalShadowState state,
        StandardRgbColorSpaceReference workingColorSpace,
        string name = "Apple Pencil display-normal surface shadow")
    {
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        this.casters = casters ?? throw new ArgumentNullException(nameof(casters));
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.workingColorSpace = workingColorSpace ??
            throw new ArgumentNullException(nameof(workingColorSpace));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!workingColorSpace.IsLinear)
        {
            throw new ArgumentException(
                "The display-normal shadow working space must be linear-light.",
                nameof(workingColorSpace));
        }
        Descriptor = new RenderPassDescriptor(
            name,
            workingColorSpace,
            new RenderPassColorAttachmentPolicy(
                GraphicsLoadOperation.Load,
                new LinearRgba(0f, 0f, 0f, 0f, workingColorSpace)));
    }

    public RenderPassDescriptor Descriptor { get; }

    public void Execute(RenderPassContext context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!state.IsVisible)
        {
            return;
        }
        if (!context.ColorTargetInitialized)
        {
            throw new InvalidOperationException(
                "The Apple Pencil display-normal shadow must follow an initialized scene color pass.");
        }
        if (!ReferenceEquals(context.Device, renderer.Device) ||
            context.WorkingColorSpace != workingColorSpace)
        {
            throw new InvalidOperationException(
                "The Apple Pencil display-normal shadow and scene renderer must share a device and working space.");
        }
        if (context.Camera is not PerspectiveCamera camera)
        {
            throw new InvalidOperationException(
                "The Apple Pencil display-normal shadow currently requires a perspective camera.");
        }

        BuildMaskVertices(camera);
        if (maskVertices.Count == 0)
        {
            return;
        }
        EnsureResources(context.Device, context.ColorTarget.Descriptor.Format, context.OutputExtent);
        renderer.RenderOutputPass(
            context.Scene,
            camera,
            normalTexture!,
            normalDepthTexture!,
            RenderOutputIds.SurfaceNormal,
            SceneRenderPassOptions.Default);
        WriteSettings(context.Device, camera);

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(maskVertices));
        EnsureVertexBuffer(context.Device, checked((ulong)bytes.Length));
        context.Device.Queue.WriteBuffer(vertexBuffer!, 0, bytes);

        using GraphicsCommandEncoder encoder =
            context.Device.CreateCommandEncoder($"{Descriptor.Name} encoder");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
            new GraphicsRenderPassDescriptor(
                new GraphicsRenderPassColorAttachment(
                    maskTexture!,
                    GraphicsLoadOperation.Clear,
                    GraphicsStoreOperation.Store,
                    new GraphicsClearColor(0f, 0f, 0f, 0f)),
                $"{Descriptor.Name} caster-mask pass")))
        {
            pass.SetPipeline(maskPipeline!);
            pass.SetVertexBuffer(0, vertexBuffer!, 0, checked((ulong)bytes.Length));
            pass.Draw(checked((uint)maskVertices.Count));
        }
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
            new GraphicsRenderPassDescriptor(
                new GraphicsRenderPassColorAttachment(
                    context.ColorTarget,
                    GraphicsLoadOperation.Load,
                    GraphicsStoreOperation.Store),
                $"{Descriptor.Name} normal/depth composite pass")))
        {
            pass.SetPipeline(compositePipeline!);
            pass.SetBindGroup(0, bindGroup!);
            if (TrySetCompositeScissor(pass, context.OutputExtent))
            {
                pass.Draw(3);
            }
        }
        using GraphicsCommandBuffer commands = encoder.Finish($"{Descriptor.Name} commands");
        context.Device.Queue.Submit(commands);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        ReleaseResources();
        GC.SuppressFinalize(this);
    }

    private void BuildMaskVertices(PerspectiveCamera camera)
    {
        maskVertices.Clear();
        Matrix4x4 view = camera.ViewMatrix;
        Matrix4x4 projection = camera.ProjectionMatrix;
        Vector3 casterViewPosition = Vector3.Transform(state.CasterWorldPosition, view);
        float casterViewDepth = -casterViewPosition.Z;
        if (!float.IsFinite(casterViewDepth) ||
            casterViewDepth <= 0.000001f ||
            !float.IsFinite(state.WorldUnitsPerMillimeter) ||
            state.WorldUnitsPerMillimeter <= 0.000001f)
        {
            return;
        }
        Vector2 landingProjectionOffset = new(
            state.LandingUv.X * 2f - 1f -
                casterViewPosition.X * projection.M11 / casterViewDepth,
            1f - state.LandingUv.Y * 2f -
                casterViewPosition.Y * projection.M22 / casterViewDepth);
        float maximumHoverDistanceMillimeters = MathF.Max(
            0.000001f,
            state.MaximumHoverDistanceMillimeters);
        float shadowDecayCoefficientPerMillimeter = MathF.Max(
            0f,
            state.ShadowDecayCoefficientPerMillimeter);
        float maximumDecay =
            shadowDecayCoefficientPerMillimeter * maximumHoverDistanceMillimeters;
        float maximumDistanceExponential = MathF.Exp(-maximumDecay);
        float exponentialDenominator = 1f - maximumDistanceExponential;
        float shadowCoefficient = Math.Clamp(state.ShadowCoefficient, 0f, 1f);
        for (int casterIndex = 0; casterIndex < casters.Count; casterIndex++)
        {
            Mesh caster = casters[casterIndex];
            Matrix4x4 worldView = caster.WorldMatrix * view;
            IReadOnlyList<Vector3> positions = caster.Geometry.Positions;
            IReadOnlyList<uint> indices = caster.Geometry.Indices;
            for (int index = 0; index < indices.Count; index++)
            {
                Vector3 position = positions[checked((int)indices[index])];
                Vector3 viewPosition = Vector3.Transform(position, worldView);
                float vertexViewDepth = -viewPosition.Z;
                float displayDistanceMillimeters = MathF.Max(
                    0f,
                    state.TipDisplayDistanceMillimeters +
                        (casterViewDepth - vertexViewDepth) /
                        state.WorldUnitsPerMillimeter);
                float normalizedProximity = Math.Clamp(
                    1f - displayDistanceMillimeters / maximumHoverDistanceMillimeters,
                    0f,
                    1f);
                float hoverFactor = normalizedProximity;
                if (maximumDecay > 0.000001f)
                {
                    hoverFactor =
                        (MathF.Exp(
                            -shadowDecayCoefficientPerMillimeter *
                            displayDistanceMillimeters) -
                            maximumDistanceExponential) /
                        exponentialDenominator;
                }
                float opacity =
                    shadowCoefficient * Math.Clamp(hoverFactor, 0f, 1f);
                maskVertices.Add(new MaskVertex(
                    new Vector4(
                        viewPosition.X * projection.M11 / casterViewDepth +
                            landingProjectionOffset.X,
                        viewPosition.Y * projection.M22 / casterViewDepth +
                            landingProjectionOffset.Y,
                        0f,
                        1f),
                    new Vector2(opacity, vertexViewDepth)));
            }
        }
    }

    private bool TrySetCompositeScissor(
        GraphicsRenderPassEncoder pass,
        GraphicsExtent3D extent)
    {
        float minimumX = float.PositiveInfinity;
        float minimumY = float.PositiveInfinity;
        float maximumX = float.NegativeInfinity;
        float maximumY = float.NegativeInfinity;
        foreach (MaskVertex vertex in maskVertices)
        {
            float reciprocalW = 1f / vertex.ClipPosition.W;
            float pixelX =
                (vertex.ClipPosition.X * reciprocalW * 0.5f + 0.5f) * extent.Width;
            float pixelY =
                (0.5f - vertex.ClipPosition.Y * reciprocalW * 0.5f) * extent.Height;
            minimumX = MathF.Min(minimumX, pixelX);
            minimumY = MathF.Min(minimumY, pixelY);
            maximumX = MathF.Max(maximumX, pixelX);
            maximumY = MathF.Max(maximumY, pixelY);
        }

        const float GaussianExtent = 3f;
        float padding = MaximumBlurSigmaPixels * GaussianExtent + 2f;
        int targetWidth = checked((int)extent.Width);
        int targetHeight = checked((int)extent.Height);
        int left = Math.Clamp((int)MathF.Floor(minimumX - padding), 0, targetWidth);
        int top = Math.Clamp((int)MathF.Floor(minimumY - padding), 0, targetHeight);
        int right = Math.Clamp((int)MathF.Ceiling(maximumX + padding), 0, targetWidth);
        int bottom = Math.Clamp((int)MathF.Ceiling(maximumY + padding), 0, targetHeight);
        if (right <= left || bottom <= top)
        {
            return false;
        }
        pass.SetScissorRect(
            checked((uint)left),
            checked((uint)top),
            checked((uint)(right - left)),
            checked((uint)(bottom - top)));
        return true;
    }

    private void WriteSettings(
        GraphicsDevice device,
        PerspectiveCamera camera)
    {
        if (!Matrix4x4.Invert(camera.ViewProjectionMatrix, out Matrix4x4 inverseViewProjection))
        {
            throw new InvalidOperationException(
                "The Apple Pencil display-normal shadow requires an invertible camera view projection.");
        }
        Matrix4x4 cameraWorld = camera.WorldMatrix;
        Vector3 cameraPosition = Vector3.Transform(Vector3.Zero, cameraWorld);
        Vector3 cameraRight = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, cameraWorld));
        Vector3 cameraUp = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, cameraWorld));
        Vector3 cameraForward = Vector3.Normalize(
            Vector3.TransformNormal(-Vector3.UnitZ, cameraWorld));
        float casterAnchorViewDepth = -Vector3.Transform(
            state.CasterWorldPosition,
            camera.ViewMatrix).Z;
        if (!float.IsFinite(state.LandingUv.X) ||
            !float.IsFinite(state.LandingUv.Y))
        {
            throw new InvalidOperationException(
                "The Apple Pencil shadow landing projection must be finite.");
        }

        Span<float> settings = stackalloc float[SettingsSize / sizeof(float)];
        settings.Clear();
        MemoryMarshal.Cast<float, Matrix4x4>(settings[..16])[0] = inverseViewProjection;
        settings[16] = state.WorldUnitsPerMillimeter;
        settings[17] = MinimumBlurSigmaPixels;
        settings[18] = state.BlurDecayCoefficientPixelsPerMillimeter;
        settings[19] = MaximumBlurSigmaPixels;
        settings[20] = Math.Clamp(state.NormalWarpStrength, 0f, 1f);
        settings[21] = casterAnchorViewDepth;
        settings[24] = cameraPosition.X;
        settings[25] = cameraPosition.Y;
        settings[26] = cameraPosition.Z;
        settings[27] = 1f;
        settings[28] = cameraRight.X;
        settings[29] = cameraRight.Y;
        settings[30] = cameraRight.Z;
        settings[32] = cameraUp.X;
        settings[33] = cameraUp.Y;
        settings[34] = cameraUp.Z;
        settings[36] = cameraForward.X;
        settings[37] = cameraForward.Y;
        settings[38] = cameraForward.Z;
        settings[40] = state.LandingUv.X;
        settings[41] = state.LandingUv.Y;
        device.Queue.WriteBuffer(settingsBuffer!, 0, MemoryMarshal.AsBytes(settings));
    }

    private void EnsureResources(
        GraphicsDevice device,
        GraphicsTextureFormat colorFormat,
        GraphicsExtent3D extent)
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
        if (bindGroup is not null && resourceExtent == extent)
        {
            return;
        }
        ReleaseTargetResources();
        resourceExtent = extent;
        maskTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: $"{Descriptor.Name} caster mask"));
        maskView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
            maskTexture,
            label: $"{Descriptor.Name} caster-mask view"));
        normalTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            colorFormat,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: $"{Descriptor.Name} SurfaceNormal output"));
        normalView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
            normalTexture,
            label: $"{Descriptor.Name} SurfaceNormal view"));
        normalDepthTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: $"{Descriptor.Name} SurfaceNormal depth"));
        normalDepthView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
            normalDepthTexture,
            label: $"{Descriptor.Name} SurfaceNormal depth view"));
        bindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
            bindGroupLayout!,
            [
                new GraphicsBindGroupEntry(0, maskView),
                new GraphicsBindGroupEntry(1, maskSampler!),
                new GraphicsBindGroupEntry(2, normalView),
                new GraphicsBindGroupEntry(3, normalDepthView),
                new GraphicsBindGroupEntry(4, settingsBuffer!, 0, SettingsSize),
            ],
            $"{Descriptor.Name} bind group"));
    }

    private void CreatePipelineResources(GraphicsDevice device, GraphicsTextureFormat colorFormat)
    {
        maskShader = device.CreateShaderModule(new GraphicsShaderModuleDescriptor(
            MaskShaderCode,
            $"{Descriptor.Name} mask shader"));
        compositeShader = device.CreateShaderModule(new GraphicsShaderModuleDescriptor(
            CompositeShaderCode,
            $"{Descriptor.Name} composite shader"));
        bindGroupLayout = device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
            [
                new GraphicsBindGroupLayoutEntry(
                    0,
                    GraphicsShaderStage.Fragment,
                    GraphicsTextureSampleType.Float,
                    GraphicsTextureViewDimension.TwoD),
                new GraphicsBindGroupLayoutEntry(
                    1,
                    GraphicsShaderStage.Fragment,
                    GraphicsSamplerBindingType.Filtering),
                new GraphicsBindGroupLayoutEntry(
                    2,
                    GraphicsShaderStage.Fragment,
                    GraphicsTextureSampleType.Float,
                    GraphicsTextureViewDimension.TwoD),
                new GraphicsBindGroupLayoutEntry(
                    3,
                    GraphicsShaderStage.Fragment,
                    GraphicsTextureSampleType.Depth,
                    GraphicsTextureViewDimension.TwoD),
                new GraphicsBindGroupLayoutEntry(
                    4,
                    GraphicsShaderStage.Fragment,
                    GraphicsBufferBindingType.Uniform,
                    SettingsSize),
            ],
            $"{Descriptor.Name} bind-group layout"));
        pipelineLayout = device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor(
            [bindGroupLayout],
            $"{Descriptor.Name} pipeline layout"));
        maskPipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
            maskShader,
            "vs_mask",
            maskShader,
            "fs_mask",
            GraphicsTextureFormat.Rgba16Float,
            label: $"{Descriptor.Name} mask pipeline",
            vertexBuffers:
            [
                new GraphicsVertexBufferLayout(
                    VertexStride,
                    [
                        new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 0, 0),
                        new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 16, 1),
                    ])
            ],
            cullMode: GraphicsCullMode.None));
        compositePipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
            compositeShader,
            "vs_fullscreen",
            compositeShader,
            "fs_composite",
            colorFormat,
            label: $"{Descriptor.Name} composite pipeline",
            layout: pipelineLayout,
            blend: MultiplyPreserveAlpha));
        settingsBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
            SettingsSize,
            GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination,
            $"{Descriptor.Name} settings"));
        maskSampler = device.CreateSampler(new GraphicsSamplerDescriptor(
            magFilter: GraphicsFilterMode.Linear,
            minFilter: GraphicsFilterMode.Linear,
            label: $"{Descriptor.Name} mask linear sampler"));
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
        normalDepthView?.Dispose();
        normalDepthView = null;
        normalDepthTexture?.Dispose();
        normalDepthTexture = null;
        normalView?.Dispose();
        normalView = null;
        normalTexture?.Dispose();
        normalTexture = null;
        maskView?.Dispose();
        maskView = null;
        maskTexture?.Dispose();
        maskTexture = null;
        resourceExtent = default;
    }

    private void ReleaseResources()
    {
        ReleaseTargetResources();
        vertexBuffer?.Dispose();
        vertexBuffer = null;
        vertexBufferCapacity = 0;
        settingsBuffer?.Dispose();
        settingsBuffer = null;
        maskSampler?.Dispose();
        maskSampler = null;
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
}

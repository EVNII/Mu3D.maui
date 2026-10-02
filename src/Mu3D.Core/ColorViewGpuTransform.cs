using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;

namespace Mu3D.Rendering;

/// <summary>Applies an explicit AgX, Filmic or ACES display view after scene-linear rendering, without readback.</summary>
/// <remarks>
/// Input textures contain scene-linear Float16/Float32 RGB in the transform's source space, with
/// alpha in [0,1]. Straight RGB (after unassociation, if selected) multiplied by exposure must remain
/// within <see cref="ColorViewTransform.MaximumExposedComponentMagnitude"/> (1e30) in absolute value.
/// As with other GPU transforms this caller data contract is not checked by readback. Output alpha
/// association is applied in linear light, before either shader or hardware sRGB encoding.
/// Output is display-linear extended sRGB or explicitly encoded SDR sRGB. HDR views require floating
/// extended-linear output. Alpha association is explicit: nonlinear view math always acts on straight
/// color. Instances own internal resources only and must be used serially with their device.
/// </remarks>
public sealed class ColorViewGpuTransform : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly GraphicsBuffer table;
    private readonly GraphicsBuffer parameters;
    private readonly GraphicsBindGroupLayout inputLayout;
    private readonly GraphicsBindGroupLayout tableLayout;
    private readonly GraphicsBindGroup tableBindings;
    private readonly GraphicsPipelineLayout pipelineLayout;
    private readonly GraphicsShaderModule shader;
    private readonly GraphicsRenderPipeline pipeline;
    private bool disposed;

    /// <summary>Creates and uploads one reusable official view program and its FP32 data.</summary>
    /// <param name="device">Caller-owned graphics device.</param>
    /// <param name="transform">Immutable CPU reference, including exposure and reference-white normalization.</param>
    /// <param name="outputFormat">Float16/Float32 for HDR; Float or eight-bit RGBA/BGRA for SDR.</param>
    /// <param name="outputEncoding">Extended-linear sRGB or explicit SDR sRGB.</param>
    /// <param name="inputPremultiplied">Whether scene texture RGB already includes alpha.</param>
    /// <param name="outputPremultiplied">Whether to associate transformed RGB with alpha.</param>
    public ColorViewGpuTransform(GraphicsDevice device, ColorViewTransform transform,
        GraphicsTextureFormat outputFormat, ColorEncoding outputEncoding = ColorEncoding.ExtendedSrgbLinear,
        bool inputPremultiplied = false, bool outputPremultiplied = false)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(transform);
        bool floating = outputFormat is GraphicsTextureFormat.Rgba16Float or GraphicsTextureFormat.Rgba32Float;
        bool unorm = outputFormat is GraphicsTextureFormat.Rgba8Unorm or GraphicsTextureFormat.Bgra8Unorm;
        bool hardwareSrgb = outputFormat is GraphicsTextureFormat.Rgba8UnormSrgb or GraphicsTextureFormat.Bgra8UnormSrgb;
        if (!floating && !unorm && !hardwareSrgb ||
            outputEncoding == ColorEncoding.ExtendedSrgbLinear && !floating ||
            outputEncoding == ColorEncoding.Srgb && !unorm && !hardwareSrgb ||
            outputEncoding is not (ColorEncoding.ExtendedSrgbLinear or ColorEncoding.Srgb))
            throw new NotSupportedException("Display views require floating extended-linear sRGB or an explicitly encoded eight-bit sRGB destination.");
        if (transform.IsHdr && outputEncoding != ColorEncoding.ExtendedSrgbLinear)
            throw new NotSupportedException("An HDR view cannot be silently clipped into SDR output. Select an SDR view explicitly.");
        if (outputFormat == GraphicsTextureFormat.Rgba16Float && !device.Capabilities.SupportsFloat16Textures)
            throw new NotSupportedException("The device does not support Float16 textures.");
        this.device = device;
        Transform = transform;
        OutputFormat = outputFormat;
        OutputEncoding = outputEncoding;
        InputPremultiplied = inputPremultiplied;
        OutputPremultiplied = outputPremultiplied;
        List<IDisposable> created = [];
        try
        {
            ReadOnlySpan<byte> data = MemoryMarshal.AsBytes(transform.Program.Table.Span);
            table = Own(device.CreateBuffer(new GraphicsBufferDescriptor((ulong)data.Length,
                GraphicsBufferUsage.Storage | GraphicsBufferUsage.CopyDestination, "Mu3D display-view data")));
            device.Queue.WriteBuffer(table, 0, data);
            parameters = Own(device.CreateBuffer(new GraphicsBufferDescriptor(16,
                GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination, "Mu3D display-view parameters")));
            WriteParameters(transform);
            inputLayout = Own(device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
            [
                new GraphicsBindGroupLayoutEntry(0, GraphicsShaderStage.Fragment,
                    GraphicsTextureSampleType.UnfilterableFloat, GraphicsTextureViewDimension.TwoD),
                new GraphicsBindGroupLayoutEntry(1, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, 16),
            ], "Mu3D display-view input layout")));
            tableLayout = Own(device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
            [new GraphicsBindGroupLayoutEntry(0, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage,
                (ulong)data.Length)], "Mu3D display-view data layout")));
            tableBindings = Own(device.CreateBindGroup(new GraphicsBindGroupDescriptor(tableLayout,
                [new GraphicsBindGroupEntry(0, table, 0, (ulong)data.Length)], "Mu3D display-view data bindings")));
            pipelineLayout = Own(device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor(
                [inputLayout, tableLayout], "Mu3D display-view pipeline layout")));
            shader = Own(device.CreateShaderModule(new GraphicsShaderModuleDescriptor(
                transform.Program.WgslSource + "\n" + Wrapper, "Mu3D display-view shader")));
            pipeline = Own(device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                shader, "mu3d_view_vertex", shader, "mu3d_view_fragment", outputFormat,
                layout: pipelineLayout, label: "Mu3D display view")));
        }
        catch
        {
            for (int index = created.Count - 1; index >= 0; index--) created[index].Dispose();
            throw;
        }
        T Own<T>(T resource) where T : IDisposable { created.Add(resource); return resource; }
    }

    /// <summary>Gets the currently selected CPU reference transform.</summary>
    public ColorViewTransform Transform { get; private set; }
    /// <summary>Gets the required output texture format.</summary>
    public GraphicsTextureFormat OutputFormat { get; }
    /// <summary>Gets the compositor/output encoding selected explicitly at construction.</summary>
    public ColorEncoding OutputEncoding { get; }
    /// <summary>Gets whether source RGB is associated with alpha.</summary>
    public bool InputPremultiplied { get; }
    /// <summary>Gets whether output RGB is associated with alpha.</summary>
    public bool OutputPremultiplied { get; }

    /// <summary>Updates exposure and reference white without re-uploading data or recompiling a shader.</summary>
    /// <remarks>The preset and source identity must remain unchanged; otherwise create a new instance.</remarks>
    public void UpdateTransform(ColorViewTransform transform)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(transform);
        if (transform.Preset != Transform.Preset || transform.SourceSpace != Transform.SourceSpace)
            throw new ArgumentException("Updating parameters must retain the preset and source space.", nameof(transform));
        WriteParameters(transform);
        Transform = transform;
    }

    /// <summary>Submits a complete display transform to a distinct, same-sized output texture.</summary>
    /// <remarks>Caller textures remain caller-owned. This does not present, schedule or wait for a frame.</remarks>
    public void Apply(GraphicsTexture source, ColorSpaceReference sourceSpace,
        GraphicsTexture destination, ColorSpaceReference destinationSpace)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateTexture(source, nameof(source));
        ValidateTexture(destination, nameof(destination));
        if (sourceSpace != Transform.SourceSpace || destinationSpace != Transform.DestinationSpace)
            throw new ArgumentException("Texture color identities must match the view's source and destination.");
        if (ReferenceEquals(source, destination) || source.Descriptor.Size != destination.Descriptor.Size ||
            source.Descriptor.Format is not (GraphicsTextureFormat.Rgba16Float or GraphicsTextureFormat.Rgba32Float) ||
            destination.Descriptor.Format != OutputFormat ||
            (source.Descriptor.Usage & GraphicsTextureUsage.TextureBinding) == 0 ||
            (destination.Descriptor.Usage & GraphicsTextureUsage.RenderAttachment) == 0)
            throw new ArgumentException("The view requires separate matching extents, floating sampled input and the selected renderable output.");
        using GraphicsTextureView sourceView = device.CreateTextureView(new GraphicsTextureViewDescriptor(source));
        using GraphicsBindGroup input = device.CreateBindGroup(new GraphicsBindGroupDescriptor(inputLayout,
            [new GraphicsBindGroupEntry(0, sourceView), new GraphicsBindGroupEntry(1, parameters, 0, 16)]));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("Mu3D display view");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(destination), "Mu3D display view")))
        {
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, input);
            pass.SetBindGroup(1, tableBindings);
            pass.Draw(3);
        }
        using GraphicsCommandBuffer commands = encoder.Finish("Mu3D display-view commands");
        device.Queue.Submit(commands);
    }

    /// <summary>Releases all owned GPU resources. Repeated disposal is harmless.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pipeline.Dispose(); shader.Dispose(); pipelineLayout.Dispose(); tableBindings.Dispose();
        tableLayout.Dispose(); inputLayout.Dispose(); parameters.Dispose(); table.Dispose();
    }

    private void WriteParameters(ColorViewTransform transform)
    {
        float flags = (InputPremultiplied ? 1 : 0) + (OutputPremultiplied ? 2 : 0) +
            (OutputEncoding == ColorEncoding.Srgb && OutputFormat is GraphicsTextureFormat.Rgba8Unorm or GraphicsTextureFormat.Bgra8Unorm ? 4 : 0);
        Vector4[] values = [new(MathF.Pow(2, transform.ExposureStops), 100f / transform.ReferenceWhiteNits, flags, 0)];
        device.Queue.WriteBuffer(parameters, 0, MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private void ValidateTexture(GraphicsTexture texture, string name)
    {
        ArgumentNullException.ThrowIfNull(texture, name);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        if (!ReferenceEquals(texture.Device, device) || texture.Descriptor.Size.DepthOrArrayLayers != 1 ||
            texture.Descriptor.MipLevelCount != 1 || texture.Descriptor.SampleCount != 1)
            throw new ArgumentException("A single-layer, single-mip, single-sample texture from the same device is required.", name);
    }

    private const string Wrapper = """
        @group(0) @binding(0) var mu3d_view_input: texture_2d<f32>;
        @group(0) @binding(1) var<uniform> mu3d_view_params: vec4<f32>;
        @vertex fn mu3d_view_vertex(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32> {
            var p = array(vec2<f32>(-1.0,-1.0), vec2<f32>(3.0,-1.0), vec2<f32>(-1.0,3.0));
            return vec4<f32>(p[index],0.0,1.0);
        }
        @fragment fn mu3d_view_fragment(@builtin(position) position: vec4<f32>) -> @location(0) vec4<f32> {
            let source = textureLoad(mu3d_view_input, vec2<i32>(position.xy), 0);
            let flags = u32(mu3d_view_params.z);
            var rgb = source.rgb;
            if ((flags & 1u) != 0u) {
                if (source.a <= 0.0) { return vec4<f32>(0.0); }
                rgb /= source.a;
            }
            rgb = mu3d_color_view(rgb * mu3d_view_params.x) * mu3d_view_params.y;
            if ((flags & 2u) != 0u) { rgb *= source.a; }
            if ((flags & 4u) != 0u) {
                rgb = select(12.92 * rgb, 1.055 * pow(max(rgb, vec3<f32>(0.0)), vec3<f32>(1.0/2.4)) - 0.055,
                    rgb > vec3<f32>(0.0031308));
            }
            return vec4<f32>(rgb, source.a);
        }
        """;
}

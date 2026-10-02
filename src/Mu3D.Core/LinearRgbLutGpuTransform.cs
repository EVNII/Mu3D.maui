using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;

namespace Mu3D.Rendering;

/// <summary>
/// Applies an explicitly tagged 3D linear RGB lookup table to straight-alpha HDR textures without
/// CPU readback. The table and manual trilinear interpolation use FP32; output storage is explicit.
/// </summary>
/// <remarks>
/// The GPU path requires an explicitly authored <see cref="ColorLutRangePolicy.Clamp"/> table:
/// per-pixel range rejection cannot be reported synchronously without readback. The caller supplies
/// finite, straight-alpha linear RGB and alpha in [0, 1]. RGB outside the authored input domain is
/// clamped by that explicit policy; transformed output is neither tone mapped nor gamut clipped.
/// Alpha is copied before output-storage rounding. Instances are not thread-safe and own only their
/// internal resources, never the device or caller textures. No UI or presentation policy is selected.
/// Domain endpoints must be zero or normal FP32 values and domain widths must be normal positive
/// FP32 values: shaders may flush subnormals to zero, which would collapse a domain or its divisor.
/// </remarks>
public sealed class LinearRgbLutGpuTransform : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly GraphicsTexture tableTexture;
    private readonly GraphicsTextureView tableView;
    private readonly GraphicsBuffer parameters;
    private readonly GraphicsBindGroupLayout bindGroupLayout;
    private readonly GraphicsPipelineLayout pipelineLayout;
    private readonly GraphicsShaderModule shader;
    private readonly GraphicsRenderPipeline pipeline;
    private bool disposed;

    /// <summary>Uploads an immutable FP32 table and creates a reusable HDR color-transform pipeline.</summary>
    /// <param name="device">The caller-owned graphics device.</param>
    /// <param name="table">A finite linear RGB table with an explicitly selected Clamp range policy.</param>
    /// <param name="outputPrecision">Explicit Float16 or Float32 output storage.</param>
    /// <exception cref="NotSupportedException">The table uses Reject, or Float16 textures are unavailable.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Float16 output cannot contain the table's finite output range, or the domain requires subnormal FP32 values.
    /// </exception>
    public LinearRgbLutGpuTransform(GraphicsDevice device, LinearRgbLut3D table, PixelPrecision outputPrecision)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(table);
        if (!Enum.IsDefined(outputPrecision))
        {
            throw new ArgumentOutOfRangeException(nameof(outputPrecision));
        }
        if (table.RangePolicy != ColorLutRangePolicy.Clamp)
        {
            throw new NotSupportedException("GPU LUT transforms require an explicitly authored Clamp range policy; use the CPU transform for per-pixel rejection.");
        }
        Vector3 domainExtent = table.DomainMaximum - table.DomainMinimum;
        for (int channel = 0; channel < 3; channel++)
        {
            if (float.IsSubnormal(table.DomainMinimum[channel]) || float.IsSubnormal(table.DomainMaximum[channel]) ||
                !float.IsNormal(domainExtent[channel]))
            {
                throw new ArgumentOutOfRangeException(nameof(table),
                    "GPU LUT domain endpoints must be zero or normal FP32 values and widths must be normal; use the CPU transform for subnormal domains.");
            }
        }
        if (outputPrecision == PixelPrecision.Float16)
        {
            if (!device.Capabilities.SupportsFloat16Textures)
            {
                throw new NotSupportedException("The graphics device does not support Float16 textures.");
            }
            foreach (Vector3 value in table.Samples)
            {
                if (MathF.Abs(value.X) > 65504f || MathF.Abs(value.Y) > 65504f || MathF.Abs(value.Z) > 65504f)
                {
                    throw new ArgumentOutOfRangeException(nameof(table), "The table exceeds finite Float16 output; select Float32 explicitly.");
                }
            }
        }

        this.device = device;
        SourceSpace = table.SourceSpace;
        DestinationSpace = table.DestinationSpace;
        OutputPrecision = outputPrecision;
        OutputFormat = outputPrecision == PixelPrecision.Float16
            ? GraphicsTextureFormat.Rgba16Float : GraphicsTextureFormat.Rgba32Float;
        Table = table;
        List<IDisposable> created = [];
        try
        {
            // R is the fastest axis, followed by G then B: a B slice occupies one complete row.
            uint edge = (uint)table.Size;
            GraphicsExtent3D extent = new(edge * edge, edge);
            tableTexture = Own(device.CreateTexture(new GraphicsTextureDescriptor(extent,
                GraphicsTextureFormat.Rgba32Float,
                GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.CopyDestination,
                label: "Mu3D FP32 color LUT")));
            Vector4[] pixels = new Vector4[table.Samples.Count];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Vector4(table.Samples[i], 1f);
            }
            device.Queue.WriteTexture(tableTexture, 0, default, extent,
                MemoryMarshal.AsBytes(pixels.AsSpan()), extent.Width * 16, extent.Height);
            tableView = Own(device.CreateTextureView(new GraphicsTextureViewDescriptor(tableTexture,
                label: "Mu3D FP32 color LUT view")));
            parameters = Own(device.CreateBuffer(new GraphicsBufferDescriptor(48,
                GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination, "Mu3D color LUT parameters")));
            Vector4[] uniform =
            [
                new Vector4(table.DomainMinimum, table.Size),
                new Vector4(table.DomainMaximum, 0f),
                new Vector4(domainExtent, 0f),
            ];
            device.Queue.WriteBuffer(parameters, 0, MemoryMarshal.AsBytes(uniform.AsSpan()));
            bindGroupLayout = Own(device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
            [
                new GraphicsBindGroupLayoutEntry(0, GraphicsShaderStage.Fragment,
                    GraphicsTextureSampleType.UnfilterableFloat, GraphicsTextureViewDimension.TwoD),
                new GraphicsBindGroupLayoutEntry(1, GraphicsShaderStage.Fragment,
                    GraphicsTextureSampleType.UnfilterableFloat, GraphicsTextureViewDimension.TwoD),
                new GraphicsBindGroupLayoutEntry(2, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, 48),
            ], "Mu3D color LUT bindings")));
            pipelineLayout = Own(device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor(
                [bindGroupLayout], "Mu3D color LUT pipeline layout")));
            shader = Own(device.CreateShaderModule(new GraphicsShaderModuleDescriptor(ShaderSource, "Mu3D color LUT shader")));
            pipeline = Own(device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                shader, "vs_main", shader, "fs_main", OutputFormat,
                layout: pipelineLayout, label: "Mu3D color LUT transform")));
        }
        catch
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                created[i].Dispose();
            }
            throw;
        }

        T Own<T>(T resource) where T : IDisposable
        {
            created.Add(resource);
            return resource;
        }
    }

    /// <summary>Gets the immutable CPU reference table represented by the uploaded GPU data.</summary>
    public LinearRgbLut3D Table { get; }

    /// <summary>Gets the required source linear-light color identity.</summary>
    public ColorSpaceReference SourceSpace { get; }

    /// <summary>Gets the transformed destination linear-light color identity.</summary>
    public ColorSpaceReference DestinationSpace { get; }

    /// <summary>Gets the explicitly selected destination storage precision.</summary>
    public PixelPrecision OutputPrecision { get; }

    /// <summary>Gets the required destination texture format.</summary>
    public GraphicsTextureFormat OutputFormat { get; }

    /// <summary>
    /// Submits a transform of the entire source into a separate same-sized destination texture.
    /// Both textures must be single-layer, single-mip, single-sample Float16/Float32 linear RGBA;
    /// source needs TextureBinding and destination needs RenderAttachment usage. The supplied
    /// color identities must equal the table identities. Caller textures remain caller-owned.
    /// </summary>
    /// <remarks>
    /// The destination format must equal <see cref="OutputFormat"/>. No CPU wait or readback occurs;
    /// later work on the device queue observes the transformed result in submission order.
    /// </remarks>
    public void Apply(GraphicsTexture source, ColorSpaceReference sourceSpace,
        GraphicsTexture destination, ColorSpaceReference destinationSpace)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(sourceSpace);
        ArgumentNullException.ThrowIfNull(destinationSpace);
        ValidateTexture(source, nameof(source));
        ValidateTexture(destination, nameof(destination));
        if (sourceSpace != SourceSpace || destinationSpace != DestinationSpace)
        {
            throw new ArgumentException("Texture color identities must match the LUT source and destination identities.");
        }
        if (ReferenceEquals(source, destination) || source.Descriptor.Size != destination.Descriptor.Size)
        {
            throw new ArgumentException("LUT input and output must be distinct textures with identical dimensions.");
        }
        if ((source.Descriptor.Usage & GraphicsTextureUsage.TextureBinding) == 0 ||
            (destination.Descriptor.Usage & GraphicsTextureUsage.RenderAttachment) == 0 ||
            destination.Descriptor.Format != OutputFormat)
        {
            throw new ArgumentException("Texture usage or destination format is incompatible with this LUT transform.");
        }

        using GraphicsTextureView sourceView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
            source, label: "Mu3D color LUT input view"));
        using GraphicsBindGroup bindings = device.CreateBindGroup(new GraphicsBindGroupDescriptor(bindGroupLayout,
        [
            new GraphicsBindGroupEntry(0, sourceView),
            new GraphicsBindGroupEntry(1, tableView),
            new GraphicsBindGroupEntry(2, parameters, 0, 48),
        ], "Mu3D color LUT input"));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("Mu3D color LUT encoder");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(destination), "Mu3D color LUT pass")))
        {
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, bindings);
            pass.Draw(3);
        }
        using GraphicsCommandBuffer commands = encoder.Finish("Mu3D color LUT commands");
        device.Queue.Submit(commands);
    }

    /// <summary>Releases all internal GPU resources. Repeated disposal is harmless.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        pipeline.Dispose();
        shader.Dispose();
        pipelineLayout.Dispose();
        bindGroupLayout.Dispose();
        parameters.Dispose();
        tableView.Dispose();
        tableTexture.Dispose();
    }

    private void ValidateTexture(GraphicsTexture texture, string name)
    {
        ArgumentNullException.ThrowIfNull(texture, name);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        if (!ReferenceEquals(texture.Device, device))
        {
            throw new ArgumentException("The texture belongs to a different graphics device.", name);
        }
        GraphicsTextureDescriptor descriptor = texture.Descriptor;
        if (descriptor.Size.DepthOrArrayLayers != 1 || descriptor.MipLevelCount != 1 ||
            descriptor.SampleCount != 1 || descriptor.Format is not
                (GraphicsTextureFormat.Rgba16Float or GraphicsTextureFormat.Rgba32Float))
        {
            throw new ArgumentException("A single-layer, single-mip, single-sample linear Float16/Float32 RGBA texture is required.", name);
        }
    }

    private const string ShaderSource = """
        struct LutParameters {
            minimum_and_size: vec4f,
            maximum: vec4f,
            extent: vec4f,
        }
        @group(0) @binding(0) var input_image: texture_2d<f32>;
        @group(0) @binding(1) var color_table: texture_2d<f32>;
        @group(0) @binding(2) var<uniform> lut: LutParameters;

        @vertex fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
            var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
            return vec4f(positions[index], 0.0, 1.0);
        }

        fn sample_table(p: vec3i, edge: i32) -> vec3f {
            return textureLoad(color_table, vec2i(p.x + edge * p.y, p.z), 0).rgb;
        }

        fn interpolate(a: vec3f, b: vec3f, amount: f32) -> vec3f {
            return a * (1.0 - amount) + b * amount;
        }

        @fragment fn fs_main(@builtin(position) position: vec4f) -> @location(0) vec4f {
            let source = textureLoad(input_image, vec2i(position.xy), 0);
            let edge = i32(lut.minimum_and_size.w);
            let coordinate = (clamp(source.rgb, lut.minimum_and_size.xyz, lut.maximum.xyz)
                - lut.minimum_and_size.xyz) / lut.extent.xyz * f32(edge - 1);
            let first = min(vec3i(coordinate), vec3i(edge - 2));
            let amount = coordinate - vec3f(first);
            let lower = interpolate(
                interpolate(sample_table(first, edge), sample_table(first + vec3i(1, 0, 0), edge), amount.x),
                interpolate(sample_table(first + vec3i(0, 1, 0), edge), sample_table(first + vec3i(1, 1, 0), edge), amount.x),
                amount.y);
            let upper = interpolate(
                interpolate(sample_table(first + vec3i(0, 0, 1), edge), sample_table(first + vec3i(1, 0, 1), edge), amount.x),
                interpolate(sample_table(first + vec3i(0, 1, 1), edge), sample_table(first + vec3i(1, 1, 1), edge), amount.x),
                amount.y);
            return vec4f(interpolate(lower, upper, amount.z), source.a);
        }
        """;
}

using Mu3D.Color;
using Mu3D.Graphics;

List<string> failures = [];

SurfaceCapabilities legacyHdrCapabilities = new(
    [PresentationFormat.Bgra8UnormSrgb, PresentationFormat.Rgba16Float],
    [SurfacePresentMode.Fifo],
    [SurfaceAlphaMode.Opaque],
    SupportsRgba16Float: true,
    []);
SurfaceOutputPlan legacyHdrPlan = SurfaceOutputNegotiator.Negotiate(
    legacyHdrCapabilities,
    OutputSettings.Default);
Expect(
    legacyHdrPlan.Output is
    {
        Format: PresentationFormat.Rgba16Float,
        DynamicRange: OutputDynamicRange.Hdr,
        Encoding: ColorEncoding.ExtendedSrgbLinear,
        HdrHeadroom: null,
    },
    "legacy format-only RGBA16Float HDR negotiation",
    failures);

SurfaceCapabilities explicitPqOnlyCapabilities = legacyHdrCapabilities with
{
    FormatCapabilities =
    [
        new SurfaceFormatCapability(
            PresentationFormat.Rgba16Float,
            [ColorEncoding.Bt2100Pq]),
        new SurfaceFormatCapability(
            PresentationFormat.Bgra8UnormSrgb,
            [ColorEncoding.Srgb]),
    ],
};
SurfaceOutputPlan explicitPqOnlyPlan = SurfaceOutputNegotiator.Negotiate(
    explicitPqOnlyCapabilities,
    OutputSettings.Default);
Expect(
    explicitPqOnlyPlan.Output.DynamicRange == OutputDynamicRange.Sdr &&
    explicitPqOnlyPlan.Output.Format == PresentationFormat.Bgra8UnormSrgb,
    "explicit format/color-space pairs prevent false extended-linear HDR",
    failures);

SurfaceCapabilities explicitExtendedLinearCapabilities = explicitPqOnlyCapabilities with
{
    FormatCapabilities =
    [
        new SurfaceFormatCapability(
            PresentationFormat.Rgba16Float,
            [ColorEncoding.Bt2100Pq, ColorEncoding.ExtendedSrgbLinear]),
        new SurfaceFormatCapability(
            PresentationFormat.Bgra8UnormSrgb,
            [ColorEncoding.Srgb]),
    ],
};
SurfaceOutputPlan explicitExtendedLinearPlan = SurfaceOutputNegotiator.Negotiate(
    explicitExtendedLinearCapabilities,
    OutputSettings.Default);
Expect(
    explicitExtendedLinearPlan.Output.DynamicRange == OutputDynamicRange.Hdr &&
    explicitExtendedLinearPlan.Output.Encoding == ColorEncoding.ExtendedSrgbLinear,
    "explicit extended-linear format/color-space pair negotiation",
    failures);
Expect(
    explicitExtendedLinearCapabilities
        .GetColorEncodings(PresentationFormat.Rgba16Float)
        .SequenceEqual([ColorEncoding.Bt2100Pq, ColorEncoding.ExtendedSrgbLinear]),
    "format color-space capability lookup",
    failures);

SurfaceOutputPlan invalidLegacyHdrFormatPlan = SurfaceOutputNegotiator.Negotiate(
    legacyHdrCapabilities,
    OutputSettings.Default with
    {
        PreferredHdrFormat = PresentationFormat.Bgra8UnormSrgb,
    });
Expect(
    invalidLegacyHdrFormatPlan.Output.DynamicRange == OutputDynamicRange.Sdr,
    "legacy non-FP16 format is not promoted to HDR",
    failures);

Expect(
    DisplayHdrInfo.Unknown.IsUnknown && DisplayHdrInfo.Unknown.ToneMapHeadroom is null,
    "unknown display HDR information remains unknown",
    failures);
DisplayHdrInfo appleDisplayInfo = new()
{
    Headroom = new DisplayHeadroom(Current: 3, Potential: 5, Reference: null),
};
Expect(appleDisplayInfo.ToneMapHeadroom == 3, "current EDR headroom precedence", failures);
DisplayHdrInfo windowsDisplayInfo = new()
{
    Luminance = new DisplayLuminance(
        MaximumNits: 800,
        MaximumFullFrameNits: 500,
        MinimumNits: 0.01f,
        SdrWhiteNits: 200),
};
Expect(windowsDisplayInfo.ToneMapHeadroom == 4, "nit-derived display headroom", failures);
DisplayHdrInfo definiteSdrDisplayInfo = windowsDisplayInfo with
{
    CoarseRange = new DisplayCoarseRange(
        SupportsHighDynamicRange: false,
        Gamut: DisplayGamut.DisplayP3),
};
Expect(
    definiteSdrDisplayInfo.ToneMapHeadroom == 1,
    "definite SDR display overrides physical nit ratio",
    failures);
DisplayHdrInfo nonFiniteHeadroomInfo = windowsDisplayInfo with
{
    Headroom = new DisplayHeadroom(float.PositiveInfinity, null, null),
};
Expect(
    nonFiniteHeadroomInfo.ToneMapHeadroom == 4,
    "non-finite EDR headroom falls through to finite nits",
    failures);

ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new GraphicsBufferDescriptor(0, GraphicsBufferUsage.CopySource),
    "zero buffer size",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new GraphicsBufferDescriptor(16, GraphicsBufferUsage.None),
    "empty buffer usage",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => _ = new GraphicsExtent3D(0),
    "zero texture width",
    failures);

using MockGraphicsDevice device = new();
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.CreateBuffer(default),
    "default buffer descriptor",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.CreateTexture(default),
    "default texture descriptor",
    failures);
ExpectThrows<ArgumentException>(
    () => device.CreateBuffer(new GraphicsBufferDescriptor(
        16,
        GraphicsBufferUsage.MapRead | GraphicsBufferUsage.Uniform)),
    "mapped read usage restriction",
    failures);
ExpectThrows<ArgumentException>(
    () => device.CreateBuffer(new GraphicsBufferDescriptor(
        16,
        GraphicsBufferUsage.MapRead | GraphicsBufferUsage.MapWrite)),
    "exclusive mapped usage",
    failures);
MockGraphicsBuffer source = (MockGraphicsBuffer)device.CreateBuffer(
    new GraphicsBufferDescriptor(
        8,
        GraphicsBufferUsage.CopySource | GraphicsBufferUsage.CopyDestination,
        "source"));
MockGraphicsBuffer destination = (MockGraphicsBuffer)device.CreateBuffer(
    new GraphicsBufferDescriptor(8, GraphicsBufferUsage.CopyDestination, "destination"));

device.Queue.WriteBuffer(source, 0, new byte[] { 1, 2, 3, 4 });
Expect(
    source.Data.AsSpan(0, 4).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
    "queue buffer write",
    failures);
Expect(source.Label == "source" && source.Size == 8, "buffer descriptor identity", failures);

using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("copy encoder");
ExpectThrows<ArgumentException>(
    () => device.Queue.WriteBuffer(source, 0, [1]),
    "unaligned queue write",
    failures);
ExpectThrows<ArgumentException>(
    () => encoder.CopyBufferToBuffer(source, 1, destination, 0, 4),
    "unaligned buffer copy",
    failures);
encoder.CopyBufferToBuffer(source, 0, destination, 4, 4);
GraphicsCommandBuffer commands = encoder.Finish("copy commands");
Expect(destination.Data.AsSpan().SequenceEqual(new byte[8]), "copy waits for submit", failures);
device.Queue.Submit(commands);
Expect(
    destination.Data.AsSpan(4, 4).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
    "submitted buffer copy",
    failures);
Expect(commands.IsSubmitted, "command buffer submission state", failures);
ExpectThrows<InvalidOperationException>(() => device.Queue.Submit(commands), "single submission", failures);
ExpectThrows<InvalidOperationException>(() => encoder.Finish(), "single finish", failures);
ExpectThrows<InvalidOperationException>(
    () => destination.ReadAsync(0, 4),
    "unmapped buffer read rejection",
    failures);

using MockGraphicsTexture readbackTexture = (MockGraphicsTexture)device.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(2, 2),
        GraphicsTextureFormat.Rgba8Unorm,
        GraphicsTextureUsage.CopySource | GraphicsTextureUsage.CopyDestination,
        label: "readback texture"));
byte[] readbackTexels =
[
    1, 2, 3, 4, 5, 6, 7, 8,
    9, 10, 11, 12, 13, 14, 15, 16,
];
device.Queue.WriteTexture(
    readbackTexture,
    0,
    default,
    new GraphicsExtent3D(2, 2),
    readbackTexels,
    bytesPerRow: 8,
    rowsPerImage: 2);
using MockGraphicsBuffer readbackBuffer = (MockGraphicsBuffer)device.CreateBuffer(
    new GraphicsBufferDescriptor(
        512,
        GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead,
        "texture readback"));
using GraphicsCommandEncoder readbackEncoder = device.CreateCommandEncoder("texture readback");
ExpectThrows<ArgumentOutOfRangeException>(
    () => readbackEncoder.CopyTextureToBuffer(
        readbackTexture,
        0,
        default,
        new GraphicsExtent3D(2, 2),
        readbackBuffer,
        0,
        bytesPerRow: 8,
        rowsPerImage: 2),
    "texture readback row alignment",
    failures);
readbackEncoder.CopyTextureToBuffer(
    readbackTexture,
    0,
    default,
    new GraphicsExtent3D(2, 2),
    readbackBuffer,
    0,
    bytesPerRow: 256,
    rowsPerImage: 2);
using GraphicsCommandBuffer readbackCommands = readbackEncoder.Finish();
device.Queue.Submit(readbackCommands);
byte[] readbackBytes = await readbackBuffer.ReadAsync(0, 512);
Expect(
    readbackBytes.AsSpan(0, 8).SequenceEqual(readbackTexels.AsSpan(0, 8)) &&
    readbackBytes.AsSpan(256, 8).SequenceEqual(readbackTexels.AsSpan(8, 8)),
    "row-padded texture readback",
    failures);
ExpectThrows<ArgumentException>(
    () => readbackBuffer.ReadAsync(4, 4),
    "mapped read offset alignment",
    failures);
using MockGraphicsTexture depthReadbackTexture = (MockGraphicsTexture)device.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(2, 2),
        GraphicsTextureFormat.Depth32Float,
        GraphicsTextureUsage.CopySource | GraphicsTextureUsage.RenderAttachment,
        label: "unsupported depth readback"));
using GraphicsCommandEncoder depthReadbackEncoder = device.CreateCommandEncoder("depth readback");
ExpectThrows<NotSupportedException>(
    () => depthReadbackEncoder.CopyTextureToBuffer(
        depthReadbackTexture,
        0,
        default,
        new GraphicsExtent3D(2, 2),
        readbackBuffer,
        0,
        bytesPerRow: 256,
        rowsPerImage: 2),
    "depth texture readback rejection",
    failures);

using MockGraphicsTexture copiedTexture = (MockGraphicsTexture)device.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(2, 2),
        GraphicsTextureFormat.Rgba8Unorm,
        GraphicsTextureUsage.CopyDestination,
        label: "copied texture"));
using GraphicsCommandEncoder textureCopyEncoder = device.CreateCommandEncoder("texture copy");
textureCopyEncoder.CopyTextureToTexture(
    readbackTexture,
    0,
    default,
    copiedTexture,
    0,
    default,
    new GraphicsExtent3D(2, 2));
using GraphicsCommandBuffer textureCopyCommands = textureCopyEncoder.Finish();
device.Queue.Submit(textureCopyCommands);
Expect(
    copiedTexture.Writes.Count == 1 &&
    copiedTexture.Writes[0].Data.SequenceEqual(readbackTexels),
    "texture-to-texture copy",
    failures);
using GraphicsTexture mismatchedCopyTexture = device.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(2, 2),
        GraphicsTextureFormat.Rgba16Float,
        GraphicsTextureUsage.CopyDestination));
using GraphicsCommandEncoder invalidTextureCopyEncoder = device.CreateCommandEncoder();
ExpectThrows<ArgumentException>(
    () => invalidTextureCopyEncoder.CopyTextureToTexture(
        readbackTexture,
        0,
        default,
        mismatchedCopyTexture,
        0,
        default,
        new GraphicsExtent3D(2, 2)),
    "texture copy format mismatch",
    failures);

GraphicsTextureDescriptor textureDescriptor = new(
    new GraphicsExtent3D(64, 32),
    GraphicsTextureFormat.Rgba16Float,
    GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.RenderAttachment,
    label: "HDR target");
using GraphicsTexture texture = device.CreateTexture(textureDescriptor);
Expect(texture.Descriptor == textureDescriptor, "texture descriptor identity", failures);
using (GraphicsCommandEncoder viewportEncoder = device.CreateCommandEncoder("viewport validation"))
{
    using (GraphicsRenderPassEncoder viewportPass = viewportEncoder.BeginRenderPass(
        new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(texture),
            "viewport validation pass")))
    {
        viewportPass.SetViewport(4f, 2f, 32f, 16f);
        viewportPass.SetScissorRect(4, 2, 32, 16);
        ExpectThrows<ArgumentOutOfRangeException>(
            () => viewportPass.SetViewport(60f, 0f, 8f, 8f),
            "viewport attachment bounds",
            failures);
        ExpectThrows<ArgumentOutOfRangeException>(
            () => viewportPass.SetScissorRect(0, 30, 8, 4),
            "scissor attachment bounds",
            failures);
    }
    using GraphicsCommandBuffer viewportCommands = viewportEncoder.Finish();
    device.Queue.Submit(viewportCommands);
}
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.CreateTexture(new GraphicsTextureDescriptor(
        new GraphicsExtent3D(4, 4),
        GraphicsTextureFormat.Rgba16Float,
        GraphicsTextureUsage.TextureBinding,
        mipLevelCount: 4)),
    "texture excessive mip count",
    failures);

using MockGraphicsTexture environmentTexture = (MockGraphicsTexture)device.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(4, 4, 6),
        GraphicsTextureFormat.Rgba16Float,
        GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
        mipLevelCount: 3,
        label: "environment cube"));
byte[] mipPixels = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
device.Queue.WriteTexture(
    environmentTexture,
    mipLevel: 1,
    new GraphicsOrigin3D(z: 5),
    new GraphicsExtent3D(2, 2),
    mipPixels,
    bytesPerRow: 16,
    rowsPerImage: 2);
Expect(
    environmentTexture.Writes.Count == 1 &&
    environmentTexture.Writes[0].MipLevel == 1 &&
    environmentTexture.Writes[0].Origin.Z == 5 &&
    environmentTexture.Writes[0].Data.SequenceEqual(mipPixels),
    "queue texture mip/layer write",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.Queue.WriteTexture(
        environmentTexture,
        1,
        default,
        new GraphicsExtent3D(2, 2),
        mipPixels,
        bytesPerRow: 8,
        rowsPerImage: 2),
    "texture write row stride",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.Queue.WriteTexture(
        environmentTexture,
        1,
        new GraphicsOrigin3D(z: 6),
        new GraphicsExtent3D(2, 2),
        mipPixels,
        bytesPerRow: 16,
        rowsPerImage: 2),
    "texture write layer bounds",
    failures);
ExpectThrows<ArgumentException>(
    () => device.Queue.WriteTexture(
        environmentTexture,
        1,
        default,
        new GraphicsExtent3D(2, 2),
        mipPixels.AsSpan(0, 16),
        bytesPerRow: 16,
        rowsPerImage: 2),
    "texture write source size",
    failures);

using MockGraphicsTexture astcTexture = (MockGraphicsTexture)device.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(10, 10),
        GraphicsTextureFormat.Astc6x6UnormSrgb,
        GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
        label: "ASTC edge-block texture"));
byte[] astcBlocks = new byte[64];
device.Queue.WriteTexture(
    astcTexture,
    0,
    default,
    new GraphicsExtent3D(12, 12),
    astcBlocks,
    bytesPerRow: 32,
    rowsPerImage: 2);
Expect(
    astcTexture.Writes.Single().Data.Length == 64 &&
    astcTexture.Writes[0].RowsPerImage == 2,
    "compressed texture padded edge blocks and block-row layout",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.Queue.WriteTexture(
        astcTexture,
        0,
        default,
        new GraphicsExtent3D(10, 10),
        astcBlocks,
        bytesPerRow: 32,
        rowsPerImage: 2),
    "compressed texture logical extent rejection",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.Queue.WriteTexture(
        astcTexture,
        0,
        new GraphicsOrigin3D(x: 1),
        new GraphicsExtent3D(6, 6),
        astcBlocks,
        bytesPerRow: 16,
        rowsPerImage: 1),
    "compressed texture block alignment",
    failures);
ExpectThrows<ArgumentException>(
    () => device.CreateTexture(new GraphicsTextureDescriptor(
        new GraphicsExtent3D(8, 8),
        GraphicsTextureFormat.Bc7RgbaUnorm,
        GraphicsTextureUsage.RenderAttachment)),
    "compressed render attachment rejection",
    failures);
using MockGraphicsDevice noCompressionDevice = new(supportsTextureCompression: false);
ExpectThrows<NotSupportedException>(
    () => noCompressionDevice.CreateTexture(new GraphicsTextureDescriptor(
        new GraphicsExtent3D(8, 8),
        GraphicsTextureFormat.Etc2Rgba8Unorm,
        GraphicsTextureUsage.TextureBinding)),
    "unavailable texture compression feature rejection",
    failures);

using GraphicsTextureView environmentCubeView = device.CreateTextureView(
    new GraphicsTextureViewDescriptor(
        environmentTexture,
        GraphicsTextureViewDimension.Cube,
        mipLevelCount: 3,
        arrayLayerCount: 6,
        label: "environment cube view"));
Expect(
    environmentCubeView.Descriptor.Dimension == GraphicsTextureViewDimension.Cube &&
    environmentCubeView.Descriptor.MipLevelCount == 3,
    "cube texture-view identity",
    failures);
ExpectThrows<ArgumentException>(
    () => device.CreateTextureView(new GraphicsTextureViewDescriptor(
        texture,
        GraphicsTextureViewDimension.Cube,
        arrayLayerCount: 1)),
    "cube texture-view layer validation",
    failures);

using GraphicsSampler sampler = device.CreateSampler(new GraphicsSamplerDescriptor(
    0f,
    0f,
    addressModeU: GraphicsAddressMode.Repeat,
    magFilter: GraphicsFilterMode.Linear,
    label: "linear sampler"));
Expect(
    sampler.Descriptor.AddressModeU == GraphicsAddressMode.Repeat &&
    sampler.Descriptor.MagFilter == GraphicsFilterMode.Linear &&
    sampler.Descriptor.LodMinClamp == 0f && sampler.Descriptor.LodMaxClamp == 0f,
    "sampler descriptor identity",
    failures);
using GraphicsBindGroupLayout environmentLayout = device.CreateBindGroupLayout(
    new GraphicsBindGroupLayoutDescriptor(
        [
            new GraphicsBindGroupLayoutEntry(
                0,
                GraphicsShaderStage.Fragment,
                GraphicsSamplerBindingType.Filtering),
            new GraphicsBindGroupLayoutEntry(
                1,
                GraphicsShaderStage.Fragment,
                GraphicsTextureSampleType.Float,
                GraphicsTextureViewDimension.Cube),
        ],
        "environment sampled layout"));
using GraphicsBindGroup environmentBindGroup = device.CreateBindGroup(
    new GraphicsBindGroupDescriptor(
        environmentLayout,
        [
            new GraphicsBindGroupEntry(0, sampler),
            new GraphicsBindGroupEntry(1, environmentCubeView),
        ],
        "environment sampled resources"));
Expect(
    ReferenceEquals(environmentBindGroup.Descriptor.Entries[0].Sampler, sampler) &&
    ReferenceEquals(environmentBindGroup.Descriptor.Entries[1].TextureView, environmentCubeView),
    "sampled texture and sampler bind group",
    failures);
using GraphicsTextureView astcView = device.CreateTextureView(
    new GraphicsTextureViewDescriptor(astcTexture));
using GraphicsBindGroupLayout compressedLayout = device.CreateBindGroupLayout(
    new GraphicsBindGroupLayoutDescriptor(
        [new GraphicsBindGroupLayoutEntry(
            0,
            GraphicsShaderStage.Fragment,
            GraphicsTextureSampleType.Float,
            GraphicsTextureViewDimension.TwoD)]));
using GraphicsBindGroup compressedBindGroup = device.CreateBindGroup(
    new GraphicsBindGroupDescriptor(
        compressedLayout,
        [new GraphicsBindGroupEntry(0, astcView)]));
Expect(
    ReferenceEquals(compressedBindGroup.Descriptor.Entries[0].TextureView, astcView),
    "compressed texture sampled binding",
    failures);
using GraphicsBindGroupLayout nonFilteringLayout = device.CreateBindGroupLayout(
    new GraphicsBindGroupLayoutDescriptor(
        [new GraphicsBindGroupLayoutEntry(
            0,
            GraphicsShaderStage.Fragment,
            GraphicsSamplerBindingType.NonFiltering)]));
ExpectThrows<ArgumentException>(
    () => device.CreateBindGroup(new GraphicsBindGroupDescriptor(
        nonFilteringLayout,
        [new GraphicsBindGroupEntry(0, sampler)])),
    "non-filtering sampler validation",
    failures);
using GraphicsBindGroupLayout twoDTextureLayout = device.CreateBindGroupLayout(
    new GraphicsBindGroupLayoutDescriptor(
        [new GraphicsBindGroupLayoutEntry(
            0,
            GraphicsShaderStage.Fragment,
            GraphicsTextureSampleType.Float,
            GraphicsTextureViewDimension.TwoD)]));
ExpectThrows<ArgumentException>(
    () => device.CreateBindGroup(new GraphicsBindGroupDescriptor(
        twoDTextureLayout,
        [new GraphicsBindGroupEntry(0, environmentCubeView)])),
    "sampled texture-view dimension validation",
    failures);
using GraphicsTexture shadowTexture = device.CreateTexture(new GraphicsTextureDescriptor(
    new GraphicsExtent3D(32, 32),
    GraphicsTextureFormat.Depth32Float,
    GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding));
using GraphicsTextureView shadowView = device.CreateTextureView(
    new GraphicsTextureViewDescriptor(shadowTexture));
using GraphicsSampler comparisonSampler = device.CreateSampler(new GraphicsSamplerDescriptor(
    magFilter: GraphicsFilterMode.Linear,
    minFilter: GraphicsFilterMode.Linear,
    compare: GraphicsCompareFunction.LessEqual));
using GraphicsBindGroupLayout shadowLayout = device.CreateBindGroupLayout(
    new GraphicsBindGroupLayoutDescriptor(
        [
            new GraphicsBindGroupLayoutEntry(
                0,
                GraphicsShaderStage.Fragment,
                GraphicsSamplerBindingType.Comparison),
            new GraphicsBindGroupLayoutEntry(
                1,
                GraphicsShaderStage.Fragment,
                GraphicsTextureSampleType.Depth,
                GraphicsTextureViewDimension.TwoD),
        ]));
using GraphicsBindGroup shadowBindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
    shadowLayout,
    [new GraphicsBindGroupEntry(0, comparisonSampler), new GraphicsBindGroupEntry(1, shadowView)]));
Expect(
    comparisonSampler.Descriptor.Compare == GraphicsCompareFunction.LessEqual &&
    shadowBindGroup.Descriptor.Entries.Count == 2,
    "comparison sampler and depth texture binding",
    failures);
ExpectThrows<ArgumentException>(
    () => device.CreateBindGroup(new GraphicsBindGroupDescriptor(
        shadowLayout,
        [new GraphicsBindGroupEntry(0, sampler), new GraphicsBindGroupEntry(1, shadowView)])),
    "comparison sampler identity validation",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.CreateSampler(new GraphicsSamplerDescriptor(addressModeU: (GraphicsAddressMode)99)),
    "unknown sampler enum",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.CreateSampler(new GraphicsSamplerDescriptor(2f, 1f)),
    "sampler LOD clamp order",
    failures);
ExpectThrows<ArgumentException>(
    () => device.CreateShaderModule(default),
    "default shader descriptor",
    failures);

const string triangleShader = """
    @vertex fn vs_main(@builtin(vertex_index) vertex_index: u32) -> @builtin(position) vec4f {
        var positions = array(vec2f(-0.7, -0.7), vec2f(0.7, -0.7), vec2f(0.0, 0.7));
        return vec4f(positions[vertex_index], 0.0, 1.0);
    }

    @fragment fn fs_main() -> @location(0) vec4f {
        return vec4f(0.05, 0.15, 2.0, 1.0);
    }
    """;
using GraphicsShaderModule shader = device.CreateShaderModule(
    new GraphicsShaderModuleDescriptor(triangleShader, "offscreen triangle shader"));
using GraphicsRenderPipeline pipeline = device.CreateRenderPipeline(
    new GraphicsRenderPipelineDescriptor(
        shader,
        "vs_main",
        shader,
        "fs_main",
        GraphicsTextureFormat.Rgba16Float,
        label: "offscreen triangle pipeline"));
using GraphicsRenderPipeline blendedPipeline = device.CreateRenderPipeline(
    new GraphicsRenderPipelineDescriptor(
        shader,
        "vs_main",
        shader,
        "fs_main",
        GraphicsTextureFormat.Rgba16Float,
        label: "premultiplied alpha pipeline",
        depthStencil: new GraphicsDepthStencilState(
            GraphicsTextureFormat.Depth32Float,
            depthWriteEnabled: false),
        blend: GraphicsBlendState.PremultipliedAlpha));
Expect(
    blendedPipeline.Descriptor.Blend == GraphicsBlendState.PremultipliedAlpha &&
    blendedPipeline.Descriptor.DepthStencil?.DepthWriteEnabled == false,
    "premultiplied blend pipeline state",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
        shader,
        "vs_main",
        shader,
        "fs_main",
        GraphicsTextureFormat.Rgba16Float,
        blend: new GraphicsBlendState(
            new GraphicsBlendComponent(
                (GraphicsBlendOperation)99,
                GraphicsBlendFactor.One,
                GraphicsBlendFactor.Zero),
            GraphicsBlendState.PremultipliedAlpha.Alpha))),
    "unknown blend operation",
    failures);
using MockGraphicsTexture renderTarget = (MockGraphicsTexture)device.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(32, 32),
        GraphicsTextureFormat.Rgba16Float,
        GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource,
        label: "offscreen target"));
using GraphicsCommandEncoder renderEncoder = device.CreateCommandEncoder("render encoder");
GraphicsClearColor clearColor = new(0, 0, 0, 1);
GraphicsRenderPassDescriptor renderPassDescriptor = new(
    new GraphicsRenderPassColorAttachment(renderTarget, clearColor: clearColor),
    "triangle pass");
using (GraphicsRenderPassEncoder renderPass = renderEncoder.BeginRenderPass(renderPassDescriptor))
{
    ExpectThrows<InvalidOperationException>(
        () => renderEncoder.Finish(),
        "finish with active render pass",
        failures);
    ExpectThrows<InvalidOperationException>(
        () => renderEncoder.CopyBufferToBuffer(source, 0, destination, 0, 1),
        "outer command during render pass",
        failures);
    ExpectThrows<InvalidOperationException>(
        () => renderPass.Draw(3),
        "draw without pipeline",
        failures);
    renderPass.SetPipeline(pipeline);
    renderPass.Draw(3);
}
GraphicsCommandBuffer renderCommands = renderEncoder.Finish("triangle commands");
Expect(!renderTarget.Rendered, "render waits for submit", failures);
device.Queue.Submit(renderCommands);
Expect(renderTarget.Rendered, "offscreen triangle submitted", failures);
Expect(renderTarget.LastClearColor == clearColor, "offscreen clear value", failures);
Expect(renderTarget.Draws.Count == 1 && renderTarget.Draws[0].VertexCount == 3, "offscreen triangle draw", failures);

using GraphicsTexture mipRenderTexture = device.CreateTexture(new GraphicsTextureDescriptor(
    new GraphicsExtent3D(32, 32),
    GraphicsTextureFormat.Rgba16Float,
    GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
    mipLevelCount: 6));
using GraphicsTextureView mipRenderView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
    mipRenderTexture,
    baseMipLevel: 2,
    label: "explicit render mip"));
using GraphicsCommandEncoder mipRenderEncoder = device.CreateCommandEncoder();
using (GraphicsRenderPassEncoder mipRenderPass = mipRenderEncoder.BeginRenderPass(
    new GraphicsRenderPassDescriptor(new GraphicsRenderPassColorAttachment(mipRenderView))))
{
    mipRenderPass.SetPipeline(pipeline);
    mipRenderPass.Draw(3);
}
mipRenderEncoder.Finish().Dispose();
Expect(
    mipRenderView.Descriptor.BaseMipLevel == 2,
    "explicit texture-view render attachment retains selected mip",
    failures);

GraphicsVertexBufferLayout cubeVertexLayout = new(
    24,
    [
        new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 0, 0),
        new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 12, 1),
    ]);
using GraphicsBindGroupLayout transformBindGroupLayout = device.CreateBindGroupLayout(
    new GraphicsBindGroupLayoutDescriptor(
        [new GraphicsBindGroupLayoutEntry(
            0,
            GraphicsShaderStage.Vertex,
            GraphicsBufferBindingType.Uniform,
            64)],
        "transform bind-group layout"));
using GraphicsPipelineLayout indexedPipelineLayout = device.CreatePipelineLayout(
    new GraphicsPipelineLayoutDescriptor([transformBindGroupLayout], "indexed pipeline layout"));
using GraphicsBuffer transformBuffer = device.CreateBuffer(
    new GraphicsBufferDescriptor(
        64,
        GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination,
        "transform uniform"));
device.Queue.WriteBuffer(transformBuffer, 0, new byte[64]);
using GraphicsBindGroup transformBindGroup = device.CreateBindGroup(
    new GraphicsBindGroupDescriptor(
        transformBindGroupLayout,
        [new GraphicsBindGroupEntry(0, transformBuffer, 0, 64)],
        "transform bind group"));
using GraphicsBindGroupLayout alternateTransformLayout = device.CreateBindGroupLayout(
    new GraphicsBindGroupLayoutDescriptor(
        [new GraphicsBindGroupLayoutEntry(
            0,
            GraphicsShaderStage.Vertex,
            GraphicsBufferBindingType.Uniform,
            64)]));
using GraphicsBindGroup alternateTransformBindGroup = device.CreateBindGroup(
    new GraphicsBindGroupDescriptor(
        alternateTransformLayout,
        [new GraphicsBindGroupEntry(0, transformBuffer, 0, 64)]));
using GraphicsRenderPipeline indexedPipeline = device.CreateRenderPipeline(
    new GraphicsRenderPipelineDescriptor(
        shader,
        "vs_main",
        shader,
        "fs_main",
        GraphicsTextureFormat.Rgba16Float,
        label: "indexed depth pipeline",
        vertexBuffers: [cubeVertexLayout],
        depthStencil: new GraphicsDepthStencilState(GraphicsTextureFormat.Depth32Float),
        layout: indexedPipelineLayout));
using GraphicsBuffer vertexBuffer = device.CreateBuffer(
    new GraphicsBufferDescriptor(72, GraphicsBufferUsage.Vertex | GraphicsBufferUsage.CopyDestination));
using GraphicsBuffer indexBuffer = device.CreateBuffer(
    new GraphicsBufferDescriptor(6, GraphicsBufferUsage.Index | GraphicsBufferUsage.CopyDestination));
using MockGraphicsTexture depthTarget = (MockGraphicsTexture)device.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(32, 32),
        GraphicsTextureFormat.Depth32Float,
        GraphicsTextureUsage.RenderAttachment));
using GraphicsCommandEncoder indexedEncoder = device.CreateCommandEncoder("indexed encoder");
using (GraphicsRenderPassEncoder indexedPass = indexedEncoder.BeginRenderPass(
    new GraphicsRenderPassDescriptor(
        new GraphicsRenderPassColorAttachment(renderTarget),
        "indexed pass",
        new GraphicsRenderPassDepthAttachment(depthTarget))))
{
    indexedPass.SetPipeline(indexedPipeline);
    ExpectThrows<InvalidOperationException>(() => indexedPass.Draw(3), "missing bind group", failures);
    ExpectThrows<ArgumentException>(
        () => indexedPass.SetBindGroup(0, alternateTransformBindGroup),
        "incompatible bind-group layout",
        failures);
    indexedPass.SetBindGroup(0, transformBindGroup);
    ExpectThrows<InvalidOperationException>(() => indexedPass.Draw(3), "missing vertex buffer", failures);
    indexedPass.SetVertexBuffer(0, vertexBuffer);
    ExpectThrows<InvalidOperationException>(
        () => indexedPass.DrawIndexed(3),
        "missing index buffer",
        failures);
    indexedPass.SetIndexBuffer(indexBuffer, GraphicsIndexFormat.Uint16);
    indexedPass.DrawIndexed(3);
}
GraphicsCommandBuffer indexedCommands = indexedEncoder.Finish("indexed commands");
Expect(!depthTarget.Rendered, "indexed depth render waits for submit", failures);
device.Queue.Submit(indexedCommands);
Expect(depthTarget.Rendered, "indexed depth attachment submitted", failures);
Expect(
    renderTarget.Draws[^1].IsIndexed && renderTarget.Draws[^1].IndexCount == 3,
    "indexed draw submitted",
    failures);
using GraphicsRenderPipeline depthOnlyPipeline = device.CreateRenderPipeline(
    new GraphicsRenderPipelineDescriptor(
        shader,
        "vs_main",
        new GraphicsDepthStencilState(GraphicsTextureFormat.Depth32Float),
        label: "depth-only pipeline",
        vertexBuffers: [cubeVertexLayout],
        layout: indexedPipelineLayout,
        cullMode: GraphicsCullMode.Front));
using GraphicsRenderPipeline alphaTestedDepthPipeline = device.CreateRenderPipeline(
    new GraphicsRenderPipelineDescriptor(
        shader,
        "vs_main",
        shader,
        "fs_main",
        new GraphicsDepthStencilState(GraphicsTextureFormat.Depth32Float),
        label: "alpha-tested depth pipeline",
        vertexBuffers: [cubeVertexLayout],
        layout: indexedPipelineLayout,
        cullMode: GraphicsCullMode.Front));
using GraphicsCommandEncoder depthOnlyEncoder = device.CreateCommandEncoder("depth-only encoder");
using (GraphicsRenderPassEncoder depthOnlyPass = depthOnlyEncoder.BeginRenderPass(
    new GraphicsRenderPassDescriptor(
        new GraphicsRenderPassDepthAttachment(depthTarget),
        "depth-only pass")))
{
    depthOnlyPass.SetPipeline(depthOnlyPipeline);
    depthOnlyPass.SetBindGroup(0, transformBindGroup);
    depthOnlyPass.SetVertexBuffer(0, vertexBuffer);
    depthOnlyPass.SetIndexBuffer(indexBuffer, GraphicsIndexFormat.Uint16);
    depthOnlyPass.DrawIndexed(3);
}
using GraphicsCommandBuffer depthOnlyCommands = depthOnlyEncoder.Finish();
device.Queue.Submit(depthOnlyCommands);
Expect(
    depthOnlyPipeline.Descriptor.FragmentShader is null &&
    depthOnlyPipeline.Descriptor.ColorFormat is null,
    "depth-only pipeline and pass submission",
    failures);
Expect(
    ReferenceEquals(alphaTestedDepthPipeline.Descriptor.FragmentShader, shader) &&
    alphaTestedDepthPipeline.Descriptor.FragmentEntryPoint == "fs_main" &&
    alphaTestedDepthPipeline.Descriptor.ColorFormat is null &&
    alphaTestedDepthPipeline.Descriptor.DepthStencil is not null,
    "fragment-discard depth-only pipeline descriptor",
    failures);
Expect(
    ReferenceEquals(renderTarget.Draws[^1].BindGroups[0], transformBindGroup),
    "uniform bind group submitted",
    failures);
indexedCommands.Dispose();

ExpectThrows<ArgumentException>(
    () => device.CreateBindGroup(new GraphicsBindGroupDescriptor(
        transformBindGroupLayout,
        [new GraphicsBindGroupEntry(0, source, 0, 8)])),
    "uniform binding usage",
    failures);
ExpectThrows<ArgumentOutOfRangeException>(
    () => device.CreateBindGroup(new GraphicsBindGroupDescriptor(
        transformBindGroupLayout,
        [new GraphicsBindGroupEntry(0, transformBuffer, 0, 32)])),
    "uniform binding minimum size",
    failures);

ExpectThrows<ArgumentOutOfRangeException>(
    () => device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
        shader,
        "vs_main",
        shader,
        "fs_main",
        GraphicsTextureFormat.Rgba16Float,
        vertexBuffers: [new GraphicsVertexBufferLayout(
            12,
            [new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 0, 0)])])),
    "vertex attribute beyond stride",
    failures);

using GraphicsTexture wrongFormatTarget = device.CreateTexture(
    new GraphicsTextureDescriptor(
        new GraphicsExtent3D(8, 8),
        GraphicsTextureFormat.Rgba8Unorm,
        GraphicsTextureUsage.RenderAttachment));
using GraphicsCommandEncoder wrongFormatEncoder = device.CreateCommandEncoder();
using (GraphicsRenderPassEncoder wrongFormatPass = wrongFormatEncoder.BeginRenderPass(
    new GraphicsRenderPassDescriptor(new GraphicsRenderPassColorAttachment(wrongFormatTarget))))
{
    ExpectThrows<ArgumentException>(
        () => wrongFormatPass.SetPipeline(pipeline),
        "pipeline attachment format mismatch",
        failures);
}
wrongFormatEncoder.Finish().Dispose();
using GraphicsCommandEncoder invalidPassEncoder = device.CreateCommandEncoder();
ExpectThrows<ArgumentOutOfRangeException>(
    () => invalidPassEncoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
        new GraphicsRenderPassColorAttachment(
            renderTarget,
            clearColor: new GraphicsClearColor(float.NaN, 0, 0, 1)))),
    "non-finite clear value",
    failures);
invalidPassEncoder.Finish().Dispose();

using MockGraphicsDevice otherDevice = new();
using GraphicsBuffer otherBuffer = otherDevice.CreateBuffer(
    new GraphicsBufferDescriptor(8, GraphicsBufferUsage.CopyDestination));
ExpectThrows<ArgumentException>(
    () => device.Queue.WriteBuffer(otherBuffer, 0, [1]),
    "cross-device buffer rejection",
    failures);
using GraphicsShaderModule otherShader = otherDevice.CreateShaderModule(
    new GraphicsShaderModuleDescriptor(triangleShader));
ExpectThrows<ArgumentException>(
    () => device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
        otherShader,
        "vs_main",
        shader,
        "fs_main",
        GraphicsTextureFormat.Rgba16Float)),
    "cross-device shader rejection",
    failures);

destination.Dispose();
ExpectThrows<ObjectDisposedException>(
    () => device.Queue.WriteBuffer(destination, 0, [1]),
    "disposed buffer rejection",
    failures);
destination.Dispose();
Expect(((MockGraphicsBuffer)destination).DisposeCount == 1, "idempotent resource disposal", failures);

int lossEvents = 0;
device.DeviceLost += (_, eventArgs) =>
{
    lossEvents++;
    Expect(eventArgs.Reason == "mock reset", "device-loss reason", failures);
};
device.TriggerLoss("mock reset");
device.TriggerLoss("ignored second loss");
Expect(device.State == GraphicsDeviceState.Lost, "device lost state", failures);
Expect(lossEvents == 1, "single device-loss event", failures);
ExpectThrows<InvalidOperationException>(
    () => device.CreateBuffer(new GraphicsBufferDescriptor(4, GraphicsBufferUsage.Vertex)),
    "creation after device loss",
    failures);

commands.Dispose();
renderCommands.Dispose();
source.Dispose();

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("Validated backend-independent graphics resource, command, and loss contracts.");
return 0;

static void Expect(bool condition, string name, ICollection<string> failures)
{
    if (!condition)
    {
        failures.Add($"FAILED: {name}");
    }
}

static void ExpectThrows<TException>(Action action, string name, ICollection<string> failures)
    where TException : Exception
{
    try
    {
        action();
        failures.Add($"FAILED: {name} did not throw {typeof(TException).Name}");
    }
    catch (TException)
    {
    }
}

sealed class MockGraphicsDevice : GraphicsDevice
{
    private readonly MockGraphicsQueue queue;

    internal MockGraphicsDevice(bool supportsTextureCompression = true)
        : base(new GraphicsCapabilities
        {
            SupportsFloat16Textures = true,
            SupportsShaderFloat16 = true,
            SupportsHdrSurface = true,
            SupportsBcTextureCompression = supportsTextureCompression,
            SupportsEtc2TextureCompression = supportsTextureCompression,
            SupportsAstcTextureCompression = supportsTextureCompression,
            SurfaceFormats = [PresentationFormat.Rgba16Float],
        })
    {
        queue = new MockGraphicsQueue(this);
    }

    public override GraphicsQueue Queue => queue;

    internal void TriggerLoss(string reason) => ReportDeviceLost(reason);

    protected override GraphicsBuffer CreateBufferCore(GraphicsBufferDescriptor descriptor) =>
        new MockGraphicsBuffer(this, descriptor);

    protected override GraphicsTexture CreateTextureCore(GraphicsTextureDescriptor descriptor) =>
        new MockGraphicsTexture(this, descriptor);

    protected override GraphicsTextureView CreateTextureViewCore(GraphicsTextureViewDescriptor descriptor) =>
        new MockGraphicsTextureView(this, descriptor);

    protected override GraphicsSampler CreateSamplerCore(GraphicsSamplerDescriptor descriptor) =>
        new MockGraphicsSampler(this, descriptor);

    protected override GraphicsBindGroupLayout CreateBindGroupLayoutCore(
        GraphicsBindGroupLayoutDescriptor descriptor) =>
        new MockGraphicsBindGroupLayout(this, descriptor);

    protected override GraphicsPipelineLayout CreatePipelineLayoutCore(
        GraphicsPipelineLayoutDescriptor descriptor) =>
        new MockGraphicsPipelineLayout(this, descriptor);

    protected override GraphicsBindGroup CreateBindGroupCore(GraphicsBindGroupDescriptor descriptor) =>
        new MockGraphicsBindGroup(this, descriptor);

    protected override GraphicsShaderModule CreateShaderModuleCore(GraphicsShaderModuleDescriptor descriptor) =>
        new MockGraphicsShaderModule(this, descriptor);

    protected override GraphicsRenderPipeline CreateRenderPipelineCore(GraphicsRenderPipelineDescriptor descriptor) =>
        new MockGraphicsRenderPipeline(this, descriptor);

    protected override GraphicsCommandEncoder CreateCommandEncoderCore(string? label) =>
        new MockGraphicsCommandEncoder(this, label);

    protected override void DisposeCore()
    {
    }
}

sealed class MockGraphicsQueue(MockGraphicsDevice device) : GraphicsQueue(device)
{
    protected override void WriteBufferCore(
        GraphicsBuffer destination,
        ulong destinationOffset,
        ReadOnlySpan<byte> data) =>
        data.CopyTo(((MockGraphicsBuffer)destination).Data.AsSpan(checked((int)destinationOffset)));

    protected override void WriteTextureCore(
        GraphicsTexture destination,
        uint mipLevel,
        GraphicsOrigin3D origin,
        GraphicsExtent3D writeSize,
        ReadOnlySpan<byte> data,
        uint bytesPerRow,
        uint rowsPerImage) =>
        ((MockGraphicsTexture)destination).Writes.Add(new MockTextureWrite(
            mipLevel,
            origin,
            writeSize,
            data.ToArray(),
            bytesPerRow,
            rowsPerImage));

    protected override void SubmitCore(GraphicsCommandBuffer commandBuffer)
    {
        foreach (IMockCommand command in ((MockGraphicsCommandBuffer)commandBuffer).Commands)
        {
            command.Execute();
        }
    }
}

sealed class MockGraphicsBuffer : GraphicsBuffer
{
    internal MockGraphicsBuffer(MockGraphicsDevice device, GraphicsBufferDescriptor descriptor)
        : base(device, descriptor)
    {
        Data = new byte[checked((int)descriptor.Size)];
    }

    internal byte[] Data { get; }

    internal int DisposeCount { get; private set; }

    protected override Task<byte[]> ReadAsyncCore(
        ulong offset,
        int byteCount,
        CancellationToken cancellationToken) => Task.FromResult(
            Data.AsSpan(checked((int)offset), byteCount).ToArray());

    protected override void DisposeCore() => DisposeCount++;
}

sealed class MockGraphicsTexture : GraphicsTexture
{
    internal MockGraphicsTexture(MockGraphicsDevice device, GraphicsTextureDescriptor descriptor)
        : base(device, descriptor)
    {
    }

    internal bool Rendered { get; set; }

    internal GraphicsClearColor? LastClearColor { get; set; }

    internal List<MockDrawCommand> Draws { get; } = [];

    internal List<MockTextureWrite> Writes { get; } = [];

    protected override void DisposeCore()
    {
    }
}

sealed record MockTextureWrite(
    uint MipLevel,
    GraphicsOrigin3D Origin,
    GraphicsExtent3D Size,
    byte[] Data,
    uint BytesPerRow,
    uint RowsPerImage);

sealed class MockGraphicsTextureView(
    MockGraphicsDevice device,
    GraphicsTextureViewDescriptor descriptor) : GraphicsTextureView(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class MockGraphicsSampler(MockGraphicsDevice device, GraphicsSamplerDescriptor descriptor)
    : GraphicsSampler(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class MockGraphicsBindGroupLayout(
    MockGraphicsDevice device,
    GraphicsBindGroupLayoutDescriptor descriptor)
    : GraphicsBindGroupLayout(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class MockGraphicsPipelineLayout(
    MockGraphicsDevice device,
    GraphicsPipelineLayoutDescriptor descriptor)
    : GraphicsPipelineLayout(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class MockGraphicsBindGroup(MockGraphicsDevice device, GraphicsBindGroupDescriptor descriptor)
    : GraphicsBindGroup(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class MockGraphicsShaderModule(MockGraphicsDevice device, GraphicsShaderModuleDescriptor descriptor)
    : GraphicsShaderModule(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class MockGraphicsRenderPipeline(MockGraphicsDevice device, GraphicsRenderPipelineDescriptor descriptor)
    : GraphicsRenderPipeline(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class MockGraphicsCommandEncoder(MockGraphicsDevice device, string? label)
    : GraphicsCommandEncoder(device, label)
{
    private readonly List<IMockCommand> commands = [];

    protected override void CopyBufferToBufferCore(
        GraphicsBuffer source,
        ulong sourceOffset,
        GraphicsBuffer destination,
        ulong destinationOffset,
        ulong size) =>
        commands.Add(new MockCopyCommand(
            (MockGraphicsBuffer)source,
            sourceOffset,
            (MockGraphicsBuffer)destination,
            destinationOffset,
            size));

    protected override void CopyTextureToBufferCore(
        GraphicsTexture source,
        uint mipLevel,
        GraphicsOrigin3D origin,
        GraphicsExtent3D copySize,
        GraphicsBuffer destination,
        ulong destinationOffset,
        uint bytesPerRow,
        uint rowsPerImage) => commands.Add(new MockTextureToBufferCommand(
            (MockGraphicsTexture)source,
            mipLevel,
            origin,
            copySize,
            (MockGraphicsBuffer)destination,
            destinationOffset,
            bytesPerRow,
            rowsPerImage));

    protected override void CopyTextureToTextureCore(
        GraphicsTexture source,
        uint sourceMipLevel,
        GraphicsOrigin3D sourceOrigin,
        GraphicsTexture destination,
        uint destinationMipLevel,
        GraphicsOrigin3D destinationOrigin,
        GraphicsExtent3D copySize) => commands.Add(new MockTextureToTextureCommand(
            (MockGraphicsTexture)source,
            sourceMipLevel,
            sourceOrigin,
            (MockGraphicsTexture)destination,
            destinationMipLevel,
            destinationOrigin,
            copySize));

    protected override GraphicsRenderPassEncoder BeginRenderPassCore(GraphicsRenderPassDescriptor descriptor) =>
        new MockGraphicsRenderPassEncoder(this, descriptor, commands);

    protected override GraphicsCommandBuffer FinishCore(string? label) =>
        new MockGraphicsCommandBuffer((MockGraphicsDevice)Device, label, [.. commands]);

    protected override void DisposeCore()
    {
    }
}

sealed class MockGraphicsCommandBuffer(
    MockGraphicsDevice device,
    string? label,
    IReadOnlyList<IMockCommand> commands)
    : GraphicsCommandBuffer(device, label)
{
    internal IReadOnlyList<IMockCommand> Commands { get; } = commands;

    protected override void DisposeCore()
    {
    }
}

interface IMockCommand
{
    void Execute();
}

sealed record MockCopyCommand(
    MockGraphicsBuffer Source,
    ulong SourceOffset,
    MockGraphicsBuffer Destination,
    ulong DestinationOffset,
    ulong Size) : IMockCommand
{
    public void Execute() =>
        Source.Data.AsSpan(checked((int)SourceOffset), checked((int)Size)).CopyTo(
            Destination.Data.AsSpan(checked((int)DestinationOffset)));
}

sealed record MockTextureToBufferCommand(
    MockGraphicsTexture Source,
    uint MipLevel,
    GraphicsOrigin3D Origin,
    GraphicsExtent3D CopySize,
    MockGraphicsBuffer Destination,
    ulong DestinationOffset,
    uint BytesPerRow,
    uint RowsPerImage) : IMockCommand
{
    public void Execute()
    {
        MockTextureWrite write = Source.Writes.Last(value => value.MipLevel == MipLevel);
        GraphicsTextureBlockLayout block =
            GraphicsTextureFormatInfo.GetBlockLayout(Source.Descriptor.Format);
        int rowBytes = checked((int)(CopySize.Width / block.BlockWidth * block.BytesPerBlock));
        int rowCount = checked((int)(CopySize.Height / block.BlockHeight));
        for (int row = 0; row < rowCount; row++)
        {
            write.Data.AsSpan(checked(row * (int)write.BytesPerRow), rowBytes).CopyTo(
                Destination.Data.AsSpan(
                    checked((int)DestinationOffset + row * (int)BytesPerRow),
                    rowBytes));
        }
    }
}

sealed record MockTextureToTextureCommand(
    MockGraphicsTexture Source,
    uint SourceMipLevel,
    GraphicsOrigin3D SourceOrigin,
    MockGraphicsTexture Destination,
    uint DestinationMipLevel,
    GraphicsOrigin3D DestinationOrigin,
    GraphicsExtent3D CopySize) : IMockCommand
{
    public void Execute()
    {
        MockTextureWrite write = Source.Writes.Last(value => value.MipLevel == SourceMipLevel);
        Destination.Writes.Add(new MockTextureWrite(
            DestinationMipLevel,
            DestinationOrigin,
            CopySize,
            write.Data.ToArray(),
            write.BytesPerRow,
            write.RowsPerImage));
    }
}

sealed class MockGraphicsRenderPassEncoder : GraphicsRenderPassEncoder
{
    private readonly List<IMockCommand> parentCommands;
    private readonly List<MockDrawCommand> draws = [];
    private readonly Dictionary<uint, MockGraphicsBindGroup> bindGroups = [];
    private MockGraphicsRenderPipeline? pipeline;

    internal MockGraphicsRenderPassEncoder(
        MockGraphicsCommandEncoder commandEncoder,
        GraphicsRenderPassDescriptor descriptor,
        List<IMockCommand> parentCommands)
        : base(commandEncoder, descriptor)
    {
        this.parentCommands = parentCommands;
    }

    protected override void SetPipelineCore(GraphicsRenderPipeline value) =>
        pipeline = (MockGraphicsRenderPipeline)value;

    protected override void SetVertexBufferCore(uint slot, GraphicsBuffer buffer, ulong offset, ulong size)
    {
    }

    protected override void SetIndexBufferCore(
        GraphicsBuffer buffer,
        GraphicsIndexFormat format,
        ulong offset,
        ulong size)
    {
    }

    protected override void SetBindGroupCore(uint groupIndex, GraphicsBindGroup bindGroup)
    {
        bindGroups[groupIndex] = (MockGraphicsBindGroup)bindGroup;
    }

    protected override void SetViewportCore(
        float x,
        float y,
        float width,
        float height,
        float minimumDepth,
        float maximumDepth)
    {
    }

    protected override void SetScissorRectCore(uint x, uint y, uint width, uint height)
    {
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance) =>
        draws.Add(new MockDrawCommand(
            pipeline!,
            false,
            vertexCount,
            instanceCount,
            firstVertex,
            0,
            firstInstance,
            new Dictionary<uint, MockGraphicsBindGroup>(bindGroups)));

    protected override void DrawIndexedCore(
        uint indexCount,
        uint instanceCount,
        uint firstIndex,
        int baseVertex,
        uint firstInstance) =>
        draws.Add(new MockDrawCommand(
            pipeline!,
            true,
            indexCount,
            instanceCount,
            firstIndex,
            baseVertex,
            firstInstance,
            new Dictionary<uint, MockGraphicsBindGroup>(bindGroups)));

    protected override void EndCore() =>
        parentCommands.Add(new MockRenderCommand(Descriptor, [.. draws]));

    protected override void DisposeCore()
    {
    }
}

sealed record MockDrawCommand(
    MockGraphicsRenderPipeline Pipeline,
    bool IsIndexed,
    uint ElementCount,
    uint InstanceCount,
    uint FirstElement,
    int BaseVertex,
    uint FirstInstance,
    IReadOnlyDictionary<uint, MockGraphicsBindGroup> BindGroups)
{
    internal uint VertexCount => IsIndexed ? 0 : ElementCount;

    internal uint IndexCount => IsIndexed ? ElementCount : 0;
}

sealed record MockRenderCommand(
    GraphicsRenderPassDescriptor Descriptor,
    IReadOnlyList<MockDrawCommand> Draws) : IMockCommand
{
    public void Execute()
    {
        if (Descriptor.ColorAttachment is GraphicsRenderPassColorAttachment colorAttachment)
        {
            MockGraphicsTexture texture = (MockGraphicsTexture)colorAttachment.Texture;
            texture.Rendered = true;
            if (colorAttachment.LoadOperation == GraphicsLoadOperation.Clear)
            {
                texture.LastClearColor = colorAttachment.ClearColor;
            }
            texture.Draws.AddRange(Draws);
        }
        if (Descriptor.DepthAttachment is GraphicsRenderPassDepthAttachment depthAttachment)
        {
            ((MockGraphicsTexture)depthAttachment.Texture).Rendered = true;
        }
    }
}

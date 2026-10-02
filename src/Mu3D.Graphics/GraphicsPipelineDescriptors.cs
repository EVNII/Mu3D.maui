namespace Mu3D.Graphics;

/// <summary>Controls how a sampler addresses texture coordinates outside the normalized range.</summary>
public enum GraphicsAddressMode
{
    /// <summary>Clamps coordinates to the edge texel.</summary>
    ClampToEdge,

    /// <summary>Repeats the texture at integer boundaries.</summary>
    Repeat,

    /// <summary>Repeats the texture while mirroring every other interval.</summary>
    MirrorRepeat,
}

/// <summary>Controls texture filtering for a sampler.</summary>
public enum GraphicsFilterMode
{
    /// <summary>Selects the nearest texel.</summary>
    Nearest,

    /// <summary>Linearly interpolates neighboring texels.</summary>
    Linear,
}

/// <summary>Describes an immutable texture sampler.</summary>
public readonly record struct GraphicsSamplerDescriptor
{
    /// <summary>Initializes a sampler descriptor.</summary>
    public GraphicsSamplerDescriptor(
        GraphicsAddressMode addressModeU = GraphicsAddressMode.ClampToEdge,
        GraphicsAddressMode addressModeV = GraphicsAddressMode.ClampToEdge,
        GraphicsAddressMode addressModeW = GraphicsAddressMode.ClampToEdge,
        GraphicsFilterMode magFilter = GraphicsFilterMode.Nearest,
        GraphicsFilterMode minFilter = GraphicsFilterMode.Nearest,
        GraphicsFilterMode mipmapFilter = GraphicsFilterMode.Nearest,
        string? label = null,
        GraphicsCompareFunction? compare = null)
        : this(
            0f,
            32f,
            addressModeU,
            addressModeV,
            addressModeW,
            magFilter,
            minFilter,
            mipmapFilter,
            label,
            compare)
    {
    }

    /// <summary>Initializes a sampler descriptor with explicit mip-level clamps.</summary>
    public GraphicsSamplerDescriptor(
        float lodMinClamp,
        float lodMaxClamp,
        GraphicsAddressMode addressModeU = GraphicsAddressMode.ClampToEdge,
        GraphicsAddressMode addressModeV = GraphicsAddressMode.ClampToEdge,
        GraphicsAddressMode addressModeW = GraphicsAddressMode.ClampToEdge,
        GraphicsFilterMode magFilter = GraphicsFilterMode.Nearest,
        GraphicsFilterMode minFilter = GraphicsFilterMode.Nearest,
        GraphicsFilterMode mipmapFilter = GraphicsFilterMode.Nearest,
        string? label = null,
        GraphicsCompareFunction? compare = null)
    {
        AddressModeU = addressModeU;
        AddressModeV = addressModeV;
        AddressModeW = addressModeW;
        MagFilter = magFilter;
        MinFilter = minFilter;
        MipmapFilter = mipmapFilter;
        Label = label;
        Compare = compare;
        LodMinClamp = lodMinClamp;
        LodMaxClamp = lodMaxClamp;
    }

    /// <summary>Gets the horizontal address mode.</summary>
    public GraphicsAddressMode AddressModeU { get; }

    /// <summary>Gets the vertical address mode.</summary>
    public GraphicsAddressMode AddressModeV { get; }

    /// <summary>Gets the depth address mode.</summary>
    public GraphicsAddressMode AddressModeW { get; }

    /// <summary>Gets the magnification filter.</summary>
    public GraphicsFilterMode MagFilter { get; }

    /// <summary>Gets the minification filter.</summary>
    public GraphicsFilterMode MinFilter { get; }

    /// <summary>Gets the mip-level filter.</summary>
    public GraphicsFilterMode MipmapFilter { get; }

    /// <summary>Gets the optional depth comparison that makes this a comparison sampler.</summary>
    public GraphicsCompareFunction? Compare { get; }

    /// <summary>Gets the minimum sampled mip level.</summary>
    public float LodMinClamp { get; }

    /// <summary>Gets the maximum sampled mip level.</summary>
    public float LodMaxClamp { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
}

/// <summary>Describes a shader module authored in WebGPU Shading Language.</summary>
public readonly record struct GraphicsShaderModuleDescriptor
{
    /// <summary>Initializes a WGSL shader-module descriptor.</summary>
    public GraphicsShaderModuleDescriptor(string code, string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Label = label;
    }

    /// <summary>Gets the WGSL source code.</summary>
    public string Code { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
}

/// <summary>Identifies how input vertices are assembled into primitives.</summary>
public enum GraphicsPrimitiveTopology
{
    /// <summary>Each group of three vertices forms one triangle.</summary>
    TriangleList,

    /// <summary>Each vertex after the first two completes one triangle.</summary>
    TriangleStrip,
}

/// <summary>Controls fixed-function triangle-face culling.</summary>
public enum GraphicsCullMode
{
    /// <summary>Does not cull either face orientation.</summary>
    None,

    /// <summary>Culls front-facing triangles.</summary>
    Front,

    /// <summary>Culls back-facing triangles.</summary>
    Back,
}

/// <summary>Identifies one source or destination term in fixed-function color blending.</summary>
public enum GraphicsBlendFactor
{
    /// <summary>Uses zero.</summary>
    Zero,

    /// <summary>Uses one.</summary>
    One,

    /// <summary>Uses source alpha.</summary>
    SourceAlpha,

    /// <summary>Uses one minus source alpha.</summary>
    OneMinusSourceAlpha,

    /// <summary>Uses destination alpha.</summary>
    DestinationAlpha,

    /// <summary>Uses one minus destination alpha.</summary>
    OneMinusDestinationAlpha,
}

/// <summary>Identifies the arithmetic used to combine scaled source and destination values.</summary>
public enum GraphicsBlendOperation
{
    /// <summary>Adds the scaled source and destination.</summary>
    Add,

    /// <summary>Subtracts the scaled destination from the scaled source.</summary>
    Subtract,

    /// <summary>Subtracts the scaled source from the scaled destination.</summary>
    ReverseSubtract,

    /// <summary>Selects the component-wise minimum.</summary>
    Minimum,

    /// <summary>Selects the component-wise maximum.</summary>
    Maximum,
}

/// <summary>Describes one RGB or alpha blend equation.</summary>
public readonly record struct GraphicsBlendComponent
{
    /// <summary>Initializes one blend equation.</summary>
    public GraphicsBlendComponent(
        GraphicsBlendOperation operation,
        GraphicsBlendFactor sourceFactor,
        GraphicsBlendFactor destinationFactor)
    {
        Operation = operation;
        SourceFactor = sourceFactor;
        DestinationFactor = destinationFactor;
    }

    /// <summary>Gets the arithmetic used to combine the scaled terms.</summary>
    public GraphicsBlendOperation Operation { get; }

    /// <summary>Gets the source scaling factor.</summary>
    public GraphicsBlendFactor SourceFactor { get; }

    /// <summary>Gets the destination scaling factor.</summary>
    public GraphicsBlendFactor DestinationFactor { get; }
}

/// <summary>Describes fixed-function RGB and alpha blending for one color target.</summary>
public readonly record struct GraphicsBlendState
{
    /// <summary>Initializes color and alpha blend equations.</summary>
    public GraphicsBlendState(GraphicsBlendComponent color, GraphicsBlendComponent alpha)
    {
        Color = color;
        Alpha = alpha;
    }

    /// <summary>Gets the RGB blend equation.</summary>
    public GraphicsBlendComponent Color { get; }

    /// <summary>Gets the alpha blend equation.</summary>
    public GraphicsBlendComponent Alpha { get; }

    /// <summary>
    /// Gets source-over blending for shader output whose RGB has already been multiplied by alpha.
    /// </summary>
    public static GraphicsBlendState PremultipliedAlpha { get; } = new(
        new GraphicsBlendComponent(
            GraphicsBlendOperation.Add,
            GraphicsBlendFactor.One,
            GraphicsBlendFactor.OneMinusSourceAlpha),
        new GraphicsBlendComponent(
            GraphicsBlendOperation.Add,
            GraphicsBlendFactor.One,
            GraphicsBlendFactor.OneMinusSourceAlpha));
}

/// <summary>Describes a render pipeline with an optional color target and optional depth state.</summary>
public readonly record struct GraphicsRenderPipelineDescriptor
{
    /// <summary>Initializes a render pipeline.</summary>
    public GraphicsRenderPipelineDescriptor(
        GraphicsShaderModule vertexShader,
        string vertexEntryPoint,
        GraphicsShaderModule fragmentShader,
        string fragmentEntryPoint,
        GraphicsTextureFormat colorFormat,
        GraphicsPrimitiveTopology topology = GraphicsPrimitiveTopology.TriangleList,
        string? label = null,
        IReadOnlyList<GraphicsVertexBufferLayout>? vertexBuffers = null,
        GraphicsDepthStencilState? depthStencil = null,
        GraphicsPipelineLayout? layout = null,
        GraphicsCullMode cullMode = GraphicsCullMode.None,
        GraphicsBlendState? blend = null)
    {
        ArgumentNullException.ThrowIfNull(vertexShader);
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexEntryPoint);
        ArgumentNullException.ThrowIfNull(fragmentShader);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentEntryPoint);
        VertexShader = vertexShader;
        VertexEntryPoint = vertexEntryPoint;
        FragmentShader = fragmentShader;
        FragmentEntryPoint = fragmentEntryPoint;
        ColorFormat = colorFormat;
        Topology = topology;
        Label = label;
        VertexBuffers = Array.AsReadOnly<GraphicsVertexBufferLayout>(
            vertexBuffers is null ? [] : [.. vertexBuffers]);
        DepthStencil = depthStencil;
        Layout = layout;
        CullMode = cullMode;
        Blend = blend;
    }

    /// <summary>Initializes a depth-only render pipeline with no fragment stage or color target.</summary>
    public GraphicsRenderPipelineDescriptor(
        GraphicsShaderModule vertexShader,
        string vertexEntryPoint,
        GraphicsDepthStencilState depthStencil,
        GraphicsPrimitiveTopology topology = GraphicsPrimitiveTopology.TriangleList,
        string? label = null,
        IReadOnlyList<GraphicsVertexBufferLayout>? vertexBuffers = null,
        GraphicsPipelineLayout? layout = null,
        GraphicsCullMode cullMode = GraphicsCullMode.None)
    {
        ArgumentNullException.ThrowIfNull(vertexShader);
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexEntryPoint);
        VertexShader = vertexShader;
        VertexEntryPoint = vertexEntryPoint;
        FragmentShader = null;
        FragmentEntryPoint = null;
        ColorFormat = null;
        Topology = topology;
        Label = label;
        VertexBuffers = Array.AsReadOnly<GraphicsVertexBufferLayout>(
            vertexBuffers is null ? [] : [.. vertexBuffers]);
        DepthStencil = depthStencil;
        Layout = layout;
        CullMode = cullMode;
        Blend = null;
    }

    /// <summary>
    /// Initializes a depth-only render pipeline whose fragment stage may discard fragments but
    /// writes no color target.
    /// </summary>
    public GraphicsRenderPipelineDescriptor(
        GraphicsShaderModule vertexShader,
        string vertexEntryPoint,
        GraphicsShaderModule fragmentShader,
        string fragmentEntryPoint,
        GraphicsDepthStencilState depthStencil,
        GraphicsPrimitiveTopology topology = GraphicsPrimitiveTopology.TriangleList,
        string? label = null,
        IReadOnlyList<GraphicsVertexBufferLayout>? vertexBuffers = null,
        GraphicsPipelineLayout? layout = null,
        GraphicsCullMode cullMode = GraphicsCullMode.None)
    {
        ArgumentNullException.ThrowIfNull(vertexShader);
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexEntryPoint);
        ArgumentNullException.ThrowIfNull(fragmentShader);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentEntryPoint);
        VertexShader = vertexShader;
        VertexEntryPoint = vertexEntryPoint;
        FragmentShader = fragmentShader;
        FragmentEntryPoint = fragmentEntryPoint;
        ColorFormat = null;
        Topology = topology;
        Label = label;
        VertexBuffers = Array.AsReadOnly<GraphicsVertexBufferLayout>(
            vertexBuffers is null ? [] : [.. vertexBuffers]);
        DepthStencil = depthStencil;
        Layout = layout;
        CullMode = cullMode;
        Blend = null;
    }

    /// <summary>Gets the vertex shader module.</summary>
    public GraphicsShaderModule VertexShader { get; }

    /// <summary>Gets the vertex entry-point name.</summary>
    public string VertexEntryPoint { get; }

    /// <summary>Gets the optional fragment shader module, including depth-only discard stages.</summary>
    public GraphicsShaderModule? FragmentShader { get; }

    /// <summary>Gets the fragment entry-point name.</summary>
    public string? FragmentEntryPoint { get; }

    /// <summary>Gets the optional color-attachment format.</summary>
    public GraphicsTextureFormat? ColorFormat { get; }

    /// <summary>Gets the primitive topology.</summary>
    public GraphicsPrimitiveTopology Topology { get; }

    /// <summary>Gets the immutable vertex-buffer layouts indexed by binding slot.</summary>
    public IReadOnlyList<GraphicsVertexBufferLayout> VertexBuffers { get; }

    /// <summary>Gets the optional fixed-function depth state.</summary>
    public GraphicsDepthStencilState? DepthStencil { get; }

    /// <summary>Gets the explicit bind-group layout, or null when the pipeline has no public bindings.</summary>
    public GraphicsPipelineLayout? Layout { get; }

    /// <summary>Gets the fixed-function face-culling mode.</summary>
    public GraphicsCullMode CullMode { get; }

    /// <summary>Gets the optional fixed-function color-target blend state.</summary>
    public GraphicsBlendState? Blend { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
}

/// <summary>Describes one full-texture depth attachment.</summary>
public readonly record struct GraphicsRenderPassDepthAttachment
{
    /// <summary>Initializes a depth attachment.</summary>
    public GraphicsRenderPassDepthAttachment(
        GraphicsTexture texture,
        GraphicsLoadOperation loadOperation = GraphicsLoadOperation.Clear,
        GraphicsStoreOperation storeOperation = GraphicsStoreOperation.Store,
        float clearValue = 1f)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Texture = texture;
        LoadOperation = loadOperation;
        StoreOperation = storeOperation;
        ClearValue = clearValue;
    }

    /// <summary>Gets the attached depth texture.</summary>
    public GraphicsTexture Texture { get; }

    /// <summary>Gets the attachment load operation.</summary>
    public GraphicsLoadOperation LoadOperation { get; }

    /// <summary>Gets the attachment store operation.</summary>
    public GraphicsStoreOperation StoreOperation { get; }

    /// <summary>Gets the normalized depth clear value.</summary>
    public float ClearValue { get; }
}

/// <summary>Controls how a render pass initializes an attachment.</summary>
public enum GraphicsLoadOperation
{
    /// <summary>Preserves the attachment's existing contents.</summary>
    Load,

    /// <summary>Initializes the attachment to a supplied clear value.</summary>
    Clear,
}

/// <summary>Controls whether a render pass preserves an attachment after rendering.</summary>
public enum GraphicsStoreOperation
{
    /// <summary>Preserves the rendered attachment contents.</summary>
    Store,

    /// <summary>Allows the backend to discard the rendered attachment contents.</summary>
    Discard,
}

/// <summary>Specifies an FP32 linear clear value without assigning a color-space identity.</summary>
public readonly record struct GraphicsClearColor
{
    /// <summary>Initializes a clear value. Negative and above-one components are preserved.</summary>
    public GraphicsClearColor(float red, float green, float blue, float alpha)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    /// <summary>Gets the red component.</summary>
    public float Red { get; }

    /// <summary>Gets the green component.</summary>
    public float Green { get; }

    /// <summary>Gets the blue component.</summary>
    public float Blue { get; }

    /// <summary>Gets the alpha component.</summary>
    public float Alpha { get; }
}

/// <summary>Describes one full-texture color attachment.</summary>
public readonly record struct GraphicsRenderPassColorAttachment
{
    /// <summary>Initializes a color attachment.</summary>
    public GraphicsRenderPassColorAttachment(
        GraphicsTexture texture,
        GraphicsLoadOperation loadOperation = GraphicsLoadOperation.Clear,
        GraphicsStoreOperation storeOperation = GraphicsStoreOperation.Store,
        GraphicsClearColor clearColor = default)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Texture = texture;
        View = null;
        LoadOperation = loadOperation;
        StoreOperation = storeOperation;
        ClearColor = clearColor;
    }

    /// <summary>Initializes a color attachment targeting an explicitly selected texture view.</summary>
    public GraphicsRenderPassColorAttachment(
        GraphicsTextureView view,
        GraphicsLoadOperation loadOperation = GraphicsLoadOperation.Clear,
        GraphicsStoreOperation storeOperation = GraphicsStoreOperation.Store,
        GraphicsClearColor clearColor = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        Texture = view.Descriptor.Texture;
        View = view;
        LoadOperation = loadOperation;
        StoreOperation = storeOperation;
        ClearColor = clearColor;
    }

    /// <summary>Gets the attached texture.</summary>
    public GraphicsTexture Texture { get; }

    /// <summary>Gets the explicit subresource view, or <see langword="null"/> for the whole texture.</summary>
    public GraphicsTextureView? View { get; }

    /// <summary>Gets the attachment load operation.</summary>
    public GraphicsLoadOperation LoadOperation { get; }

    /// <summary>Gets the attachment store operation.</summary>
    public GraphicsStoreOperation StoreOperation { get; }

    /// <summary>Gets the clear value used when <see cref="LoadOperation"/> is clear.</summary>
    public GraphicsClearColor ClearColor { get; }
}

/// <summary>Describes a render pass with an optional color attachment and optional depth.</summary>
public readonly record struct GraphicsRenderPassDescriptor
{
    /// <summary>Initializes a render-pass descriptor.</summary>
    public GraphicsRenderPassDescriptor(
        GraphicsRenderPassColorAttachment colorAttachment,
        string? label = null,
        GraphicsRenderPassDepthAttachment? depthAttachment = null)
    {
        if (colorAttachment.Texture is null)
        {
            throw new ArgumentNullException(nameof(colorAttachment));
        }

        ColorAttachment = colorAttachment;
        Label = label;
        DepthAttachment = depthAttachment;
    }

    /// <summary>Initializes a depth-only render pass.</summary>
    public GraphicsRenderPassDescriptor(
        GraphicsRenderPassDepthAttachment depthAttachment,
        string? label = null)
    {
        if (depthAttachment.Texture is null)
        {
            throw new ArgumentNullException(nameof(depthAttachment));
        }
        ColorAttachment = null;
        Label = label;
        DepthAttachment = depthAttachment;
    }

    /// <summary>Gets the color attachment.</summary>
    public GraphicsRenderPassColorAttachment? ColorAttachment { get; }

    /// <summary>Gets the optional depth attachment.</summary>
    public GraphicsRenderPassDepthAttachment? DepthAttachment { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
}

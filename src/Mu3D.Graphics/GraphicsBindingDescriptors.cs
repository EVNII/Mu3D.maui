using System.Collections.ObjectModel;

namespace Mu3D.Graphics;

/// <summary>Identifies shader stages that can access a binding.</summary>
[Flags]
public enum GraphicsShaderStage
{
    /// <summary>No shader stage can access the binding.</summary>
    None = 0,

    /// <summary>The vertex stage can access the binding.</summary>
    Vertex = 1 << 0,

    /// <summary>The fragment stage can access the binding.</summary>
    Fragment = 1 << 1,
}

/// <summary>Identifies how a shader accesses a bound buffer.</summary>
public enum GraphicsBufferBindingType
{
    /// <summary>A read-only uniform buffer.</summary>
    Uniform,

    /// <summary>A read-only storage buffer.</summary>
    ReadOnlyStorage,

    /// <summary>A read-write storage buffer.</summary>
    Storage,
}

/// <summary>Identifies the resource category declared by a bind-group entry.</summary>
public enum GraphicsBindingResourceType
{
    /// <summary>A byte range from a GPU buffer.</summary>
    Buffer,

    /// <summary>An immutable texture sampler.</summary>
    Sampler,

    /// <summary>A sampled texture view.</summary>
    SampledTexture,
}

/// <summary>Identifies whether a shader sampler may perform filtering.</summary>
public enum GraphicsSamplerBindingType
{
    /// <summary>The sampler may use linear or nearest filtering.</summary>
    Filtering,

    /// <summary>The sampler must use nearest filtering for all filters.</summary>
    NonFiltering,

    /// <summary>The sampler performs depth-reference comparisons.</summary>
    Comparison,
}

/// <summary>Identifies the floating-point sampling capability required from a texture view.</summary>
public enum GraphicsTextureSampleType
{
    /// <summary>A filterable floating-point or normalized texture.</summary>
    Float,

    /// <summary>A floating-point texture that is not required to support filtering.</summary>
    UnfilterableFloat,

    /// <summary>A depth texture sampled through comparison or depth-load operations.</summary>
    Depth,
}

/// <summary>Declares one buffer binding visible to WGSL.</summary>
public readonly record struct GraphicsBindGroupLayoutEntry
{
    /// <summary>Initializes a buffer binding declaration.</summary>
    public GraphicsBindGroupLayoutEntry(
        uint binding,
        GraphicsShaderStage visibility,
        GraphicsBufferBindingType bufferType,
        ulong minBindingSize = 0)
    {
        Binding = binding;
        Visibility = visibility;
        BufferType = bufferType;
        MinBindingSize = minBindingSize;
        ResourceType = GraphicsBindingResourceType.Buffer;
    }

    /// <summary>Initializes a sampler binding declaration.</summary>
    public GraphicsBindGroupLayoutEntry(
        uint binding,
        GraphicsShaderStage visibility,
        GraphicsSamplerBindingType samplerType)
    {
        Binding = binding;
        Visibility = visibility;
        SamplerType = samplerType;
        ResourceType = GraphicsBindingResourceType.Sampler;
    }

    /// <summary>Initializes a sampled-texture binding declaration.</summary>
    public GraphicsBindGroupLayoutEntry(
        uint binding,
        GraphicsShaderStage visibility,
        GraphicsTextureSampleType textureSampleType,
        GraphicsTextureViewDimension textureViewDimension,
        bool multisampled = false)
    {
        Binding = binding;
        Visibility = visibility;
        TextureSampleType = textureSampleType;
        TextureViewDimension = textureViewDimension;
        Multisampled = multisampled;
        ResourceType = GraphicsBindingResourceType.SampledTexture;
    }

    /// <summary>Gets the WGSL <c>@binding</c> number.</summary>
    public uint Binding { get; }

    /// <summary>Gets the shader stages allowed to access this binding.</summary>
    public GraphicsShaderStage Visibility { get; }

    /// <summary>Gets how the shader accesses the buffer.</summary>
    public GraphicsBufferBindingType BufferType { get; }

    /// <summary>Gets the minimum bound byte count, or zero when unspecified.</summary>
    public ulong MinBindingSize { get; }

    /// <summary>Gets the declared resource category.</summary>
    public GraphicsBindingResourceType ResourceType { get; }

    /// <summary>Gets the sampler capability when <see cref="ResourceType"/> is sampler.</summary>
    public GraphicsSamplerBindingType SamplerType { get; }

    /// <summary>Gets the sample type when <see cref="ResourceType"/> is sampled texture.</summary>
    public GraphicsTextureSampleType TextureSampleType { get; }

    /// <summary>Gets the required texture-view dimension.</summary>
    public GraphicsTextureViewDimension TextureViewDimension { get; }

    /// <summary>Gets whether the sampled texture must be multisampled.</summary>
    public bool Multisampled { get; }
}

/// <summary>Describes the immutable binding schema for one bind-group slot.</summary>
public sealed class GraphicsBindGroupLayoutDescriptor
{
    /// <summary>Initializes a bind-group layout descriptor.</summary>
    public GraphicsBindGroupLayoutDescriptor(
        IEnumerable<GraphicsBindGroupLayoutEntry> entries,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = new ReadOnlyCollection<GraphicsBindGroupLayoutEntry>([.. entries]);
        Label = label;
    }

    /// <summary>Gets the immutable binding declarations.</summary>
    public IReadOnlyList<GraphicsBindGroupLayoutEntry> Entries { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
}

/// <summary>Describes the ordered bind-group layouts accepted by a pipeline.</summary>
public sealed class GraphicsPipelineLayoutDescriptor
{
    /// <summary>Initializes a pipeline-layout descriptor.</summary>
    public GraphicsPipelineLayoutDescriptor(
        IEnumerable<GraphicsBindGroupLayout> bindGroupLayouts,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(bindGroupLayouts);
        BindGroupLayouts = new ReadOnlyCollection<GraphicsBindGroupLayout>([.. bindGroupLayouts]);
        Label = label;
    }

    /// <summary>Gets layouts indexed by WGSL <c>@group</c> number.</summary>
    public IReadOnlyList<GraphicsBindGroupLayout> BindGroupLayouts { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
}

/// <summary>Binds one byte range to a declared buffer binding.</summary>
public readonly record struct GraphicsBindGroupEntry
{
    /// <summary>Initializes a buffer binding.</summary>
    public GraphicsBindGroupEntry(uint binding, GraphicsBuffer buffer, ulong offset, ulong size)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Binding = binding;
        Buffer = buffer;
        Offset = offset;
        Size = size;
        ResourceType = GraphicsBindingResourceType.Buffer;
    }

    /// <summary>Initializes a sampler binding.</summary>
    public GraphicsBindGroupEntry(uint binding, GraphicsSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        Binding = binding;
        Sampler = sampler;
        ResourceType = GraphicsBindingResourceType.Sampler;
    }

    /// <summary>Initializes a sampled-texture-view binding.</summary>
    public GraphicsBindGroupEntry(uint binding, GraphicsTextureView textureView)
    {
        ArgumentNullException.ThrowIfNull(textureView);
        Binding = binding;
        TextureView = textureView;
        ResourceType = GraphicsBindingResourceType.SampledTexture;
    }

    /// <summary>Gets the WGSL <c>@binding</c> number.</summary>
    public uint Binding { get; }

    /// <summary>Gets the bound buffer.</summary>
    public GraphicsBuffer? Buffer { get; }

    /// <summary>Gets the first bound byte.</summary>
    public ulong Offset { get; }

    /// <summary>Gets the bound byte count.</summary>
    public ulong Size { get; }

    /// <summary>Gets the bound sampler, when present.</summary>
    public GraphicsSampler? Sampler { get; }

    /// <summary>Gets the bound texture view, when present.</summary>
    public GraphicsTextureView? TextureView { get; }

    /// <summary>Gets the supplied resource category.</summary>
    public GraphicsBindingResourceType ResourceType { get; }
}

/// <summary>Describes immutable resources bound against one layout.</summary>
public sealed class GraphicsBindGroupDescriptor
{
    /// <summary>Initializes a bind-group descriptor.</summary>
    public GraphicsBindGroupDescriptor(
        GraphicsBindGroupLayout layout,
        IEnumerable<GraphicsBindGroupEntry> entries,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(entries);
        Layout = layout;
        Entries = new ReadOnlyCollection<GraphicsBindGroupEntry>([.. entries]);
        Label = label;
    }

    /// <summary>Gets the schema implemented by this bind group.</summary>
    public GraphicsBindGroupLayout Layout { get; }

    /// <summary>Gets the immutable buffer bindings.</summary>
    public IReadOnlyList<GraphicsBindGroupEntry> Entries { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
}

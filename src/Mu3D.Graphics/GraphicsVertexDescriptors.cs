using System.Collections.ObjectModel;

namespace Mu3D.Graphics;

/// <summary>Identifies the scalar layout delivered to one vertex-shader input.</summary>
public enum GraphicsVertexFormat
{
    /// <summary>Two 32-bit floating-point components.</summary>
    Float32x2,

    /// <summary>Three 32-bit floating-point components.</summary>
    Float32x3,

    /// <summary>Four 32-bit floating-point components.</summary>
    Float32x4,
}

/// <summary>Controls how frequently a vertex buffer advances.</summary>
public enum GraphicsVertexStepMode
{
    /// <summary>Advances once per vertex.</summary>
    Vertex,

    /// <summary>Advances once per instance.</summary>
    Instance,
}

/// <summary>Maps one byte range in a vertex-buffer element to a shader location.</summary>
public readonly record struct GraphicsVertexAttribute
{
    /// <summary>Initializes a vertex attribute.</summary>
    public GraphicsVertexAttribute(GraphicsVertexFormat format, ulong offset, uint shaderLocation)
    {
        Format = format;
        Offset = offset;
        ShaderLocation = shaderLocation;
    }

    /// <summary>Gets the component format.</summary>
    public GraphicsVertexFormat Format { get; }

    /// <summary>Gets the byte offset from the start of each buffer element.</summary>
    public ulong Offset { get; }

    /// <summary>Gets the WGSL <c>@location</c> consumed by the vertex shader.</summary>
    public uint ShaderLocation { get; }
}

/// <summary>Describes one interleaved or planar vertex-buffer slot.</summary>
public sealed class GraphicsVertexBufferLayout
{
    /// <summary>Initializes an immutable vertex-buffer layout.</summary>
    public GraphicsVertexBufferLayout(
        ulong arrayStride,
        IEnumerable<GraphicsVertexAttribute> attributes,
        GraphicsVertexStepMode stepMode = GraphicsVertexStepMode.Vertex)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArrayStride = arrayStride;
        Attributes = new ReadOnlyCollection<GraphicsVertexAttribute>([.. attributes]);
        StepMode = stepMode;
    }

    /// <summary>Gets the byte distance between consecutive vertices or instances.</summary>
    public ulong ArrayStride { get; }

    /// <summary>Gets the immutable shader-input mappings in this buffer slot.</summary>
    public IReadOnlyList<GraphicsVertexAttribute> Attributes { get; }

    /// <summary>Gets whether the buffer advances per vertex or per instance.</summary>
    public GraphicsVertexStepMode StepMode { get; }
}

/// <summary>Identifies the integer width used by an index buffer.</summary>
public enum GraphicsIndexFormat
{
    /// <summary>Unsigned 16-bit indices.</summary>
    Uint16,

    /// <summary>Unsigned 32-bit indices.</summary>
    Uint32,
}

/// <summary>Identifies the comparison used by fixed-function depth testing.</summary>
public enum GraphicsCompareFunction
{
    /// <summary>The comparison never passes.</summary>
    Never,

    /// <summary>Passes when the incoming value is less.</summary>
    Less,

    /// <summary>Passes when the values are equal.</summary>
    Equal,

    /// <summary>Passes when the incoming value is less or equal.</summary>
    LessEqual,

    /// <summary>Passes when the incoming value is greater.</summary>
    Greater,

    /// <summary>Passes when the values differ.</summary>
    NotEqual,

    /// <summary>Passes when the incoming value is greater or equal.</summary>
    GreaterEqual,

    /// <summary>The comparison always passes.</summary>
    Always,
}

/// <summary>Describes fixed-function depth state for a render pipeline.</summary>
public readonly record struct GraphicsDepthStencilState
{
    /// <summary>Initializes depth state. Stencil operations are reserved for a later API revision.</summary>
    public GraphicsDepthStencilState(
        GraphicsTextureFormat format,
        bool depthWriteEnabled = true,
        GraphicsCompareFunction depthCompare = GraphicsCompareFunction.Less)
    {
        Format = format;
        DepthWriteEnabled = depthWriteEnabled;
        DepthCompare = depthCompare;
    }

    /// <summary>Gets the depth attachment format.</summary>
    public GraphicsTextureFormat Format { get; }

    /// <summary>Gets whether passing fragments update depth.</summary>
    public bool DepthWriteEnabled { get; }

    /// <summary>Gets the depth comparison.</summary>
    public GraphicsCompareFunction DepthCompare { get; }
}

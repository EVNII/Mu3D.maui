namespace Mu3D.Graphics;

/// <summary>
/// Describes device and surface features that affect Mu3D rendering policy.
/// </summary>
public sealed record GraphicsCapabilities
{
    /// <summary>Gets whether binary16 textures can be created.</summary>
    public required bool SupportsFloat16Textures { get; init; }

    /// <summary>Gets whether shaders can use binary16 arithmetic.</summary>
    public required bool SupportsShaderFloat16 { get; init; }

    /// <summary>Gets whether the surface advertises an extended-linear HDR format.</summary>
    public required bool SupportsHdrSurface { get; init; }

    /// <summary>Gets whether BC-family compressed sampled textures are enabled on the device.</summary>
    public bool SupportsBcTextureCompression { get; init; }

    /// <summary>Gets whether ETC2/EAC-family compressed sampled textures are enabled on the device.</summary>
    public bool SupportsEtc2TextureCompression { get; init; }

    /// <summary>Gets whether ASTC LDR compressed sampled textures are enabled on the device.</summary>
    public bool SupportsAstcTextureCompression { get; init; }

    /// <summary>Gets the presentation formats advertised by the surface.</summary>
    public required IReadOnlyList<PresentationFormat> SurfaceFormats { get; init; }
}

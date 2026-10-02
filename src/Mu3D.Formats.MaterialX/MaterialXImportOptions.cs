using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Formats.MaterialX;

/// <summary>Defines explicit color interpretation and allocation limits for a MaterialX import.</summary>
public sealed class MaterialXImportOptions
{
    /// <summary>
    /// Gets the explicitly assigned space for untagged authored color constants. Null rejects them.
    /// Supported identities are linear sRGB and ACEScg; built-in omitted OpenPBR defaults retain ACEScg.
    /// </summary>
    public StandardRgbColorSpaceReference? DefaultColorSpace { get; init; }

    /// <summary>Gets the maximum encoded XML byte count. The default is 1 MiB; the maximum is 32 MiB.</summary>
    public int MaximumDocumentBytes { get; init; } = 1024 * 1024;

    /// <summary>Gets the maximum surface node count. The default is 128; the maximum is 4096.</summary>
    public int MaximumSurfaceCount { get; init; } = 128;

    /// <summary>Gets the maximum surface material count. The default is 128; the maximum is 4096.</summary>
    public int MaximumMaterialCount { get; init; } = 128;

    /// <summary>Gets the maximum graph node inventory per XML document, default 256, maximum 4096.</summary>
    public int MaximumGraphNodes { get; init; } = 256;

    /// <summary>Gets the application-owned image resolver (filename, source colorspace, requested type).</summary>
    /// <remarks>The callback must decode color to tagged linear RGB before FromColor, or return raw data
    /// through FromData. Preserve Source and SourceColorSpace to round-trip resource metadata. Null
    /// rejects image connections. The importer never opens paths, URLs, includes or embedded code.</remarks>
    public Func<string, string?, OpenPbrNodeType, OpenPbrTexture>? TextureResolver { get; init; }

    internal void Validate()
    {
        if (MaximumGraphNodes is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(MaximumGraphNodes));
        if (MaximumDocumentBytes is < 1 or > 32 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumDocumentBytes));
        if (MaximumSurfaceCount is < 1 or > MaterialXOpenPbrDocument.MaximumInventoryCount)
            throw new ArgumentOutOfRangeException(nameof(MaximumSurfaceCount));
        if (MaximumMaterialCount is < 0 or > MaterialXOpenPbrDocument.MaximumInventoryCount)
            throw new ArgumentOutOfRangeException(nameof(MaximumMaterialCount));
        if (DefaultColorSpace is not null)
            _ = MaterialXOpenPbrSerializer.GetColorSpaceName(DefaultColorSpace);
    }
}

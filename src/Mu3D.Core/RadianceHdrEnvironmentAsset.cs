using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>Configures one bounded Radiance HDR environment load.</summary>
public sealed class RadianceHdrEnvironmentLoadOptions
{
    /// <summary>Initializes options with the required explicit linear RGB interpretation.</summary>
    /// <param name="colorSpace">The linear-light RGB identity assigned to decoded values.</param>
    public RadianceHdrEnvironmentLoadOptions(ColorSpaceReference colorSpace)
    {
        ArgumentNullException.ThrowIfNull(colorSpace);
        if (!colorSpace.IsLinear)
        {
            throw new ArgumentException("Radiance RGBE values require a linear-light RGB space.", nameof(colorSpace));
        }
        ColorSpace = colorSpace;
    }

    /// <summary>Gets the explicit linear-light RGB interpretation.</summary>
    public ColorSpaceReference ColorSpace { get; }

    /// <summary>Gets or initializes the optional application-facing environment name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets or initializes the maximum accepted encoded source byte count.</summary>
    public int MaximumSourceByteCount { get; init; } =
        RadianceHdrEnvironmentLoader.DefaultMaximumSourceByteCount;

    /// <summary>Gets or initializes the maximum decoded FP32 RGB output byte count.</summary>
    public long MaximumOutputByteCount { get; init; } =
        RadianceHdrEnvironmentLoader.DefaultMaximumOutputByteCount;

    /// <summary>
    /// Gets or initializes whether the returned asset owns an exact encoded-source copy. This is
    /// independent of an application-owned source cache and defaults to <see langword="false"/>.
    /// </summary>
    public bool RetainEncodedSource { get; init; }
}

/// <summary>
/// Owns one decoded FP32 Radiance environment and, when requested, an exact encoded-source copy.
/// </summary>
public sealed class RadianceHdrEnvironmentAsset
{
    private readonly byte[]? retainedEncodedSource;

    internal RadianceHdrEnvironmentAsset(
        EquirectangularHdrEnvironment environment,
        byte[]? retainedEncodedSource)
    {
        Environment = environment;
        this.retainedEncodedSource = retainedEncodedSource;
        OutputByteCount = checked((long)environment.Width * environment.Height * 3 * sizeof(float));
    }

    /// <summary>Gets the immutable decoded FP32 RGB environment.</summary>
    public EquirectangularHdrEnvironment Environment { get; }

    /// <summary>Gets the decoded FP32 RGB output byte count.</summary>
    public long OutputByteCount { get; }

    /// <summary>Gets whether this asset owns an exact encoded-source copy.</summary>
    public bool HasRetainedEncodedSource => retainedEncodedSource is not null;

    /// <summary>
    /// Gets the asset-owned exact encoded source, or an empty memory when retention was not
    /// requested. The returned memory remains valid for the lifetime of this asset.
    /// </summary>
    public ReadOnlyMemory<byte> RetainedEncodedSource => retainedEncodedSource;
}

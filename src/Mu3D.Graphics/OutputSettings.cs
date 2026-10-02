namespace Mu3D.Graphics;

/// <summary>
/// Specifies the dynamic range requested for a presentation surface.
/// </summary>
public enum OutputDynamicRange
{
    /// <summary>Prefer HDR and use an ordinary surface only when HDR is unavailable.</summary>
    Automatic,

    /// <summary>Require an HDR-capable presentation surface.</summary>
    Hdr,

    /// <summary>Request standard dynamic range presentation explicitly.</summary>
    Sdr,
}

/// <summary>
/// Specifies what Mu3D does when an HDR-capable presentation surface is unavailable.
/// </summary>
public enum SdrFallbackMode
{
    /// <summary>Use the ordinary surface and let values outside its numeric range be clipped.</summary>
    Clamp,

    /// <summary>Apply an explicit HDR-to-SDR tone mapping operation.</summary>
    ToneMap,

    /// <summary>Fail surface creation instead of displaying an SDR result.</summary>
    Fail,
}

/// <summary>
/// Identifies presentation formats relevant to Mu3D's initial surface negotiation.
/// </summary>
public enum PresentationFormat
{
    /// <summary>The format has not been selected.</summary>
    Unknown,

    /// <summary>Eight-bit BGRA in a linear UNORM encoding.</summary>
    Bgra8Unorm,

    /// <summary>Eight-bit BGRA with hardware sRGB encoding.</summary>
    Bgra8UnormSrgb,

    /// <summary>Eight-bit RGBA in a linear UNORM encoding.</summary>
    Rgba8Unorm,

    /// <summary>Eight-bit RGBA with hardware sRGB encoding.</summary>
    Rgba8UnormSrgb,

    /// <summary>Four IEEE 754 binary16 floating-point channels.</summary>
    Rgba16Float,

    /// <summary>Ten normalized bits per RGB channel and two alpha bits.</summary>
    Rgb10A2Unorm,
}

/// <summary>
/// Configures presentation dynamic range, explicit SDR fallback and automatic-host white behavior.
/// </summary>
public sealed record OutputSettings
{
    private OutputWhiteMode whiteMode;
    private float referenceWhiteNits = 100;

    /// <summary>Gets the final presentation white policy, defaulting to system SDR white.</summary>
    /// <remarks>Applied by automatic MAUI presentation after display transforms. Manual hosts own this conversion.</remarks>
    public OutputWhiteMode WhiteMode
    {
        get => whiteMode;
        init => whiteMode = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Gets the nits represented by display-linear value one in FixedAbsolute mode, in [1,10000].</summary>
    /// <remarks>Match the display transform's reference-white normalization. This does not control panel brightness.</remarks>
    public float ReferenceWhiteNits
    {
        get => referenceWhiteNits;
        init => referenceWhiteNits = float.IsFinite(value) && value is >= 1 and <= 10000
            ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    /// <summary>Gets the default HDR-first output policy.</summary>
    public static OutputSettings Default { get; } = new();

    /// <summary>Gets the requested presentation dynamic range.</summary>
    public OutputDynamicRange DynamicRange { get; init; } = OutputDynamicRange.Automatic;

    /// <summary>Gets the behavior used when HDR presentation is unavailable.</summary>
    public SdrFallbackMode SdrFallback { get; init; } = SdrFallbackMode.Clamp;

    /// <summary>Gets the preferred extended-linear HDR surface format.</summary>
    public PresentationFormat PreferredHdrFormat { get; init; } = PresentationFormat.Rgba16Float;

    /// <summary>Gets the requested native-compositor alpha mode.</summary>
    /// <remarks>
    /// The default keeps the platform-selected behavior, which prefers an opaque surface. A
    /// transparent presentation request must use an alpha mode advertised by the concrete surface;
    /// for example, Metal commonly exposes <see cref="SurfaceAlphaMode.Unpremultiplied"/> while
    /// Windows commonly exposes <see cref="SurfaceAlphaMode.Premultiplied"/>. Surface creation
    /// fails explicitly when the requested mode is unavailable.
    /// </remarks>
    public SurfaceAlphaMode AlphaMode { get; init; } = SurfaceAlphaMode.Automatic;
}

/// <summary>Defines how automatic presentation maps display-linear RGB to platform white.</summary>
public enum OutputWhiteMode
{
    /// <summary>Match system SDR white; platforms that already manage relative white receive unchanged values.</summary>
    System,
    /// <summary>Use ReferenceWhiteNits on supported absolute HDR surfaces; unsupported platforms fail explicitly.</summary>
    FixedAbsolute,
    /// <summary>Pass through native surface values for applications that already perform their own calibration.</summary>
    PlatformNative,
}

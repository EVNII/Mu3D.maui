namespace Mu3D.Color;

/// <summary>Selects luma coefficients and zero-centered color-difference scaling.</summary>
/// <remarks>The model does not change RGB primaries or transfer encoding.</remarks>
public enum LumaChromaModel
{
    /// <summary>BT.601 luma with U = 0.492(B′ − Y′) and V = 0.877(R′ − Y′).</summary>
    YuvBt601,
    /// <summary>BT.601 Y′CbCr with normalized, zero-centered Cb and Cr.</summary>
    YCbCrBt601,
    /// <summary>BT.709 Y′CbCr with normalized, zero-centered Cb and Cr.</summary>
    YCbCrBt709,
    /// <summary>BT.2020 non-constant-luminance Y′CbCr; not constant-luminance Y′CbcCrc.</summary>
    YCbCrBt2020,
}

/// <summary>A finite, straight-alpha luma/chroma coordinate with explicit RGB encoding and model.</summary>
/// <remarks>Luma is Y′, not linear-light luminance. For RGB in [0,1], YCbCr differences
/// occupy [-0.5,0.5]; YUV uses distinct U/V scaling. Extended values are preserved.
/// These are floating-point coordinates, without video quantization, limited range or subsampling.</remarks>
public readonly record struct LumaChromaColor
{
    /// <summary>Constructs coordinates without clipping, requiring finite values and alpha in [0,1].</summary>
    public LumaChromaColor(float luma, float chromaBlue, float chromaRed, float alpha,
        StandardRgbEncoding encoding, LumaChromaModel model)
    {
        _ = StandardRgbEncodingConverter.GetLinearSpace(encoding);
        _ = LumaChromaConverter.Coefficients(model);
        if (!float.IsFinite(luma) || !float.IsFinite(chromaBlue) || !float.IsFinite(chromaRed))
            throw new ArgumentOutOfRangeException(nameof(luma), "Luma and chroma must be finite.");
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be in [0,1].");
        Luma = luma; ChromaBlue = chromaBlue; ChromaRed = chromaRed;
        Alpha = alpha; Encoding = encoding; Model = model;
    }

    /// <summary>Gets nonlinear luma Y′.</summary>
    public float Luma { get; }
    /// <summary>Gets zero-centered U or Cb, according to <see cref="Model"/>.</summary>
    public float ChromaBlue { get; }
    /// <summary>Gets zero-centered V or Cr, according to <see cref="Model"/>.</summary>
    public float ChromaRed { get; }
    /// <summary>Gets unpremultiplied alpha, which is not color transformed.</summary>
    public float Alpha { get; }
    /// <summary>Gets the RGB primaries, white point and transfer encoding used by these coordinates.</summary>
    public StandardRgbEncoding Encoding { get; }
    /// <summary>Gets the luma and color-difference matrix identity.</summary>
    public LumaChromaModel Model { get; }
}

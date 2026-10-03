namespace Mu3D.Color;

/// <summary>Converts tagged nonlinear RGB to and from floating-point Y′UV and Y′CbCr coordinates.</summary>
/// <remarks>FP32 calculations preserve alpha and extended RGB without tone mapping or clipping.
/// Selecting a matrix does not convert the tagged RGB primaries or transfer function. Convert RGB
/// encoding explicitly before calling this API when a different RGB identity is required.
/// This coordinate transform is not a video codec and applies no quantization or subsampling.</remarks>
public static class LumaChromaConverter
{
    /// <summary>Computes luma/chroma from the source's encoded RGB components using the selected matrix.</summary>
    /// <param name="source">Finite, unpremultiplied encoded RGB; negative and above-one values are allowed.</param>
    /// <param name="model">The coefficient and color-difference scaling identity.</param>
    /// <returns>Coordinates retaining the source RGB encoding and alpha.</returns>
    public static LumaChromaColor FromRgb(StandardEncodedRgba source, LumaChromaModel model)
    {
        var c = Coefficients(model);
        float luma = c.Red * source.Red + c.Green * source.Green + c.Blue * source.Blue;
        return new(luma, (source.Blue - luma) * c.BlueScale,
            (source.Red - luma) * c.RedScale, source.Alpha, source.Encoding, model);
    }

    /// <summary>Reconstructs encoded RGB in the coordinate's original tagged encoding without clipping.</summary>
    /// <param name="source">Finite floating-point luma/chroma coordinates with explicit matrix identity.</param>
    /// <returns>Unpremultiplied encoded RGB retaining alpha and RGB identity.</returns>
    public static StandardEncodedRgba ToRgb(LumaChromaColor source)
    {
        var c = Coefficients(source.Model);
        float red = source.Luma + source.ChromaRed / c.RedScale;
        float blue = source.Luma + source.ChromaBlue / c.BlueScale;
        float green = (source.Luma - c.Red * red - c.Blue * blue) / c.Green;
        return new(red, green, blue, source.Alpha, source.Encoding);
    }

    internal static (float Red, float Green, float Blue, float BlueScale, float RedScale)
        Coefficients(LumaChromaModel model) => model switch
    {
        LumaChromaModel.YuvBt601 => (0.299f, 0.587f, 0.114f, 0.492f, 0.877f),
        LumaChromaModel.YCbCrBt601 => (0.299f, 0.587f, 0.114f, 1 / 1.772f, 1 / 1.402f),
        LumaChromaModel.YCbCrBt709 => (0.2126f, 0.7152f, 0.0722f, 1 / 1.8556f, 1 / 1.5748f),
        LumaChromaModel.YCbCrBt2020 => (0.2627f, 0.6780f, 0.0593f, 1 / 1.8814f, 1 / 1.4746f),
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };
}

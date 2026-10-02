using System.Buffers.Binary;
using System.Text;

namespace Mu3D.Color;

/// <summary>
/// Converts encoded RGB samples through one selected ICC device-to-PCS transform and then into a
/// standard linear-light working space.
/// </summary>
/// <remarks>
/// Media-relative matrix/TRC, legacy <c>lut8Type</c>/<c>lut16Type</c>, ICC v4
/// <c>lutAToBType</c>, and RGB-to-PCS <c>multiProcessElementsType</c> transforms with bounded
/// one-to-sixteen-channel intermediate elements are supported.
/// ICC-absolute input is supported when the profile supplies a direct <c>D2B3</c> transform.
/// Otherwise it is synthesized from the media-relative transform and <c>mediaWhitePointTag</c>.
/// Black-point compensation remains an explicit unsupported path.
/// </remarks>
public sealed class IccRgbToLinearTransform
{
    private readonly IccMatrixTrcTransform? matrixTrc;
    private readonly LutTransform? lut;
    private readonly PcsScale? absolutePcsScale;

    /// <summary>Initializes and validates a transform for one application-selected rendering intent.</summary>
    /// <param name="profile">The immutable RGB ICC v2/v4 source profile.</param>
    /// <param name="renderingIntent">The requested colour-rendering objective.</param>
    public IccRgbToLinearTransform(
        IccProfile profile,
        IccRenderingIntent renderingIntent = IccRenderingIntent.MediaRelativeColorimetric)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(renderingIntent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderingIntent),
                renderingIntent,
                "Unknown ICC rendering intent.");
        }
        IccRenderingIntent selectedIntent = renderingIntent;
        if (renderingIntent == IccRenderingIntent.IccAbsoluteColorimetric)
        {
            try
            {
                if (IccMultiProcessElementsTransform.TryCreate(
                    profile,
                    "D2B3",
                    out IccMultiProcessElementsTransform? absoluteMultiProcess))
                {
                    lut = absoluteMultiProcess!.TransformEncodedRgb;
                    RenderingIntent = renderingIntent;
                    return;
                }
            }
            catch (NotSupportedException)
            {
                // ICC 8.10 permits an unsupported DToB chain to use the corresponding AToB path.
            }
            absolutePcsScale = ReadMediaRelativeToAbsolutePcsScale(profile);
            renderingIntent = IccRenderingIntent.MediaRelativeColorimetric;
        }

        string dToBTagSignature = renderingIntent switch
        {
            IccRenderingIntent.Perceptual => "D2B0",
            IccRenderingIntent.MediaRelativeColorimetric => "D2B1",
            IccRenderingIntent.Saturation => "D2B2",
            _ => throw new InvalidOperationException("Unexpected validated ICC rendering intent."),
        };
        try
        {
            if (IccMultiProcessElementsTransform.TryCreate(
                profile,
                dToBTagSignature,
                out IccMultiProcessElementsTransform? multiProcess))
            {
                lut = multiProcess!.TransformEncodedRgb;
                RenderingIntent = selectedIntent;
                return;
            }
        }
        catch (NotSupportedException)
        {
            // ICC 8.10 requires an unsupported DToB processing element chain to fall back to AToB.
        }

        string tagSignature = renderingIntent switch
        {
            IccRenderingIntent.Perceptual => "A2B0",
            IccRenderingIntent.MediaRelativeColorimetric => "A2B1",
            IccRenderingIntent.Saturation => "A2B2",
            _ => throw new InvalidOperationException("Unexpected validated ICC rendering intent."),
        };
        if (LegacyLutTransform.TryCreate(profile, tagSignature, out LutTransform? parsed))
        {
            lut = parsed;
            RenderingIntent = selectedIntent;
            return;
        }
        if (renderingIntent == IccRenderingIntent.MediaRelativeColorimetric)
        {
            matrixTrc = new IccMatrixTrcTransform(profile);
            RenderingIntent = selectedIntent;
            return;
        }

        throw new NotSupportedException(
            $"ICC profile does not contain the required {tagSignature} transform for {selectedIntent}.");
    }

    /// <summary>Gets the application-selected rendering intent used by this transform.</summary>
    public IccRenderingIntent RenderingIntent { get; }

    /// <summary>
    /// Converts one unpremultiplied encoded RGB sample into a standard linear-light working space.
    /// </summary>
    /// <param name="red">The encoded red component in the inclusive zero-to-one range.</param>
    /// <param name="green">The encoded green component in the inclusive zero-to-one range.</param>
    /// <param name="blue">The encoded blue component in the inclusive zero-to-one range.</param>
    /// <param name="alpha">The unpremultiplied alpha component in the inclusive zero-to-one range.</param>
    /// <param name="destination">The destination standard linear RGB identity.</param>
    /// <returns>The transformed value without clipping, gamut mapping or black-point compensation.</returns>
    public LinearRgba TransformEncodedRgb(
        float red,
        float green,
        float blue,
        float alpha,
        StandardRgbColorSpaceReference destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        LinearRgba transformed = lut is not null
            ? lut(red, green, blue, alpha, destination)
            : matrixTrc!.TransformEncodedRgb(red, green, blue, alpha, destination);
        if (absolutePcsScale is not PcsScale scale)
        {
            return transformed;
        }

        (float x, float y, float z) = StandardLinearRgbConverter.ToXyzD50(
            transformed,
            destination);
        return StandardLinearRgbConverter.FromXyzD50(
            x * scale.X,
            y * scale.Y,
            z * scale.Z,
            alpha,
            destination);
    }

    internal static PcsScale ReadMediaRelativeToAbsolutePcsScale(IccProfile profile)
    {
        const float pcsWhiteX = 0.9642f;
        const float pcsWhiteY = 1f;
        const float pcsWhiteZ = 0.8249f;
        ReadOnlySpan<byte> profileData = profile.AsSpan();
        float headerX = ReadS15Fixed16(profileData.Slice(68, 4));
        float headerY = ReadS15Fixed16(profileData.Slice(72, 4));
        float headerZ = ReadS15Fixed16(profileData.Slice(76, 4));
        const float fixedPointTolerance = 1f / 65536f;
        if (MathF.Abs(headerX - pcsWhiteX) > fixedPointTolerance ||
            MathF.Abs(headerY - pcsWhiteY) > fixedPointTolerance ||
            MathF.Abs(headerZ - pcsWhiteZ) > fixedPointTolerance)
        {
            throw new InvalidDataException(
                "ICC profile header illuminant must encode the PCS D50 white point.");
        }
        if (!TryFindProfileTag(profileData, "wtpt", out ReadOnlySpan<byte> mediaWhiteTag))
        {
            throw new InvalidDataException(
                "ICC mediaWhitePointTag is required to synthesize ICC-absolute colorimetry.");
        }
        if (mediaWhiteTag.Length != 20 || !mediaWhiteTag[..4].SequenceEqual("XYZ "u8))
        {
            throw new InvalidDataException(
                "ICC mediaWhitePointTag must contain exactly one XYZType value.");
        }
        EnsureReservedZero(mediaWhiteTag.Slice(4, 4));
        float mediaX = ReadS15Fixed16(mediaWhiteTag.Slice(8, 4));
        float mediaY = ReadS15Fixed16(mediaWhiteTag.Slice(12, 4));
        float mediaZ = ReadS15Fixed16(mediaWhiteTag.Slice(16, 4));
        if (mediaX <= 0f || mediaY <= 0f || mediaZ <= 0f)
        {
            throw new InvalidDataException(
                "ICC mediaWhitePointTag XYZ components must be positive.");
        }
        return new PcsScale(mediaX / pcsWhiteX, mediaY / pcsWhiteY, mediaZ / pcsWhiteZ);
    }

    private static bool TryFindProfileTag(
        ReadOnlySpan<byte> profile,
        string requested,
        out ReadOnlySpan<byte> tag)
    {
        if (profile.Length < 132)
        {
            throw new InvalidDataException("ICC tag table is missing.");
        }
        uint count = BinaryPrimitives.ReadUInt32BigEndian(profile.Slice(128, 4));
        if (count > (profile.Length - 132) / 12)
        {
            throw new InvalidDataException("ICC tag table extends beyond the profile.");
        }
        uint firstTagData = checked(132u + count * 12u);
        HashSet<string> signatures = new(StringComparer.Ordinal);
        tag = default;
        for (uint index = 0; index < count; index++)
        {
            int entry = checked(132 + (int)index * 12);
            string signature = ReadProfileSignature(profile.Slice(entry, 4));
            if (!signatures.Add(signature))
            {
                throw new InvalidDataException($"ICC tag '{signature}' is duplicated.");
            }
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(profile.Slice(entry + 4, 4));
            uint size = BinaryPrimitives.ReadUInt32BigEndian(profile.Slice(entry + 8, 4));
            if (offset % 4 != 0 || offset < firstTagData || size == 0 ||
                offset > (uint)profile.Length || size > (uint)profile.Length - offset)
            {
                throw new InvalidDataException($"ICC tag '{signature}' has an invalid range.");
            }
            if (signature == requested)
            {
                tag = profile.Slice(checked((int)offset), checked((int)size));
            }
        }
        return !tag.IsEmpty;
    }

    private static string ReadProfileSignature(ReadOnlySpan<byte> value)
    {
        foreach (byte item in value)
        {
            if (item is < 0x20 or > 0x7e)
            {
                throw new InvalidDataException("ICC tag table contains a non-printable signature.");
            }
        }
        return Encoding.ASCII.GetString(value);
    }

    private static float ReadS15Fixed16(ReadOnlySpan<byte> value) =>
        BinaryPrimitives.ReadInt32BigEndian(value) / 65536f;

    private static void EnsureReservedZero(ReadOnlySpan<byte> value)
    {
        foreach (byte item in value)
        {
            if (item != 0)
            {
                throw new InvalidDataException("ICC reserved bytes must be zero.");
            }
        }
    }

    private delegate LinearRgba LutTransform(
        float red,
        float green,
        float blue,
        float alpha,
        StandardRgbColorSpaceReference destination);

    internal delegate (float Red, float Green, float Blue) PcsToRgbTransform(
        float x,
        float y,
        float z);

    internal static bool TryCreatePcsToRgbTransform(
        IccProfile profile,
        string tagSignature,
        out PcsToRgbTransform? transform)
    {
        if (!TryFindProfileTag(profile.AsSpan(), tagSignature, out ReadOnlySpan<byte> tag))
        {
            transform = null;
            return false;
        }
        if (tag.Length < 4)
        {
            throw new InvalidDataException($"ICC {tagSignature} tag is truncated.");
        }
        string type = ReadProfileSignature(tag[..4]);
        transform = type switch
        {
            "mft1" => LegacyLutTransform.ParseLut8(
                    tag,
                    profile.ProfileConnectionSpace,
                    pcsToDevice: true)
                .TransformPcsToEncodedRgb,
            "mft2" => LegacyLutTransform.ParseLut16(
                    tag,
                    profile.ProfileConnectionSpace,
                    pcsToDevice: true)
                .TransformPcsToEncodedRgb,
            "mBA " => ModernLutAToBTransform.ParseBToA(tag, profile.ProfileConnectionSpace)
                .TransformPcsToEncodedRgb,
            _ => throw new NotSupportedException(
                $"ICC {tagSignature} tag type '{type}' is not a supported PCS-to-device LUT."),
        };
        return true;
    }

    internal readonly record struct PcsScale(float X, float Y, float Z);

    private sealed class ModernLutAToBTransform
    {
        private readonly Curve[] aCurves;
        private readonly Curve[] mCurves;
        private readonly Curve[] bCurves;
        private readonly float[]? clut;
        private readonly int redGrid;
        private readonly int greenGrid;
        private readonly int blueGrid;
        private readonly Matrix3x4? matrix;
        private readonly bool pcsIsXyz;

        private ModernLutAToBTransform(
            Curve[] aCurves,
            Curve[] mCurves,
            Curve[] bCurves,
            float[]? clut,
            int redGrid,
            int greenGrid,
            int blueGrid,
            Matrix3x4? matrix,
            bool pcsIsXyz)
        {
            this.aCurves = aCurves;
            this.mCurves = mCurves;
            this.bCurves = bCurves;
            this.clut = clut;
            this.redGrid = redGrid;
            this.greenGrid = greenGrid;
            this.blueGrid = blueGrid;
            this.matrix = matrix;
            this.pcsIsXyz = pcsIsXyz;
        }

        internal static ModernLutAToBTransform Parse(ReadOnlySpan<byte> tag, string pcs)
        {
            if (tag.Length < 32)
            {
                throw new InvalidDataException("ICC lutAToBType header is truncated.");
            }
            EnsureZero(tag.Slice(4, 4));
            if (tag[8] != 3 || tag[9] != 3)
            {
                throw new NotSupportedException(
                    $"ICC RGB lutAToBType requires 3 input and 3 output channels, not {tag[8]} and {tag[9]}.");
            }
            EnsureZero(tag.Slice(10, 2));
            int bOffset = ReadElementOffset(tag, 12, "B curves", required: true);
            int matrixOffset = ReadElementOffset(tag, 16, "matrix", required: false);
            int mOffset = ReadElementOffset(tag, 20, "M curves", required: false);
            int clutOffset = ReadElementOffset(tag, 24, "CLUT", required: false);
            int aOffset = ReadElementOffset(tag, 28, "A curves", required: false);
            if ((matrixOffset == 0) != (mOffset == 0))
            {
                throw new InvalidDataException(
                    "ICC lutAToBType M curves and matrix must either both be present or both be absent.");
            }
            if ((clutOffset == 0) != (aOffset == 0))
            {
                throw new InvalidDataException(
                    "ICC lutAToBType A curves and CLUT must either both be present or both be absent.");
            }
            if (pcs is not ("XYZ " or "Lab "))
            {
                throw new NotSupportedException($"ICC lutAToBType PCS '{pcs}' is not supported.");
            }

            Curve[] bCurves = ReadCurveSet(tag, bOffset, 3, "B");
            Curve[] mCurves = mOffset == 0 ? [] : ReadCurveSet(tag, mOffset, 3, "M");
            Curve[] aCurves = aOffset == 0 ? [] : ReadCurveSet(tag, aOffset, 3, "A");
            Matrix3x4? matrix = matrixOffset == 0 ? null : ReadMatrix(tag, matrixOffset);
            (float[]? values, int redGrid, int greenGrid, int blueGrid) = clutOffset == 0
                ? (null, 0, 0, 0)
                : ReadClut(tag, clutOffset);
            return new ModernLutAToBTransform(
                aCurves,
                mCurves,
                bCurves,
                values,
                redGrid,
                greenGrid,
                blueGrid,
                matrix,
                pcs == "XYZ ");
        }

        internal static ModernLutAToBTransform ParseBToA(ReadOnlySpan<byte> tag, string pcs)
        {
            ModernLutAToBTransform parsed = Parse(tag, pcs);
            return parsed;
        }

        internal (float Red, float Green, float Blue) TransformPcsToEncodedRgb(
            float x,
            float y,
            float z)
        {
            Triple value = pcsIsXyz
                ? new Triple(
                    x / (65535f / 32768f),
                    y / (65535f / 32768f),
                    z / (65535f / 32768f))
                : EncodeModernLab(x, y, z);
            value = new Triple(
                Math.Clamp(value.First, 0f, 1f),
                Math.Clamp(value.Second, 0f, 1f),
                Math.Clamp(value.Third, 0f, 1f));
            value = ApplyCurves(bCurves, value);
            if (mCurves.Length != 0)
            {
                value = matrix!.Value.TransformAndClip(value);
                value = ApplyCurves(mCurves, value);
            }
            if (aCurves.Length != 0)
            {
                value = SampleClut(value);
                value = ApplyCurves(aCurves, value);
            }
            return (value.First, value.Second, value.Third);
        }

        private static Triple EncodeModernLab(float x, float y, float z)
        {
            (float l, float a, float b) = XyzD50ToLab(x, y, z);
            return new Triple(l / 100f, (a + 128f) / 255f, (b + 128f) / 255f);
        }

        private static (float L, float A, float B) XyzD50ToLab(float x, float y, float z)
        {
            float fx = LabFunction(x / 0.9642f);
            float fy = LabFunction(y);
            float fz = LabFunction(z / 0.8249f);
            return (116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static float LabFunction(float value)
        {
            const float epsilon = 216f / 24389f;
            const float kappa = 24389f / 27f;
            return value > epsilon ? MathF.Cbrt(value) : (kappa * value + 16f) / 116f;
        }

        internal LinearRgba Transform(
            float red,
            float green,
            float blue,
            float alpha,
            StandardRgbColorSpaceReference destination)
        {
            ValidateComponent(red, nameof(red));
            ValidateComponent(green, nameof(green));
            ValidateComponent(blue, nameof(blue));
            ValidateComponent(alpha, nameof(alpha));
            Triple value = new(red, green, blue);
            if (aCurves.Length != 0)
            {
                value = ApplyCurves(aCurves, value);
                value = SampleClut(value);
            }
            if (mCurves.Length != 0)
            {
                value = ApplyCurves(mCurves, value);
                value = matrix!.Value.TransformAndClip(value);
            }
            value = ApplyCurves(bCurves, value);
            (float x, float y, float z) = pcsIsXyz
                ? (value.First * (65535f / 32768f),
                    value.Second * (65535f / 32768f),
                    value.Third * (65535f / 32768f))
                : LabToXyzD50(
                    value.First * 100f,
                    value.Second * 255f - 128f,
                    value.Third * 255f - 128f);
            return StandardLinearRgbConverter.FromXyzD50(x, y, z, alpha, destination);
        }

        private Triple SampleClut(Triple input)
        {
            float rPosition = input.First * (redGrid - 1);
            float gPosition = input.Second * (greenGrid - 1);
            float bPosition = input.Third * (blueGrid - 1);
            int r0 = Math.Min((int)rPosition, redGrid - 1);
            int g0 = Math.Min((int)gPosition, greenGrid - 1);
            int b0 = Math.Min((int)bPosition, blueGrid - 1);
            int r1 = Math.Min(r0 + 1, redGrid - 1);
            int g1 = Math.Min(g0 + 1, greenGrid - 1);
            int b1 = Math.Min(b0 + 1, blueGrid - 1);
            float fr = rPosition - r0;
            float fg = gPosition - g0;
            float fb = bPosition - b0;
            return new Triple(
                SampleTetrahedron(0, r0, g0, b0, r1, g1, b1, fr, fg, fb),
                SampleTetrahedron(1, r0, g0, b0, r1, g1, b1, fr, fg, fb),
                SampleTetrahedron(2, r0, g0, b0, r1, g1, b1, fr, fg, fb));
        }

        private float SampleTetrahedron(
            int channel,
            int r0,
            int g0,
            int b0,
            int r1,
            int g1,
            int b1,
            float fr,
            float fg,
            float fb)
        {
            float c000 = GetClut(r0, g0, b0, channel);
            float c111 = GetClut(r1, g1, b1, channel);
            if (fr >= fg)
            {
                if (fg >= fb)
                {
                    float c100 = GetClut(r1, g0, b0, channel);
                    float c110 = GetClut(r1, g1, b0, channel);
                    return c000 + fr * (c100 - c000) +
                        fg * (c110 - c100) + fb * (c111 - c110);
                }
                if (fr >= fb)
                {
                    float c100 = GetClut(r1, g0, b0, channel);
                    float c101 = GetClut(r1, g0, b1, channel);
                    return c000 + fr * (c100 - c000) +
                        fb * (c101 - c100) + fg * (c111 - c101);
                }
                float c001 = GetClut(r0, g0, b1, channel);
                float c101Last = GetClut(r1, g0, b1, channel);
                return c000 + fb * (c001 - c000) +
                    fr * (c101Last - c001) + fg * (c111 - c101Last);
            }
            if (fr >= fb)
            {
                float c010 = GetClut(r0, g1, b0, channel);
                float c110 = GetClut(r1, g1, b0, channel);
                return c000 + fg * (c010 - c000) +
                    fr * (c110 - c010) + fb * (c111 - c110);
            }
            if (fg >= fb)
            {
                float c010 = GetClut(r0, g1, b0, channel);
                float c011 = GetClut(r0, g1, b1, channel);
                return c000 + fg * (c010 - c000) +
                    fb * (c011 - c010) + fr * (c111 - c011);
            }
            float c001Last = GetClut(r0, g0, b1, channel);
            float c011Last = GetClut(r0, g1, b1, channel);
            return c000 + fb * (c001Last - c000) +
                fg * (c011Last - c001Last) + fr * (c111 - c011Last);
        }

        private float GetClut(int red, int green, int blue, int channel) =>
            clut![checked(((red * greenGrid + green) * blueGrid + blue) * 3 + channel)];

        private static Triple ApplyCurves(Curve[] curves, Triple value) => new(
            curves[0].Evaluate(value.First),
            curves[1].Evaluate(value.Second),
            curves[2].Evaluate(value.Third));

        private static Curve[] ReadCurveSet(
            ReadOnlySpan<byte> tag,
            int firstOffset,
            int count,
            string name)
        {
            Curve[] curves = new Curve[count];
            int offset = firstOffset;
            for (int index = 0; index < count; index++)
            {
                (curves[index], int length) = ReadCurve(tag, offset, name);
                if (index == count - 1)
                {
                    break;
                }
                int next = checked((offset + length + 3) & ~3);
                if (next > tag.Length)
                {
                    throw new InvalidDataException($"ICC lutAToBType {name} curve padding is truncated.");
                }
                EnsureZero(tag.Slice(offset + length, next - offset - length));
                offset = next;
            }
            return curves;
        }

        private static (Curve Curve, int Length) ReadCurve(
            ReadOnlySpan<byte> tag,
            int offset,
            string name)
        {
            if (offset > tag.Length - 12)
            {
                throw new InvalidDataException($"ICC lutAToBType {name} curve is truncated.");
            }
            ReadOnlySpan<byte> curve = tag[offset..];
            string type = ReadSignature(curve[..4]);
            EnsureZero(curve.Slice(4, 4));
            if (type == "curv")
            {
                uint count = BinaryPrimitives.ReadUInt32BigEndian(curve.Slice(8, 4));
                if (count > (curve.Length - 12) / 2)
                {
                    throw new InvalidDataException($"ICC lutAToBType {name} curve samples are truncated.");
                }
                int length = checked(12 + (int)count * 2);
                if (count == 0)
                {
                    return (Curve.Identity, length);
                }
                if (count == 1)
                {
                    float gamma = BinaryPrimitives.ReadUInt16BigEndian(curve.Slice(12, 2)) / 256f;
                    if (gamma <= 0f)
                    {
                        throw new InvalidDataException("ICC curveType gamma must be positive.");
                    }
                    return (Curve.Parametric(0, [gamma]), length);
                }
                float[] samples = new float[checked((int)count)];
                for (int index = 0; index < samples.Length; index++)
                {
                    samples[index] = BinaryPrimitives.ReadUInt16BigEndian(
                        curve.Slice(12 + index * 2, 2)) / 65535f;
                    if (index != 0 && samples[index] < samples[index - 1])
                    {
                        throw new InvalidDataException("ICC curveType samples must be non-decreasing.");
                    }
                }
                return (Curve.Sampled(samples), length);
            }
            if (type != "para")
            {
                throw new NotSupportedException(
                    $"ICC lutAToBType embedded curve '{type}' is not supported.");
            }
            ushort function = BinaryPrimitives.ReadUInt16BigEndian(curve.Slice(8, 2));
            EnsureZero(curve.Slice(10, 2));
            int parameterCount = function switch
            {
                0 => 1,
                1 => 3,
                2 => 4,
                3 => 5,
                4 => 7,
                _ => throw new NotSupportedException(
                    $"ICC parametricCurveType function {function} is not defined."),
            };
            int parametricLength = checked(12 + parameterCount * 4);
            if (curve.Length < parametricLength)
            {
                throw new InvalidDataException("ICC parametricCurveType parameters are truncated.");
            }
            float[] parameters = new float[parameterCount];
            for (int index = 0; index < parameters.Length; index++)
            {
                parameters[index] = ReadS15Fixed16(curve.Slice(12 + index * 4, 4));
            }
            if (parameters[0] <= 0f || (function != 0 && parameters[1] == 0f))
            {
                throw new InvalidDataException("ICC parametricCurveType has invalid gamma or scale.");
            }
            return (Curve.Parametric(function, parameters), parametricLength);
        }

        private static (float[] Values, int RedGrid, int GreenGrid, int BlueGrid) ReadClut(
            ReadOnlySpan<byte> tag,
            int offset)
        {
            if (offset > tag.Length - 20)
            {
                throw new InvalidDataException("ICC lutAToBType CLUT header is truncated.");
            }
            ReadOnlySpan<byte> header = tag.Slice(offset, 20);
            int redGrid = header[0];
            int greenGrid = header[1];
            int blueGrid = header[2];
            if (redGrid < 2 || greenGrid < 2 || blueGrid < 2)
            {
                throw new InvalidDataException(
                    "ICC lutAToBType requires at least two CLUT points in every RGB dimension.");
            }
            EnsureZero(header.Slice(3, 13));
            int precision = header[16];
            if (precision is not (1 or 2))
            {
                throw new InvalidDataException("ICC lutAToBType CLUT precision must be one or two bytes.");
            }
            EnsureZero(header.Slice(17, 3));
            int valueCount = checked(redGrid * greenGrid * blueGrid * 3);
            int byteCount = checked(valueCount * precision);
            if (offset + 20 > tag.Length - byteCount)
            {
                throw new InvalidDataException("ICC lutAToBType CLUT samples are truncated.");
            }
            ReadOnlySpan<byte> data = tag.Slice(offset + 20, byteCount);
            float[] values = new float[valueCount];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = precision == 1
                    ? data[index] / 255f
                    : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index * 2, 2)) / 65535f;
            }
            return (values, redGrid, greenGrid, blueGrid);
        }

        private static Matrix3x4 ReadMatrix(ReadOnlySpan<byte> tag, int offset)
        {
            if (offset > tag.Length - 48)
            {
                throw new InvalidDataException("ICC lutAToBType matrix is truncated.");
            }
            float[] values = new float[12];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = ReadS15Fixed16(tag.Slice(offset + index * 4, 4));
            }
            return new Matrix3x4(
                values[0], values[1], values[2],
                values[3], values[4], values[5],
                values[6], values[7], values[8],
                values[9], values[10], values[11]);
        }

        private static int ReadElementOffset(
            ReadOnlySpan<byte> tag,
            int fieldOffset,
            string name,
            bool required)
        {
            uint encoded = BinaryPrimitives.ReadUInt32BigEndian(tag.Slice(fieldOffset, 4));
            if (encoded == 0)
            {
                if (required)
                {
                    throw new InvalidDataException($"ICC lutAToBType requires {name}.");
                }
                return 0;
            }
            if (encoded % 4 != 0 || encoded < 32 || encoded >= (uint)tag.Length)
            {
                throw new InvalidDataException($"ICC lutAToBType {name} offset is invalid.");
            }
            return checked((int)encoded);
        }

        private static (float X, float Y, float Z) LabToXyzD50(float l, float a, float b)
        {
            float fy = (l + 16f) / 116f;
            float fx = fy + a / 500f;
            float fz = fy - b / 200f;
            return (
                0.9642f * InverseLabFunction(fx),
                InverseLabFunction(fy),
                0.8249f * InverseLabFunction(fz));
        }

        private static float InverseLabFunction(float value)
        {
            const float epsilon = 216f / 24389f;
            const float kappa = 24389f / 27f;
            float cube = value * value * value;
            return cube > epsilon ? cube : (116f * value - 16f) / kappa;
        }

        private static float ReadS15Fixed16(ReadOnlySpan<byte> value) =>
            BinaryPrimitives.ReadInt32BigEndian(value) / 65536f;

        private static string ReadSignature(ReadOnlySpan<byte> value)
        {
            foreach (byte component in value)
            {
                if (component is < 0x20 or > 0x7e)
                {
                    throw new InvalidDataException("ICC tag contains a non-printable signature.");
                }
            }
            return Encoding.ASCII.GetString(value);
        }

        private static void EnsureZero(ReadOnlySpan<byte> value)
        {
            foreach (byte component in value)
            {
                if (component != 0)
                {
                    throw new InvalidDataException("ICC reserved or padding bytes must be zero.");
                }
            }
        }

        private static void ValidateComponent(float value, string name)
        {
            if (!float.IsFinite(value) || value is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(name, "ICC encoded components must be in [0, 1].");
            }
        }

        private readonly struct Curve
        {
            private readonly ushort function;
            private readonly float[]? parameters;
            private readonly float[]? samples;

            private Curve(ushort function, float[]? parameters, float[]? samples)
            {
                this.function = function;
                this.parameters = parameters;
                this.samples = samples;
            }

            internal static Curve Identity { get; } = new(0, [1f], null);

            internal static Curve Parametric(ushort function, float[] parameters) =>
                new(function, parameters, null);

            internal static Curve Sampled(float[] samples) => new(0, null, samples);

            internal float Evaluate(float input)
            {
                float result;
                if (samples is not null)
                {
                    float position = input * (samples.Length - 1);
                    int lower = Math.Min((int)position, samples.Length - 1);
                    int upper = Math.Min(lower + 1, samples.Length - 1);
                    result = samples[lower] +
                        (samples[upper] - samples[lower]) * (position - lower);
                }
                else
                {
                    float[] p = parameters!;
                    float g = p[0];
                    result = function switch
                    {
                        0 => MathF.Pow(input, g),
                        1 => input >= -p[2] / p[1] ? MathF.Pow(p[1] * input + p[2], g) : 0f,
                        2 => input >= -p[2] / p[1]
                            ? MathF.Pow(p[1] * input + p[2], g) + p[3]
                            : p[3],
                        3 => input >= p[4]
                            ? MathF.Pow(p[1] * input + p[2], g)
                            : p[3] * input,
                        4 => input >= p[4]
                            ? MathF.Pow(p[1] * input + p[2], g) + p[5]
                            : p[3] * input + p[6],
                        _ => throw new InvalidOperationException("Unknown parsed ICC curve."),
                    };
                }
                if (!float.IsFinite(result) || result is < 0f or > 1f)
                {
                    throw new InvalidDataException(
                        "ICC lutAToBType curve produced a value outside its defined [0, 1] range.");
                }
                return result;
            }
        }

        private readonly record struct Triple(float First, float Second, float Third);

        private readonly record struct Matrix3x4(
            float M11,
            float M12,
            float M13,
            float M21,
            float M22,
            float M23,
            float M31,
            float M32,
            float M33,
            float O1,
            float O2,
            float O3)
        {
            internal Triple TransformAndClip(Triple value) => new(
                Math.Clamp(M11 * value.First + M12 * value.Second + M13 * value.Third + O1, 0f, 1f),
                Math.Clamp(M21 * value.First + M22 * value.Second + M23 * value.Third + O2, 0f, 1f),
                Math.Clamp(M31 * value.First + M32 * value.Second + M33 * value.Third + O3, 0f, 1f));
        }
    }

    private sealed class LegacyLutTransform
    {
        private const float LegacyLabScale = 65535f / 65280f;
        private readonly float[][] inputTables;
        private readonly float[] clut;
        private readonly float[][] outputTables;
        private readonly int gridPoints;
        private readonly PcsEncoding pcsEncoding;
        private readonly LegacyMatrix3x3 inputMatrix;

        private LegacyLutTransform(
            float[][] inputTables,
            float[] clut,
            float[][] outputTables,
            int gridPoints,
            PcsEncoding pcsEncoding,
            LegacyMatrix3x3 inputMatrix)
        {
            this.inputTables = inputTables;
            this.clut = clut;
            this.outputTables = outputTables;
            this.gridPoints = gridPoints;
            this.pcsEncoding = pcsEncoding;
            this.inputMatrix = inputMatrix;
        }

        internal static bool TryCreate(
            IccProfile profile,
            string tagSignature,
            out LutTransform? transform)
        {
            ReadOnlySpan<byte> profileData = profile.AsSpan();
            if (!TryFindTag(profileData, tagSignature, out ReadOnlySpan<byte> tag))
            {
                transform = null;
                return false;
            }
            if (tag.Length < 4)
            {
                throw new InvalidDataException($"ICC {tagSignature} tag is truncated.");
            }
            string type = ReadSignature(tag[..4]);
            transform = type switch
            {
                "mft1" => ParseLut8(tag, profile.ProfileConnectionSpace).Transform,
                "mft2" => ParseLut16(tag, profile.ProfileConnectionSpace).Transform,
                "mAB " => ModernLutAToBTransform.Parse(tag, profile.ProfileConnectionSpace).Transform,
                _ => throw new NotSupportedException(
                    $"ICC {tagSignature} tag type '{type}' is not a supported device-to-PCS LUT."),
            };
            return true;
        }

        internal LinearRgba Transform(
            float red,
            float green,
            float blue,
            float alpha,
            StandardRgbColorSpaceReference destination)
        {
            ValidateComponent(red, nameof(red));
            ValidateComponent(green, nameof(green));
            ValidateComponent(blue, nameof(blue));
            ValidateComponent(alpha, nameof(alpha));

            float r = SampleTable(inputTables[0], red);
            float g = SampleTable(inputTables[1], green);
            float b = SampleTable(inputTables[2], blue);
            (float first, float second, float third) = SampleClut(r, g, b);
            first = SampleTable(outputTables[0], first);
            second = SampleTable(outputTables[1], second);
            third = SampleTable(outputTables[2], third);

            (float x, float y, float z) = pcsEncoding switch
            {
                PcsEncoding.Xyz16 => (
                    first * (65535f / 32768f),
                    second * (65535f / 32768f),
                    third * (65535f / 32768f)),
                PcsEncoding.LegacyLab16 => LabToXyzD50(
                    first * 100f * LegacyLabScale,
                    second * 255f * LegacyLabScale - 128f,
                    third * 255f * LegacyLabScale - 128f),
                PcsEncoding.Lab8 => LabToXyzD50(
                    first * 100f,
                    second * 255f - 128f,
                    third * 255f - 128f),
                _ => throw new InvalidOperationException("Unexpected parsed ICC PCS encoding."),
            };
            return StandardLinearRgbConverter.FromXyzD50(x, y, z, alpha, destination);
        }

        internal static LegacyLutTransform ParseLut8(
            ReadOnlySpan<byte> tag,
            string pcs,
            bool pcsToDevice = false)
        {
            if (pcs == "XYZ ")
            {
                throw new NotSupportedException(
                    "ICC does not define a portable 8-bit PCSXYZ encoding for lut8Type.");
            }
            if (pcs != "Lab ")
            {
                throw new NotSupportedException($"ICC lut8Type PCS '{pcs}' is not supported.");
            }
            LutHeader header = ReadHeader(
                tag,
                minimumLength: 48,
                allowInputMatrix: pcsToDevice && pcs == "XYZ ");
            int inputBytes = checked(3 * 256);
            int clutValues = checked(Cube(header.GridPoints) * 3);
            int outputBytes = checked(3 * 256);
            int expectedLength = checked(48 + inputBytes + clutValues + outputBytes);
            RequireExactLength(tag, expectedLength, "lut8Type");
            int offset = 48;
            float[][] input = ReadByteTables(tag.Slice(offset, inputBytes), 256);
            offset += inputBytes;
            float[] clut = ReadByteValues(tag.Slice(offset, clutValues));
            offset += clutValues;
            float[][] output = ReadByteTables(tag.Slice(offset, outputBytes), 256);
            return new LegacyLutTransform(
                input,
                clut,
                output,
                header.GridPoints,
                PcsEncoding.Lab8,
                header.InputMatrix);
        }

        internal static LegacyLutTransform ParseLut16(
            ReadOnlySpan<byte> tag,
            string pcs,
            bool pcsToDevice = false)
        {
            PcsEncoding encoding = pcs switch
            {
                "XYZ " => PcsEncoding.Xyz16,
                "Lab " => PcsEncoding.LegacyLab16,
                _ => throw new NotSupportedException($"ICC lut16Type PCS '{pcs}' is not supported."),
            };
            LutHeader header = ReadHeader(
                tag,
                minimumLength: 52,
                allowInputMatrix: pcsToDevice && encoding == PcsEncoding.Xyz16);
            int inputEntries = BinaryPrimitives.ReadUInt16BigEndian(tag.Slice(48, 2));
            int outputEntries = BinaryPrimitives.ReadUInt16BigEndian(tag.Slice(50, 2));
            if (inputEntries is < 2 or > 4096 || outputEntries is < 2 or > 4096)
            {
                throw new InvalidDataException(
                    "ICC lut16Type input/output tables require between 2 and 4096 entries.");
            }
            int inputValues = checked(3 * inputEntries);
            int clutValues = checked(Cube(header.GridPoints) * 3);
            int outputValues = checked(3 * outputEntries);
            int expectedLength = checked(52 + 2 * (inputValues + clutValues + outputValues));
            RequireExactLength(tag, expectedLength, "lut16Type");
            int offset = 52;
            float[][] input = ReadUInt16Tables(tag.Slice(offset, inputValues * 2), inputEntries);
            offset += inputValues * 2;
            float[] clut = ReadUInt16Values(tag.Slice(offset, clutValues * 2));
            offset += clutValues * 2;
            float[][] output = ReadUInt16Tables(tag.Slice(offset, outputValues * 2), outputEntries);
            return new LegacyLutTransform(
                input,
                clut,
                output,
                header.GridPoints,
                encoding,
                header.InputMatrix);
        }

        private static LutHeader ReadHeader(
            ReadOnlySpan<byte> tag,
            int minimumLength,
            bool allowInputMatrix)
        {
            if (tag.Length < minimumLength)
            {
                throw new InvalidDataException("ICC legacy LUT header is truncated.");
            }
            EnsureZero(tag.Slice(4, 4));
            if (tag[8] != 3 || tag[9] != 3)
            {
                throw new NotSupportedException(
                    $"ICC RGB document LUT requires 3 input and 3 output channels, not {tag[8]} and {tag[9]}.");
            }
            int grid = tag[10];
            if (grid < 2)
            {
                throw new InvalidDataException("ICC legacy LUT requires at least two CLUT grid points.");
            }
            if (tag[11] != 0)
            {
                throw new InvalidDataException("ICC legacy LUT padding byte must be zero.");
            }
            float[] matrix = new float[9];
            for (int index = 0; index < matrix.Length; index++)
            {
                matrix[index] = ReadS15Fixed16(tag.Slice(12 + index * 4, 4));
                float expected = index is 0 or 4 or 8 ? 1f : 0f;
                if (!allowInputMatrix && matrix[index] != expected)
                {
                    throw new InvalidDataException(
                        "ICC device-to-PCS or Lab legacy LUT matrix must be the identity matrix.");
                }
            }
            return new LutHeader(
                grid,
                new LegacyMatrix3x3(
                    matrix[0], matrix[1], matrix[2],
                    matrix[3], matrix[4], matrix[5],
                    matrix[6], matrix[7], matrix[8]));
        }

        private (float First, float Second, float Third) SampleClut(float r, float g, float b)
        {
            float rPosition = r * (gridPoints - 1);
            float gPosition = g * (gridPoints - 1);
            float bPosition = b * (gridPoints - 1);
            int r0 = Math.Min((int)rPosition, gridPoints - 1);
            int g0 = Math.Min((int)gPosition, gridPoints - 1);
            int b0 = Math.Min((int)bPosition, gridPoints - 1);
            int r1 = Math.Min(r0 + 1, gridPoints - 1);
            int g1 = Math.Min(g0 + 1, gridPoints - 1);
            int b1 = Math.Min(b0 + 1, gridPoints - 1);
            float fr = rPosition - r0;
            float fg = gPosition - g0;
            float fb = bPosition - b0;
            return (
                SampleTetrahedron(0, r0, g0, b0, r1, g1, b1, fr, fg, fb),
                SampleTetrahedron(1, r0, g0, b0, r1, g1, b1, fr, fg, fb),
                SampleTetrahedron(2, r0, g0, b0, r1, g1, b1, fr, fg, fb));
        }

        internal (float Red, float Green, float Blue) TransformPcsToEncodedRgb(
            float x,
            float y,
            float z)
        {
            (float first, float second, float third) = pcsEncoding switch
            {
                PcsEncoding.Xyz16 => (
                    x / (65535f / 32768f),
                    y / (65535f / 32768f),
                    z / (65535f / 32768f)),
                PcsEncoding.LegacyLab16 => EncodeLegacyLab(x, y, z, LegacyLabScale),
                PcsEncoding.Lab8 => EncodeLegacyLab(x, y, z, 1f),
                _ => throw new InvalidOperationException("Unexpected parsed ICC PCS encoding."),
            };
            (first, second, third) = inputMatrix.TransformAndClip(first, second, third);
            float r = SampleTable(inputTables[0], Math.Clamp(first, 0f, 1f));
            float g = SampleTable(inputTables[1], Math.Clamp(second, 0f, 1f));
            float b = SampleTable(inputTables[2], Math.Clamp(third, 0f, 1f));
            (first, second, third) = SampleClut(r, g, b);
            return (
                SampleTable(outputTables[0], first),
                SampleTable(outputTables[1], second),
                SampleTable(outputTables[2], third));
        }

        private static (float First, float Second, float Third) EncodeLegacyLab(
            float x,
            float y,
            float z,
            float scale)
        {
            (float l, float a, float b) = XyzD50ToLab(x, y, z);
            return (
                l / (100f * scale),
                (a + 128f) / (255f * scale),
                (b + 128f) / (255f * scale));
        }

        private static (float L, float A, float B) XyzD50ToLab(float x, float y, float z)
        {
            float fx = LabFunction(x / 0.9642f);
            float fy = LabFunction(y);
            float fz = LabFunction(z / 0.8249f);
            return (116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static float LabFunction(float value)
        {
            const float epsilon = 216f / 24389f;
            const float kappa = 24389f / 27f;
            return value > epsilon ? MathF.Cbrt(value) : (kappa * value + 16f) / 116f;
        }

        private float SampleTetrahedron(
            int channel,
            int r0,
            int g0,
            int b0,
            int r1,
            int g1,
            int b1,
            float fr,
            float fg,
            float fb)
        {
            float c000 = GetClut(r0, g0, b0, channel);
            float c111 = GetClut(r1, g1, b1, channel);
            if (fr >= fg)
            {
                if (fg >= fb)
                {
                    float c100 = GetClut(r1, g0, b0, channel);
                    float c110 = GetClut(r1, g1, b0, channel);
                    return c000 + fr * (c100 - c000) +
                        fg * (c110 - c100) + fb * (c111 - c110);
                }
                if (fr >= fb)
                {
                    float c100 = GetClut(r1, g0, b0, channel);
                    float c101 = GetClut(r1, g0, b1, channel);
                    return c000 + fr * (c100 - c000) +
                        fb * (c101 - c100) + fg * (c111 - c101);
                }
                float c001 = GetClut(r0, g0, b1, channel);
                float c101Last = GetClut(r1, g0, b1, channel);
                return c000 + fb * (c001 - c000) +
                    fr * (c101Last - c001) + fg * (c111 - c101Last);
            }
            if (fr >= fb)
            {
                float c010 = GetClut(r0, g1, b0, channel);
                float c110 = GetClut(r1, g1, b0, channel);
                return c000 + fg * (c010 - c000) +
                    fr * (c110 - c010) + fb * (c111 - c110);
            }
            if (fg >= fb)
            {
                float c010 = GetClut(r0, g1, b0, channel);
                float c011 = GetClut(r0, g1, b1, channel);
                return c000 + fg * (c010 - c000) +
                    fb * (c011 - c010) + fr * (c111 - c011);
            }
            float c001Last = GetClut(r0, g0, b1, channel);
            float c011Last = GetClut(r0, g1, b1, channel);
            return c000 + fb * (c001Last - c000) +
                fg * (c011Last - c001Last) + fr * (c111 - c011Last);
        }

        private float GetClut(int red, int green, int blue, int channel) =>
            clut[checked(((red * gridPoints + green) * gridPoints + blue) * 3 + channel)];

        private static float SampleTable(float[] table, float value)
        {
            float position = Math.Clamp(value, 0f, 1f) * (table.Length - 1);
            int lower = Math.Min((int)position, table.Length - 1);
            int upper = Math.Min(lower + 1, table.Length - 1);
            return table[lower] + (table[upper] - table[lower]) * (position - lower);
        }

        private static float[][] ReadByteTables(ReadOnlySpan<byte> data, int entries)
        {
            float[][] tables = new float[3][];
            for (int channel = 0; channel < 3; channel++)
            {
                tables[channel] = ReadByteValues(data.Slice(channel * entries, entries));
            }
            return tables;
        }

        private static float[] ReadByteValues(ReadOnlySpan<byte> data)
        {
            float[] values = new float[data.Length];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = data[index] / 255f;
            }
            return values;
        }

        private static float[][] ReadUInt16Tables(ReadOnlySpan<byte> data, int entries)
        {
            float[][] tables = new float[3][];
            int tableBytes = checked(entries * 2);
            for (int channel = 0; channel < 3; channel++)
            {
                tables[channel] = ReadUInt16Values(data.Slice(channel * tableBytes, tableBytes));
            }
            return tables;
        }

        private static float[] ReadUInt16Values(ReadOnlySpan<byte> data)
        {
            float[] values = new float[data.Length / 2];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = BinaryPrimitives.ReadUInt16BigEndian(
                    data.Slice(index * 2, 2)) / 65535f;
            }
            return values;
        }

        private static bool TryFindTag(
            ReadOnlySpan<byte> profile,
            string requested,
            out ReadOnlySpan<byte> tag)
        {
            if (profile.Length < 132)
            {
                throw new InvalidDataException("ICC tag table is missing.");
            }
            uint count = BinaryPrimitives.ReadUInt32BigEndian(profile.Slice(128, 4));
            if (count > (profile.Length - 132) / 12)
            {
                throw new InvalidDataException("ICC tag table extends beyond the profile.");
            }
            uint firstTagData = checked(132u + count * 12u);
            HashSet<string> signatures = new(StringComparer.Ordinal);
            bool found = false;
            tag = default;
            for (uint index = 0; index < count; index++)
            {
                int entry = checked(132 + (int)index * 12);
                string signature = ReadSignature(profile.Slice(entry, 4));
                if (!signatures.Add(signature))
                {
                    throw new InvalidDataException($"ICC tag '{signature}' is duplicated.");
                }
                uint offset = BinaryPrimitives.ReadUInt32BigEndian(profile.Slice(entry + 4, 4));
                uint size = BinaryPrimitives.ReadUInt32BigEndian(profile.Slice(entry + 8, 4));
                if (offset % 4 != 0 || offset < firstTagData || size == 0 ||
                    offset > (uint)profile.Length || size > (uint)profile.Length - offset)
                {
                    throw new InvalidDataException($"ICC tag '{signature}' has an invalid range.");
                }
                if (signature != requested)
                {
                    continue;
                }
                found = true;
                tag = profile.Slice(checked((int)offset), checked((int)size));
            }
            return found;
        }

        private static (float X, float Y, float Z) LabToXyzD50(float l, float a, float b)
        {
            float fy = (l + 16f) / 116f;
            float fx = fy + a / 500f;
            float fz = fy - b / 200f;
            return (
                0.9642f * InverseLabFunction(fx),
                InverseLabFunction(fy),
                0.8249f * InverseLabFunction(fz));
        }

        private static float InverseLabFunction(float value)
        {
            const float epsilon = 216f / 24389f;
            const float kappa = 24389f / 27f;
            float cube = value * value * value;
            return cube > epsilon ? cube : (116f * value - 16f) / kappa;
        }

        private static int Cube(int value) => checked(value * value * value);

        private static float ReadS15Fixed16(ReadOnlySpan<byte> value) =>
            BinaryPrimitives.ReadInt32BigEndian(value) / 65536f;

        private static string ReadSignature(ReadOnlySpan<byte> value)
        {
            foreach (byte component in value)
            {
                if (component is < 0x20 or > 0x7e)
                {
                    throw new InvalidDataException("ICC tag contains a non-printable signature.");
                }
            }
            return Encoding.ASCII.GetString(value);
        }

        private static void EnsureZero(ReadOnlySpan<byte> value)
        {
            foreach (byte component in value)
            {
                if (component != 0)
                {
                    throw new InvalidDataException("ICC reserved bytes must be zero.");
                }
            }
        }

        private static void RequireExactLength(
            ReadOnlySpan<byte> tag,
            int expected,
            string type)
        {
            if (tag.Length != expected)
            {
                throw new InvalidDataException(
                    $"ICC {type} length {tag.Length} does not match its declared table dimensions ({expected}).");
            }
        }

        private static void ValidateComponent(float value, string name)
        {
            if (!float.IsFinite(value) || value is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(name, "ICC encoded components must be in [0, 1].");
            }
        }

        private readonly record struct LutHeader(
            int GridPoints,
            LegacyMatrix3x3 InputMatrix);

        private readonly record struct LegacyMatrix3x3(
            float M11,
            float M12,
            float M13,
            float M21,
            float M22,
            float M23,
            float M31,
            float M32,
            float M33)
        {
            internal (float First, float Second, float Third) TransformAndClip(
                float first,
                float second,
                float third) =>
                (
                    Math.Clamp(M11 * first + M12 * second + M13 * third, 0f, 1f),
                    Math.Clamp(M21 * first + M22 * second + M23 * third, 0f, 1f),
                    Math.Clamp(M31 * first + M32 * second + M33 * third, 0f, 1f));
        }

        private enum PcsEncoding
        {
            Xyz16,
            LegacyLab16,
            Lab8,
        }
    }
}

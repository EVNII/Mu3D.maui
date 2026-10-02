using System.Buffers.Binary;
using System.Text;

namespace Mu3D.Color;

/// <summary>
/// Applies the media-relative colorimetric device-to-PCS transform of an RGB ICC v2/v4
/// matrix/TRC profile and converts the resulting XYZ D50 value to a standard linear working space.
/// </summary>
/// <remarks>
/// This transform deliberately supports only <c>rXYZ/gXYZ/bXYZ</c> plus
/// <c>rTRC/gTRC/bTRC</c> profiles. LUT, MPE, perceptual, saturation, absolute-colorimetric and
/// black-point-compensated transforms require a later full CMM path and fail closed here.
/// </remarks>
public sealed class IccMatrixTrcTransform
{
    private readonly Matrix3x3 deviceToPcs;
    private readonly ToneCurve redCurve;
    private readonly ToneCurve greenCurve;
    private readonly ToneCurve blueCurve;

    /// <summary>Parses the required matrix and tone-curve tags from an immutable ICC profile.</summary>
    /// <param name="profile">An RGB ICC v2/v4 profile using XYZ PCS.</param>
    public IccMatrixTrcTransform(IccProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ProfileConnectionSpace != "XYZ ")
        {
            throw new NotSupportedException(
                "Matrix/TRC ICC conversion requires XYZ PCS; Lab PCS requires a LUT transform.");
        }

        ReadOnlySpan<byte> data = profile.AsSpan();
        Dictionary<string, TagEntry> tags = ReadTagTable(data);
        Vector3 red = ReadXyzTag(data, GetRequiredTag(tags, "rXYZ"));
        Vector3 green = ReadXyzTag(data, GetRequiredTag(tags, "gXYZ"));
        Vector3 blue = ReadXyzTag(data, GetRequiredTag(tags, "bXYZ"));
        deviceToPcs = new Matrix3x3(
            red.X, green.X, blue.X,
            red.Y, green.Y, blue.Y,
            red.Z, green.Z, blue.Z);
        if (!deviceToPcs.IsInvertible())
        {
            throw new InvalidDataException("ICC matrix colorants form a singular transform.");
        }
        redCurve = ReadToneCurve(data, GetRequiredTag(tags, "rTRC"));
        greenCurve = ReadToneCurve(data, GetRequiredTag(tags, "gTRC"));
        blueCurve = ReadToneCurve(data, GetRequiredTag(tags, "bTRC"));
    }

    /// <summary>
    /// Converts one unpremultiplied encoded RGB sample through the profile's relative-colorimetric
    /// device-to-PCS transform into a standard linear-light destination.
    /// </summary>
    /// <param name="red">The encoded red component in the inclusive zero-to-one range.</param>
    /// <param name="green">The encoded green component in the inclusive zero-to-one range.</param>
    /// <param name="blue">The encoded blue component in the inclusive zero-to-one range.</param>
    /// <param name="alpha">The unpremultiplied alpha component in the inclusive zero-to-one range.</param>
    /// <param name="destination">The destination standard linear RGB identity.</param>
    /// <returns>The transformed color without gamut mapping or clipping.</returns>
    public LinearRgba TransformEncodedRgb(
        float red,
        float green,
        float blue,
        float alpha,
        StandardRgbColorSpaceReference destination)
    {
        ValidateEncoded(red, nameof(red));
        ValidateEncoded(green, nameof(green));
        ValidateEncoded(blue, nameof(blue));
        Vector3 pcs = deviceToPcs.Transform(new Vector3(
            redCurve.Evaluate(red),
            greenCurve.Evaluate(green),
            blueCurve.Evaluate(blue)));
        return StandardLinearRgbConverter.FromXyzD50(
            pcs.X,
            pcs.Y,
            pcs.Z,
            alpha,
            destination);
    }

    private static Dictionary<string, TagEntry> ReadTagTable(ReadOnlySpan<byte> profile)
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
        uint firstTagDataOffset = checked(132u + count * 12u);
        Dictionary<string, TagEntry> tags = new(StringComparer.Ordinal);
        for (uint index = 0; index < count; index++)
        {
            int entryOffset = checked(132 + (int)index * 12);
            string signature = ReadSignature(profile.Slice(entryOffset, 4));
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(profile.Slice(entryOffset + 4, 4));
            uint size = BinaryPrimitives.ReadUInt32BigEndian(profile.Slice(entryOffset + 8, 4));
            if (offset % 4 != 0 || offset < firstTagDataOffset || size == 0 ||
                offset > (uint)profile.Length ||
                size > (uint)profile.Length - offset)
            {
                throw new InvalidDataException($"ICC tag '{signature}' has an invalid range.");
            }
            if (!tags.TryAdd(signature, new TagEntry(checked((int)offset), checked((int)size))))
            {
                throw new InvalidDataException($"ICC tag '{signature}' is duplicated.");
            }
        }
        return tags;
    }

    private static Vector3 ReadXyzTag(ReadOnlySpan<byte> profile, TagEntry entry)
    {
        ReadOnlySpan<byte> tag = profile.Slice(entry.Offset, entry.Size);
        if (tag.Length < 20 || !tag[..4].SequenceEqual("XYZ "u8))
        {
            throw new InvalidDataException("ICC matrix column is not a complete XYZType value.");
        }
        EnsureReservedZero(tag.Slice(4, 4));
        return new Vector3(
            ReadS15Fixed16(tag.Slice(8, 4)),
            ReadS15Fixed16(tag.Slice(12, 4)),
            ReadS15Fixed16(tag.Slice(16, 4)));
    }

    private static ToneCurve ReadToneCurve(ReadOnlySpan<byte> profile, TagEntry entry)
    {
        ReadOnlySpan<byte> tag = profile.Slice(entry.Offset, entry.Size);
        if (tag.Length < 12)
        {
            throw new InvalidDataException("ICC tone-curve tag is truncated.");
        }
        EnsureReservedZero(tag.Slice(4, 4));
        if (tag[..4].SequenceEqual("curv"u8))
        {
            uint count = BinaryPrimitives.ReadUInt32BigEndian(tag.Slice(8, 4));
            if (count > (tag.Length - 12) / 2)
            {
                throw new InvalidDataException("ICC curveType samples extend beyond the tag.");
            }
            if (count == 0)
            {
                return ToneCurve.Identity;
            }
            if (count == 1)
            {
                float gamma = BinaryPrimitives.ReadUInt16BigEndian(tag.Slice(12, 2)) / 256f;
                if (gamma <= 0f)
                {
                    throw new InvalidDataException("ICC curveType gamma must be positive.");
                }
                return ToneCurve.Parametric(0, [gamma]);
            }
            float[] samples = new float[checked((int)count)];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = BinaryPrimitives.ReadUInt16BigEndian(
                    tag.Slice(12 + index * 2, 2)) / 65535f;
                if (index != 0 && samples[index] < samples[index - 1])
                {
                    throw new InvalidDataException("ICC curveType samples must be non-decreasing.");
                }
            }
            return ToneCurve.Sampled(samples);
        }
        if (!tag[..4].SequenceEqual("para"u8))
        {
            throw new NotSupportedException(
                $"ICC tone-curve type '{ReadSignature(tag[..4])}' is not curveType or parametricCurveType.");
        }
        ushort functionType = BinaryPrimitives.ReadUInt16BigEndian(tag.Slice(8, 2));
        EnsureReservedZero(tag.Slice(10, 2));
        int parameterCount = functionType switch
        {
            0 => 1,
            1 => 3,
            2 => 4,
            3 => 5,
            4 => 7,
            _ => throw new NotSupportedException(
                $"ICC parametricCurveType function {functionType} is not defined by ICC v2/v4."),
        };
        if (tag.Length < 12 + parameterCount * 4)
        {
            throw new InvalidDataException("ICC parametricCurveType parameters are truncated.");
        }
        float[] parameters = new float[parameterCount];
        for (int index = 0; index < parameters.Length; index++)
        {
            parameters[index] = ReadS15Fixed16(tag.Slice(12 + index * 4, 4));
        }
        if (parameters[0] <= 0f || (functionType is >= 1 and <= 4 && parameters[1] == 0f))
        {
            throw new InvalidDataException("ICC parametricCurveType has invalid gamma or scale.");
        }
        return ToneCurve.Parametric(functionType, parameters);
    }

    private static TagEntry GetRequiredTag(IReadOnlyDictionary<string, TagEntry> tags, string signature) =>
        tags.TryGetValue(signature, out TagEntry entry)
            ? entry
            : throw new NotSupportedException(
                $"ICC matrix/TRC profile is missing required tag '{signature}'.");

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

    private static void EnsureReservedZero(ReadOnlySpan<byte> value)
    {
        foreach (byte component in value)
        {
            if (component != 0)
            {
                throw new InvalidDataException("ICC reserved bytes must be zero.");
            }
        }
    }

    private static void ValidateEncoded(float value, string name)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(name, "Encoded ICC components must be in [0, 1].");
        }
    }

    private readonly record struct TagEntry(int Offset, int Size);

    private readonly record struct Vector3(float X, float Y, float Z);

    private readonly record struct Matrix3x3(
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
        internal Vector3 Transform(Vector3 value) => new(
            M11 * value.X + M12 * value.Y + M13 * value.Z,
            M21 * value.X + M22 * value.Y + M23 * value.Z,
            M31 * value.X + M32 * value.Y + M33 * value.Z);

        internal bool IsInvertible()
        {
            float determinant = M11 * (M22 * M33 - M23 * M32) -
                M12 * (M21 * M33 - M23 * M31) +
                M13 * (M21 * M32 - M22 * M31);
            return float.IsFinite(determinant) && MathF.Abs(determinant) >= 1e-8f;
        }
    }

    private readonly struct ToneCurve
    {
        private readonly ushort functionType;
        private readonly float[]? parameters;
        private readonly float[]? samples;

        private ToneCurve(ushort functionType, float[]? parameters, float[]? samples)
        {
            this.functionType = functionType;
            this.parameters = parameters;
            this.samples = samples;
        }

        internal static ToneCurve Identity { get; } = new(0, [1f], null);

        internal static ToneCurve Parametric(ushort functionType, float[] parameters) =>
            new(functionType, parameters, null);

        internal static ToneCurve Sampled(float[] samples) => new(0, null, samples);

        internal float Evaluate(float encoded)
        {
            if (samples is not null)
            {
                float position = encoded * (samples.Length - 1);
                int lower = Math.Min((int)position, samples.Length - 1);
                int upper = Math.Min(lower + 1, samples.Length - 1);
                return samples[lower] +
                    (samples[upper] - samples[lower]) * (position - lower);
            }
            float[] values = parameters!;
            float g = values[0];
            float decoded = functionType switch
            {
                0 => MathF.Pow(encoded, g),
                1 => encoded >= -values[2] / values[1]
                    ? MathF.Pow(values[1] * encoded + values[2], g)
                    : 0f,
                2 => encoded >= -values[2] / values[1]
                    ? MathF.Pow(values[1] * encoded + values[2], g) + values[3]
                    : values[3],
                3 => encoded >= values[4]
                    ? MathF.Pow(values[1] * encoded + values[2], g)
                    : values[3] * encoded,
                4 => encoded >= values[4]
                    ? MathF.Pow(values[1] * encoded + values[2], g) + values[5]
                    : values[3] * encoded + values[6],
                _ => throw new InvalidOperationException("Unknown parsed ICC tone curve."),
            };
            if (!float.IsFinite(decoded))
            {
                throw new InvalidDataException(
                    "ICC tone-curve parameters produced a non-finite component.");
            }
            return decoded;
        }
    }
}

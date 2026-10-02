using System.Buffers.Binary;
using System.Text;

namespace Mu3D.Color;

internal sealed class IccMultiProcessElementsTransform
{
    private const int EndpointChannelCount = 3;
    private const int MaxChannelCount = 16;
    private const int MaxClutValueCount = 16_777_216;

    private readonly ProcessingElement[] elements;
    private readonly bool pcsIsXyz;

    private IccMultiProcessElementsTransform(ProcessingElement[] elements, bool pcsIsXyz)
    {
        this.elements = elements;
        this.pcsIsXyz = pcsIsXyz;
    }

    internal static bool TryCreate(
        IccProfile profile,
        string tagSignature,
        out IccMultiProcessElementsTransform? transform)
    {
        if (!TryFindTag(profile.AsSpan(), tagSignature, out ReadOnlySpan<byte> tag))
        {
            transform = null;
            return false;
        }
        if (tag.Length < 16)
        {
            throw new InvalidDataException($"ICC {tagSignature} multi-process header is truncated.");
        }
        if (ReadSignature(tag[..4]) != "mpet")
        {
            throw new InvalidDataException($"ICC {tagSignature} must use multiProcessElementsType.");
        }
        EnsureZero(tag.Slice(4, 4));
        (int inputChannels, int outputChannels) = ReadChannels(tag, "multiProcessElementsType");
        if (inputChannels != EndpointChannelCount || outputChannels != EndpointChannelCount)
        {
            throw new NotSupportedException(
                $"ICC RGB multiProcessElementsType requires three endpoint channels, not " +
                $"{inputChannels} and {outputChannels}.");
        }
        uint elementCountValue = BinaryPrimitives.ReadUInt32BigEndian(tag.Slice(12, 4));
        if (elementCountValue is 0 or > 1024)
        {
            throw new InvalidDataException("ICC multiProcessElementsType has an invalid element count.");
        }
        int elementCount = checked((int)elementCountValue);
        int tableEnd = checked(16 + elementCount * 8);
        if (tableEnd > tag.Length)
        {
            throw new InvalidDataException("ICC multi-process element position table is truncated.");
        }

        ProcessingElement[] elements = new ProcessingElement[elementCount];
        for (int index = 0; index < elementCount; index++)
        {
            int entry = 16 + index * 8;
            uint offsetValue = BinaryPrimitives.ReadUInt32BigEndian(tag.Slice(entry, 4));
            uint sizeValue = BinaryPrimitives.ReadUInt32BigEndian(tag.Slice(entry + 4, 4));
            if (offsetValue % 4 != 0 || offsetValue < tableEnd || sizeValue < 12 ||
                offsetValue > (uint)tag.Length || sizeValue > (uint)tag.Length - offsetValue)
            {
                throw new InvalidDataException(
                    $"ICC multi-process element {index} has an invalid position or size.");
            }
            if (index == 0 && offsetValue != tableEnd)
            {
                throw new InvalidDataException(
                    "ICC first multi-process element does not immediately follow its position table.");
            }
            elements[index] = ParseElement(
                tag.Slice(checked((int)offsetValue), checked((int)sizeValue)));
        }

        int expectedInputChannels = inputChannels;
        for (int index = 0; index < elements.Length; index++)
        {
            ProcessingElement element = elements[index];
            if (element.InputChannels != expectedInputChannels)
            {
                throw new InvalidDataException(
                    $"ICC multi-process element {index} expects {element.InputChannels} channels " +
                    $"after an element that produces {expectedInputChannels}.");
            }
            expectedInputChannels = element.OutputChannels;
        }
        if (expectedInputChannels != outputChannels)
        {
            throw new InvalidDataException(
                $"ICC multi-process chain produces {expectedInputChannels} channels instead of " +
                $"the declared {outputChannels}.");
        }

        string pcs = profile.ProfileConnectionSpace;
        if (pcs is not ("XYZ " or "Lab "))
        {
            throw new NotSupportedException($"ICC multi-process PCS '{pcs}' is not supported.");
        }
        transform = new IccMultiProcessElementsTransform(elements, pcs == "XYZ ");
        return true;
    }

    internal LinearRgba TransformEncodedRgb(
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
        ArgumentNullException.ThrowIfNull(destination);

        (float first, float second, float third) = TransformThreeChannels(red, green, blue);
        (float x, float y, float z) = pcsIsXyz
            ? (first, second, third)
            : LabToXyzD50(first, second, third);
        return StandardLinearRgbConverter.FromXyzD50(x, y, z, alpha, destination);
    }

    internal (float Red, float Green, float Blue) TransformPcsToEncodedRgb(
        float x,
        float y,
        float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "PCS XYZ components must be finite.");
        }
        (float first, float second, float third) = pcsIsXyz
            ? (x, y, z)
            : XyzD50ToLab(x, y, z);
        return TransformThreeChannels(first, second, third);
    }

    private (float First, float Second, float Third) TransformThreeChannels(
        float firstValue,
        float secondValue,
        float thirdValue)
    {
        Span<float> first = stackalloc float[MaxChannelCount];
        Span<float> second = stackalloc float[MaxChannelCount];
        first[0] = firstValue;
        first[1] = secondValue;
        first[2] = thirdValue;
        bool useFirst = true;
        foreach (ProcessingElement element in elements)
        {
            ReadOnlySpan<float> input = (useFirst ? first : second)[..element.InputChannels];
            Span<float> output = (useFirst ? second : first)[..element.OutputChannels];
            element.Apply(input, output);
            useFirst = !useFirst;
        }
        ReadOnlySpan<float> result = useFirst ? first : second;
        return (result[0], result[1], result[2]);
    }

    private static ProcessingElement ParseElement(ReadOnlySpan<byte> data)
    {
        string signature = ReadSignature(data[..4]);
        EnsureZero(data.Slice(4, 4));
        (int inputChannels, int outputChannels) = ReadChannels(data, signature);
        return signature switch
        {
            "cvst" => CurveSetElement.Parse(data, inputChannels, outputChannels),
            "matf" => MatrixElement.Parse(data, inputChannels, outputChannels),
            "clut" => ClutElement.Parse(data, inputChannels, outputChannels),
            "bACS" or "eACS" => PassThroughElement.Parse(data, inputChannels, outputChannels),
            _ => throw new NotSupportedException(
                $"ICC multi-process element '{signature}' is not supported."),
        };
    }

    private abstract class ProcessingElement
    {
        protected ProcessingElement(int inputChannels, int outputChannels)
        {
            InputChannels = inputChannels;
            OutputChannels = outputChannels;
        }

        internal int InputChannels { get; }

        internal int OutputChannels { get; }

        internal abstract void Apply(ReadOnlySpan<float> input, Span<float> output);
    }

    private sealed class PassThroughElement : ProcessingElement
    {
        private PassThroughElement(int channels) : base(channels, channels)
        {
        }

        internal static PassThroughElement Parse(
            ReadOnlySpan<byte> data,
            int inputChannels,
            int outputChannels)
        {
            RequireExactLength(data, 12, "pass-through element");
            RequireEqualChannels(inputChannels, outputChannels, "pass-through element");
            return new PassThroughElement(inputChannels);
        }

        internal override void Apply(ReadOnlySpan<float> input, Span<float> output) =>
            input.CopyTo(output);
    }

    private sealed class MatrixElement : ProcessingElement
    {
        private readonly float[] values;

        private MatrixElement(int inputChannels, int outputChannels, float[] values)
            : base(inputChannels, outputChannels) => this.values = values;

        internal static MatrixElement Parse(
            ReadOnlySpan<byte> data,
            int inputChannels,
            int outputChannels)
        {
            int valueCount = checked(outputChannels * (inputChannels + 1));
            RequireExactLength(data, checked(12 + valueCount * 4), "matrix element");
            float[] values = new float[valueCount];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = ReadStoredFloat(data.Slice(12 + index * 4, 4));
            }
            return new MatrixElement(inputChannels, outputChannels, values);
        }

        internal override void Apply(ReadOnlySpan<float> input, Span<float> output)
        {
            int offsetStart = checked(InputChannels * OutputChannels);
            for (int row = 0; row < OutputChannels; row++)
            {
                int coefficient = row * InputChannels;
                float result = values[offsetStart + row];
                for (int column = 0; column < InputChannels; column++)
                {
                    result += values[coefficient + column] * input[column];
                }
                output[row] = RequireComputedFloat(result, "matrix element");
            }
        }
    }

    private sealed class CurveSetElement : ProcessingElement
    {
        private readonly SegmentedCurve[] curves;

        private CurveSetElement(SegmentedCurve[] curves) : base(curves.Length, curves.Length) =>
            this.curves = curves;

        internal static CurveSetElement Parse(
            ReadOnlySpan<byte> data,
            int inputChannels,
            int outputChannels)
        {
            RequireEqualChannels(inputChannels, outputChannels, "curve-set element");
            int tableEnd = checked(12 + inputChannels * 8);
            if (data.Length < tableEnd)
            {
                throw new InvalidDataException("ICC curve-set position table is truncated.");
            }
            SegmentedCurve[] curves = new SegmentedCurve[inputChannels];
            for (int channel = 0; channel < curves.Length; channel++)
            {
                int entry = 12 + channel * 8;
                uint offsetValue = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(entry, 4));
                uint sizeValue = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(entry + 4, 4));
                if (offsetValue % 4 != 0 || offsetValue < tableEnd || sizeValue < 24 ||
                    offsetValue > (uint)data.Length || sizeValue > (uint)data.Length - offsetValue)
                {
                    throw new InvalidDataException(
                        $"ICC curve-set channel {channel} has an invalid position or size.");
                }
                if (channel == 0 && offsetValue != tableEnd)
                {
                    throw new InvalidDataException(
                        "ICC first segmented curve does not immediately follow its position table.");
                }
                curves[channel] = SegmentedCurve.Parse(
                    data.Slice(checked((int)offsetValue), checked((int)sizeValue)));
            }
            return new CurveSetElement(curves);
        }

        internal override void Apply(ReadOnlySpan<float> input, Span<float> output)
        {
            for (int channel = 0; channel < curves.Length; channel++)
            {
                output[channel] = curves[channel].Evaluate(input[channel]);
            }
        }
    }

    private sealed class SegmentedCurve
    {
        private readonly float[] breakPoints;
        private readonly CurveSegment[] segments;

        private SegmentedCurve(float[] breakPoints, CurveSegment[] segments)
        {
            this.breakPoints = breakPoints;
            this.segments = segments;
        }

        internal static SegmentedCurve Parse(ReadOnlySpan<byte> data)
        {
            if (data.Length < 24 || ReadSignature(data[..4]) != "curf")
            {
                throw new InvalidDataException("ICC segmented curve is truncated or has the wrong signature.");
            }
            EnsureZero(data.Slice(4, 4));
            ushort segmentCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(8, 2));
            EnsureZero(data.Slice(10, 2));
            if (segmentCount == 0)
            {
                throw new InvalidDataException("ICC segmented curve must contain at least one segment.");
            }
            int segmentOffset = checked(12 + (segmentCount - 1) * 4);
            if (segmentOffset > data.Length)
            {
                throw new InvalidDataException("ICC segmented curve break-point table is truncated.");
            }
            float[] breakPoints = new float[segmentCount - 1];
            for (int index = 0; index < breakPoints.Length; index++)
            {
                breakPoints[index] = ReadStoredFloat(data.Slice(12 + index * 4, 4));
                if (index != 0 && breakPoints[index] < breakPoints[index - 1])
                {
                    throw new InvalidDataException(
                        "ICC segmented curve break-points must be non-decreasing.");
                }
            }

            CurveSegment[] segments = new CurveSegment[segmentCount];
            int cursor = segmentOffset;
            for (int index = 0; index < segments.Length; index++)
            {
                if (cursor > data.Length - 12)
                {
                    throw new InvalidDataException("ICC curve segment is truncated.");
                }
                string signature = ReadSignature(data.Slice(cursor, 4));
                (segments[index], int length) = signature switch
                {
                    "parf" => FormulaSegment.Parse(data[cursor..]),
                    "samf" => SampledSegment.Parse(data[cursor..]),
                    _ => throw new NotSupportedException(
                        $"ICC curve segment '{signature}' is not supported."),
                };
                cursor = checked(cursor + length);
            }
            if (cursor != data.Length)
            {
                throw new InvalidDataException("ICC segmented curve size does not match its segments.");
            }
            if (segments[0] is not FormulaSegment || segments[^1] is not FormulaSegment)
            {
                throw new InvalidDataException(
                    "ICC first and last segmented-curve elements must use formulas.");
            }
            return new SegmentedCurve(breakPoints, segments);
        }

        internal float Evaluate(float input)
        {
            int segment = 0;
            while (segment < breakPoints.Length && input > breakPoints[segment])
            {
                segment++;
            }
            return EvaluateSegment(segment, input);
        }

        private float EvaluateSegment(int index, float input)
        {
            if (segments[index] is FormulaSegment formula)
            {
                return formula.Evaluate(input);
            }
            SampledSegment sampled = (SampledSegment)segments[index];
            float lower = breakPoints[index - 1];
            float upper = breakPoints[index];
            float lowerValue = EvaluateSegment(index - 1, lower);
            return sampled.Evaluate(input, lower, upper, lowerValue);
        }
    }

    private abstract class CurveSegment
    {
    }

    private sealed class FormulaSegment : CurveSegment
    {
        private readonly ushort function;
        private readonly float[] parameters;

        private FormulaSegment(ushort function, float[] parameters)
        {
            this.function = function;
            this.parameters = parameters;
        }

        internal static (CurveSegment Segment, int Length) Parse(ReadOnlySpan<byte> data)
        {
            EnsureZero(data.Slice(4, 4));
            ushort function = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(8, 2));
            EnsureZero(data.Slice(10, 2));
            int parameterCount = function switch
            {
                0 => 4,
                1 or 2 => 5,
                _ => throw new NotSupportedException(
                    $"ICC formula curve function {function} is not defined."),
            };
            int length = checked(12 + parameterCount * 4);
            if (data.Length < length)
            {
                throw new InvalidDataException("ICC formula curve parameters are truncated.");
            }
            float[] parameters = new float[parameterCount];
            for (int index = 0; index < parameters.Length; index++)
            {
                parameters[index] = ReadStoredFloat(data.Slice(12 + index * 4, 4));
            }
            return (new FormulaSegment(function, parameters), length);
        }

        internal float Evaluate(float input)
        {
            float result = function switch
            {
                0 => MathF.Pow(parameters[1] * input + parameters[2], parameters[0]) + parameters[3],
                1 => parameters[1] * MathF.Log10(
                    parameters[2] * MathF.Pow(input, parameters[0]) + parameters[3]) + parameters[4],
                2 => parameters[0] * MathF.Pow(
                    parameters[1],
                    parameters[2] * input + parameters[3]) + parameters[4],
                _ => throw new InvalidOperationException("Unknown parsed ICC formula curve."),
            };
            return RequireComputedFloat(result, "formula curve");
        }
    }

    private sealed class SampledSegment : CurveSegment
    {
        private readonly float[] samples;

        private SampledSegment(float[] samples) => this.samples = samples;

        internal static (CurveSegment Segment, int Length) Parse(ReadOnlySpan<byte> data)
        {
            EnsureZero(data.Slice(4, 4));
            uint countValue = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4));
            if (countValue is 0 or > 1_048_576)
            {
                throw new InvalidDataException("ICC sampled curve has an invalid sample count.");
            }
            int count = checked((int)countValue);
            int length = checked(12 + count * 4);
            if (data.Length < length)
            {
                throw new InvalidDataException("ICC sampled curve entries are truncated.");
            }
            float[] samples = new float[count];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = ReadStoredFloat(data.Slice(12 + index * 4, 4));
            }
            return (new SampledSegment(samples), length);
        }

        internal float Evaluate(float input, float lower, float upper, float lowerValue)
        {
            if (!(upper > lower))
            {
                return samples[^1];
            }
            float position = Math.Clamp((input - lower) / (upper - lower), 0f, 1f) * samples.Length;
            int interval = Math.Min((int)position, samples.Length - 1);
            float start = interval == 0 ? lowerValue : samples[interval - 1];
            float end = samples[interval];
            return RequireComputedFloat(start + (end - start) * (position - interval), "sampled curve");
        }
    }

    private sealed class ClutElement : ProcessingElement
    {
        private readonly int[] gridPoints;
        private readonly float[] values;

        private ClutElement(
            int inputChannels,
            int outputChannels,
            int[] gridPoints,
            float[] values)
            : base(inputChannels, outputChannels)
        {
            this.gridPoints = gridPoints;
            this.values = values;
        }

        internal static ClutElement Parse(
            ReadOnlySpan<byte> data,
            int inputChannels,
            int outputChannels)
        {
            if (data.Length < 28)
            {
                throw new InvalidDataException("ICC float CLUT header is truncated.");
            }
            int[] gridPoints = new int[inputChannels];
            long sampleCount = 1;
            for (int dimension = 0; dimension < inputChannels; dimension++)
            {
                int count = data[12 + dimension];
                if (count < 2)
                {
                    throw new InvalidDataException(
                        "ICC float CLUT requires at least two points per input dimension.");
                }
                gridPoints[dimension] = count;
                sampleCount = checked(sampleCount * count);
            }
            EnsureZero(data.Slice(12 + inputChannels, 16 - inputChannels));
            long valueCountValue = checked(sampleCount * outputChannels);
            if (valueCountValue > MaxClutValueCount)
            {
                throw new InvalidDataException(
                    $"ICC float CLUT contains {valueCountValue} values, exceeding the " +
                    $"{MaxClutValueCount} value safety limit.");
            }
            int valueCount = checked((int)valueCountValue);
            RequireExactLength(data, checked(28 + valueCount * 4), "float CLUT element");
            float[] values = new float[valueCount];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = ReadStoredFloat(data.Slice(28 + index * 4, 4));
            }
            return new ClutElement(inputChannels, outputChannels, gridPoints, values);
        }

        internal override void Apply(ReadOnlySpan<float> input, Span<float> output)
        {
            Span<int> coordinates = stackalloc int[MaxChannelCount];
            Span<int> sortedDimensions = stackalloc int[MaxChannelCount];
            Span<float> fractions = stackalloc float[MaxChannelCount];
            for (int dimension = 0; dimension < InputChannels; dimension++)
            {
                float position = Math.Clamp(input[dimension], 0f, 1f) *
                    (gridPoints[dimension] - 1);
                coordinates[dimension] = Math.Min((int)position, gridPoints[dimension] - 1);
                fractions[dimension] = position - coordinates[dimension];
                sortedDimensions[dimension] = dimension;
            }
            for (int index = 1; index < InputChannels; index++)
            {
                int dimension = sortedDimensions[index];
                int cursor = index;
                while (cursor > 0 &&
                    fractions[sortedDimensions[cursor - 1]] < fractions[dimension])
                {
                    sortedDimensions[cursor] = sortedDimensions[cursor - 1];
                    cursor--;
                }
                sortedDimensions[cursor] = dimension;
            }

            Span<float> previous = stackalloc float[MaxChannelCount];
            Sample(coordinates, previous);
            previous[..OutputChannels].CopyTo(output);
            for (int index = 0; index < InputChannels; index++)
            {
                int dimension = sortedDimensions[index];
                coordinates[dimension] = Math.Min(
                    coordinates[dimension] + 1,
                    gridPoints[dimension] - 1);
                int sampleOffset = GetSampleOffset(coordinates);
                float weight = fractions[dimension];
                for (int channel = 0; channel < OutputChannels; channel++)
                {
                    float next = values[sampleOffset + channel];
                    output[channel] += weight * (next - previous[channel]);
                    previous[channel] = next;
                }
            }
            for (int channel = 0; channel < OutputChannels; channel++)
            {
                output[channel] = RequireComputedFloat(output[channel], "float CLUT element");
            }
        }

        private void Sample(ReadOnlySpan<int> coordinates, Span<float> destination)
        {
            int offset = GetSampleOffset(coordinates);
            values.AsSpan(offset, OutputChannels).CopyTo(destination);
        }

        private int GetSampleOffset(ReadOnlySpan<int> coordinates)
        {
            int index = 0;
            for (int dimension = 0; dimension < InputChannels; dimension++)
            {
                index = checked(index * gridPoints[dimension] + coordinates[dimension]);
            }
            return checked(index * OutputChannels);
        }
    }

    private static (int InputChannels, int OutputChannels) ReadChannels(
        ReadOnlySpan<byte> data,
        string name)
    {
        int inputChannels = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(8, 2));
        int outputChannels = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(10, 2));
        if (inputChannels is < 1 or > MaxChannelCount ||
            outputChannels is < 1 or > MaxChannelCount)
        {
            throw new InvalidDataException(
                $"ICC {name} channel counts {inputChannels} and {outputChannels} must each be " +
                $"between 1 and {MaxChannelCount}.");
        }
        return (inputChannels, outputChannels);
    }

    private static void RequireEqualChannels(int inputChannels, int outputChannels, string name)
    {
        if (inputChannels != outputChannels)
        {
            throw new InvalidDataException(
                $"ICC {name} requires equal input and output channel counts, not " +
                $"{inputChannels} and {outputChannels}.");
        }
    }

    private static float ReadStoredFloat(ReadOnlySpan<byte> data)
    {
        float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(data));
        if (!float.IsFinite(value) || (value != 0f && !float.IsNormal(value)))
        {
            throw new InvalidDataException("ICC stored float must be finite and normalized or zero.");
        }
        return value;
    }

    private static float RequireComputedFloat(float value, string source)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException($"ICC {source} produced a non-finite value.");
        }
        return value;
    }

    private static void RequireExactLength(ReadOnlySpan<byte> data, int expected, string name)
    {
        if (data.Length != expected)
        {
            throw new InvalidDataException(
                $"ICC {name} length {data.Length} does not equal its required length {expected}.");
        }
    }

    private static void ValidateComponent(float value, string name)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(name, "ICC encoded components must be in [0, 1].");
        }
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

    private static float InverseLabFunction(float value)
    {
        const float epsilon = 216f / 24389f;
        const float kappa = 24389f / 27f;
        float cube = value * value * value;
        return cube > epsilon ? cube : (116f * value - 16f) / kappa;
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
        tag = default;
        bool found = false;
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
            if (signature == requested)
            {
                found = true;
                tag = profile.Slice(checked((int)offset), checked((int)size));
            }
        }
        return found;
    }

    private static string ReadSignature(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            if (value is < 0x20 or > 0x7e)
            {
                throw new InvalidDataException("ICC data contains a non-printable signature.");
            }
        }
        return Encoding.ASCII.GetString(data);
    }

    private static void EnsureZero(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            if (value != 0)
            {
                throw new InvalidDataException("ICC reserved bytes must be zero.");
            }
        }
    }
}

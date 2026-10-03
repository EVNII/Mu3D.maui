using System.Runtime.InteropServices;
using Mu3D.Color;

internal static class ColorBatchTests
{
    internal static void Run()
    {
        int checks = 0;
        var space = StandardColorSpaces.LinearSrgb;
        LinearRgba[] input = [new(-.1f, .5f, 4, 0, space), new(8, 2, .18f, .37f, space)];
        var lab = new CieLabColor[3];
        lab[2] = new(19, 2, 3);
        ColorBatch.Transform<LinearRgba, CieLabColor, LinearRgbToLabTransform>(input, lab, default);
        Check(lab[0] == PerceptualColorConverter.ToLab(input[0]) && lab[1] == PerceptualColorConverter.ToLab(input[1]), "Lab batch");
        Check(lab[2] == new CieLabColor(19, 2, 3), "Destination tail retained");
        var restored = new LinearRgba[2];
        ColorBatch.Transform<CieLabColor, LinearRgba, LabToLinearRgbTransform>(lab.AsSpan(0, 2), restored, new(space));
        for (int i = 0; i < input.Length; i++) RgbNear(input[i], restored[i]);
        var ok = new OklabColor[2];
        ColorBatch.Transform<LinearRgba, OklabColor, LinearRgbToOklabTransform>(input, ok, default);
        Check(ok[0] == PerceptualColorConverter.ToOklab(input[0]), "Oklab batch");
        ColorBatch.Transform<OklabColor, LinearRgba, OklabToLinearRgbTransform>(ok, restored, new(space));
        for (int i = 0; i < input.Length; i++) RgbNear(input[i], restored[i]);

        StandardEncodedRgba[] encoded = [new(-.2f, .4f, 3, .25f, StandardRgbEncoding.DisplayP3)];
        var chroma = new LumaChromaColor[1];
        var returned = new StandardEncodedRgba[1];
        ColorBatch.Transform<StandardEncodedRgba, LumaChromaColor, EncodedRgbToLumaChromaTransform>(encoded, chroma, new(LumaChromaModel.YCbCrBt709));
        ColorBatch.Transform<LumaChromaColor, StandardEncodedRgba, LumaChromaToEncodedRgbTransform>(chroma, returned, default);
        Check(returned[0].Encoding == encoded[0].Encoding && returned[0].Alpha == encoded[0].Alpha && MathF.Abs(returned[0].Blue - 3) < .00001f, "Encoded metadata and HDR retained");

        // An application-owned coordinate and rule do not need a library registration or RGB tag.
        BrushCoordinate[] custom = [new(2, 3), new(-4, 5)];
        float[] customResult = new float[2];
        ColorBatch.Transform<BrushCoordinate, float, BrushRule>(custom, customResult, new(7));
        Check(customResult[0] == 17 && customResult[1] == -23, "Custom typed rule");
        int[] numbers = [1, 2, 3, 4];
        var referenceRule = new AddReference(8);
        ColorBatch.Transform<int, int, AddReference>(numbers, numbers, referenceRule);
        Check(numbers.SequenceEqual(new[] { 9, 10, 11, 12 }) && referenceRule.Calls == 4, "Caller-owned class and exact in-place");
        var taggedInPlace = (LinearRgba[])input.Clone();
        ColorBatch.Transform<LinearRgba, LinearRgba, ExposureRule>(taggedInPlace, taggedInPlace, default);
        Check(taggedInPlace[0].Blue == 8 && taggedInPlace[0].Alpha == 0 && ReferenceEquals(taggedInPlace[0].ColorSpace, space), "In-place coordinates containing metadata references");

        int[] alias = [1, 2, 3, 4];
        Reject<ArgumentException>(() => ColorBatch.Transform<int, int, AddRule>(alias.AsSpan(0, 3), alias.AsSpan(1, 3), new(1)));
        Reject<ArgumentException>(() => ColorBatch.Transform<int, int, AddRule>(alias.AsSpan(1, 3), alias.AsSpan(0, 3), new(1)));
        Reject<ArgumentException>(() => ColorBatch.Transform<int, float, IntToFloat>(alias, MemoryMarshal.Cast<int, float>(alias.AsSpan()), default));
        Reject<ArgumentException>(() => ColorBatch.Transform<int, byte, IntToByte>(alias, MemoryMarshal.AsBytes(alias.AsSpan()).Slice(1, 4), default));
        Check(alias.SequenceEqual(new[] { 1, 2, 3, 4 }), "Overlap rejected before writes");
        // Only the destination prefix is written: a tail overlapping source is harmless.
        ColorBatch.Transform<int, int, AddRule>(alias.AsSpan(2, 2), alias, new(10));
        Check(alias.SequenceEqual(new[] { 13, 14, 3, 4 }), "Unused destination tail may overlap");
        Reject<ArgumentException>(() => ColorBatch.Transform<int, int, AddRule>(alias, new int[3], default));
        Reject<ArgumentNullException>(() => ColorBatch.Transform<int, int, AddReference>(alias, new int[4], null!));
        Reject<ArgumentNullException>(() => ColorBatch.Transform<int, int, AddReference>(Array.Empty<int>(), Array.Empty<int>(), null!));
        ColorBatch.Transform<int, int, AddRule>(Array.Empty<int>(), Array.Empty<int>(), default);
        Reject<InvalidOperationException>(() => ColorBatch.Transform<CieLabColor, LinearRgba, LabToLinearRgbTransform>(lab.AsSpan(0, 1), restored, default));
        Reject<InvalidOperationException>(() => ColorBatch.Transform<OklabColor, LinearRgba, OklabToLinearRgbTransform>(ok.AsSpan(0, 1), restored, default));
        Reject<ArgumentOutOfRangeException>(() => new EncodedRgbToLumaChromaTransform((LumaChromaModel)99));

        int[] prefix = [99, 99, 99];
        Reject<InvalidOperationException>(() => ColorBatch.Transform<int, int, FailAtTwo>(new[] { 1, 2, 3 }, prefix, default));
        Check(prefix.SequenceEqual(new[] { 11, 99, 99 }), "Failure retains completed prefix only");

        // Warm all generic/static paths before checking allocation; no timing or speed claim.
        var warmInput = Enumerable.Repeat(input[0], 256).ToArray();
        var warmOutput = new CieLabColor[256];
        for (int i = 0; i < 32; i++) ColorBatch.Transform<LinearRgba, CieLabColor, LinearRgbToLabTransform>(warmInput, warmOutput, default);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) ColorBatch.Transform<LinearRgba, CieLabColor, LinearRgbToLabTransform>(warmInput, warmOutput, default);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"Prepared Lab batch allocated {allocated} bytes");
        Console.WriteLine($"Color batch: {checks} checks passed (typed extensions, aliases, metadata, failures; 65,536 prepared conversions allocated {allocated} bytes).");

        void RgbNear(LinearRgba a, LinearRgba b) => Check(MathF.Abs(a.Red - b.Red) < .001f && MathF.Abs(a.Green - b.Green) < .001f && MathF.Abs(a.Blue - b.Blue) < .001f && a.Alpha == b.Alpha && a.ColorSpace == b.ColorSpace, "Batch inverse and metadata");
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        void Reject<T>(Action action) where T : Exception
        {
            checks++;
            try { action(); } catch (T) { return; }
            throw new Exception($"Color batch: expected {typeof(T).Name}.");
        }
    }

    private readonly record struct BrushCoordinate(float Pigment, float Water);
    private readonly record struct BrushRule(float Weight) : IColorTransform<BrushCoordinate, float>
    { public float Transform(BrushCoordinate source) => source.Pigment * Weight + source.Water; }
    private readonly record struct AddRule(int Offset) : IColorTransform<int, int>
    { public int Transform(int source) => source + Offset; }
    private sealed class AddReference(int offset) : IColorTransform<int, int>
    { public int Calls { get; private set; } public int Transform(int source) { Calls++; return source + offset; } }
    private readonly struct IntToFloat : IColorTransform<int, float>
    { public float Transform(int source) => source; }
    private readonly struct IntToByte : IColorTransform<int, byte>
    { public byte Transform(int source) => (byte)source; }
    private readonly struct ExposureRule : IColorTransform<LinearRgba, LinearRgba>
    { public LinearRgba Transform(LinearRgba source) => new(source.Red * 2, source.Green * 2, source.Blue * 2, source.Alpha, source.ColorSpace); }
    private readonly struct FailAtTwo : IColorTransform<int, int>
    { public int Transform(int source) => source == 2 ? throw new InvalidOperationException() : source + 10; }
}

using System.Numerics;
using Mu3D.Color;

internal static class StandardEncodingTests
{
    internal static void Run()
    {
        int checks = 0;
        float[] midpoints = [0.21404114f, 0.21404114f, 0.21775553f, 0.28717459f, 0.25971944f];
        foreach (var encoding in Enum.GetValues<StandardRgbEncoding>())
        {
            var space = StandardRgbEncodingConverter.GetLinearSpace(encoding);
            Near(midpoints[(int)encoding], StandardRgbEncodingConverter.Decode(new(.5f, .5f, .5f, 1, encoding)).Red, 2e-6f);
            var profile = StandardRgbProfiles.Get(encoding);
            if (Environment.GetEnvironmentVariable("MU3D_ICC_TEST_OUTPUT") is string output)
            {
                Directory.CreateDirectory(output);
                File.WriteAllBytes(Path.Combine(output, $"{encoding}.icc"), profile.ToArray());
            }
            var iccDecode = new IccRgbToLinearTransform(profile);
            foreach (float value in new[] { -2f, -.04f, 0, .001f, .03125f, .04045f, .08124286f, .18f, .5f, 1f, 4f })
            {
                var linear = StandardRgbEncodingConverter.Decode(new(value, value, value, .37f, encoding));
                var encoded = StandardRgbEncodingConverter.Encode(linear, encoding);
                Near(value, encoded.Red, 3e-6f);
                if (encoded.Alpha != .37f || linear.ColorSpace != space) throw new Exception("Lost alpha or tag.");
                if (value is >= 0 and <= 1)
                {
                    var icc = iccDecode.TransformEncodedRgb(value, value, value, .37f, space);
                    Near(linear.Red, icc.Red, 2e-4f);
                    Near(linear.Green, icc.Green, 2e-4f);
                    Near(linear.Blue, icc.Blue, 2e-4f);
                }
            }
            foreach (var destination in Enum.GetValues<StandardRgbEncoding>())
            {
                var original = new StandardEncodedRgba(.13f, .5f, .87f, .23f, encoding);
                var round = StandardRgbEncodingConverter.Convert(StandardRgbEncodingConverter.Convert(original, destination), encoding);
                Near(original.Red, round.Red, 2e-5f); Near(original.Green, round.Green, 2e-5f); Near(original.Blue, round.Blue, 2e-5f);
            }
            var image = StandardRgbEncodingConverter.DecodeImage(2, 1, [new(.13f,.5f,.87f,0), new(2,-.1f,.02f,1)], encoding, StandardColorSpaces.AcesCg);
            var pixels = StandardRgbEncodingConverter.EncodeImage(image, encoding);
            Near(.13f, pixels[0].X, 2e-5f); Near(2, pixels[1].X, 2e-5f); Near(-.1f, pixels[1].Y, 2e-5f);
            if (pixels[0].W != 0) throw new Exception("Transparent RGB was discarded.");
            foreach (var preset in Enum.GetValues<ColorViewPreset>())
            {
                var view = new ColorViewTransform(preset, space);
                var color = new LinearRgba(.13f, .5f, 2, .37f, space);
                var reference = new ColorViewTransform(preset, StandardColorSpaces.AcesCg).Transform(
                    StandardLinearRgbConverter.Convert(color, StandardColorSpaces.AcesCg));
                var actual = view.Transform(color);
                Near(reference.Red, actual.Red, .0005f); Near(reference.Green, actual.Green, .0005f); Near(reference.Blue, actual.Blue, .0005f);
            }
        }
        var red = StandardRgbEncodingConverter.Decode(new(1,0,0,1,StandardRgbEncoding.DisplayP3), StandardColorSpaces.LinearSrgb);
        // Independent Display P3 -> linear sRGB primary reference (CSS Color 4 matrices).
        Near(1.2249402f, red.Red, 2e-5f); Near(-.04205695f, red.Green, 2e-5f); Near(-.01963755f, red.Blue, 2e-5f);
        try { StandardRgbEncodingConverter.Encode(red, StandardRgbEncoding.Srgb, RgbEncodingRangePolicy.Reject); throw new Exception("Expected gamut rejection."); }
        catch (InvalidOperationException) { checks++; }
        var clipped = StandardRgbEncodingConverter.Encode(red, StandardRgbEncoding.Srgb, RgbEncodingRangePolicy.Clip);
        Near(1, clipped.Red, 0); Near(0, clipped.Green, 0); Near(0, clipped.Blue, 0);
        Console.WriteLine($"Standard RGB encoding/ICC/view tests: {checks} checks passed.");
        void Near(float expected, float actual, float tolerance)
        {
            checks++;
            if (!float.IsFinite(actual) || MathF.Abs(expected - actual) > tolerance)
                throw new Exception($"Standard encoding: expected {expected}, got {actual}, tolerance {tolerance}.");
        }
    }
}

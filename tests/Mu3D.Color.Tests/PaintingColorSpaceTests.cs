using Mu3D.Color;

// The same independent anchors and extended-value checks run on desktop and WASM.
internal static class PaintingColorSpaceTests
{
    internal static void Run()
    {
        ColorBatchTests.Run();
        int checks = 0;
        var srgb = StandardColorSpaces.LinearSrgb;
        StandardRgbColorSpaceReference[] spaces = [srgb, StandardColorSpaces.LinearDisplayP3,
            StandardColorSpaces.LinearAdobeRgb, StandardColorSpaces.LinearProPhotoRgb,
            StandardColorSpaces.LinearRec2020, StandardColorSpaces.AcesCg];

        // CSS Color 4 D50-adapted sRGB red, and the Oklab author's primary anchors.
        var lab = PerceptualColorConverter.ToLab(new(1, 0, 0, .37f, srgb));
        Near(54.29054f, lab.Lightness, .0003f); Near(80.80492f, lab.A, .0003f); Near(69.89099f, lab.B, .0003f);
        var red = PerceptualColorConverter.ToOklab(new(1, 0, 0, .37f, srgb));
        Near(.62795536f, red.Lightness); Near(.22486306f, red.A); Near(.12584630f, red.B);
        var green = PerceptualColorConverter.ToOklab(new(0, 1, 0, 1, srgb));
        Near(.86643961f, green.Lightness); Near(-.23388757f, green.A); Near(.17949848f, green.B);
        var blue = PerceptualColorConverter.ToOklab(new(0, 0, 1, 1, srgb));
        Near(.45201372f, blue.Lightness); Near(-.03245698f, blue.A); Near(-.31152815f, blue.B);
        Near(100, PerceptualColorConverter.ToLab(new(1, 1, 1, 0, srgb)).Lightness);
        Near(76.06926f, PerceptualColorConverter.ToLab(new(.5f, .5f, .5f, 1, srgb)).Lightness);
        Near(216, PerceptualColorConverter.ToLab(new(8, 8, 8, 1, srgb)).Lightness);
        Near(2, PerceptualColorConverter.ToOklab(new(8, 8, 8, 1, srgb)).Lightness);
        Near(0, PerceptualColorConverter.ToLab(defaultColor()).Lightness);

        // Independent polar coordinates: a=3,b=4 yields chroma=5, hue=atan2(4,3).
        var polar = PerceptualColorConverter.ToLch(new(42, 3, 4, .2f));
        Near(5, polar.Chroma); Near(53.130102f, polar.HueDegrees); Near(.2f, polar.Alpha, 0);
        var rectangular = PerceptualColorConverter.FromLch(new(42, 5, -306.8699f, .2f));
        Near(3, rectangular.A); Near(4, rectangular.B);
        var okPolar = PerceptualColorConverter.ToOklch(new(.5f, .03f, .04f, .2f));
        Near(.05f, okPolar.Chroma); Near(53.130102f, okPolar.HueDegrees);
        Near(0, PerceptualColorConverter.ToLch(new(50, 0, 0, 1)).HueDegrees, 0);
        Near(0, PerceptualColorConverter.ToOklch(new(.5f, 0, 0, 1)).HueDegrees, 0);
        Near(30, new CieLchColor(50, 20, 750, 1).HueDegrees, 0);
        Near(330, new OklchColor(.5f, .1f, -30, 1).HueDegrees, 0);

        // All six primaries/white identities, signed and HDR values, including hidden RGB at alpha=0.
        var random = new Random(443);
        foreach (var space in spaces)
        {
            foreach (var color in new LinearRgba[] { new(0, 0, 0, 0, space), new(1, 1, 1, 1, space),
                new(-.1f, .5f, 4, 0, space), new(-2, -1, -.5f, .37f, space), new(8, 2, .18f, .5f, space) })
                RoundTrip(color);
            for (int i = 0; i < 24; i++)
                RoundTrip(new((float)random.NextDouble() * 5 - .5f, (float)random.NextDouble() * 5 - .5f,
                    (float)random.NextDouble() * 5 - .5f, (float)random.NextDouble(), space));
        }
        Reject<ArgumentException>(() => PerceptualColorConverter.ToLab(default));
        Reject<NotSupportedException>(() => PerceptualColorConverter.ToOklab(default));
        Reject<ArgumentOutOfRangeException>(() => new CieLabColor(float.NaN, 0, 0, 1));
        Reject<ArgumentOutOfRangeException>(() => new OklabColor(0, float.PositiveInfinity, 0, 1));
        Reject<ArgumentOutOfRangeException>(() => new CieLchColor(50, -1, 0, 1));
        Reject<ArgumentOutOfRangeException>(() => new OklchColor(.5f, .1f, float.NaN, 1));
        Reject<ArgumentOutOfRangeException>(() => new CieLabColor(50, 0, 0, -1));
        Reject<ArgumentOutOfRangeException>(() => PerceptualColorConverter.FromLab(new(float.MaxValue, 0, 0, 1), srgb));
        Reject<ArgumentOutOfRangeException>(() => PerceptualColorConverter.FromOklab(new(float.MaxValue, 0, 0, 1), srgb));

        var encodedRed = new StandardEncodedRgba(1, 0, 0, .25f, StandardRgbEncoding.Srgb);
        var yuv = LumaChromaConverter.FromRgb(encodedRed, LumaChromaModel.YuvBt601);
        Near(.299f, yuv.Luma); Near(-.147108f, yuv.ChromaBlue); Near(.614777f, yuv.ChromaRed);
        var y601 = LumaChromaConverter.FromRgb(encodedRed, LumaChromaModel.YCbCrBt601);
        Near(.299f, y601.Luma); Near(-.16873589f, y601.ChromaBlue); Near(.5f, y601.ChromaRed);
        var y709 = LumaChromaConverter.FromRgb(encodedRed, LumaChromaModel.YCbCrBt709);
        Near(.2126f, y709.Luma); Near(-.11457211f, y709.ChromaBlue); Near(.5f, y709.ChromaRed);
        var y2020 = LumaChromaConverter.FromRgb(encodedRed, LumaChromaModel.YCbCrBt2020);
        Near(.2627f, y2020.Luma); Near(-.13963006f, y2020.ChromaBlue); Near(.5f, y2020.ChromaRed);
        foreach (var encoding in Enum.GetValues<StandardRgbEncoding>())
        {
            foreach (var model in Enum.GetValues<LumaChromaModel>())
            {
                var neutral = LumaChromaConverter.FromRgb(new(.5f, .5f, .5f, 0, encoding), model);
                Near(.5f, neutral.Luma); Near(0, neutral.ChromaBlue); Near(0, neutral.ChromaRed);
                foreach (var c in new StandardEncodedRgba[] { new(1, 0, 0, .25f, encoding), new(0, 1, 1, 1, encoding),
                    new(-.3f, .5f, 4, 0, encoding), new(8, 2, .001f, .37f, encoding) })
                {
                    var coordinates = LumaChromaConverter.FromRgb(c, model);
                    Check(coordinates.Encoding == encoding && coordinates.Model == model, "Luma/chroma identity");
                    EncodedNear(c, LumaChromaConverter.ToRgb(coordinates));
                }
            }
            for (int i = 0; i < 32; i++)
            {
                var c = new StandardEncodedRgba((float)random.NextDouble(), (float)random.NextDouble(),
                    (float)random.NextDouble(), .37f, encoding);
                EncodedNear(c, CylindricalRgbConverter.FromHsl(CylindricalRgbConverter.ToHsl(c)));
                EncodedNear(c, CylindricalRgbConverter.FromHsv(CylindricalRgbConverter.ToHsv(c)));
            }
        }
        var cyan = new StandardEncodedRgba(0, 1, 1, 0, StandardRgbEncoding.DisplayP3);
        var hsv = CylindricalRgbConverter.ToHsv(cyan);
        Near(180, hsv.HueDegrees); Near(1, hsv.Saturation); Near(1, hsv.Value);
        EncodedNear(cyan, CylindricalRgbConverter.FromHsv(hsv));
        var hsl = CylindricalRgbConverter.ToHsl(new(.2f, .4f, .6f, .5f, StandardRgbEncoding.Srgb));
        Near(210, hsl.HueDegrees); Near(.5f, hsl.Saturation); Near(.4f, hsl.Lightness);
        Near(0, CylindricalRgbConverter.ToHsl(new(.5f, .5f, .5f, 1, StandardRgbEncoding.Srgb)).HueDegrees, 0);
        Near(0, CylindricalRgbConverter.ToHsv(new(0, 0, 0, 0, StandardRgbEncoding.Srgb)).HueDegrees, 0);
        EncodedNear(encodedRed, CylindricalRgbConverter.FromHsl(new(360, 1, .5f, .25f, StandardRgbEncoding.Srgb)));
        EncodedNear(encodedRed, CylindricalRgbConverter.FromHsv(new(-360, 1, 1, .25f, StandardRgbEncoding.Srgb)));
        Reject<ArgumentOutOfRangeException>(() => CylindricalRgbConverter.ToHsl(new(2, 0, 0, 1, StandardRgbEncoding.Srgb)));
        Reject<ArgumentOutOfRangeException>(() => CylindricalRgbConverter.ToHsv(new(-.01f, 0, 0, 1, StandardRgbEncoding.Srgb)));
        Reject<ArgumentOutOfRangeException>(() => new HslColor(0, 2, .5f, 1, StandardRgbEncoding.Srgb));
        Reject<ArgumentOutOfRangeException>(() => new HsvColor(float.NaN, 1, 1, 1, StandardRgbEncoding.Srgb));
        Reject<ArgumentOutOfRangeException>(() => new LumaChromaColor(0, 0, 0, 1, (StandardRgbEncoding)99, LumaChromaModel.YuvBt601));
        Reject<ArgumentOutOfRangeException>(() => LumaChromaConverter.FromRgb(encodedRed, (LumaChromaModel)99));
        Console.WriteLine($"Painting color spaces: {checks} checks passed (independent primary/neutral/polar anchors, six working spaces, extended RGB, alpha, tags and SDR-only selectors).");

        LinearRgba defaultColor() => new(0, 0, 0, 0, srgb);
        void RoundTrip(LinearRgba input)
        {
            var target = (StandardRgbColorSpaceReference)input.ColorSpace;
            var labColor = PerceptualColorConverter.ToLab(input);
            var okColor = PerceptualColorConverter.ToOklab(input);
            LinearNear(input, PerceptualColorConverter.FromLab(labColor, target));
            LinearNear(input, PerceptualColorConverter.FromLab(
                PerceptualColorConverter.FromLch(PerceptualColorConverter.ToLch(labColor)), target));
            LinearNear(input, PerceptualColorConverter.FromOklab(okColor, target));
            LinearNear(input, PerceptualColorConverter.FromOklab(
                PerceptualColorConverter.FromOklch(PerceptualColorConverter.ToOklch(okColor)), target));
        }
        void LinearNear(LinearRgba expected, LinearRgba actual)
        {
            float tolerance = .00012f * MathF.Max(1, MathF.Max(MathF.Abs(expected.Red), MathF.Max(MathF.Abs(expected.Green), MathF.Abs(expected.Blue))));
            Near(expected.Red, actual.Red, tolerance); Near(expected.Green, actual.Green, tolerance); Near(expected.Blue, actual.Blue, tolerance);
            Check(expected.Alpha == actual.Alpha && expected.ColorSpace == actual.ColorSpace, "Linear alpha/identity");
        }
        void EncodedNear(StandardEncodedRgba expected, StandardEncodedRgba actual)
        {
            Near(expected.Red, actual.Red); Near(expected.Green, actual.Green); Near(expected.Blue, actual.Blue);
            Check(expected.Alpha == actual.Alpha && expected.Encoding == actual.Encoding, "Encoded alpha/identity");
        }
        void Near(float expected, float actual, float tolerance = .00002f)
        {
            checks++;
            if (!float.IsFinite(actual) || MathF.Abs(expected - actual) > tolerance)
                throw new Exception($"Painting space: expected {expected}, got {actual}, tolerance {tolerance}.");
        }
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        void Reject<T>(Action action) where T : Exception
        {
            checks++;
            try { action(); } catch (T) { return; }
            throw new Exception($"Painting space: expected {typeof(T).Name}.");
        }
    }
}

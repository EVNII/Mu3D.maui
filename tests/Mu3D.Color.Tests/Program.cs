using System.Numerics;
using Mu3D.Color;

StandardEncodingTests.Run();

StandardRgbColorSpaceReference srgb = StandardColorSpaces.LinearSrgb;
StandardRgbColorSpaceReference aces = StandardColorSpaces.AcesCg;
Vector3 min = new(-2), max = new(8);
LinearRgbLut3D lut = LinearRgbLut3D.Bake(5, min, max, srgb, aces,
    color => StandardLinearRgbConverter.Convert(color, aces));
Random random = new(149);
for (int i = 0; i < 500; i++)
{
    LinearRgba input = new(Next(), Next(), Next(), (float)random.NextDouble(), srgb);
    LinearRgba expected = StandardLinearRgbConverter.Convert(input, aces);
    LinearRgba actual = lut.Transform(input);
    Near(expected.Red, actual.Red); Near(expected.Green, actual.Green); Near(expected.Blue, actual.Blue);
    if (actual.Alpha != input.Alpha || actual.ColorSpace != aces) throw new Exception("Alpha/space changed.");
}
foreach (float edge in new[] { -2f, 8f })
{
    LinearRgba input = new(edge, edge, edge, 0, srgb);
    Near(StandardLinearRgbConverter.Convert(input, aces).Red, lut.Transform(input).Red);
}
Reject<ArgumentOutOfRangeException>(() => lut.Transform(new(9, 0, 0, 1, srgb)));
Reject<ArgumentException>(() => lut.Transform(new(0, 0, 0, 1, aces)));
Reject<ArgumentException>(() => lut.Transform(default));
Reject<ArgumentOutOfRangeException>(() => LinearRgbLut3D.Bake(66, min, max, srgb, aces, c => c));
Reject<InvalidOperationException>(() => LinearRgbLut3D.Bake(2, min, max, srgb, aces, c => c));
Reject<ArgumentOutOfRangeException>(() => LinearRgbLut3D.Bake(2, max, min, srgb, aces, c => c));
Reject<ArgumentException>(() => new LinearRgbLut3D(2, new Vector3[7], min, max, srgb, aces));

Vector3[] curves = [new(-4, -2, 0), new(0, 2, 4), new(4, 6, 8)];
LinearRgbLut1D shaper = new(curves, new(-1), new(1), srgb, srgb);
curves[0] = new(100); // Constructor owns a copy.
LinearRgba shaped = shaper.Transform(new(-0.5f, 0, 0.5f, 0.25f, srgb));
Near(-2, shaped.Red); Near(2, shaped.Green); Near(6, shaped.Blue); Near(0.25f, shaped.Alpha);
Reject<ArgumentOutOfRangeException>(() => shaper.Transform(new(-2, 0, 0, 1, srgb)));
LinearRgbLut1D clipped = new(shaper.Samples, new(-1), new(1), srgb, srgb, ColorLutRangePolicy.Clamp);
Near(-4, clipped.Transform(new(-2, 0, 0, 1, srgb)).Red);
Reject<ArgumentException>(() => new LinearRgbLut1D([Vector3.Zero, new(float.NaN)], min, max, srgb, srgb));

// Nonseparable tri-affine function checks axis order and every interpolation weight.
LinearRgbLut3D coupled = LinearRgbLut3D.Bake(2, Vector3.Zero, Vector3.One, srgb, srgb,
    c => new(c.Red * c.Green, c.Green * c.Blue, c.Red * c.Green * c.Blue, c.Alpha, srgb));
LinearRgba coupledResult = coupled.Transform(new(0.2f, 0.3f, 0.7f, 1, srgb));
Near(0.06f, coupledResult.Red); Near(0.21f, coupledResult.Green); Near(0.042f, coupledResult.Blue);

// Baking must visit the authored endpoints even when subtraction loses the smaller bound.
LinearRgbLut3D asymmetric = LinearRgbLut3D.Bake(2, new(-1e20f), Vector3.One, srgb, srgb, c => c);
Near(1, asymmetric.Transform(new(1, 1, 1, 1, srgb)).Red);
if (asymmetric.Samples[0] != new Vector3(-1e20f) || asymmetric.Samples[^1] != Vector3.One)
    throw new Exception("Baking changed an authored endpoint.");
LinearRgbLut3D largeDomain = LinearRgbLut3D.Bake(5, Vector3.Zero, new(1e38f), srgb, srgb, c => c);
if (largeDomain.Samples[^1] != new Vector3(1e38f))
    throw new Exception("Baking overflowed a finite domain.");
Console.WriteLine("Color LUT regressions passed: 500 off-grid gamut conversions, HDR/negative bounds, alpha, ownership, range policy, coupled interpolation.");

float Next() => -2f + (float)random.NextDouble() * 10f;
static void Near(float expected, float actual)
{
    if (MathF.Abs(expected - actual) > 0.00002f) throw new Exception($"Expected {expected}, received {actual}.");
}
static void Reject<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

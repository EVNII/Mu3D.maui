using System.Numerics;
using Mu3D.Color;

internal static class PaintingTransformChecks
{
    internal static LinearRgbLut3D CreateTable(Action<bool, string> expect)
    {
        // Application-owned editing policy, sampled once for the existing RGB GPU executor.
        LabChromaReduction edit = new(0.82f);
        LinearRgbLut3D table = LinearRgbLut3D.Bake(65, new Vector3(-0.125f), new Vector3(2f),
            edit.SourceSpace, edit.DestinationSpace, edit.Transform, ColorLutRangePolicy.Clamp);
        expect(table.Samples.Any(p => p.X < 0 || p.Y < 0 || p.Z < 0) &&
            table.Samples.Any(p => p.X > 1 || p.Y > 1 || p.Z > 1),
            "painting LUT retains negative and HDR output rather than clipping to display RGB");

        float maximumError = 0;
        double squaredError = 0;
        int count = 0;
        Vector3[] darkAndExtended =
        [
            Vector3.Zero, new(0.001f, 0.003f, 0.007f), new(-0.031f, 0.006f, 0.012f),
            new(0.07f, -0.023f, 0.03f), new(-0.1f, 0.4f, 1.7f), new(1.95f, 0.12f, 0.02f),
        ];
        foreach (Vector3 p in darkAndExtended)
            Compare(p);
        // Deterministic off-grid points; approximation error is measured separately from GPU error.
        for (int b = 0; b < 12; b++)
        for (int g = 0; g < 12; g++)
        for (int r = 0; r < 12; r++)
        {
            Vector3 t = (new Vector3(r, g, b) + new Vector3(0.37f, 0.53f, 0.71f)) / 12f;
            Compare(table.DomainMinimum + t * (table.DomainMaximum - table.DomainMinimum));
        }
        expect(maximumError <= 0.004f,
            $"painting direct vs 65-cube LUT off-grid maximum RGB error {maximumError:G9} <= 0.004");
        Console.WriteLine($"Painting direct vs LUT: {count} off-grid colors, max RGB error {maximumError:G9}, " +
            $"RGB RMSE {Math.Sqrt(squaredError / (count * 3)):G9}; domain [-0.125, 2], edge 65.");

        LinearRgba opaque = edit.Transform(new LinearRgba(0.2f, 0.4f, 1.2f, 1f, edit.SourceSpace));
        LinearRgba transparent = edit.Transform(new LinearRgba(0.2f, 0.4f, 1.2f, 0f, edit.SourceSpace));
        expect(opaque.Red == transparent.Red && opaque.Green == transparent.Green && opaque.Blue == transparent.Blue &&
            opaque.Alpha == 1 && transparent.Alpha == 0,
            "application Lab edit depends on RGB alone and preserves zero-alpha hidden color");
        expect(MathF.Abs(opaque.Red - 0.2f) > 0.01f || MathF.Abs(opaque.Blue - 1.2f) > 0.01f,
            "application Lab edit changes color rather than merely testing an identity LUT");

        // The selected GPU-compatible Clamp policy is explicit, independent of the edit itself.
        LinearRgba clamped = table.Transform(new LinearRgba(-8, 9, 2, 0.25f, edit.SourceSpace));
        LinearRgba atCorner = edit.Transform(new LinearRgba(-0.125f, 2, 2, 0.25f, edit.SourceSpace));
        expect(MathF.Abs(clamped.Red - atCorner.Red) <= 0.00001f &&
            MathF.Abs(clamped.Green - atCorner.Green) <= 0.00001f &&
            MathF.Abs(clamped.Blue - atCorner.Blue) <= 0.00001f && clamped.Alpha == 0.25f,
            "painting LUT applies its declared input-domain clamp without changing alpha");
        return table;

        void Compare(Vector3 p)
        {
            float alpha = (count % 3) switch { 0 => 0f, 1 => 0.333f, _ => 1f };
            LinearRgba input = new(p.X, p.Y, p.Z, alpha, edit.SourceSpace);
            LinearRgba direct = edit.Transform(input);
            LinearRgba sampled = table.Transform(input);
            expect(sampled.ColorSpace == edit.DestinationSpace && sampled.Alpha == alpha && direct.Alpha == alpha,
                $"painting direct/LUT color identity and unchanged alpha at off-grid point {count}");
            Vector3 error = Vector3.Abs(new Vector3(sampled.Red - direct.Red,
                sampled.Green - direct.Green, sampled.Blue - direct.Blue));
            maximumError = MathF.Max(maximumError, MathF.Max(error.X, MathF.Max(error.Y, error.Z)));
            squaredError += (double)error.X * error.X + (double)error.Y * error.Y + (double)error.Z * error.Z;
            count++;
        }
    }

    internal static Vector4[] CreateGpuPixels() =>
    [
        new(-0.125f, -0.125f, -0.125f, 0f), new(2f, 2f, 2f, 1f),
        new(0.13f, 1.82f, 0.4f, 0.333f), new(-0.1f, 0.42f, 1.7f, 0.5f),
        new(-8f, 9f, 2f, 0.25f), new(1.65f, 0.006f, -0.023f, 0.8f),
        new(0f, 0f, 0f, 0.4f), new(0.07f, -0.023f, 0.03f, 0f),
    ];

    private readonly struct LabChromaReduction(float amount) : ILinearRgbTransform
    {
        public ColorSpaceReference SourceSpace => StandardColorSpaces.LinearSrgb;
        public ColorSpaceReference DestinationSpace => StandardColorSpaces.LinearSrgb;

        public LinearRgba Transform(LinearRgba source)
        {
            if (source.ColorSpace != SourceSpace)
                throw new ArgumentException("The application's edit requires tagged linear sRGB.", nameof(source));
            CieLabColor lab = PerceptualColorConverter.ToLab(source);
            return PerceptualColorConverter.FromLab(new CieLabColor(
                lab.Lightness, lab.A * amount, lab.B * amount, lab.Alpha), StandardColorSpaces.LinearSrgb);
        }
    }
}

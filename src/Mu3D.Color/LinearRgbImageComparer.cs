namespace Mu3D.Color;

/// <summary>Reports deterministic RGB and perceptual errors between two linear-light images.</summary>
/// <param name="PixelCount">The number of compared pixels.</param>
/// <param name="ReferencePeakMagnitude">The largest absolute reference RGB component.</param>
/// <param name="ActualPeakMagnitude">The largest absolute actual RGB component after conversion.</param>
/// <param name="MeanAbsoluteRgbError">The mean absolute error across all RGB components.</param>
/// <param name="RootMeanSquareRgbError">The root-mean-square error across all RGB components.</param>
/// <param name="MaximumAbsoluteRgbError">The largest absolute RGB component error.</param>
/// <param name="PeakRelativeRgbError">
/// The maximum absolute RGB error divided by the reference peak magnitude.
/// </param>
/// <param name="PeakSignalToNoiseRatioDecibels">
/// RGB PSNR using the reference peak magnitude. An exact match returns positive infinity.
/// </param>
/// <param name="MeanDeltaE2000">The mean per-pixel CIEDE2000 difference in D50 Lab.</param>
/// <param name="MaximumDeltaE2000">The largest per-pixel CIEDE2000 difference in D50 Lab.</param>
public readonly record struct LinearRgbImageComparison(
    int PixelCount,
    double ReferencePeakMagnitude,
    double ActualPeakMagnitude,
    double MeanAbsoluteRgbError,
    double RootMeanSquareRgbError,
    double MaximumAbsoluteRgbError,
    double PeakRelativeRgbError,
    double PeakSignalToNoiseRatioDecibels,
    double MeanDeltaE2000,
    double MaximumDeltaE2000);

/// <summary>Compares explicitly tagged linear-light RGB images without tone or gamut mapping.</summary>
public static class LinearRgbImageComparer
{
    /// <summary>
    /// Compares RGB samples after converting the actual image into the reference image's standard
    /// linear RGB space. Alpha is not included in the color metrics.
    /// </summary>
    /// <remarks>
    /// CIEDE2000 is useful for deterministic codec and color-patch regression, but it is not the
    /// BT.2124 Delta E ITP metric required for calibrated HDR display measurements.
    /// </remarks>
    /// <param name="reference">The reference image and comparison working-space identity.</param>
    /// <param name="actual">The reconstructed image; it may use another built-in linear RGB space.</param>
    /// <returns>RGB signal-error and D50 Lab perceptual statistics.</returns>
    /// <exception cref="ArgumentException">The image dimensions differ.</exception>
    /// <exception cref="NotSupportedException">Either image does not use a built-in linear RGB space.</exception>
    public static LinearRgbImageComparison Compare(
        LinearRgbaImage reference,
        LinearRgbaImage actual)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(actual);
        if (reference.Width != actual.Width || reference.Height != actual.Height)
        {
            throw new ArgumentException("Compared images must have identical dimensions.", nameof(actual));
        }
        if (reference.ColorSpace is not StandardRgbColorSpaceReference referenceSpace)
        {
            throw new NotSupportedException(
                "The reference image must use a built-in standard linear RGB space.");
        }
        if (actual.ColorSpace is not StandardRgbColorSpaceReference)
        {
            throw new NotSupportedException(
                "The actual image must use a built-in standard linear RGB space.");
        }

        double absoluteErrorSum = 0;
        double squaredErrorSum = 0;
        double maximumAbsoluteError = 0;
        double referencePeak = 0;
        double actualPeak = 0;
        double deltaESum = 0;
        double maximumDeltaE = 0;
        int pixelCount = reference.Pixels.Count;
        for (int index = 0; index < pixelCount; index++)
        {
            System.Numerics.Vector4 expectedPixel = reference.Pixels[index];
            System.Numerics.Vector4 actualPixel = actual.Pixels[index];
            LinearRgba expected = new(
                expectedPixel.X,
                expectedPixel.Y,
                expectedPixel.Z,
                expectedPixel.W,
                referenceSpace);
            LinearRgba converted = StandardLinearRgbConverter.Convert(
                new LinearRgba(
                    actualPixel.X,
                    actualPixel.Y,
                    actualPixel.Z,
                    actualPixel.W,
                    actual.ColorSpace),
                referenceSpace);

            Accumulate(expected.Red, converted.Red);
            Accumulate(expected.Green, converted.Green);
            Accumulate(expected.Blue, converted.Blue);
            double deltaE = DeltaE2000(ToLab(expected, referenceSpace), ToLab(converted, referenceSpace));
            deltaESum += deltaE;
            maximumDeltaE = Math.Max(maximumDeltaE, deltaE);
        }

        double componentCount = checked(pixelCount * 3d);
        double meanAbsoluteError = absoluteErrorSum / componentCount;
        double rootMeanSquareError = Math.Sqrt(squaredErrorSum / componentCount);
        double peakRelativeError = referencePeak == 0
            ? maximumAbsoluteError == 0 ? 0 : double.PositiveInfinity
            : maximumAbsoluteError / referencePeak;
        double psnr = rootMeanSquareError == 0
            ? double.PositiveInfinity
            : referencePeak == 0
                ? double.NegativeInfinity
                : 20 * Math.Log10(referencePeak / rootMeanSquareError);
        return new LinearRgbImageComparison(
            pixelCount,
            referencePeak,
            actualPeak,
            meanAbsoluteError,
            rootMeanSquareError,
            maximumAbsoluteError,
            peakRelativeError,
            psnr,
            deltaESum / pixelCount,
            maximumDeltaE);

        void Accumulate(float expected, float observed)
        {
            double error = Math.Abs((double)observed - expected);
            absoluteErrorSum += error;
            squaredErrorSum += error * error;
            maximumAbsoluteError = Math.Max(maximumAbsoluteError, error);
            referencePeak = Math.Max(referencePeak, Math.Abs((double)expected));
            actualPeak = Math.Max(actualPeak, Math.Abs((double)observed));
        }
    }

    private static LabColor ToLab(LinearRgba color, StandardRgbColorSpaceReference space)
    {
        (float x, float y, float z) = StandardLinearRgbConverter.ToXyzD50(color, space);
        double fx = LabFunction(x / 0.9642);
        double fy = LabFunction(y);
        double fz = LabFunction(z / 0.8249);
        return new LabColor(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static double LabFunction(double value)
    {
        const double epsilon = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        return value > epsilon ? Math.Cbrt(value) : (kappa * value + 16) / 116;
    }

    private static double DeltaE2000(LabColor first, LabColor second)
    {
        double c1 = Math.Sqrt(first.A * first.A + first.B * first.B);
        double c2 = Math.Sqrt(second.A * second.A + second.B * second.B);
        double meanC = (c1 + c2) / 2;
        double meanC7 = Math.Pow(meanC, 7);
        double g = 0.5 * (1 - Math.Sqrt(meanC7 / (meanC7 + Math.Pow(25, 7))));
        double a1 = (1 + g) * first.A;
        double a2 = (1 + g) * second.A;
        double adjustedC1 = Math.Sqrt(a1 * a1 + first.B * first.B);
        double adjustedC2 = Math.Sqrt(a2 * a2 + second.B * second.B);
        double h1 = HueDegrees(first.B, a1);
        double h2 = HueDegrees(second.B, a2);
        double deltaL = second.L - first.L;
        double deltaC = adjustedC2 - adjustedC1;
        double deltaHAngle = h2 - h1;
        if (adjustedC1 * adjustedC2 == 0)
        {
            deltaHAngle = 0;
        }
        else if (deltaHAngle > 180)
        {
            deltaHAngle -= 360;
        }
        else if (deltaHAngle < -180)
        {
            deltaHAngle += 360;
        }
        double deltaH = 2 * Math.Sqrt(adjustedC1 * adjustedC2) *
            Math.Sin(DegreesToRadians(deltaHAngle / 2));
        double meanL = (first.L + second.L) / 2;
        double adjustedMeanC = (adjustedC1 + adjustedC2) / 2;
        double meanH;
        if (adjustedC1 * adjustedC2 == 0)
        {
            meanH = h1 + h2;
        }
        else if (Math.Abs(h1 - h2) <= 180)
        {
            meanH = (h1 + h2) / 2;
        }
        else if (h1 + h2 < 360)
        {
            meanH = (h1 + h2 + 360) / 2;
        }
        else
        {
            meanH = (h1 + h2 - 360) / 2;
        }
        double t = 1 - 0.17 * Math.Cos(DegreesToRadians(meanH - 30)) +
            0.24 * Math.Cos(DegreesToRadians(2 * meanH)) +
            0.32 * Math.Cos(DegreesToRadians(3 * meanH + 6)) -
            0.20 * Math.Cos(DegreesToRadians(4 * meanH - 63));
        double deltaTheta = 30 * Math.Exp(-Math.Pow((meanH - 275) / 25, 2));
        double meanLMinus50Squared = (meanL - 50) * (meanL - 50);
        double sL = 1 + 0.015 * meanLMinus50Squared / Math.Sqrt(20 + meanLMinus50Squared);
        double sC = 1 + 0.045 * adjustedMeanC;
        double sH = 1 + 0.015 * adjustedMeanC * t;
        double adjustedMeanC7 = Math.Pow(adjustedMeanC, 7);
        double rC = 2 * Math.Sqrt(adjustedMeanC7 / (adjustedMeanC7 + Math.Pow(25, 7)));
        double rT = -Math.Sin(DegreesToRadians(2 * deltaTheta)) * rC;
        double lTerm = deltaL / sL;
        double cTerm = deltaC / sC;
        double hTerm = deltaH / sH;
        return Math.Sqrt(
            lTerm * lTerm + cTerm * cTerm + hTerm * hTerm + rT * cTerm * hTerm);
    }

    private static double HueDegrees(double b, double a)
    {
        double degrees = Math.Atan2(b, a) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private readonly record struct LabColor(double L, double A, double B);
}

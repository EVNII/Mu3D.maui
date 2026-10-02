namespace Mu3D.Color;

/// <summary>
/// Estimates media-relative RGB ICC profile black points for black-point compensation.
/// </summary>
/// <remarks>
/// The estimator implements the RGB path of ISO 18619. It tests both neutral RGB device vertices
/// for inverse-polarity profiles and evaluates LUT-based destinations with the specified 256-sample
/// CIELAB round-trip ramp, monotonic correction, mid-range test and shadow-curve fit. The target
/// profile must provide both a supported PCS-to-RGB transform for the selected intent and a
/// supported media-relative RGB-to-PCS transform. ICC-absolute intent is not a BPC intent.
/// </remarks>
public static class IccBlackPointEstimator
{
    private const int RampSampleCount = 256;

    /// <summary>Estimates the source and destination black-point lightness for an RGB profile link.</summary>
    /// <param name="sourceProfile">The source RGB input, display, output or color-space profile.</param>
    /// <param name="destinationProfile">The destination RGB input, display, output or color-space profile.</param>
    /// <param name="renderingIntent">Perceptual, media-relative colorimetric or saturation intent.</param>
    /// <returns>Black-point compensation parameters suitable for the same profile link and intent.</returns>
    public static IccBlackPointCompensation Estimate(
        IccProfile sourceProfile,
        IccProfile destinationProfile,
        IccRenderingIntent renderingIntent = IccRenderingIntent.MediaRelativeColorimetric)
    {
        ArgumentNullException.ThrowIfNull(sourceProfile);
        ArgumentNullException.ThrowIfNull(destinationProfile);
        ValidateProfileClass(sourceProfile, nameof(sourceProfile));
        ValidateProfileClass(destinationProfile, nameof(destinationProfile));
        if (!Enum.IsDefined(renderingIntent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderingIntent),
                renderingIntent,
                "Unknown ICC rendering intent.");
        }
        if (renderingIntent == IccRenderingIntent.IccAbsoluteColorimetric)
        {
            throw new ArgumentException(
                "ISO 18619 black-point compensation does not use ICC-absolute intent.",
                nameof(renderingIntent));
        }

        IccRgbToLinearTransform sourceToPcs = new(sourceProfile, renderingIntent);
        IccRgbToLinearTransform destinationToRelativePcs = new(
            destinationProfile,
            IccRenderingIntent.MediaRelativeColorimetric);
        IccRgbToLinearTransform destinationToSelectedPcs =
            renderingIntent == IccRenderingIntent.MediaRelativeColorimetric
                ? destinationToRelativePcs
                : new IccRgbToLinearTransform(destinationProfile, renderingIntent);
        IccLinearToRgbTransform pcsToDestination = new(destinationProfile, renderingIntent);

        Lab sourceBlack = FindDarkestRgbVertex(sourceToPcs);
        float sourceLightness = ValidateAndCapLightness(sourceBlack.L, "source");
        Lab initialLab = renderingIntent == IccRenderingIntent.MediaRelativeColorimetric
            ? FindDarkestRgbVertex(destinationToSelectedPcs)
            : default;
        initialLab = new Lab(
            ValidateAndCapLightness(initialLab.L, "destination"),
            initialLab.A,
            initialLab.B);
        float destinationLightness = EstimateLutDestinationLightness(
            initialLab,
            renderingIntent,
            pcsToDestination,
            destinationToRelativePcs);
        return new IccBlackPointCompensation(sourceLightness, destinationLightness);
    }

    private static float EstimateLutDestinationLightness(
        Lab initialLab,
        IccRenderingIntent renderingIntent,
        IccLinearToRgbTransform pcsToDestination,
        IccRgbToLinearTransform destinationToRelativePcs)
    {
        float startA = Math.Clamp(initialLab.A, -50f, 50f);
        float startB = Math.Clamp(initialLab.B, -50f, 50f);
        double[] inputRamp = new double[RampSampleCount];
        double[] outputRamp = new double[RampSampleCount];
        for (int index = 0; index < RampSampleCount; index++)
        {
            float amount = index / (RampSampleCount - 1f);
            Lab input = new(
                100f * amount,
                startA * (1f - amount),
                startB * (1f - amount));
            inputRamp[index] = input.L;
            IccEncodedRgba encoded = TransformLabToDevice(input, pcsToDestination);
            Lab output = TransformDeviceToLab(
                destinationToRelativePcs,
                Math.Clamp(encoded.Red, 0f, 1f),
                Math.Clamp(encoded.Green, 0f, 1f),
                Math.Clamp(encoded.Blue, 0f, 1f));
            if (!float.IsFinite(output.L))
            {
                throw new InvalidDataException(
                    "ICC destination black-point round trip produced a non-finite lightness.");
            }
            outputRamp[index] = output.L;
        }

        for (int index = outputRamp.Length - 2; index >= 0; index--)
        {
            outputRamp[index] = Math.Min(outputRamp[index], outputRamp[index + 1]);
        }

        double minimumLightness = outputRamp[0];
        double maximumLightness = outputRamp[^1];
        if (!(minimumLightness < maximumLightness))
        {
            return 0f;
        }
        if (renderingIntent == IccRenderingIntent.MediaRelativeColorimetric &&
            IsMidRangeStraight(inputRamp, outputRamp, minimumLightness, maximumLightness))
        {
            return initialLab.L;
        }

        double lowerShadowLimit = renderingIntent == IccRenderingIntent.MediaRelativeColorimetric
            ? 0.1
            : 0.03;
        double upperShadowLimit = renderingIntent == IccRenderingIntent.MediaRelativeColorimetric
            ? 0.5
            : 0.25;
        List<(double X, double Y)> shadowPoints = [];
        double outputRange = maximumLightness - minimumLightness;
        for (int index = 0; index < inputRamp.Length; index++)
        {
            double normalized = (outputRamp[index] - minimumLightness) / outputRange;
            if (normalized >= lowerShadowLimit && normalized < upperShadowLimit)
            {
                shadowPoints.Add((inputRamp[index], normalized));
            }
        }
        if (shadowPoints.Count < 3 || !TryFitQuadratic(shadowPoints, out double a, out double b, out double c))
        {
            return 0f;
        }

        double root;
        if (Math.Abs(a) < 1e-10)
        {
            if (Math.Abs(b) < double.Epsilon)
            {
                return 0f;
            }
            root = -c / b;
        }
        else
        {
            double discriminant = b * b - 4d * a * c;
            if (discriminant <= 0d)
            {
                return 0f;
            }
            root = (-b + Math.Sqrt(discriminant)) / (2d * a);
        }
        return double.IsFinite(root) ? (float)Math.Clamp(root, 0d, 50d) : 0f;
    }

    private static bool IsMidRangeStraight(
        IReadOnlyList<double> inputRamp,
        IReadOnlyList<double> outputRamp,
        double minimumLightness,
        double maximumLightness)
    {
        double threshold = minimumLightness + 0.2d * (maximumLightness - minimumLightness);
        for (int index = 0; index < inputRamp.Count; index++)
        {
            if (inputRamp[index] > threshold &&
                Math.Abs(inputRamp[index] - outputRamp[index]) >= 4d)
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryFitQuadratic(
        IReadOnlyList<(double X, double Y)> points,
        out double a,
        out double b,
        out double c)
    {
        double[,] system = new double[3, 4];
        foreach ((double x, double y) in points)
        {
            double x2 = x * x;
            system[0, 0] += x2 * x2;
            system[0, 1] += x2 * x;
            system[0, 2] += x2;
            system[0, 3] += x2 * y;
            system[1, 0] += x2 * x;
            system[1, 1] += x2;
            system[1, 2] += x;
            system[1, 3] += x * y;
            system[2, 0] += x2;
            system[2, 1] += x;
            system[2, 2] += 1d;
            system[2, 3] += y;
        }

        for (int column = 0; column < 3; column++)
        {
            int pivot = column;
            for (int row = column + 1; row < 3; row++)
            {
                if (Math.Abs(system[row, column]) > Math.Abs(system[pivot, column]))
                {
                    pivot = row;
                }
            }
            if (Math.Abs(system[pivot, column]) < 1e-14)
            {
                a = b = c = 0d;
                return false;
            }
            if (pivot != column)
            {
                for (int item = column; item < 4; item++)
                {
                    (system[column, item], system[pivot, item]) =
                        (system[pivot, item], system[column, item]);
                }
            }
            double divisor = system[column, column];
            for (int item = column; item < 4; item++)
            {
                system[column, item] /= divisor;
            }
            for (int row = 0; row < 3; row++)
            {
                if (row == column)
                {
                    continue;
                }
                double factor = system[row, column];
                for (int item = column; item < 4; item++)
                {
                    system[row, item] -= factor * system[column, item];
                }
            }
        }
        a = system[0, 3];
        b = system[1, 3];
        c = system[2, 3];
        return double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c);
    }

    private static Lab FindDarkestRgbVertex(IccRgbToLinearTransform transform)
    {
        Lab black = TransformDeviceToLab(transform, 0f, 0f, 0f);
        Lab white = TransformDeviceToLab(transform, 1f, 1f, 1f);
        return black.L <= white.L ? black : white;
    }

    private static Lab TransformDeviceToLab(
        IccRgbToLinearTransform transform,
        float red,
        float green,
        float blue)
    {
        LinearRgba working = transform.TransformEncodedRgb(
            red,
            green,
            blue,
            1f,
            StandardColorSpaces.LinearProPhotoRgb);
        (float x, float y, float z) = StandardLinearRgbConverter.ToXyzD50(
            working,
            StandardColorSpaces.LinearProPhotoRgb);
        float fx = LabFunction(x / 0.9642f);
        float fy = LabFunction(y);
        float fz = LabFunction(z / 0.8249f);
        return new Lab(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
    }

    private static IccEncodedRgba TransformLabToDevice(
        Lab value,
        IccLinearToRgbTransform transform)
    {
        float fy = (value.L + 16f) / 116f;
        float fx = fy + value.A / 500f;
        float fz = fy - value.B / 200f;
        LinearRgba working = StandardLinearRgbConverter.FromXyzD50(
            0.9642f * InverseLabFunction(fx),
            InverseLabFunction(fy),
            0.8249f * InverseLabFunction(fz),
            1f,
            StandardColorSpaces.LinearProPhotoRgb);
        return transform.Transform(working);
    }

    private static float ValidateAndCapLightness(float value, string profileRole)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new InvalidDataException(
                $"ICC {profileRole} profile produced an invalid black-point lightness.");
        }
        return MathF.Min(value, 50f);
    }

    private static void ValidateProfileClass(IccProfile profile, string parameterName)
    {
        if (profile.ProfileClass is not ("scnr" or "mntr" or "prtr" or "spac"))
        {
            throw new ArgumentException(
                $"ISO 18619 BPC does not accept ICC profile class '{profile.ProfileClass}'.",
                parameterName);
        }
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

    private readonly record struct Lab(float L, float A, float B);
}

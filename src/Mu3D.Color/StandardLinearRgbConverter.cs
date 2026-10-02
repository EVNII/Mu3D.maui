namespace Mu3D.Color;

/// <summary>
/// Converts unpremultiplied linear-light RGB values between Mu3D's standard parametric RGB spaces.
/// Conversion preserves negative and above-one components and does not perform gamut mapping,
/// tone mapping or clipping.
/// </summary>
public static class StandardLinearRgbConverter
{
    private static readonly Matrix3x3 Bradford = new(
        0.8951f, 0.2664f, -0.1614f,
        -0.7502f, 1.7135f, 0.0367f,
        0.0389f, -0.0685f, 1.0296f);

    private static readonly Matrix3x3 BradfordInverse = Bradford.Inverse();
    private static readonly Vector3 D50WhitePoint = ToUnitY(new Chromaticity(0.3457f, 0.3585f));
    private static readonly SpaceDefinition[] Definitions =
    [
        CreateDefinition(
            new Chromaticity(0.6400f, 0.3300f),
            new Chromaticity(0.3000f, 0.6000f),
            new Chromaticity(0.1500f, 0.0600f),
            new Chromaticity(0.3127f, 0.3290f)),
        CreateDefinition(
            new Chromaticity(0.6800f, 0.3200f),
            new Chromaticity(0.2650f, 0.6900f),
            new Chromaticity(0.1500f, 0.0600f),
            new Chromaticity(0.3127f, 0.3290f)),
        CreateDefinition(
            new Chromaticity(0.6400f, 0.3300f),
            new Chromaticity(0.2100f, 0.7100f),
            new Chromaticity(0.1500f, 0.0600f),
            new Chromaticity(0.3127f, 0.3290f)),
        CreateDefinition(
            new Chromaticity(0.7347f, 0.2653f),
            new Chromaticity(0.1596f, 0.8404f),
            new Chromaticity(0.0366f, 0.0001f),
            new Chromaticity(0.3457f, 0.3585f)),
        CreateDefinition(
            new Chromaticity(0.7080f, 0.2920f),
            new Chromaticity(0.1700f, 0.7970f),
            new Chromaticity(0.1310f, 0.0460f),
            new Chromaticity(0.3127f, 0.3290f)),
        CreateDefinition(
            new Chromaticity(0.7130f, 0.2930f),
            new Chromaticity(0.1650f, 0.8300f),
            new Chromaticity(0.1280f, 0.0440f),
            new Chromaticity(0.32168f, 0.33767f)),
    ];

    private static readonly Matrix3x3[,] Transforms = CreateTransforms();
    private static readonly Matrix3x3[] XyzD50ToRgbTransforms = CreateXyzD50ToRgbTransforms();
    private static readonly Matrix3x3[] RgbToXyzD50Transforms =
        XyzD50ToRgbTransforms.Select(static transform => transform.Inverse()).ToArray();

    /// <summary>
    /// Converts a standard linear-light RGB color to another standard linear-light RGB space using
    /// Bradford chromatic adaptation when the source and destination white points differ.
    /// </summary>
    /// <param name="source">The explicitly tagged source color.</param>
    /// <param name="destination">The destination standard linear RGB identity.</param>
    /// <returns>A converted color tagged with <paramref name="destination"/>.</returns>
    /// <exception cref="NotSupportedException">
    /// The source is not one of the built-in standard linear RGB spaces.
    /// </exception>
    public static LinearRgba Convert(
        LinearRgba source,
        StandardRgbColorSpaceReference destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (source.ColorSpace is not StandardRgbColorSpaceReference sourceSpace)
        {
            throw new NotSupportedException(
                $"The standard converter cannot transform {source.ColorSpace?.Name ?? "an untagged color"}.");
        }

        ValidateSpace(sourceSpace.Space);
        ValidateSpace(destination.Space);
        if (sourceSpace.Space == destination.Space)
        {
            return new LinearRgba(
                source.Red,
                source.Green,
                source.Blue,
                source.Alpha,
                destination);
        }

        Vector3 converted = Transforms[(int)sourceSpace.Space, (int)destination.Space]
            .Transform(new Vector3(source.Red, source.Green, source.Blue));
        return new LinearRgba(converted.X, converted.Y, converted.Z, source.Alpha, destination);
    }

    /// <summary>
    /// Converts an ICC PCS XYZ D50 value to a built-in linear RGB working space.
    /// No gamut mapping, clipping, rendering-intent remapping or black-point compensation occurs.
    /// </summary>
    /// <param name="x">The finite PCS X component.</param>
    /// <param name="y">The finite PCS Y component.</param>
    /// <param name="z">The finite PCS Z component.</param>
    /// <param name="alpha">The unpremultiplied alpha component in the inclusive zero-to-one range.</param>
    /// <param name="destination">The destination standard linear RGB identity.</param>
    /// <returns>The converted, explicitly tagged linear-light color.</returns>
    public static LinearRgba FromXyzD50(
        float x,
        float y,
        float z,
        float alpha,
        StandardRgbColorSpaceReference destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "PCS XYZ components must be finite.");
        }
        ValidateSpace(destination.Space);
        Vector3 converted = XyzD50ToRgbTransforms[(int)destination.Space]
            .Transform(new Vector3(x, y, z));
        return new LinearRgba(converted.X, converted.Y, converted.Z, alpha, destination);
    }

    /// <summary>Converts a tagged standard linear RGB color to media-relative CIE XYZ D50.</summary>
    /// <remarks>Preserves extended values; no tone mapping, gamut mapping or alpha conversion occurs.</remarks>
    public static System.Numerics.Vector3 ToXyzD50(LinearRgba source)
    {
        if (source.ColorSpace is not StandardRgbColorSpaceReference space)
            throw new ArgumentException("A standard linear RGB source is required.", nameof(source));
        var xyz = ToXyzD50(source, space);
        return new System.Numerics.Vector3(xyz.X, xyz.Y, xyz.Z);
    }

    internal static (float X, float Y, float Z) ToXyzD50(
        LinearRgba source,
        StandardRgbColorSpaceReference expectedSource)
    {
        ArgumentNullException.ThrowIfNull(expectedSource);
        if (!ReferenceEquals(source.ColorSpace, expectedSource))
        {
            throw new InvalidOperationException(
                "The linear RGB value does not carry the expected working-space identity.");
        }
        ValidateSpace(expectedSource.Space);
        Vector3 converted = RgbToXyzD50Transforms[(int)expectedSource.Space]
            .Transform(new Vector3(source.Red, source.Green, source.Blue));
        return (converted.X, converted.Y, converted.Z);
    }

    private static Matrix3x3[] CreateXyzD50ToRgbTransforms()
    {
        Matrix3x3[] transforms = new Matrix3x3[Definitions.Length];
        for (int destination = 0; destination < transforms.Length; destination++)
        {
            SpaceDefinition definition = Definitions[destination];
            transforms[destination] = definition.RgbToXyz.Inverse() *
                CreateChromaticAdaptation(D50WhitePoint, definition.WhitePoint);
        }
        return transforms;
    }

    private static Matrix3x3[,] CreateTransforms()
    {
        int count = Enum.GetValues<StandardRgbColorSpace>().Length;
        if (count != Definitions.Length)
        {
            throw new InvalidOperationException("Every standard RGB space requires a transform definition.");
        }

        Matrix3x3[,] transforms = new Matrix3x3[count, count];
        for (int source = 0; source < count; source++)
        {
            for (int destination = 0; destination < count; destination++)
            {
                if (source == destination)
                {
                    transforms[source, destination] = Matrix3x3.Identity;
                    continue;
                }

                SpaceDefinition sourceDefinition = Definitions[source];
                SpaceDefinition destinationDefinition = Definitions[destination];
                Matrix3x3 adaptation = CreateChromaticAdaptation(
                    sourceDefinition.WhitePoint,
                    destinationDefinition.WhitePoint);
                transforms[source, destination] = destinationDefinition.RgbToXyz.Inverse() *
                    adaptation * sourceDefinition.RgbToXyz;
            }
        }
        return transforms;
    }

    private static SpaceDefinition CreateDefinition(
        Chromaticity red,
        Chromaticity green,
        Chromaticity blue,
        Chromaticity white)
    {
        Vector3 redXyz = ToUnitY(red);
        Vector3 greenXyz = ToUnitY(green);
        Vector3 blueXyz = ToUnitY(blue);
        Vector3 whiteXyz = ToUnitY(white);
        Matrix3x3 primaries = new(
            redXyz.X, greenXyz.X, blueXyz.X,
            redXyz.Y, greenXyz.Y, blueXyz.Y,
            redXyz.Z, greenXyz.Z, blueXyz.Z);
        Vector3 scale = primaries.Inverse().Transform(whiteXyz);
        Matrix3x3 rgbToXyz = primaries * new Matrix3x3(
            scale.X, 0, 0,
            0, scale.Y, 0,
            0, 0, scale.Z);
        return new SpaceDefinition(rgbToXyz, whiteXyz);
    }

    private static Vector3 ToUnitY(Chromaticity chromaticity)
    {
        if (chromaticity.Y <= 0f)
        {
            throw new InvalidOperationException("A standard chromaticity must have positive y.");
        }
        return new Vector3(
            chromaticity.X / chromaticity.Y,
            1f,
            (1f - chromaticity.X - chromaticity.Y) / chromaticity.Y);
    }

    internal static float[] GetD50Adaptation(StandardRgbColorSpace space)
    {
        ValidateSpace(space);
        Matrix3x3 m = CreateChromaticAdaptation(Definitions[(int)space].WhitePoint, D50WhitePoint);
        return [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];
    }

    private static Matrix3x3 CreateChromaticAdaptation(Vector3 sourceWhite, Vector3 destinationWhite)
    {
        if (sourceWhite == destinationWhite)
        {
            return Matrix3x3.Identity;
        }

        Vector3 sourceCone = Bradford.Transform(sourceWhite);
        Vector3 destinationCone = Bradford.Transform(destinationWhite);
        Matrix3x3 scale = new(
            destinationCone.X / sourceCone.X, 0, 0,
            0, destinationCone.Y / sourceCone.Y, 0,
            0, 0, destinationCone.Z / sourceCone.Z);
        return BradfordInverse * scale * Bradford;
    }

    private static void ValidateSpace(StandardRgbColorSpace space)
    {
        if ((uint)space >= (uint)Definitions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(space), space, "Unknown standard RGB space.");
        }
    }

    private readonly record struct SpaceDefinition(Matrix3x3 RgbToXyz, Vector3 WhitePoint);

    private readonly record struct Chromaticity(float X, float Y);

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
        internal static Matrix3x3 Identity { get; } = new(
            1, 0, 0,
            0, 1, 0,
            0, 0, 1);

        internal Vector3 Transform(Vector3 value) => new(
            M11 * value.X + M12 * value.Y + M13 * value.Z,
            M21 * value.X + M22 * value.Y + M23 * value.Z,
            M31 * value.X + M32 * value.Y + M33 * value.Z);

        internal Matrix3x3 Inverse()
        {
            float c11 = M22 * M33 - M23 * M32;
            float c12 = M13 * M32 - M12 * M33;
            float c13 = M12 * M23 - M13 * M22;
            float c21 = M23 * M31 - M21 * M33;
            float c22 = M11 * M33 - M13 * M31;
            float c23 = M13 * M21 - M11 * M23;
            float c31 = M21 * M32 - M22 * M31;
            float c32 = M12 * M31 - M11 * M32;
            float c33 = M11 * M22 - M12 * M21;
            float determinant = M11 * c11 + M12 * c21 + M13 * c31;
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 1e-8f)
            {
                throw new InvalidOperationException("The standard RGB transform matrix is singular.");
            }
            float inverseDeterminant = 1f / determinant;
            return new Matrix3x3(
                c11 * inverseDeterminant,
                c12 * inverseDeterminant,
                c13 * inverseDeterminant,
                c21 * inverseDeterminant,
                c22 * inverseDeterminant,
                c23 * inverseDeterminant,
                c31 * inverseDeterminant,
                c32 * inverseDeterminant,
                c33 * inverseDeterminant);
        }

        public static Matrix3x3 operator *(Matrix3x3 left, Matrix3x3 right) => new(
            left.M11 * right.M11 + left.M12 * right.M21 + left.M13 * right.M31,
            left.M11 * right.M12 + left.M12 * right.M22 + left.M13 * right.M32,
            left.M11 * right.M13 + left.M12 * right.M23 + left.M13 * right.M33,
            left.M21 * right.M11 + left.M22 * right.M21 + left.M23 * right.M31,
            left.M21 * right.M12 + left.M22 * right.M22 + left.M23 * right.M32,
            left.M21 * right.M13 + left.M22 * right.M23 + left.M23 * right.M33,
            left.M31 * right.M11 + left.M32 * right.M21 + left.M33 * right.M31,
            left.M31 * right.M12 + left.M32 * right.M22 + left.M33 * right.M32,
            left.M31 * right.M13 + left.M32 * right.M23 + left.M33 * right.M33);
    }
}

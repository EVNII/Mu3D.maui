using System.Numerics;
using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>
/// Represents shared color, intensity, and transform state for a punctual light.
/// </summary>
public abstract class PunctualLight : SceneNode
{
    private LinearRgba color;
    private float intensity;

    /// <summary>Initializes the shared state of a transformable punctual light.</summary>
    protected PunctualLight(LinearRgba color, float intensity, string? name)
        : base(name)
    {
        ValidateColor(color);
        ValidateIntensity(intensity);
        this.color = color;
        this.intensity = intensity;
    }

    /// <summary>Gets or sets the opaque linear-light color multiplier.</summary>
    public LinearRgba Color
    {
        get => color;
        set
        {
            ValidateColor(value);
            color = value;
        }
    }

    /// <summary>Gets or sets the non-negative finite intensity.</summary>
    public float Intensity
    {
        get => intensity;
        set
        {
            ValidateIntensity(value);
            intensity = value;
        }
    }

    /// <summary>Gets the world-space origin inherited from the scene-node transform.</summary>
    public Vector3 WorldPosition => Vector3.Transform(Vector3.Zero, WorldMatrix);

    /// <summary>Gets the normalized world-space direction of the local negative Z axis.</summary>
    protected Vector3 WorldNegativeZ
    {
        get
        {
            Vector3 direction = Vector3.TransformNormal(-Vector3.UnitZ, WorldMatrix);
            float lengthSquared = direction.LengthSquared();
            if (!float.IsFinite(lengthSquared) || lengthSquared <= 1e-12f)
            {
                throw new InvalidOperationException("The light world transform has no usable direction.");
            }
            return direction / MathF.Sqrt(lengthSquared);
        }
    }

    private static void ValidateColor(LinearRgba value)
    {
        if (value.ColorSpace is null)
        {
            throw new ArgumentException("The light color must carry a color-space identity.", nameof(value));
        }
        if (value.Alpha != 1f)
        {
            throw new ArgumentException("Light color does not use alpha and must specify one.", nameof(value));
        }
    }

    private static void ValidateIntensity(float value)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Light intensity must be non-negative and finite.");
        }
    }
}

/// <summary>
/// Represents a transformable directional light. Its local negative Z axis is the direction in
/// which light rays travel; translation does not affect directional lighting.
/// </summary>
public sealed class DirectionalLight : PunctualLight
{
    private float angularDiameterRadians = 0.0093f;
    private float shadowOpacity = 1f;

    /// <summary>Initializes a directional light with explicitly tagged linear-light radiance color.</summary>
    public DirectionalLight(
        LinearRgba color,
        float intensity = 1f,
        string? name = null)
        : base(color, intensity, name)
    {
    }

    /// <summary>
    /// Gets or sets whether this light renders the initial single directional shadow map. The
    /// default is false so adding shadows remains an explicit scene cost.
    /// </summary>
    public bool CastsShadows { get; set; }

    /// <summary>
    /// Gets or sets the directional shadow's opacity in the inclusive range zero to one. Zero
    /// leaves this light's direct contribution unshadowed; one preserves the full sampled
    /// shadow-map visibility for that contribution.
    /// </summary>
    public float ShadowOpacity
    {
        get => shadowOpacity;
        set
        {
            if (!float.IsFinite(value) || value is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Shadow opacity must be finite and in the range [0, 1].");
            }
            shadowOpacity = value;
        }
    }

    /// <summary>
    /// Gets or sets the apparent angular diameter of the distant emitter in radians. Zero produces
    /// an ideal point-direction hard shadow; the default 0.0093 radians approximates the Sun as seen
    /// from Earth. Larger values produce wider distance-dependent penumbrae.
    /// </summary>
    public float AngularDiameterRadians
    {
        get => angularDiameterRadians;
        set
        {
            if (!float.IsFinite(value) || value < 0f || value >= MathF.PI)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Angular diameter must be finite and in the range [0, pi).");
            }

            angularDiameterRadians = value;
        }
    }

    /// <summary>Gets the normalized world-space direction in which light rays travel.</summary>
    public Vector3 WorldDirection => WorldNegativeZ;
}

/// <summary>
/// Represents an isotropic point light. Intensity is luminous intensity in candela and physical
/// illumination follows inverse-square distance attenuation.
/// </summary>
public sealed class PointLight : PunctualLight
{
    private float range = float.PositiveInfinity;

    /// <summary>Initializes a transformable point light.</summary>
    public PointLight(LinearRgba color, float intensity = 1f, string? name = null)
        : base(color, intensity, name)
    {
    }

    /// <summary>
    /// Gets or sets the positive finite cutoff distance, or positive infinity for no cutoff.
    /// Transform scale does not change this value.
    /// </summary>
    public float Range
    {
        get => range;
        set
        {
            if (float.IsNaN(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Light range must be positive or infinity.");
            }
            range = value;
        }
    }
}

/// <summary>
/// Represents a point light constrained to a cone along its local negative Z axis.
/// </summary>
public sealed class SpotLight : PunctualLight
{
    private float range = float.PositiveInfinity;
    private float innerConeAngle;
    private float outerConeAngle = MathF.PI / 4f;

    /// <summary>Initializes a transformable spot light.</summary>
    public SpotLight(LinearRgba color, float intensity = 1f, string? name = null)
        : base(color, intensity, name)
    {
    }

    /// <summary>Gets the normalized world-space direction in which light rays travel.</summary>
    public Vector3 WorldDirection => WorldNegativeZ;

    /// <summary>Gets or sets the positive cutoff distance, or infinity for no cutoff.</summary>
    public float Range
    {
        get => range;
        set
        {
            if (float.IsNaN(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Light range must be positive or infinity.");
            }
            range = value;
        }
    }

    /// <summary>Gets or sets the cone angle in radians at which angular falloff begins.</summary>
    public float InnerConeAngle
    {
        get => innerConeAngle;
        set
        {
            ValidateConeAngles(value, outerConeAngle);
            innerConeAngle = value;
        }
    }

    /// <summary>Gets or sets the cone angle in radians at which illumination reaches zero.</summary>
    public float OuterConeAngle
    {
        get => outerConeAngle;
        set
        {
            ValidateConeAngles(innerConeAngle, value);
            outerConeAngle = value;
        }
    }

    private static void ValidateConeAngles(float inner, float outer)
    {
        if (!float.IsFinite(inner) || !float.IsFinite(outer) ||
            inner < 0f || inner >= outer || outer > MathF.PI / 2f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outer),
                "Spot-light angles must satisfy 0 <= inner < outer <= pi/2.");
        }
    }
}

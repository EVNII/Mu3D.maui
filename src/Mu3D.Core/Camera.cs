using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Base class for a transformable FP32 scene camera.</summary>
public abstract class Camera : SceneNode
{
    /// <summary>Initializes a camera node.</summary>
    protected Camera(string? name = null)
        : base(name)
    {
    }

    /// <summary>Gets the FP32 projection matrix.</summary>
    public abstract Matrix4x4 ProjectionMatrix { get; }

    /// <summary>Gets the inverse world matrix used to transform world positions into view space.</summary>
    public Matrix4x4 ViewMatrix => Matrix4x4.Invert(WorldMatrix, out Matrix4x4 view)
        ? view
        : throw new InvalidOperationException("The camera world transform is not invertible.");

    /// <summary>Gets the System.Numerics row-vector view/projection matrix.</summary>
    public Matrix4x4 ViewProjectionMatrix => ViewMatrix * ProjectionMatrix;
}

/// <summary>Represents a right-handed perspective camera with a zero-to-one depth range.</summary>
public sealed class PerspectiveCamera : Camera
{
    private float fieldOfViewRadians;
    private float aspectRatio;
    private float nearClip;
    private float farClip;

    /// <summary>Initializes a perspective camera.</summary>
    public PerspectiveCamera(
        float fieldOfViewRadians = MathF.PI / 3f,
        float aspectRatio = 1f,
        float nearClip = 0.1f,
        float farClip = 1000f,
        string? name = null)
        : base(name)
    {
        Validate(fieldOfViewRadians, aspectRatio, nearClip, farClip);
        this.fieldOfViewRadians = fieldOfViewRadians;
        this.aspectRatio = aspectRatio;
        this.nearClip = nearClip;
        this.farClip = farClip;
    }

    /// <summary>Gets or sets the vertical field of view in radians.</summary>
    public float FieldOfViewRadians
    {
        get => fieldOfViewRadians;
        set
        {
            Validate(value, aspectRatio, nearClip, farClip);
            fieldOfViewRadians = value;
        }
    }

    /// <summary>Gets or sets the positive viewport width/height ratio.</summary>
    public float AspectRatio
    {
        get => aspectRatio;
        set
        {
            Validate(fieldOfViewRadians, value, nearClip, farClip);
            aspectRatio = value;
        }
    }

    /// <summary>Gets or sets the positive near clipping distance.</summary>
    public float NearClip
    {
        get => nearClip;
        set
        {
            Validate(fieldOfViewRadians, aspectRatio, value, farClip);
            nearClip = value;
        }
    }

    /// <summary>Gets or sets the far clipping distance, which must exceed the near distance.</summary>
    public float FarClip
    {
        get => farClip;
        set
        {
            Validate(fieldOfViewRadians, aspectRatio, nearClip, value);
            farClip = value;
        }
    }

    /// <inheritdoc />
    public override Matrix4x4 ProjectionMatrix => Matrix4x4.CreatePerspectiveFieldOfView(
        fieldOfViewRadians,
        aspectRatio,
        nearClip,
        farClip);

    private static void Validate(float fieldOfView, float aspect, float near, float far)
    {
        if (!float.IsFinite(fieldOfView) || fieldOfView <= 0 || fieldOfView >= MathF.PI)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldOfView), "Field of view must be between zero and pi.");
        }
        if (!float.IsFinite(aspect) || aspect <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(aspect), "Aspect ratio must be positive and finite.");
        }
        if (!float.IsFinite(near) || near <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(near), "Near clip must be positive and finite.");
        }
        if (!float.IsFinite(far) || far <= near)
        {
            throw new ArgumentOutOfRangeException(nameof(far), "Far clip must be finite and greater than near clip.");
        }
    }
}

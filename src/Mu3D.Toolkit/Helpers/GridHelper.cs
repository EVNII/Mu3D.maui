using System.Numerics;

namespace Mu3D.Toolkit.Helpers;

/// <summary>Identifies the local plane occupied by a finite grid helper.</summary>
public enum GridHelperPlane
{
    /// <summary>Uses X and Y as the grid axes, with Z as the plane normal.</summary>
    XY,

    /// <summary>Uses X and Z as the grid axes, with Y as the plane normal.</summary>
    XZ,

    /// <summary>Uses Y and Z as the grid axes, with X as the plane normal.</summary>
    YZ,
}

/// <summary>Defines one finite, UI-independent scene-space reference grid.</summary>
/// <remarks>
/// The helper is not a scene node and owns no camera, renderer, input source, or native resource.
/// Its origin, plane, and spacing are expressed in world units. Rendering adapters may borrow it
/// while producing a frame, but it never contributes to scene bounds, selection, or export.
/// </remarks>
public sealed class GridHelper
{
    /// <summary>Maximum supported division count, bounding generated line geometry.</summary>
    public const int MaximumDivisions = 4096;

    private Vector3 origin;
    private float size = 10f;
    private int divisions = 20;
    private int majorLineEvery = 5;

    /// <summary>Gets or sets the grid's finite world-space center.</summary>
    public Vector3 Origin
    {
        get => origin;
        set
        {
            ThrowIfNotFinite(value, nameof(value));
            origin = value;
        }
    }

    /// <summary>Gets or sets the plane whose axes span the grid.</summary>
    public GridHelperPlane Plane { get; set; } = GridHelperPlane.XZ;

    /// <summary>Gets or sets the positive full grid width and height in world units.</summary>
    public float Size
    {
        get => size;
        set
        {
            ThrowIfPositiveFinite(value, nameof(value));
            size = value;
        }
    }

    /// <summary>Gets or sets the positive number of equal intervals along each grid axis.</summary>
    public int Divisions
    {
        get => divisions;
        set
        {
            if (value is < 1 or > MaximumDivisions)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            divisions = value;
        }
    }

    /// <summary>Gets or sets the positive interval between emphasized grid lines.</summary>
    public int MajorLineEvery
    {
        get => majorLineEvery;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            majorLineEvery = value;
        }
    }

    /// <summary>Gets or sets whether non-major grid lines are generated.</summary>
    public bool ShowMinorLines { get; set; } = true;

    /// <summary>Gets or sets whether every <see cref="MajorLineEvery"/> line is emphasized.</summary>
    public bool ShowMajorLines { get; set; } = true;

    /// <summary>Gets or sets whether the two signed world axes through the origin are emphasized.</summary>
    public bool ShowCenterAxes { get; set; } = true;

    internal (Vector3 FirstAxis, Vector3 SecondAxis) GetPlaneAxes()
    {
        if (!Enum.IsDefined(Plane))
        {
            throw new InvalidOperationException($"Unknown grid-helper plane {Plane}.");
        }
        return Plane switch
        {
            GridHelperPlane.XY => (Vector3.UnitX, Vector3.UnitY),
            GridHelperPlane.XZ => (Vector3.UnitX, Vector3.UnitZ),
            GridHelperPlane.YZ => (Vector3.UnitY, Vector3.UnitZ),
            _ => throw new InvalidOperationException($"Unknown grid-helper plane {Plane}."),
        };
    }

    private static void ThrowIfPositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ThrowIfNotFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

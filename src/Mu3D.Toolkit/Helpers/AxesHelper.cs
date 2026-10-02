using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Toolkit.Helpers;

/// <summary>Identifies one stable alignment point inside a viewport overlay.</summary>
public enum ViewportOverlayPlacement
{
    /// <summary>Places the helper at the top-left corner.</summary>
    TopLeft,

    /// <summary>Places the helper at the horizontal center of the top edge.</summary>
    TopCenter,

    /// <summary>Places the helper at the top-right corner.</summary>
    TopRight,

    /// <summary>Places the helper at the vertical center of the left edge.</summary>
    CenterLeft,

    /// <summary>Places the helper at the center of the viewport.</summary>
    Center,

    /// <summary>Places the helper at the vertical center of the right edge.</summary>
    CenterRight,

    /// <summary>Places the helper at the bottom-left corner.</summary>
    BottomLeft,

    /// <summary>Places the helper at the horizontal center of the bottom edge.</summary>
    BottomCenter,

    /// <summary>Places the helper at the bottom-right corner.</summary>
    BottomRight,
}

/// <summary>Identifies the orientation basis displayed by an axes helper.</summary>
public enum AxesHelperSpace
{
    /// <summary>Displays the fixed world X, Y and Z axes.</summary>
    World,

    /// <summary>Displays the world orientation of the explicitly assigned target node.</summary>
    TargetLocal,
}

/// <summary>Identifies a cardinal axis or a two-axis 45-degree view handle.</summary>
public enum AxesHelperAxis
{
    /// <summary>The red X axis.</summary>
    X,

    /// <summary>The green Y axis.</summary>
    Y,

    /// <summary>The blue Z axis.</summary>
    Z,

    /// <summary>A neutral two-axis diagonal handle.</summary>
    Diagonal,
}

/// <summary>Identifies one deterministic camera-direction preset exposed by an axes helper.</summary>
public enum AxesViewPreset
{
    /// <summary>Camera on the positive X axis.</summary>
    PositiveX,

    /// <summary>Camera on the negative X axis.</summary>
    NegativeX,

    /// <summary>Camera on the positive Y axis.</summary>
    PositiveY,

    /// <summary>Camera on the negative Y axis.</summary>
    NegativeY,

    /// <summary>Camera on the positive Z axis.</summary>
    PositiveZ,

    /// <summary>Camera on the negative Z axis.</summary>
    NegativeZ,

    /// <summary>Camera halfway between positive X and positive Y.</summary>
    PositiveXPositiveY,

    /// <summary>Camera halfway between positive X and negative Y.</summary>
    PositiveXNegativeY,

    /// <summary>Camera halfway between negative X and positive Y.</summary>
    NegativeXPositiveY,

    /// <summary>Camera halfway between negative X and negative Y.</summary>
    NegativeXNegativeY,

    /// <summary>Camera halfway between positive X and positive Z.</summary>
    PositiveXPositiveZ,

    /// <summary>Camera halfway between positive X and negative Z.</summary>
    PositiveXNegativeZ,

    /// <summary>Camera halfway between negative X and positive Z.</summary>
    NegativeXPositiveZ,

    /// <summary>Camera halfway between negative X and negative Z.</summary>
    NegativeXNegativeZ,

    /// <summary>Camera halfway between positive Y and positive Z.</summary>
    PositiveYPositiveZ,

    /// <summary>Camera halfway between positive Y and negative Z.</summary>
    PositiveYNegativeZ,

    /// <summary>Camera halfway between negative Y and positive Z.</summary>
    NegativeYPositiveZ,

    /// <summary>Camera halfway between negative Y and negative Z.</summary>
    NegativeYNegativeZ,
}

/// <summary>Describes one accepted axes-helper view handle.</summary>
public sealed class AxesHelperHit
{
    internal AxesHelperHit(
        AxesViewPreset preset,
        AxesHelperAxis axis,
        Vector3 cameraDirection,
        Vector2 screenPositionPixels,
        float screenDistancePixels)
    {
        Preset = preset;
        Axis = axis;
        CameraDirection = cameraDirection;
        ScreenPositionPixels = screenPositionPixels;
        ScreenDistancePixels = screenDistancePixels;
    }

    /// <summary>Gets the deterministic cardinal or 45-degree preset.</summary>
    public AxesViewPreset Preset { get; }

    /// <summary>Gets the cardinal axis or diagonal classification.</summary>
    public AxesHelperAxis Axis { get; }

    /// <summary>Gets the normalized world direction from the orbit target toward the camera.</summary>
    public Vector3 CameraDirection { get; }

    /// <summary>Gets the top-left-origin physical-pixel center of the accepted handle.</summary>
    public Vector2 ScreenPositionPixels { get; }

    /// <summary>Gets the non-negative pointer distance from the accepted handle center.</summary>
    public float ScreenDistancePixels { get; }

    /// <summary>Gets whether this is one of the optional two-axis 45-degree handles.</summary>
    public bool IsDiagonal => Axis == AxesHelperAxis.Diagonal;
}

/// <summary>
/// Defines a fixed-pixel orientation axes overlay independently of UI and graphics backends.
/// </summary>
/// <remarks>
/// The helper borrows an optional orientation target and camera only while calculating a frame or
/// hit test. It owns no scene node, input source, camera controller, renderer, or native resource.
/// Layout calculations reevaluate the current camera and borrowed target without retaining a
/// previous projection. The explicit target supplies orientation independently of scene membership
/// or visibility.
/// </remarks>
public sealed class AxesHelper
{
    internal const int MaximumHandleCount = 18;

    private static readonly HandleDefinition[] HandleDefinitions =
    [
        new(AxesViewPreset.PositiveX, AxesHelperAxis.X, Vector3.UnitX, true),
        new(AxesViewPreset.NegativeX, AxesHelperAxis.X, -Vector3.UnitX, false),
        new(AxesViewPreset.PositiveY, AxesHelperAxis.Y, Vector3.UnitY, true),
        new(AxesViewPreset.NegativeY, AxesHelperAxis.Y, -Vector3.UnitY, false),
        new(AxesViewPreset.PositiveZ, AxesHelperAxis.Z, Vector3.UnitZ, true),
        new(AxesViewPreset.NegativeZ, AxesHelperAxis.Z, -Vector3.UnitZ, false),
        Diagonal(AxesViewPreset.PositiveXPositiveY, 1f, 1f, 0f),
        Diagonal(AxesViewPreset.PositiveXNegativeY, 1f, -1f, 0f),
        Diagonal(AxesViewPreset.NegativeXPositiveY, -1f, 1f, 0f),
        Diagonal(AxesViewPreset.NegativeXNegativeY, -1f, -1f, 0f),
        Diagonal(AxesViewPreset.PositiveXPositiveZ, 1f, 0f, 1f),
        Diagonal(AxesViewPreset.PositiveXNegativeZ, 1f, 0f, -1f),
        Diagonal(AxesViewPreset.NegativeXPositiveZ, -1f, 0f, 1f),
        Diagonal(AxesViewPreset.NegativeXNegativeZ, -1f, 0f, -1f),
        Diagonal(AxesViewPreset.PositiveYPositiveZ, 0f, 1f, 1f),
        Diagonal(AxesViewPreset.PositiveYNegativeZ, 0f, 1f, -1f),
        Diagonal(AxesViewPreset.NegativeYPositiveZ, 0f, -1f, 1f),
        Diagonal(AxesViewPreset.NegativeYNegativeZ, 0f, -1f, -1f),
    ];

    private float screenSizePixels = 112f;
    private float marginPixels = 14f;

    /// <summary>Gets or sets the corner containing the helper.</summary>
    public ViewportOverlayPlacement Placement { get; set; } = ViewportOverlayPlacement.TopRight;

    /// <summary>Gets or sets the positive diameter of the helper in physical pixels.</summary>
    public float ScreenSizePixels
    {
        get => screenSizePixels;
        set
        {
            ThrowIfPositiveFinite(value, nameof(value));
            screenSizePixels = value;
        }
    }

    /// <summary>Gets or sets the non-negative physical-pixel distance from the selected corner.</summary>
    public float MarginPixels
    {
        get => marginPixels;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            marginPixels = value;
        }
    }

    /// <summary>Gets or sets whether negative cardinal directions are displayed and hit-testable.</summary>
    public bool ShowNegativeAxes { get; set; } = true;

    /// <summary>Gets or sets whether the twelve two-axis 45-degree handles are displayed.</summary>
    public bool ShowDiagonalViews { get; set; } = true;

    /// <summary>Gets or sets world or target-local orientation.</summary>
    public AxesHelperSpace Space { get; set; }

    /// <summary>Gets or sets the borrowed target whose world orientation defines target-local axes.</summary>
    public SceneNode? OrientationTarget { get; set; }

    internal AxesHelperLayout CalculateLayout(
        PerspectiveCamera camera,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        Span<AxesHelperHandleLayout> handles)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfZero(viewportWidthPixels);
        ArgumentOutOfRangeException.ThrowIfZero(viewportHeightPixels);
        if (!Enum.IsDefined(Placement))
        {
            throw new InvalidOperationException($"Unknown axes-helper placement {Placement}.");
        }
        if (!Enum.IsDefined(Space))
        {
            throw new InvalidOperationException($"Unknown axes-helper space {Space}.");
        }
        if (handles.Length < MaximumHandleCount)
        {
            throw new ArgumentException("Axes-helper layout requires capacity for every handle.", nameof(handles));
        }

        float halfSize = screenSizePixels * 0.5f;
        float centerX = Placement switch
        {
            ViewportOverlayPlacement.TopLeft or
            ViewportOverlayPlacement.CenterLeft or
            ViewportOverlayPlacement.BottomLeft => marginPixels + halfSize,
            ViewportOverlayPlacement.TopCenter or
            ViewportOverlayPlacement.Center or
            ViewportOverlayPlacement.BottomCenter => viewportWidthPixels * 0.5f,
            _ => viewportWidthPixels - marginPixels - halfSize,
        };
        float centerY = Placement switch
        {
            ViewportOverlayPlacement.TopLeft or
            ViewportOverlayPlacement.TopCenter or
            ViewportOverlayPlacement.TopRight => marginPixels + halfSize,
            ViewportOverlayPlacement.CenterLeft or
            ViewportOverlayPlacement.Center or
            ViewportOverlayPlacement.CenterRight => viewportHeightPixels * 0.5f,
            _ => viewportHeightPixels - marginPixels - halfSize,
        };
        centerX = ClampCenter(centerX, halfSize, viewportWidthPixels);
        centerY = ClampCenter(centerY, halfSize, viewportHeightPixels);
        Vector2 center = new(centerX, centerY);
        float axisRadius = screenSizePixels * 0.34f;

        (Vector3 xAxis, Vector3 yAxis, Vector3 zAxis) = GetWorldBasis();
        Matrix4x4 view = camera.ViewMatrix;
        int handleCount = 0;
        foreach (HandleDefinition definition in HandleDefinitions)
        {
            bool isDiagonal = definition.Axis == AxesHelperAxis.Diagonal;
            if ((!definition.IsPositive && !isDiagonal && !ShowNegativeAxes) ||
                (isDiagonal && !ShowDiagonalViews))
            {
                continue;
            }

            Vector3 worldDirection = Vector3.Normalize(
                xAxis * definition.LocalDirection.X +
                yAxis * definition.LocalDirection.Y +
                zAxis * definition.LocalDirection.Z);
            Vector3 viewDirection = Vector3.TransformNormal(worldDirection, view);
            ThrowIfFiniteNonZero(viewDirection, "The axes-helper view direction is invalid.");
            viewDirection = Vector3.Normalize(viewDirection);
            float radius = isDiagonal ? axisRadius * 0.78f : axisRadius;
            Vector2 position = center + new Vector2(viewDirection.X, -viewDirection.Y) * radius;
            handles[handleCount++] = new AxesHelperHandleLayout(
                definition.Preset,
                definition.Axis,
                definition.IsPositive,
                worldDirection,
                position,
                viewDirection.Z);
        }
        return new AxesHelperLayout(center, halfSize, handleCount);
    }

    private (Vector3 X, Vector3 Y, Vector3 Z) GetWorldBasis()
    {
        if (Space == AxesHelperSpace.World)
        {
            return (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        }

        SceneNode target = OrientationTarget ??
            throw new InvalidOperationException(
                "Target-local axes require an explicit OrientationTarget.");
        Matrix4x4 world = target.WorldMatrix;
        Vector3 x = Vector3.TransformNormal(Vector3.UnitX, world);
        Vector3 y = Vector3.TransformNormal(Vector3.UnitY, world);
        Vector3 z = Vector3.TransformNormal(Vector3.UnitZ, world);
        ThrowIfFiniteNonZero(x, "The target-local X axis is degenerate.");
        ThrowIfFiniteNonZero(y, "The target-local Y axis is degenerate.");
        ThrowIfFiniteNonZero(z, "The target-local Z axis is degenerate.");
        return (Vector3.Normalize(x), Vector3.Normalize(y), Vector3.Normalize(z));
    }

    private static HandleDefinition Diagonal(
        AxesViewPreset preset,
        float x,
        float y,
        float z) => new(
            preset,
            AxesHelperAxis.Diagonal,
            Vector3.Normalize(new Vector3(x, y, z)),
            true);

    private static float ClampCenter(float preferred, float halfSize, uint extent)
    {
        if (extent <= halfSize * 2f)
        {
            return extent * 0.5f;
        }
        return Math.Clamp(preferred, halfSize, extent - halfSize);
    }

    private static void ThrowIfPositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ThrowIfFiniteNonZero(Vector3 value, string message)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) || value.LengthSquared() <= float.Epsilon)
        {
            throw new InvalidOperationException(message);
        }
    }

    private readonly record struct HandleDefinition(
        AxesViewPreset Preset,
        AxesHelperAxis Axis,
        Vector3 LocalDirection,
        bool IsPositive);
}

/// <summary>Hit-tests visible axes-helper markers in physical viewport pixels.</summary>
public sealed class AxesHelperHitTester
{
    private float hitTolerancePixels = 12f;

    /// <summary>Initializes a hit tester with a twelve-physical-pixel tolerance.</summary>
    public AxesHelperHitTester()
    {
    }

    /// <summary>Initializes a hit tester with an explicit positive physical-pixel tolerance.</summary>
    /// <param name="hitTolerancePixels">Maximum pointer distance from a handle center.</param>
    public AxesHelperHitTester(float hitTolerancePixels) => HitTolerancePixels = hitTolerancePixels;

    /// <summary>Gets or sets the positive physical-pixel marker hit tolerance.</summary>
    public float HitTolerancePixels
    {
        get => hitTolerancePixels;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            hitTolerancePixels = value;
        }
    }

    /// <summary>Returns the closest visible view handle, or null when the pointer missed.</summary>
    /// <param name="helper">Borrowed axes-helper definition.</param>
    /// <param name="camera">Camera defining the current on-screen orientation.</param>
    /// <param name="viewportWidthPixels">Positive physical-pixel viewport width.</param>
    /// <param name="viewportHeightPixels">Positive physical-pixel viewport height.</param>
    /// <param name="viewportPositionPixels">Top-left-origin physical-pixel pointer position.</param>
    /// <returns>The accepted closest handle, or <see langword="null"/>.</returns>
    public AxesHelperHit? HitTest(
        AxesHelper helper,
        PerspectiveCamera camera,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        Vector2 viewportPositionPixels)
    {
        ArgumentNullException.ThrowIfNull(helper);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfZero(viewportWidthPixels);
        ArgumentOutOfRangeException.ThrowIfZero(viewportHeightPixels);
        if (!float.IsFinite(viewportPositionPixels.X) ||
            !float.IsFinite(viewportPositionPixels.Y) ||
            viewportPositionPixels.X < 0f || viewportPositionPixels.Y < 0f ||
            viewportPositionPixels.X > viewportWidthPixels ||
            viewportPositionPixels.Y > viewportHeightPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportPositionPixels));
        }

        AxesHelperHandleLayout? best = null;
        float bestDistance = float.MaxValue;
        Span<AxesHelperHandleLayout> handles = stackalloc AxesHelperHandleLayout[AxesHelper.MaximumHandleCount];
        AxesHelperLayout layout = helper.CalculateLayout(
            camera,
            viewportWidthPixels,
            viewportHeightPixels,
            handles);
        foreach (AxesHelperHandleLayout handle in handles[..layout.HandleCount])
        {
            float distance = Vector2.Distance(viewportPositionPixels, handle.ScreenPositionPixels);
            float tolerance = handle.Axis == AxesHelperAxis.Diagonal
                ? hitTolerancePixels * 0.75f
                : hitTolerancePixels;
            if (distance > tolerance || !IsBetter(handle, distance, best, bestDistance))
            {
                continue;
            }
            best = handle;
            bestDistance = distance;
        }

        return best is AxesHelperHandleLayout accepted
            ? new AxesHelperHit(
                accepted.Preset,
                accepted.Axis,
                accepted.CameraDirection,
                accepted.ScreenPositionPixels,
                bestDistance)
            : null;
    }

    private static bool IsBetter(
        AxesHelperHandleLayout candidate,
        float distance,
        AxesHelperHandleLayout? best,
        float bestDistance)
    {
        const float epsilon = 0.0001f;
        if (best is null || distance < bestDistance - epsilon)
        {
            return true;
        }
        if (MathF.Abs(distance - bestDistance) > epsilon)
        {
            return false;
        }
        AxesHelperHandleLayout accepted = best.Value;
        bool candidateCardinal = candidate.Axis != AxesHelperAxis.Diagonal;
        bool acceptedCardinal = accepted.Axis != AxesHelperAxis.Diagonal;
        if (candidateCardinal != acceptedCardinal)
        {
            return candidateCardinal;
        }
        return candidate.ViewDepth > accepted.ViewDepth;
    }
}

internal readonly record struct AxesHelperLayout(
    Vector2 CenterPixels,
    float BackgroundRadiusPixels,
    int HandleCount);

internal readonly record struct AxesHelperHandleLayout(
    AxesViewPreset Preset,
    AxesHelperAxis Axis,
    bool IsPositive,
    Vector3 CameraDirection,
    Vector2 ScreenPositionPixels,
    float ViewDepth);

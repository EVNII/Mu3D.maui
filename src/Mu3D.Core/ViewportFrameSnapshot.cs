using System.Numerics;

namespace Mu3D.Rendering;

/// <summary>
/// Captures the exact camera matrices and viewport extents associated with one successfully
/// presented frame.
/// </summary>
public sealed class ViewportFrameSnapshot
{
    /// <summary>Initializes an immutable viewport frame snapshot.</summary>
    /// <param name="frameId">A host-defined monotonically increasing successful-frame identity.</param>
    /// <param name="viewMatrix">The finite world-to-view matrix used by the frame.</param>
    /// <param name="projectionMatrix">The finite view-to-clip matrix used by the frame.</param>
    /// <param name="pixelWidth">The non-zero physical-pixel viewport width.</param>
    /// <param name="pixelHeight">The non-zero physical-pixel viewport height.</param>
    /// <param name="logicalWidth">The positive finite logical viewport width.</param>
    /// <param name="logicalHeight">The positive finite logical viewport height.</param>
    public ViewportFrameSnapshot(
        ulong frameId,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix,
        uint pixelWidth,
        uint pixelHeight,
        double logicalWidth,
        double logicalHeight)
    {
        ValidateFinite(viewMatrix, nameof(viewMatrix));
        ValidateFinite(projectionMatrix, nameof(projectionMatrix));
        ArgumentOutOfRangeException.ThrowIfZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfZero(pixelHeight);
        if (!double.IsFinite(logicalWidth) || logicalWidth <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        }
        if (!double.IsFinite(logicalHeight) || logicalHeight <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalHeight));
        }

        FrameId = frameId;
        ViewMatrix = viewMatrix;
        ProjectionMatrix = projectionMatrix;
        ViewProjectionMatrix = viewMatrix * projectionMatrix;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
    }

    /// <summary>Gets the host-defined successful-frame identity.</summary>
    public ulong FrameId { get; }

    /// <summary>Gets the captured world-to-view matrix.</summary>
    public Matrix4x4 ViewMatrix { get; }

    /// <summary>Gets the captured view-to-clip matrix.</summary>
    public Matrix4x4 ProjectionMatrix { get; }

    /// <summary>Gets the captured System.Numerics row-vector world-to-clip matrix.</summary>
    public Matrix4x4 ViewProjectionMatrix { get; }

    /// <summary>Gets the physical-pixel viewport width.</summary>
    public uint PixelWidth { get; }

    /// <summary>Gets the physical-pixel viewport height.</summary>
    public uint PixelHeight { get; }

    /// <summary>Gets the logical viewport width used by host UI placement.</summary>
    public double LogicalWidth { get; }

    /// <summary>Gets the logical viewport height used by host UI placement.</summary>
    public double LogicalHeight { get; }

    /// <summary>Gets the physical pixels per logical unit on the horizontal axis.</summary>
    public double DisplayScaleX => PixelWidth / LogicalWidth;

    /// <summary>Gets the physical pixels per logical unit on the vertical axis.</summary>
    public double DisplayScaleY => PixelHeight / LogicalHeight;

    /// <summary>
    /// Projects a finite world-space point into the captured physical and logical viewport.
    /// </summary>
    /// <param name="worldPosition">The finite world-space point.</param>
    /// <param name="projection">
    /// The projected coordinates and depth when the point is in front of the camera.
    /// </param>
    /// <returns>
    /// True when projection is finite and the point is in front of the camera; inspect
    /// <see cref="ViewportProjection.IsInsideViewport"/> separately for clipping.
    /// </returns>
    public bool TryProject(Vector3 worldPosition, out ViewportProjection projection)
    {
        projection = default;
        if (!IsFinite(worldPosition))
        {
            return false;
        }

        Vector4 clip = Vector4.Transform(new Vector4(worldPosition, 1f), ViewProjectionMatrix);
        if (!IsFinite(clip) || clip.W <= 1e-7f)
        {
            return false;
        }

        float inverseW = 1f / clip.W;
        Vector3 normalizedDevice = new(clip.X * inverseW, clip.Y * inverseW, clip.Z * inverseW);
        if (!IsFinite(normalizedDevice))
        {
            return false;
        }

        double normalizedX = normalizedDevice.X * 0.5d + 0.5d;
        double normalizedY = 0.5d - normalizedDevice.Y * 0.5d;
        projection = new ViewportProjection(
            new Vector2((float)(normalizedX * PixelWidth), (float)(normalizedY * PixelHeight)),
            new ViewportPoint(normalizedX * LogicalWidth, normalizedY * LogicalHeight),
            normalizedDevice.Z,
            normalizedDevice.X is >= -1f and <= 1f &&
            normalizedDevice.Y is >= -1f and <= 1f &&
            normalizedDevice.Z is >= 0f and <= 1f);
        return true;
    }

    private static void ValidateFinite(Matrix4x4 matrix, string parameterName)
    {
        ReadOnlySpan<float> elements =
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        ];
        foreach (float element in elements)
        {
            if (!float.IsFinite(element))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Matrices must be finite.");
            }
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);
}

/// <summary>Stores one world point projected into physical and logical viewport coordinates.</summary>
/// <param name="PixelPosition">The top-left-origin physical-pixel coordinate.</param>
/// <param name="LogicalPosition">The top-left-origin logical host-UI coordinate.</param>
/// <param name="Depth">The normalized zero-to-one depth when inside the clip volume.</param>
/// <param name="IsInsideViewport">Whether X, Y and depth lie inside the captured clip volume.</param>
public readonly record struct ViewportProjection(
    Vector2 PixelPosition,
    ViewportPoint LogicalPosition,
    float Depth,
    bool IsInsideViewport);

/// <summary>Stores a backend- and UI-framework-independent two-dimensional double coordinate.</summary>
/// <param name="X">The horizontal component.</param>
/// <param name="Y">The vertical component.</param>
public readonly record struct ViewportPoint(double X, double Y);

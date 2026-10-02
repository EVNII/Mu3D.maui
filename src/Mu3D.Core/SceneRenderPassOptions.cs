using Mu3D.Color;
using Mu3D.Graphics;

namespace Mu3D.Rendering;

/// <summary>Identifies how a render pass derives the extent of its attachments.</summary>
public enum RenderPassSizeDependency
{
    /// <summary>The pass follows the complete output-target extent.</summary>
    OutputExtent,

    /// <summary>The pass owns an extent independent of the final output.</summary>
    Independent,
}

/// <summary>
/// Describes explicit color and depth initialization for one scene-linear HDR render pass.
/// </summary>
/// <remarks>
/// This contract performs no display encoding, gamut mapping, tone mapping, or clipping. A clear
/// color carries an explicit linear-light color-space identity and is converted into the renderer's
/// working space only when the color attachment is cleared.
/// </remarks>
public sealed class SceneRenderPassOptions
{
    private static readonly LinearRgba DefaultClearColor = new(
        0f,
        0f,
        0f,
        1f,
        StandardColorSpaces.LinearSrgb);

    /// <summary>Initializes scene-pass attachment behavior.</summary>
    /// <param name="colorLoadOperation">Whether existing color is preserved or replaced.</param>
    /// <param name="clearColor">The explicitly tagged linear-light color used when clearing.</param>
    /// <param name="depthLoadOperation">Whether existing depth is preserved or replaced.</param>
    /// <param name="clearDepth">The normalized depth used when clearing.</param>
    public SceneRenderPassOptions(
        GraphicsLoadOperation colorLoadOperation,
        LinearRgba clearColor,
        GraphicsLoadOperation depthLoadOperation = GraphicsLoadOperation.Clear,
        float clearDepth = 1f)
    {
        if (!Enum.IsDefined(colorLoadOperation))
        {
            throw new ArgumentOutOfRangeException(nameof(colorLoadOperation));
        }
        if (!Enum.IsDefined(depthLoadOperation))
        {
            throw new ArgumentOutOfRangeException(nameof(depthLoadOperation));
        }
        if (clearColor.ColorSpace is null)
        {
            throw new ArgumentException(
                "A scene-pass clear color must have an explicit linear color-space identity.",
                nameof(clearColor));
        }
        if (!float.IsFinite(clearDepth) || clearDepth is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clearDepth),
                "A depth clear value must be finite and between zero and one.");
        }

        ColorLoadOperation = colorLoadOperation;
        ClearColor = clearColor;
        DepthLoadOperation = depthLoadOperation;
        ClearDepth = clearDepth;
    }

    /// <summary>Gets the default opaque linear-sRGB black color/depth clear policy.</summary>
    public static SceneRenderPassOptions Default { get; } = new(
        GraphicsLoadOperation.Clear,
        DefaultClearColor);

    /// <summary>Gets whether the color attachment is loaded or cleared.</summary>
    public GraphicsLoadOperation ColorLoadOperation { get; }

    /// <summary>Gets the explicitly tagged linear-light color used when clearing.</summary>
    public LinearRgba ClearColor { get; }

    /// <summary>Gets whether the depth attachment is loaded or cleared.</summary>
    public GraphicsLoadOperation DepthLoadOperation { get; }

    /// <summary>Gets the normalized value used when clearing depth.</summary>
    public float ClearDepth { get; }

    /// <summary>Gets the scene pass's output-size dependency.</summary>
    public RenderPassSizeDependency SizeDependency => RenderPassSizeDependency.OutputExtent;
}

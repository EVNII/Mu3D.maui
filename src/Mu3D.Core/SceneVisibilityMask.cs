namespace Mu3D.SceneGraph;

/// <summary>
/// Identifies any combination of the thirty-two backend-independent scene visibility layers.
/// </summary>
/// <remarks>
/// Layer zero is the default layer. A scene node is visible to a camera when their masks intersect;
/// the mask does not alter hierarchy traversal or application selection policy.
/// </remarks>
public readonly record struct SceneVisibilityMask
{
    /// <summary>The number of addressable visibility layers.</summary>
    public const int LayerCount = 32;

    /// <summary>Initializes a mask from its complete bit field.</summary>
    /// <param name="value">The enabled layer bits.</param>
    public SceneVisibilityMask(uint value) => Value = value;

    /// <summary>Gets a mask with no enabled layers.</summary>
    public static SceneVisibilityMask None => new(0u);

    /// <summary>Gets the default mask containing only layer zero.</summary>
    public static SceneVisibilityMask Default => new(1u);

    /// <summary>Gets a mask containing every layer.</summary>
    public static SceneVisibilityMask All => new(uint.MaxValue);

    /// <summary>Gets the complete enabled-layer bit field.</summary>
    public uint Value { get; }

    /// <summary>Creates a mask containing one layer.</summary>
    /// <param name="layer">The zero-based layer index.</param>
    /// <returns>A mask containing only <paramref name="layer"/>.</returns>
    public static SceneVisibilityMask FromLayer(int layer) => new(LayerBit(layer));

    /// <summary>Returns whether this mask and another mask share at least one enabled layer.</summary>
    public bool Intersects(SceneVisibilityMask other) => (Value & other.Value) != 0u;

    /// <summary>Returns whether one layer is enabled.</summary>
    /// <param name="layer">The zero-based layer index.</param>
    public bool IsEnabled(int layer) => (Value & LayerBit(layer)) != 0u;

    /// <summary>Returns a copy with one layer enabled.</summary>
    /// <param name="layer">The zero-based layer index.</param>
    public SceneVisibilityMask Enable(int layer) => new(Value | LayerBit(layer));

    /// <summary>Returns a copy with one layer disabled.</summary>
    /// <param name="layer">The zero-based layer index.</param>
    public SceneVisibilityMask Disable(int layer) => new(Value & ~LayerBit(layer));

    /// <summary>Returns the union of two visibility masks.</summary>
    public static SceneVisibilityMask operator |(
        SceneVisibilityMask left,
        SceneVisibilityMask right) => new(left.Value | right.Value);

    /// <summary>Returns the intersection of two visibility masks.</summary>
    public static SceneVisibilityMask operator &(
        SceneVisibilityMask left,
        SceneVisibilityMask right) => new(left.Value & right.Value);

    private static uint LayerBit(int layer)
    {
        if ((uint)layer >= LayerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(layer),
                $"A visibility layer must be between zero and {LayerCount - 1}.");
        }

        return 1u << layer;
    }
}

namespace Mu3D.SceneGraph;

/// <summary>
/// Associates versioned OpenPBR Surface authoring data with a scene mesh without converting it to
/// the glTF metallic/roughness material model.
/// </summary>
/// <remarks>
/// An OpenPBR render pass evaluates this material. The default scene renderer rejects it instead
/// of silently selecting an approximate metallic/roughness preview. Authoring data is borrowed by
/// reference; scene-asset snapshots clone the surface to isolate independent mutable instances.
/// OpenPBR transport uses <see cref="OpenPbrSurface.GeometryOpacity"/> and
/// <see cref="OpenPbrSurface.GeometryThinWalled"/> for coverage and interface behavior. Inherited
/// raster alpha-cutoff, alpha-mode and face-culling metadata do not replace those inputs; closed
/// medium traversal must intersect both entry and exit faces.
/// </remarks>
public sealed class OpenPbrMaterial : Material
{
    private OpenPbrSurface surface;
    private float? nitsPerSceneUnit;

    /// <summary>Initializes an OpenPBR material with explicitly supplied authoring data.</summary>
    /// <param name="surface">The non-null mutable authoring surface retained by reference.</param>
    /// <param name="name">The optional material name; defaults to the surface's current name.</param>
    public OpenPbrMaterial(OpenPbrSurface surface, string? name = null)
        : base(name ?? surface?.Name)
    {
        ArgumentNullException.ThrowIfNull(surface);
        this.surface = surface;
    }

    /// <inheritdoc />
    public override MaterialBaseModel BaseModel => MaterialBaseModel.OpenPbr;

    /// <summary>
    /// Gets or sets the non-null mutable OpenPBR inputs retained by reference. Sharing this surface
    /// between materials deliberately shares subsequent authoring edits.
    /// </summary>
    public OpenPbrSurface Surface
    {
        get => surface;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            surface = value;
        }
    }

    /// <summary>
    /// Gets or sets the positive finite luminance in nits represented by one scene radiance unit.
    /// Null leaves the conversion unspecified; an OpenPBR evaluator must reject active emission
    /// until the application supplies this scale. Nonemissive surfaces need no luminance scale.
    /// </summary>
    public float? NitsPerSceneUnit
    {
        get => nitsPerSceneUnit;
        set
        {
            if (value is float scale && (!float.IsFinite(scale) || scale <= 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), "The luminance scale must be positive and finite when supplied.");
            }
            nitsPerSceneUnit = value;
        }
    }

    internal OpenPbrMaterial CloneForSceneInstance()
    {
        OpenPbrMaterial clone = new(surface.Clone(), Name)
        {
            NitsPerSceneUnit = nitsPerSceneUnit,
        };
        CopyInstanceStateTo(clone);
        return clone;
    }
}

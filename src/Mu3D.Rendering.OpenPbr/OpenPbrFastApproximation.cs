namespace Mu3D.Rendering.OpenPbr;

/// <summary>Identifies the documented approximations Fast mode applies to one surface.</summary>
/// <remarks>
/// Every approximation is explicit and reported; nothing is silently dropped. Base specular,
/// diffuse and coat environment terms always use the split-sum prefiltered chains by definition
/// of the mode and are not individually flagged. See <see cref="OpenPbrRenderMode.Fast"/>.
/// </remarks>
[Flags]
public enum OpenPbrFastApproximationKinds
{
    /// <summary>The surface's active inputs match the reference modes' lobe set.</summary>
    None = 0,

    /// <summary>The fuzz lobe's environment contribution uses the Charlie sheen chain and its directional-albedo table.</summary>
    /// <remarks>Direct punctual lighting still evaluates the pinned fuzz closure.</remarks>
    FuzzEnvironmentCharlieChain = 1,

    /// <summary>The coat lobe's environment contribution uses the base GGX chain at coat roughness.</summary>
    CoatEnvironmentBaseGgxChain = 2,

    /// <summary>The subsurface lobe is dropped entirely; its weight does not scatter or tint any term.</summary>
    SubsurfaceDropped = 4,

    /// <summary>The thin-film lobe is dropped entirely; its iridescence does not modulate any term.</summary>
    ThinFilmDropped = 8,

    /// <summary>Connected inputs are baked to mipmapped textures sampled with hardware filtering and repeat addressing.</summary>
    /// <remarks>Reference modes evaluate the graph per texel with level-zero nearest/bilinear and explicit
    /// periodic/clamp/mirror addressing. Baking fixes the result onto the UV unit square at the largest
    /// connected source resolution, so clamp/mirror behavior outside that square and magnification above
    /// the bake resolution follow the baked texture instead of the source texels.</remarks>
    BakedGraphTextures = 16,
}

/// <summary>Reports the Fast-mode approximations and baked-texture footprint of one compiled material.</summary>
public sealed class OpenPbrFastApproximation
{
    internal OpenPbrFastApproximation(int materialIndex, string? materialName,
        OpenPbrFastApproximationKinds kinds, int bakedTextureCount, long bakedBytes)
    {
        MaterialIndex = materialIndex;
        MaterialName = materialName;
        Kinds = kinds;
        BakedTextureCount = bakedTextureCount;
        BakedBytes = bakedBytes;
    }

    /// <summary>Gets the material's index in the compiled scene's material order.</summary>
    public int MaterialIndex { get; }

    /// <summary>Gets the material's optional authoring name.</summary>
    public string? MaterialName { get; }

    /// <summary>Gets the approximations active on this surface.</summary>
    public OpenPbrFastApproximationKinds Kinds { get; }

    /// <summary>Gets how many mipmapped textures this surface's connected inputs were baked into.</summary>
    public int BakedTextureCount { get; }

    /// <summary>Gets this surface's baked FP16 texture bytes including mip chains.</summary>
    public long BakedBytes { get; }
}

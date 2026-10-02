namespace Mu3D.Rendering.OpenPbr;

/// <summary>Selects an explicit OpenPBR visibility and light-transport policy.</summary>
public enum OpenPbrRenderMode
{
    /// <summary>Traces a bounded sample for every frame without retaining history across frames.</summary>
    Interactive,

    /// <summary>Accumulates independent FP32 samples until the scene, camera or settings change.</summary>
    Reference,

    /// <summary>Rasterizes opaque primary visibility with direct lights, optional directional shadow maps and deterministic environment quadrature.</summary>
    /// <remarks>No secondary rays, interreflection, transmission, subsurface scattering or partial opacity.
    /// Unsupported surface inputs throw instead of silently losing their transport.</remarks>
    Raster,

    /// <summary>Rasterizes primary visibility, then progressively accumulates bounded secondary RGB transport and ray shadows.</summary>
    /// <remarks>Uses pixel-center primary samples. Camera media or bounds crossing the near plane use primary
    /// ray traversal to retain medium boundaries; see <see cref="OpenPbrRenderPass.UsesPrimaryRayFallback"/>.</remarks>
    Hybrid,

    /// <summary>Rasterizes primary visibility and shades with pinned-closure direct lights plus split-sum image-based lighting.</summary>
    /// <remarks>The one explicitly approximate mode: the environment uses a prefiltered GGX/Charlie cube chain and a
    /// split-sum BRDF LUT instead of per-pixel closure quadrature, connected material graphs are baked to mipmapped,
    /// hardware-filtered textures at scene-compile time, and the fuzz, subsurface, thin-film and dispersion lobes
    /// receive documented mappings or dropout reported per surface through <see cref="OpenPbrRenderPass.FastApproximations"/>.
    /// Transmission and partial opacity remain unsupported and throw rather than degrading silently. Direct punctual
    /// lighting still evaluates the pinned closure. The conformance default remains <see cref="Raster"/>.</remarks>
    Fast,
}

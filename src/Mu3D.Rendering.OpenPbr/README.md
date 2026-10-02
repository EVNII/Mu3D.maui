# Mu3D.Rendering.OpenPbr

Optional backend-independent OpenPBR rendering for Mu3D. `OpenPbrRenderPass` implements `IRenderPass`
and provides a reusable `Pipeline` for `Mu3DSceneView.RenderPipeline`. The host retains surface,
frame scheduling and output policy ownership; dispose the pass when the host no longer uses it.

Install this package explicitly alongside `Mu3D.Maui` to use the full renderer. `Mu3D.Maui`,
Core and the default native backend do not depend on this package. Installing only Mu3D.Maui
does not include the OpenPBR BSDF shaders, LUT or precompiled bytecode, and never prepares an
OpenPBR pipeline. MaterialX import/export is a separate optional `Mu3D.Formats.MaterialX` package.
Lightweight material authoring types remain in Core/MAUI for interchange and the explicit PBR
preview; they do not load or activate this renderer. No reflection-based extension discovery is used.

For responsive first entry, await `pass.PrepareAsync(device, outputFormat, cancellationToken)`
from `Mu3DSceneView.PrepareGraphicsAsync` before its automatic session is published. Use the
actual scene attachment format (normally Rgba16Float when a display view is selected). The
control keeps the device alive through cancellation and completion. Standalone callers must do
the same and must not call Execute/change preparation settings concurrently. Disposing the pass
abandons its result; native compilation may continue until safe cleanup. See ADR 0033/public
OpenPBR guidance for measured startup costs. Raster uses a dedicated full-BSDF entry, and resizing
retains pipelines. The matching Windows backend loads bundled DXIL before its per-user disk cache
and compiler fallback. This package alone deploys the Windows bytecode; no registration or consumer
shader compiler is required. Unsupported variants, changed compiler/backend versions and a new GPU's
driver compilation can still cost time, so retain asynchronous preparation. Android/Apple keep their
existing backend behavior. Cache failure never prevents normal compilation.

```csharp
using Mu3D.Rendering.OpenPbr;

var pass = new OpenPbrRenderPass {
    Mode = OpenPbrRenderMode.Reference,
    ReferenceMaxBounces = 32,
};
sceneView.RenderPipeline = pass.Pipeline;
```

Use Core `OpenPbrMaterial` or MAUI `OpenPbrMaterial3D` with `OpenPbrSurface3D`. All 41 OpenPBR 1.1.1
constant or connected inputs reach the pinned Adobe BSDF. `OpenPbrPreviewMaterial3D` remains the separately named
metallic/roughness approximation. `.mtlx` interchange belongs to `Mu3D.Formats.MaterialX`.

## Five explicit modes

- Interactive: independent sample per pixel per frame, four path events by default, half linear
  resolution by default; configurable resolution and event budget. It does not retain stale history
  during motion. Noise and frame time depend on scene, resolution and hardware; no frame-rate
  guarantee or implicit denoiser is supplied.
- Reference: full resolution, 32 path events by default, FP32 progressive mean. Camera, geometry,
  lights, material inputs, environment and mode changes reset history. The application schedules
  each additional frame and can inspect `ReferenceSamples` or call `ResetAccumulation()`.
- Raster: full-resolution triangle/depth visibility; deterministic punctual lights and
  fixed BSDF environment quadrature (`RasterEnvironmentSamples`, default 16, range 1–64). Finite
  suns use their central direction. Optional directional shadow maps; no interreflection or volume transport. Surfaces with
  nonzero transmission/subsurface or opacity below one throw; select Hybrid/Reference instead.
  Fixed quadrature is stable but can miss small bright environment features. No material conversion.
  Raster remains the conformance default.
- Hybrid: full-resolution raster primary visibility, then the same secondary RGB transport and ray
  shadows as the path tracer. `HybridMaxBounces` defaults to four (range 1–128), counting the primary
  surface. Progressive FP32 averaging remains noisy until enough samples accumulate. Inspect
  `AccumulatedSamples` for Hybrid/Reference; `ReferenceSamples` stays Reference-only. Camera media
  or scene bounds before the near plane conservatively use primary ray traversal, exposed by
  `UsesPrimaryRayFallback`, to retain volume boundaries. Cutout hits continue through the BVH.
- Fast (ADR 0030, the one explicitly approximate mode): raster primary visibility, pinned-closure
  direct punctual lighting, and split-sum image-based lighting reusing Core's prefiltered GGX cube,
  Charlie sheen chain and BRDF integration LUT instead of per-pixel quadrature. Lobe mapping: base
  specular → GGX chain, fuzz → Charlie chain, coat → GGX chain at coat roughness, diffuse → SH
  irradiance cube; subsurface, thin-film and dispersion lobes drop out. Every active approximation
  is reported per surface through `FastApproximations`. Transmission and partial opacity still throw
  rather than degrade silently. Connected material graphs are baked at scene-compile time into real
  mipmapped, hardware-filtered FP16 textures (bounded by `MaximumTextureBytes`, at most eight
  distinct baked inputs per scene) while uniform connections fold back into the material constants;
  baked sampling uses repeat addressing on the UV unit square. A root tangent normal map on a shading
  normal bakes its [0,1] data and decodes with the hit frame; normal maps elsewhere in a graph, and
  one connection mixing UV0 and UV1, are rejected explicitly. Fast compiles its own fragment pipeline
  with no BVH, media or path-tracing code and renders at full resolution without accumulation.

Raster/Hybrid primary samples use pixel centers without MSAA; edges can alias. Raster obeys near
and far clipping. Hybrid's conservative primary-ray fallback preserves the path tracer's camera-origin
transport semantics. Both share compiled geometry/BVH preparation; reduced primary traversal is not
a promise of faster frame times on every scene/device. Raster attachments add 20 bytes/pixel to the
history budget. No hardware ray-tracing extension is required.

## Directional shadows in Raster and Fast

Set `CastsShadows = true` on one `DirectionalLight` (default false). Both modes then render an
opaque, two-sided directional depth map fitted to the compiled scene bounds. Offscreen casters
are included. `ShadowOpacity` affects only this light's direct contribution; environment light,
emission and other lights stay unchanged. Mesh `ShadowCastingMode.Off` disables casting while
keeping camera visibility; `ShadowsOnly` casts without appearing in the Raster/Fast camera pass.

`OpenPbrRenderPass` controls:

| Property | Default | Meaning |
| --- | --- | --- |
| `DirectionalShadowsEnabled` | true | Pass-level map switch; the light must also opt in |
| `DirectionalShadowMapSize` | 1024 | Square resolution, power of two from 64 to 4096 |
| `DirectionalShadowDepthBias` | 0.001 | Receiver offset in normalized depth, 0–1 |
| `DirectionalShadowNormalBias` | 0.002 | Nonnegative slope-scaled geometric-normal offset in scene units |
| `DirectionalShadowPcfRadius` | 1 | 0 = hard, 1 = 3×3, 2 = 5×5 comparison filter |

PCF uses receiver-plane correction to avoid shading a sloped receiver as its own occluder.
This softens texel edges; it does not simulate distance-dependent penumbrae from the light's
angular diameter. Increasing bias trades acne for detached shadows. The map costs
`4 * size * size` bytes and is included in `MaximumHistoryBytes`. Multiple marked directional
lights fail explicitly. Cascades, point/spot maps and transparent shadow transmission are not
implemented. Hybrid/Interactive/Reference retain their ray shadows independently of these
map controls. Changing settings requires a new frame, not a pipeline replacement.

Interactive, Reference and Hybrid include reflection, refraction, anisotropy, coat, fuzz, thin film, RGB dispersion,
subsurface/volume random walks, stochastic opacity and emission. They share the same closure;
the event budget truncates long paths. This is RGB path tracing, not a spectral renderer.

## Scene and color contract

- Use perspective cameras and triangle meshes carrying OpenPBR materials. Morph targets and skinning
  are compiled before world transforms. The camera near plane sets projection geometry; transport
  begins at the camera origin to retain every medium boundary. The primary ray uses the far clip.
- Up to eight properly nested, consistently outward-oriented closed media; thin-walled surfaces
  do not enter the medium stack. Camera-inside initialization conservatively validates closed manifold (including concave)
  containing volumes and rejects ambiguous or unsupported configurations. Overlapping/touching media and nonuniform camera-interior volume fields fail explicitly.
  OpenPBR geometry opacity controls stochastic coverage; inherited raster alpha-cutoff/mode and
  face-culling metadata do not control this pass, which must intersect entry and exit faces.
  A path that exceeds the eight-medium stack or produces nonfinite throughput emits opaque magenta
  as a visible invalid-path diagnostic. This is not a fallback material or a conformance result.
- Directional (including finite angular diameter), point and spot lights, one lat-long HDR environment,
  plus constant `EnvironmentRadiance`. Mesh emission and environment illumination are sampled through
  BSDF paths; their lack of importance sampling can make small bright emitters very noisy. Raster
  shadow-map controls do not disable physical visibility in this pass.
  Bidirectional/light-traced caustic connections to ideal point/direction lights are not implemented;
  use finite angular emitters for paths that must encounter a light after specular transport.
- Internal transport and accumulation use FP32 ACEScg. Output converts to the chosen standard linear
  RGB space without tone mapping, gamut clipping or display encoding. The shared output must be FP16
  or FP32; FP16 has its usual representable range. Alpha is premultiplied stochastic coverage.
- Physical reflectance inputs must be in [0,1] after ACEScg conversion, radiance nonnegative. Values
  outside this domain fail explicitly, while the independent authoring data remains intact.
- `MetersPerSceneUnit` defines geometry units; surface distances honor `MetersPerUnit`. Thin-film
  thickness stays in micrometres. Active emission requires explicit `NitsPerSceneUnit`.
- Constants and typed graphs support color/data textures, UV0/UV1, add/multiply/mix/clamp, channel
  extraction and tangent normal maps. Graphs are bounded to 64 nodes, textures to four million texels
  each, with a default combined 64 MiB texture/program budget. Image filtering is level-zero nearest
  or bilinear; mip filtering and arbitrary MaterialX nodes are not implemented. The default Mu3D renderer rejects the distinct OpenPBR root, preventing silent
  conversion to glTF PBR.
- The default compilation budget is 500,000 source triangles and 512 MiB; each GPU storage binding
  is additionally limited to 128 MiB before allocation. FP32 history has its own 512 MiB budget.

## Reference provenance and limits

The complete Adobe OpenPBR 1.1.1 closure is pinned at
`c91aad1d1ce1693e803f039d7c92c2965c4eb013` under Apache-2.0. Source, hashes, Slang version and maintainer
generation live in `eng/openpbr` in the Mu3D source repository. Generated WGSL and the FP32 lookup
buffer are embedded: consumer builds never require Slang, Clang or downloads.

The upstream implementation includes approximations/TODOs around interior coating, thin-film
transmission and thin-film/dispersion interactions, and applies dispersion to relative IOR. Fixed
RGB wavelengths are 620/540/450 nm. A scalar nested-medium IOR cannot represent an arbitrary spectral
  stack. Very small volume colors/distances and extreme anisotropy follow upstream numerical limits.
These boundaries also apply to Reference mode; numerical agreement with the pinned implementation
does not imply perfect spectral/specification conformance.

Maintainer checks compare shader evaluation, sampling, PDF, emission, white-furnace integrals and
Beer–Lambert transmittance against independently compiled pinned C++, and exercise real scene
transport using headless wgpu/Metal. Target-platform presentation and interactive performance are
separate acceptance gates. See `ThirdParty/NOTICE.md` and `Adobe-LICENSE`.

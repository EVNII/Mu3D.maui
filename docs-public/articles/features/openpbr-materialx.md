---
title: OpenPBR rendering and MaterialX XML
description: Raster, hybrid, interactive and reference OpenPBR rendering, bindable MAUI XAML and MaterialX files.
feature_id: openpbr-materialx
---

# OpenPBR rendering and MaterialX XML

## Optional installation

Use `Mu3D.Maui` alone for the standard renderer. Add `Mu3D.Rendering.OpenPbr` explicitly when
you need full OpenPBR, then assign an `OpenPbrRenderPass.Pipeline` to the scene view. The base
package does not reference or initialize the optional renderer and carries none of its BSDF
shaders, LUT or precompiled shader assets. `Mu3D.Formats.MaterialX` is independently optional
for `.mtlx` files. Core/MAUI material authoring types remain available without activating OpenPBR.

```xml
<PackageReference Include="Mu3D.Maui" Version="YOUR_MU3D_VERSION" />
<PackageReference Include="Mu3D.Rendering.OpenPbr" Version="YOUR_MU3D_VERSION" />
```

Mu3D supports the **constant and connected authoring parameters** of OpenPBR Surface 1.1.1. The optional
`Mu3D.Formats.MaterialX` package reads and writes that subset as MaterialX 1.39 `.mtlx` XML.
MAUI XAML is a separate entry point: it creates bindable Mu3D objects and does not pass page XML
through the MaterialX file parser.

The optional `Mu3D.Rendering.OpenPbr` package evaluates the pinned Adobe OpenPBR 1.1.1 BSDF through
`OpenPbrRenderPass`. Interactive, progressive Reference and Hybrid use the same transport closure, including
coat, fuzz, anisotropy, thin film, RGB dispersion, transmission and subsurface/volume scattering.
`OpenPbrPreviewMaterial3D` remains a separately named approximation for the existing PBR renderer.

## Rendering modes

### Preparing the first frame

The optional package includes Windows precompiled shader bytecode. The matching native backend
loads it before checking its per-user disk cache or compiling a missing variant. Applications need
no registration, Rust/Slang tools or shader-generation step. Only Windows deployments receive this
bytecode; Android and Apple retain their existing backends.

An uncached variant or driver-specific compilation can still take seconds. The samples use the automatic host's
`PrepareGraphicsAsync` callback to await `OpenPbrRenderPass.PrepareAsync` before publishing the
presentation session. Compilation runs on a worker, with a preparation message visible in the page.
The host retains the device while compilation finishes even when navigation cancels publication.

```csharp
sceneView.PrepareGraphicsAsync = (device, format, cancellationToken) =>
    pass.PrepareAsync(device,
        sceneView.DisplayTransform is null ? format : GraphicsTextureFormat.Rgba16Float,
        cancellationToken);
```

Assign the callback before the control loads, using the same pass assigned to its render pipeline.
Do not call Execute, change preparation settings or prepare the same pass again before completion.
Standalone callers must keep the device alive until the returned task completes. Cancellation
does not forcibly interrupt a native compiler; it discards the result safely after completion.
Pass disposal may abandon preparation. Samples disable mode selection while preparing.

Raster now has a separate generated entry using the same full BSDF, without unreachable ray
transport code. Resize replaces image attachments and bindings while keeping compiled pipelines.
Missing precompiled variants and cold mode switches can still compile. The Windows persistent
cache stores DXIL bytecode; GPU/driver pipeline compilation is a separate step. Consequently,
precompiled assets do not guarantee an instant first image on every new GPU or driver.

### Mode behavior

| Mode | Visibility and lighting | Limits |
| --- | --- | --- |
| Raster | Full-resolution depth-tested triangles; pinned BSDF direct lights, optional directional shadow map and fixed environment quadrature | No indirect transport; transmission, subsurface and partial opacity are rejected |
| Hybrid | Raster primary hits plus bounded ray shadows/secondary transport; progressive FP32 mean | Default four events; sampling noise; conservative primary-ray fallback for media/near clipping |
| Interactive | Independent path sample each frame; half-resolution default | Default four events; no history or denoiser |
| Reference | Full-resolution jittered paths; progressive FP32 mean | Default 32 events; finite RGB transport |

Set `Mode = OpenPbrRenderMode.Raster` or `Mode = OpenPbrRenderMode.Hybrid` on the same pass.
`RasterEnvironmentSamples` controls fixed environment quadrature (1–64, default 16); it can miss
small bright HDR features. Finite suns use their central direction in Raster. `HybridMaxBounces`
controls Hybrid's event budget (1–128). `AccumulatedSamples` reports Hybrid/Reference history;
`UsesPrimaryRayFallback` exposes Hybrid's conservative camera-medium/near-boundary fallback.
Raster/Hybrid sample primary visibility at pixel centers without MSAA, so silhouettes may alias.
Neither path promises a particular frame rate; both use ordinary WGSL and depth attachments.

### Directional shadow maps

Opt one directional light into shadow mapping:

```xaml
<mu3d:DirectionalLight3D Intensity="4" RotationX="-25" RotationY="-30"
    CastsShadows="True" ShadowOpacity="1" />
```

The pass enables maps by default when a light opts in. Set `DirectionalShadowsEnabled` to false
to compare unshadowed lighting. `DirectionalShadowMapSize` defaults to 1024 and accepts powers
of two from 64 to 4096. `DirectionalShadowPcfRadius` selects hard (0), 3×3 (1, default) or 5×5 (2)
filtering. `DirectionalShadowDepthBias` defaults to 0.001 in normalized depth;
`DirectionalShadowNormalBias` defaults to 0.002 scene units, scaled by surface slope. Increase
bias cautiously: excessive offsets detach shadows from their casters. These controls are also
available on the sample page.

The single map covers complete scene bounds, including offscreen casters, and affects only the
marked light's direct contribution. Environment light and emission remain unaffected. Mesh
`ShadowCastingMode` selects `On`, `Off` or `ShadowsOnly`. No cascades, point/spot shadow maps or
transparent shadow transmission are provided. Multiple marked directional lights are rejected.
PCF softens map edges; physically sized penumbrae require the ray-transport modes. Those modes
keep their ray visibility independently of these map controls.

## MAUI XAML

Declare `xmlns:mu3d="clr-namespace:Mu3D.Maui.Controls;assembly=Mu3D.Maui"` on the page. Inside a
`Scene3D`, use an ordinary primitive and a nested authoring component:

```xaml
<mu3d:Sphere3D Radius="1">
  <mu3d:OpenPbrMaterial3D NitsPerSceneUnit="100">
    <mu3d:OpenPbrSurface3D x:Name="AuthoredSurface"
        BaseColor="0.08,0.35,0.8;acescg"
        BaseMetalness="0.75" SpecularRoughness="0.22" />
  </mu3d:OpenPbrMaterial3D>
</mu3d:Sphere3D>
```

`OpenPbrSurface3D` exposes all 41 constant inputs plus name and scene scale as bindable properties.
It inherits the material wrapper's binding context unless explicitly assigned its own. Changes
update the same Core material identity and invalidate attached viewports. Configure the optional
pass on the enclosing scene view:

```csharp
using Mu3D.Rendering.OpenPbr;

var openPbrPass = new OpenPbrRenderPass {
    Mode = OpenPbrRenderMode.Reference,
    ReferenceMaxBounces = 32,
};
SceneView.RenderPipeline = openPbrPass.Pipeline;
```

The application schedules additional frames for progressive refinement and disposes the pass after
detaching it from the view. `ReferenceSamples` reports accumulated samples per pixel. Scene, camera,
material, environment and light changes reset history. `ResetAccumulation()` also resets it explicitly.
No surface session is created or owned by application rendering code. The default renderer rejects
the distinct OpenPBR material until this pass is selected.

The pass and mode can also be declared in XAML. Add
`xmlns:pbr="clr-namespace:Mu3D.Rendering.OpenPbr;assembly=Mu3D.Rendering.OpenPbr"`:

```xaml
<ContentPage.Resources>
  <pbr:OpenPbrRenderPass x:Key="OpenPbrPass" Mode="Reference" ReferenceMaxBounces="32" />
</ContentPage.Resources>
```

Set `RenderPipeline="{Binding Pipeline, Source={StaticResource OpenPbrPass}}"` on `Mu3DSceneView`.
The page still owns scheduling and disposal; resource declaration does not create an autonomous loop.

Interactive mode starts a fresh estimate each frame, with configurable path-event budget and
`InteractiveResolutionScale` (default 0.5). Reference mode retains a full-resolution FP32 mean.
Both preserve linear HDR, with no implicit tone mapping. Noise and speed depend on the scene and
hardware; interactive mode does not promise a fixed frame rate or include a denoiser.

Colors use `r,g,b[,a];space` with invariant decimal points and a mandatory linear-space identifier:
`acescg`, `lin_rec709`, `lin_displayp3`, `lin_adobergb`, `lin_prophoto`, or `lin_rec2020`.
OpenPBR color3 requires alpha one. Geometry opacity is separate. These are linear components, not
encoded UI hex colors. Vector inputs use `x,y,z` and receive no color conversion. Null geometry
normal/tangent values inherit mesh geometry.

For the optional compatibility preview, `PreviewPolicy` defaults to `RejectUnsupported`. Unsupported active inputs hide the preview and
populate the bindable `PreviewError`; the authoring values remain intact. Applications may explicitly
choose `AllowLossyApproximation` and inspect `PreviewResult.Diagnostics`. Even a baseline preview
reports the shader-model approximation. `NitsPerSceneUnit` is required for active emission because
OpenPBR luminance is in nits and the renderer consumes scene-linear emission. It does not configure
display luminance or tone mapping.

## MaterialX files

```xml
<materialx version="1.39" colorspace="acescg">
  <open_pbr_surface name="blue" type="surfaceshader" version="1.1.1">
    <input name="base_color" type="color3" value="0.08, 0.35, 0.8" />
    <input name="base_metalness" type="float" value="0.75" />
  </open_pbr_surface>
  <surfacematerial name="material" type="material">
    <input name="surfaceshader" type="surfaceshader" nodename="blue" />
  </surfacematerial>
</materialx>
```

The file importer returns backend-independent <xref:Mu3D.SceneGraph.OpenPbrSurface> values:

```csharp
using Mu3D.Formats.MaterialX;

using Stream input = await FileSystem.OpenAppPackageFileAsync("MaterialX/blue-metal.mtlx");
MaterialXOpenPbrDocument document = await MaterialXOpenPbrSerializer.ImportAsync(input);
AuthoredSurface.LoadSurface(document.Surfaces[0].Surface);

using MemoryStream output = new();
MaterialXOpenPbrSerializer.Export(output, new MaterialXOpenPbrDocument(
    [new MaterialXOpenPbrSurface("surface", AuthoredSurface.ToSurface())],
    [new MaterialXSurfaceMaterial("material", "surface")]));
```

`LoadSurface` assigns local values and can override bindings on those properties. Applications
synchronize their controls/model and restore any bindings that should continue to drive the loaded
values; the Gallery sample demonstrates this for its roughness slider.

Application code selects the file or stream. The serializer leaves caller streams open and uses no
file/network resolver, native runtime or global material registry. It rejects DTDs, includes,
entities, unsupported connections/graphs, ambiguous names and unsupported versions/types. Import
limits bound bytes and node counts. Untagged authored colors require an explicit import assignment;
export emits explicit supported color identities. Scene lengths currently require metre units;
thin-film thickness remains in micrometres. Non-metre interchange fails explicitly.

The Gallery **OpenPBR + MaterialX** page compares rendering modes on a sphere and pedestal,
with visible approximation notes. It includes XAML, a packaged `.mtlx` example and an export/import
round trip. Tests validate the 41-input schema and exported XML against
the pinned upstream definition. File validation is separate from OpenPBR rendering conformance.

Open **Show material parameters** to edit all 41 inputs, grouped by Base, Specular, Coat,
Transmission, Subsurface, Fuzz, ThinFilm, Emission and Geometry. Authoring also exposes the
material name and metres per scene unit. Enter numbers with decimal points, colors with an
explicit space (`0.2,0.4,0.8;acescg`), and directions as `x,y,z`; an empty direction inherits
the mesh. **Apply this group** validates all of its edits before updating the surface. Changing
groups or loading a preset discards unapplied edits. Applied edits also redraw when Run is off.

Connected inputs show **Use texture / graph** and disable the inactive constant field. Turn
that switch off and apply to disconnect only that input; the other connections remain intact.
The texture preset's specular roughness is a map multiplied by 0.3 (range 0.21–0.3), while its
coat has weight 0.4 and independent roughness 0.2. To examine a uniformly rough, uncoated
surface, disconnect `specular_roughness`, set it to 1, and set `coat_weight` to 0 in the Coat
group. Reloading **Texture material** restores the preset and its connections.

Mode-incompatible transmission, opacity or subsurface settings are reported before applying.
Select Hybrid, Interactive or Reference to explore those transport effects and ray shadows.

The separate [OpenPBR White Furnace](openpbr-white-furnace.md) page checks uniform-environment
energy ratios with explicit scene-linear measurements and error visualization.

## Texture and node connections

All 41 inputs can connect through `OpenPbrSurface.Graph`. Graphs are immutable; replacing a graph
resets accumulation. `OpenPbrSurface3D.Graph` supports normal XAML bindings and resources. Gallery
includes a color/roughness/normal texture preset and its XML export/import round trip.

```csharp
var texture = OpenPbrTexture.FromColor(decodedLinearImage, "paint.exr");
var surface = new OpenPbrSurface
{
    Graph = new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode>
    {
        [OpenPbrInput.BaseColor] = OpenPbrNode.Image(texture, OpenPbrNodeType.Color3),
        [OpenPbrInput.SpecularRoughness] = OpenPbrNode.Float(0.35f),
    }),
};
```

Use `OpenPbrTexture.FromData` for normal, roughness and packed numeric channels. Colors are converted
to ACEScg; data is never color transformed. MaterialX uses bottom-left UVs, while supplied pixel rows
run top-to-bottom. UV0/UV1, closest/bilinear sampling and repeat/clamp/mirror addressing are supported.
Graphs also support add, multiply, mix, explicit clamp, channel extract and tangent normal maps.
Each surface is limited to 64 distinct nodes; textures and total GPU graph data have explicit budgets.
There is no implicit mip generation/filtering. Geometry must contain the referenced UV sets.

MaterialX import supports root connections and named nodegraph outputs. Set
`MaterialXImportOptions.TextureResolver` to resolve only resources your application permits. The
callback receives filename, original color-space assignment and requested value type. Decode color
to linear light before `FromColor`, preserve the filename and `SourceColorSpace`, and use `FromData`
for raw channels. Export writes resource references; the application writes the resource files.
No XML-specified file or URL is opened automatically. Unsupported nodes/attributes fail explicitly.

Mu3D uses a managed XML adapter, not the official native MaterialX runtime. Official **1.39.4** is
used independently to validate exported constants and graphs against its standard node definitions.
Its WebGPU shader generator is not the renderer used here: Mu3D uses pinned Adobe OpenPBR through
Slang-generated WGSL. These are separate integration paths.

## Rendering boundary

The ray-transport modes support constant and connected OpenPBR inputs, perspective cameras, triangle geometry with
morph/skinning, punctual lights and one HDR environment. Mesh-emitter and environment paths are
sampled through the BSDF; small bright emitters can converge slowly. Transparent primary backgrounds
retain lighting in reflection and refraction. Output alpha is premultiplied coverage.

Interior transport requires consistently outward-oriented closed surfaces, with up to eight nested
media. Camera-inside initialization validates closed concave or convex containing volumes and explicitly rejects
ambiguous, overlapping or unsupported configurations. The near plane controls projection; transport
starts at the camera origin so it cannot skip a volume boundary. `MetersPerSceneUnit` sets geometry
scale, while authored distances retain `MetersPerUnit`.

Reference rendering uses fixed RGB wavelengths and a finite path-event budget. It preserves the
pinned implementation's documented interior-coat and thin-film/dispersion approximations. It is not
full spectral rendering. Arbitrary MaterialX nodes, intersecting media, spatial volume fields, external-renderer
image conformance and target-device performance acceptance remain separate work. GPU/C++ numerical comparisons cover the
closure independently of XAML construction and file validation.

## Reference

Parameter identity follows the [OpenPBR v1.1.1 reference](https://github.com/AcademySoftwareFoundation/OpenPBR/tree/v1.1.1).
The XML container follows [MaterialX 1.39](https://materialx.org/Specification.html).
The renderer uses the [pinned Adobe OpenPBR implementation](https://github.com/adobe/openpbr-bsdf/tree/c91aad1d1ce1693e803f039d7c92c2965c4eb013).

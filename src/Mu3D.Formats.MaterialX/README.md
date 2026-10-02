# Mu3D.Formats.MaterialX

Optional, pure-managed MaterialX 1.39 XML interchange for the **constant parameters and typed input graphs** of
[OpenPBR Surface 1.1.1](https://github.com/AcademySoftwareFoundation/OpenPBR/tree/v1.1.1).
It depends on Mu3D.Core and creates no renderer, native library, GPU object or MAUI control.

```csharp
using Mu3D.Formats.MaterialX;

// The caller opens, positions and disposes both streams.
MaterialXOpenPbrDocument document =
    await MaterialXOpenPbrSerializer.ImportAsync(inputStream, cancellationToken: cancellationToken);

var surface = document.Surfaces[0].Surface; // Mu3D.SceneGraph.OpenPbrSurface
surface.SpecularRoughness = 0.25f;
MaterialXOpenPbrSerializer.Export(outputStream, document);
```

Example accepted `.mtlx` document:

```xml
<?xml version="1.0"?>
<materialx version="1.39" colorspace="acescg">
  <open_pbr_surface name="CopperSurface" type="surfaceshader" version="1.1.1">
    <input name="base_color" type="color3" value="0.93, 0.63, 0.48" />
    <input name="base_metalness" type="float" value="1" />
    <input name="specular_roughness" type="float" value="0.25" />
  </open_pbr_surface>
  <surfacematerial name="Copper" type="material">
    <input name="surfaceshader" type="surfaceshader" nodename="CopperSurface" />
  </surfacematerial>
</materialx>
```

The adapter preserves all 41 inputs in the pinned definition, including dormant parameters that
the current renderer cannot evaluate. The four geometry vectors are nullable; omitted values
retain the definition's inherited world-normal/tangent semantics. Explicit directions are not
normalized. Node inventories and material references are immutable; their `OpenPbrSurface` objects
remain mutable and application-owned. References can point forward, and multiple materials can
refer to the same surface. Export uses node inventory order and definition input order.

## Explicit interpretation

- Color constants must have `colorspace` on the input, surface or document, or an explicit
  `MaterialXImportOptions.DefaultColorSpace` assignment. Supported names are `lin_rec709` and
  `acescg` (`lin_ap1` is an accepted ACEScg alias). Export writes the identity on each color input.
  No color conversion, transfer decoding, gamut mapping or clipping occurs. Signed/above-one FP32
  components remain intact. Untagged omitted definition defaults use the Core model's documented
  ACEScg convention; an explicitly inherited/assigned space applies to these defaults as well.
- `subsurface_radius_scale` is stored as numeric RGB-channel length multipliers, although its
  upstream XML type is `color3`. It is never color transformed, and an explicit per-input
  `colorspace` on that numeric parameter is rejected.
- The current scene-length convention is metres (`OpenPbrSurface.MetersPerUnit = 1`). The adapter
  rejects unit metadata and non-metre model export instead of silently changing physical scale.
  `thin_film_thickness` always preserves the OpenPBR micrometre value; `emission_luminance`
  preserves nits. Scene-unit conversion is an explicit application operation before export.
- An omitted node `version` resolves to the pinned `1.1.1` definition. An explicit different
  version is rejected. Document `version="1.39"` is required. A `nodedef` attribute is accepted
  only when it names `ND_open_pbr_surface_surfaceshader`.
- Identifiers in this subset match `[A-Za-z_][A-Za-z0-9_]*` and are limited to 256 characters.
  The wrapper node name is the stable reference identity; `OpenPbrSurface.Name` is display metadata.

## Bounds and unsupported content

Defaults limit XML to 1 MiB, 128 surfaces and 128 materials. Callers can raise the bounded limits
to 32 MiB and 4096 nodes of each kind. The reader checks nesting before constructing a tree. It
prohibits DTDs, entities, processing instructions, includes and unknown appearance attributes.
Images require an explicit application TextureResolver; no path is opened by the importer. There is no file, URL or network resolution.
Duplicate names/inputs, broken references, type mismatches and non-finite values are rejected.
Both synchronous and cancellable asynchronous imports leave the caller's stream open, including
on failure. Export validates model semantics before writing and leaves the output open.

This is a deliberately bounded interchange subset, not a general MaterialX evaluator or an
OpenPBR renderer. The supported graph operations are constant, texcoord (UV0/UV1), image, add,
multiply, mix, clamp, extract and tangent normalmap, plus raw radius-channel conversion. Root
connections and named nodegraph outputs support forward references. Cycles and unreachable nodes
are rejected. Arbitrary graphs, UDIM, animation, displacement and custom unit systems are outside
this adapter. XML graph limits default to 256 document nodes and 64 connected nodes per surface. `OpenPbrSurface.ToPbrPreview` is a separate explicit approximation boundary with its
own diagnostics and policy; successful XML interchange never implies OpenPBR rendering conformance.

## Reference and validation

Parameter names, types and defaults are pinned to the unmodified
[OpenPBR v1.1.1 reference definition](https://raw.githubusercontent.com/AcademySoftwareFoundation/OpenPBR/v1.1.1/reference/open_pbr_surface.mtlx),
whose SHA-256 is `c15674aaa82ef4bf0a27388b3b3a8f0f171982f53df6ebbceed679ce63699f51`.
The definition declares MaterialX `1.39` and OpenPBR `1.1.1`. The matching Apache-2.0 license and
original fixture are retained with the tests. Tests compare exported defaults and the complete
input inventory against this source, then exercise full constant roundtrips, colors, forward
references, culture independence, byte/count limits, cancellation and hostile XML.

```text
dotnet run --project tests/Mu3D.Formats.MaterialX.Tests -c Release
```

The managed test also writes `ExportedOpenPbr.mtlx` into its ignored Release output directory.
An optional independent developer check uses the official `MaterialX==1.39.4` Python SDK and the
exact OpenPBR 1.1.1 reference library:

```text
python3 tests/Mu3D.Formats.MaterialX.Tests/validate_with_upstream.py
```

That SDK check validates the exported document and confirms that all 41 inputs resolve against
the pinned `1.1.1` node definition. It is not a production dependency and does not test rendering.

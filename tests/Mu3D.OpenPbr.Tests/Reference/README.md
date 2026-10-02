# OpenPBR numerical oracle

This directory fixes an independent C++ oracle for the OpenPBR GPU implementation. The oracle
calls the unmodified Adobe BSDF; it does not implement Mu3D's shading equations. Ordinary managed
tests consume the committed JSON. Application builds require neither C++, GLM, network access nor
shader generation.

## Source pins and licenses

| Dependency | Fixed source | Archive SHA-256 | License |
| --- | --- | --- | --- |
| Adobe OpenPBR BSDF | `c91aad1d1ce1693e803f039d7c92c2965c4eb013` | `3e9f0925c92b634d90830354e18845ee551a8384c36e761274f44a58b0e246ec` | Apache-2.0, accompanying `Adobe-LICENSE` |
| GLM | `1.0.1` | `9f3174561fd26904b23f0db5e560971cbf9b3cbda0b280f04d5c379d03bf234c` | MIT or Happy Bunny, upstream `copying.txt`; no GLM code is redistributed here |

Archives:

- https://codeload.github.com/adobe/openpbr-bsdf/tar.gz/c91aad1d1ce1693e803f039d7c92c2965c4eb013
- https://codeload.github.com/g-truc/glm/tar.gz/refs/tags/1.0.1

Specification: https://github.com/AcademySoftwareFoundation/OpenPBR/tree/v1.1.1.
Upstream implementation and declared deviations:
https://github.com/adobe/openpbr-bsdf/tree/c91aad1d1ce1693e803f039d7c92c2965c4eb013.

`oracle.cpp` and `cases.h` are Mu3D test wrappers. The accompanying Apache license applies to the
external Adobe implementation and its generated numerical reference data; copyright 2026 Adobe.
The oracle uses upstream defaults, including non-reciprocal, energy-conserving coat/fuzz layering.

## Regeneration

With Python 3.12 or later, download the two exact archives above into a maintainer temporary/cache
directory. Then run:

```sh
python3 tests/Mu3D.OpenPbr.Tests/Reference/generate.py \
  --openpbr-archive /tmp/mu3d-openpbr-bsdf-c91aad1.tar.gz \
  --glm-archive /tmp/mu3d-glm-1.0.1.tar.gz
```

Add `--check` to compare with the committed fixture without rewriting it. Both complete archives
are verified before extraction; paths and symlinks are rejected if unsafe. The script accepts
`--compiler clang++` (default) or a compatible C++17 compiler. The initial fixture was generated
on macOS arm64 with Apple clang 21.0.0, `-O2 -fno-fast-math -ffp-contract=off`. Float outputs use
nine significant decimal digits. CPU/GPU comparison uses
`abs(actual - expected) <= 2e-5 + 2e-4 * abs(expected)`; this is a numerical portability tolerance,
not a visual-error or whole-renderer conformance tolerance.

## Case and output contract

The shared C++/Slang `cases.h` defines 24 deterministic cases. In aggregate they assign every one of
the 41 OpenPBR inputs: diffuse weight/color/roughness, metal F82 edge tint, dielectric specular
weight/color/IOR/roughness/anisotropy, coat/fuzz layering, thin film, transmission, dispersion,
subsurface, volume scattering/absorption, both independent geometry bases, emission, opacity and
thin-walled behavior. The four geometry vectors are orthonormalized into the upstream's two basis
structs. Upstream anisotropy rotation extensions keep their identity defaults.

All colors are untransformed linear RGB numerical inputs. This fixture tests the BSDF boundary;
Mu3D color-space conversion and explicit scene-unit/nits mapping have their own tests. Default
view is `normalize(0.3, 0.2, 1)`, light is `normalize(-0.4, 0.1, 1)`, sample variate is
`(0.23, 0.67, 0.41)`, path throughput is `(1, 0.8, 0.6)` and exterior IOR is `1`. Per-case view,
light and wavelengths are included in the JSON; wavelengths default to upstream `(620, 540, 450)`
nm. Cases 19/20 exercise interior view directions; transmission cases also evaluate back lights.

Each `values` array has 32 FP32 entries, suitable for eight RGBA32F pixels:

| Offset | Values |
| --- | --- |
| 0–2, 3 | Evaluated diffuse RGB, PDF at supplied light |
| 4–6, 7 | Evaluated specular RGB, volume anisotropy |
| 8–10, 11 | Volume extinction RGB, sample PDF |
| 12–14, 15 | Volume single-scattering albedo RGB, sampled lobe type |
| 16–18, 19 | Attenuated emission in nits RGB, needs-wavelength flag |
| 20–22, 23 | Sampled direction, separately evaluated PDF at sampled direction |
| 24–26, 27 | Sample diffuse weight RGB, zero padding |
| 28–30, 31 | Sample specular weight RGB, zero padding |

`openpbr_eval` already includes the incoming cosine. Sample weight is BSDF × cosine / PDF.
If sampling fails (`sample PDF == 0`), the upstream leaves direction, weight and lobe undefined;
the wrappers must explicitly write zeros for those fields and for the sampled-direction PDF.
Opacity is deliberately handled outside the BSDF: cases 0 and 17 have identical results.

Supplemental data contains fixed 256×256 hemisphere midpoint quadratures for white rough metal,
white rough diffuse and white layered opaque dielectric. The generator checks each is within
2% of unit energy. These are local directional-hemispherical checks, not a scene white-furnace
render and not proof of volumetric or multiple-bounce convergence. Three materials also record
Beer–Lambert transmittance at distances 0, 0.1 and 1.

## Boundaries retained from upstream

- Host renderer owns opacity, geometry intersections and interior-volume integration.
- Coat/fuzz defaults favor camera-to-light energy conservation over reciprocity. The fuzz LTC
  approximation remains non-reciprocal even with the optional symmetric layer setting.
- Thin-film color on thin-walled transmission is an acknowledged unsupported combination.
- Fixed RGB wavelengths are reproducible probes, not full spectral integration. Smooth dispersion
  needs wavelength sampling and matching host light transport.
- Matching this oracle validates numerical agreement with this implementation, not perfect
  physical conformance, arbitrary MaterialX graph execution, or platform presentation.

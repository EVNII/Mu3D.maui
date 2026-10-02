# Pinned portable display views

The presets are Blender 5.0.0 AgX Base SDR (100-nit Rec.709), Blender AgX HDR
(1000-nit P3 D65 limiting gamut), ACES 2.0 SDR (100-nit Rec.709), and ACES 2.0 HDR
(1000-nit Rec.2020 or P3 D65 limiting gamut). Inputs are scene-linear sRGB or ACEScg. Outputs
are **display-linear sRGB** relative to 100 nits; runtime normalization changes
that unit to `ReferenceWhiteNits`. Output is not PQ, HLG, gamma, or scene-linear
light. Extended sRGB values preserve the HDR and wide-gamut result. Alpha is not
processed. Exposure occurs before the view.

`manifest.json` pins Blender source files, the OCIO 2.5.2 built-in ACES config,
source archive, exact Python wheel and Slang 2026.14. The upstream Blender config
is unchanged; the three selected AgX LUTs, two Filmic LUTs and its reference adaptation
matrix are vendored. Other views from that config are not runtime features.

The generator asks official OCIO for the complete optimized HLSL processor,
substitutes exact storage-buffer texture fetches, and emits both a literal C#
translation and WGSL via Slang. Original AgX tetrahedral interpolation and log2
shaping are retained. ACES 2.0 retains the complete analytic output transform and
the small official reach/cusp tables. The AgX HDR processor's official half-domain
1D HLG decoding LUT is retained rather than approximated. Table dimensions and
interpolation are inspectable in the generated `.tables.json` descriptors.

To regenerate, install the pinned OCIO 2.5.2 and NumPy 2.4.3 wheels into a temporary
maintainer environment and provide the pinned macOS ARM64 Slang executable:

```sh
PYTHONPATH=/path/to/temporary/python-packages python3 eng/color-management/generate.py --slangc /path/to/slangc
PYTHONPATH=/path/to/temporary/python-packages python3 eng/color-management/generate.py --slangc /path/to/slangc --check
python3 eng/color-management/generate.py --validate-input-pins
```

No dependency downloads are performed by the script or consumer builds. `--check`
compares every generated code/resource/oracle byte without modifying outputs.
Source inputs, the exact compiler executable/library, and the OCIO wheel module/library
are hash-checked before generation.

`tests/Mu3D.Color.Views.Tests` compares the portable evaluator against committed
results from OCIO's independent CPU processor: fixed neutral/saturated/negative
colors and a deterministic log-uniform HDR color sweep. The reference is not
produced by the translated evaluator. Generation also independently selects each
official Display/View and decodes only its display colorspace, then requires those
results to agree with the generated processor's linear-output pipeline. The tests also check alpha, scene exposure,
reference-white normalization and retained HDR peaks. Native GPU parity is covered
by the color GPU test project. CLI tests do not open a display or launch Gallery.

Third-party notices and license text are packed with Mu3D.Color. The original
Blender configuration's attribution and license reference are preserved; see the
license migration record in `manifest.json` and `ColorViews/NOTICE.md`.

The additional AgXSdrRec2020 preset selects Blender 5.0 AgX Base Rec.2020 and
its original AgX_Base_Rec2020.cube. It uses the same extended-linear-sRGB output
contract, preserving wide-gamut values for explicit downstream conversion.

Aces2Hdr1000P3 uses the official ACES 2.0 1000-nit P3-D65 output, independently
verified against the matching OCIO Display/View. Gallery uses it with AgX HDR to
hold peak, reference white and limiting gamut constant during saturated-color comparisons.

FilmicSdr selects Blender's Filmic sRGB view with no additional Look. It retains the
original 33-cube desaturation table and 1D base contrast curve, with tetrahedral/linear
interpolation respectively, then decodes the sRGB display result to our linear output.
The independently selected sRGB / Filmic Display/View must match. Its source is HDR,
but its output is SDR Rec.709; the pinned config has no Filmic HDR display view.

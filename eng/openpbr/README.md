# Maintainer OpenPBR shader generation

Windows DXIL precompilation and native cache maintenance are documented in
[WINDOWS_PRECOMPILED.md](WINDOWS_PRECOMPILED.md). Bytecode belongs exclusively to the optional
renderer package; the normal WGSL/Slang generation contract below remains unchanged.

`manifest.json` pins the unmodified Adobe OpenPBR 1.1.1 source archive, every vendored source file,
and official Slang 2026.14 compiler binaries. Source and runtime lookup/shader attribution are
Apache-2.0. The compiler is a development tool and is not redistributed in Mu3D packages.

Consumers use committed WGSL and lookup data. No application build downloads source, installs a
compiler or runs this script. Verified maintainer compilers are the official macOS arm64 and
Windows x86_64 archives listed in the manifest; supporting another generation host requires adding its
verified release asset and binary hashes deliberately.

After explicitly downloading and checking the archive SHA-256 from the manifest, extract it in a
maintainer cache and run:

```sh
python3 eng/openpbr/generate.py --validate-input-pins
python3 eng/openpbr/generate.py --slangc /path/to/slang/bin/slangc
python3 eng/openpbr/generate.py --slangc /path/to/slang/bin/slangc --check
```

`--shader render`, `--shader raster`, `--shader fast` or `--shader reference` limits compilation to one entry. The script also checks
the compiler executable and loaded Slang library hashes and exact version. It rejects changed or
extra upstream files. Source pins normalize Git CRLF checkouts to upstream LF; binary pins remain
byte-exact. The generated lookup buffer has 72,736 little-endian FP32 values; seven
energy tables are normalized exactly as upstream's array implementation, then the 32×32 RGB LTC
table is appended. `lut-buffer.slang` implements the upstream texture lookup hooks using this
buffer at group 1, binding 0, with explicit bilinear/trilinear interpolation.

The buffer avoids embedding one megabyte of constants in WGSL. This is a resource representation
change, validated against the independent unmodified C++ array-mode oracle. All feature toggles
remain enabled. `OPENPBR_RECIPROCAL_COAT_AND_FUZZ` retains upstream's default zero; the behavior and
limitations are described in upstream README and the renderer documentation.

Slang currently reports conservative warning E41035 at the unmodified thin-film `r23[0]` copy.
The preceding loop iteration always initializes channel zero before this branch can execute. The
warning is retained rather than patching upstream or disabling warnings globally. Numerical
conformance covers thin film with both dielectric and metallic bases and dispersion.

`render.slang` also shades raster visibility and hybrid primary hits through the pinned BSDF.
`raster.slang` includes that same source with `MU3D_RASTER_ONLY`, omitting the ray-transport
entry call so Slang removes unreachable media/multi-bounce code. It retains the full shared
BSDF and fixed environment quadrature; it is not the approximate Fast program. Both outputs
are committed and reproduced by `--check`.
`src/Mu3D.Rendering.OpenPbr/Shaders/visibility.wgsl` is a small handwritten triangle/depth adapter;
it contains no copied BSDF and is not a generated artifact. Keep the shared frame layout (20 vec4s)
and triangle layout in agreement. Raster uses fixed environment quadrature; Hybrid retains the
existing transport after the first hit and has explicit primary-ray fallback for medium boundaries.

`shadow.slang` supplies the shared directional depth comparison and receiver-plane-corrected PCF
for Raster and Fast. Group 3 contains six uniform vectors, a depth texture and comparison sampler.
`OpenPbrDirectionalShadow` owns the handwritten depth-only vertex adapter and map lifecycle; it
uses the same triangle positions and the casting-mode lane at triangle vector 2.w. Generated WGSL
must be regenerated from these sources, never patched by hand.

Optional Dawn/Tint validation catches browser WGSL errors that a native shader compiler may accept.
Install the official [Dawn Node addon](https://github.com/dawn-gpu/node-webgpu) into an isolated
maintainer directory, then pass its module path explicitly:

```sh
npm install --prefix artifacts/toolchain/node-webgpu --no-save --package-lock=false \
  --ignore-scripts --no-audit --no-fund webgpu@0.6.1
node tests/Mu3D.OpenPbr.Rendering.Tests/validate-wgsl.mjs \
  artifacts/toolchain/node-webgpu/node_modules/webgpu/index.js \
  src/Mu3D.Rendering.OpenPbr/Shaders/raster.wgsl
```

The check reads Fast, transport, visibility and reference WGSL, plus the dedicated Raster module
passed above and the actual fullscreen vertex source. It rejects
errors/warnings or disabled derivative-uniformity diagnostics, and validates the Fast fragment
pipeline with reflected bindings and its actual FP32 target. Optional WGSL paths after the addon
path add negative/reproducer checks; explicit host-layout validation remains in native GPU tests.
It uses Dawn's null backend without drawing or opening UI;
success does not establish browser rendering, driver execution or physical HDR. This tool is not
installed by consumer builds and adds no application npm dependency.

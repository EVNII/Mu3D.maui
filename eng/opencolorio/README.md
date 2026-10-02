# Pinned OpenColorIO adapter build

This directory is a maintainer tool, never part of consumer MSBuild. `manifest.json` pins OCIO
2.5.2 at commit `c52966a6677723d5bd2dbef0ccec3fed9cbc3790`, its source SHA256, and all six mandatory
dependency archives. The selected upstream release includes the CVE-2026-42450 LUT-parser fixes.

```sh
python3 eng/opencolorio/build.py --work /private/tmp/mu3d-ocio-build --download --jobs 6
```

Use Python 3.12+, CMake, Ninja and the host C++17 compiler. Omit `--download` for an offline build
with the seven pinned archives already present in the work directory. All source archives are
SHA256 checked before extraction. The script redirects upstream ExternalProject download clauses
to verified local sources, without modifying library implementation. Everything builds under the
chosen work directory; no system package or library is installed.

The default `--target osx-arm64` recipe builds a host macOS shared wrapper with static OCIO and
static third-party dependencies. Its only dynamic dependencies are Apple/system libraries. Scalar
FP32 OCIO evaluation avoids additional architecture-specific SIMD dependencies. Only the twelve
Mu3D C entry points are exported from the shared wrapper. The committed C ABI wrapper contains no
OCIO types in its public interface. Both current recipes require an arm64 macOS maintainer host.

The resulting `build/libmu3d_opencolorio.dylib` must be copied to the native test/application output
directory explicitly. The managed loader checks wrapper ABI 1 and exact OCIO 2.5.2.

## Mac Catalyst arm64

```sh
python3 eng/opencolorio/build.py --work /private/tmp/mu3d-ocio-build --target maccatalyst-arm64 --jobs 6
python3 eng/opencolorio/verify_catalyst.py --build /private/tmp/mu3d-ocio-build/build-maccatalyst-arm64
```

This separate build uses the repository's `arm64-apple-ios15.0-macabi` toolchain for OCIO, the wrapper
and all six dependencies. The OCIO target selects upstream `OCIO_HEADLESS_ENABLED` and removes
its unused macOS monitor frameworks from the link interface; CPU transforms neither query monitors
nor own display presentation. No upstream processing implementation is changed. The result is
`build-maccatalyst-arm64/runtimes/maccatalyst-arm64/native/libmu3d_opencolorio.a`, a complete static
archive for the package's Apple `NativeReference` target. It is not a macOS dylib.

Local Xcode 27 verification passed: all **256** archive objects carry `PLATFORM_MACCATALYST`, and
the headless C ABI executable reports `MACCATALYST`, minimum 15.0, SDK 27.0. It links only libc++ and
libSystem and executed the built-in ACES/HDR/alpha/lifetime checks. The separate Catalyst direct-C++
oracle reproduced all **384** committed reference components. The verification script repeats these
checks. This validates native CPU execution; this adapter's full .NET MAUI app linking, signing
and presentation remain unverified. Gallery's later Xcode 27 toolchain repair removed the former
workload blocker, but its portable views do not exercise optional native OCIO linkage.

Native artifacts stay in the explicit work directory and are not committed or bundled automatically.
iOS, Android, Windows and other architectures have no verified runtime recipe in this package and
are rejected by its loader. Existing general Apple/Android toolchains elsewhere in the repository
are not evidence of OpenColorIO support on those targets.

## Optional local package with verified runtimes

After building both targets, stage their verified artifacts and pack the already-built managed
assembly. These are maintainer commands; no package build target invokes either Python or CMake.

```sh
python3 eng/opencolorio/stage_runtime.py --work /private/tmp/mu3d-ocio-build --output /private/tmp/mu3d-ocio-runtime-stage
dotnet pack src/Mu3D.Native.OpenColorIO/Mu3D.Native.OpenColorIO.csproj -c Release --no-build --no-restore -p:Mu3DOpenColorIORuntimeRoot=/private/tmp/mu3d-ocio-runtime-stage -o /private/tmp/mu3d-ocio-packages
```

The staging check verifies macOS architecture/platform and ABI/release, the complete Catalyst
archive, native C ABI smoke, and 384 independent reference components on each target. It emits
artifact SHA256/size/provenance in `verification.json`. The optional pack property includes exactly
the two verified files under `runtimes/osx-arm64/native` and `runtimes/maccatalyst-arm64/native`, plus
that report. Omitting the property produces the managed adapter package without native binaries.
The runtime-bearing package's `buildTransitive` target automatically links its Catalyst archive
for `maccatalyst-arm64`; an explicit `Mu3DOpenColorIONativeArchive` still overrides its archive path.

`build/mu3d_ocio_oracle config.ocio source destination` is an independent direct-C++ OCIO processor.
It reads one whitespace-separated RGB triple per input line and writes its transformed triple.
Both native oracle and adapter request OCIO's lossless CPU optimization preset. The .NET test
executable exercises explicit config selection, RGB/HDR/negative values, Looks/display output,
output decoding, metadata, caches, native errors, exact alpha and independent processor lifetime.

Regenerate or verify the committed independent C++ reference with:

```sh
python3 eng/opencolorio/generate_reference.py --oracle /private/tmp/mu3d-ocio-build/build/mu3d_ocio_oracle --check
```

The 384 reference components cover signed/HDR matrices, nonlinear encoding, ACES AP1/AP0 and the
official ACES 2.0 SDR display transform. The test fixture stores source/config hashes and explicit
absolute/relative FP32 tolerances. It does not replace target-platform presentation validation.

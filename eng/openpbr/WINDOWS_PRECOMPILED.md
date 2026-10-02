# Maintainer Windows precompiled shaders

This is an optional `Mu3D.Rendering.OpenPbr` payload, not a consumer build task. Read ADR 0034
and preserve the pinned wgpu-native v29.0.1.1 / locked wgpu-hal 29.0.3 source and existing
D3D12 composition bridge. Windows native binaries never embed OpenPBR assets.

## Build the native backend

Use Rust 1.93.1, libclang 18.1.1 and Visual Studio C++ tools/libraries for each requested target.
The implementation was built with a repository-local Rust/Cargo setup and does not require changing
the system PATH. `CARGO_HOME`, `RUSTUP_HOME` and `LIBCLANG_PATH` can select isolated maintainer tools.

If only ARM64 CRT libraries are missing, `eng/prepare-windows-arm64-linker.ps1` downloads two
SHA-256-pinned Microsoft CRT development archives into artifacts and returns a target linker
wrapper. Assign its result to the process-local `CARGO_TARGET_AARCH64_PC_WINDOWS_MSVC_LINKER`
before the build. It uses the installed x64 host linker and ARM64 Windows SDK libraries and does
not install/modify Visual Studio. `-Linker` and `-WindowsSdkLibraries` select existing tools;
defaults match the verified MSVC 14.51.36231 / Windows SDK 10.0.26100.0 setup.

1. Clone the exact native tag with its webgpu-headers submodule beneath artifacts. Verify commit
   `6aed50955d934ac36049ba8d002034841633ae02`.
2. Apply `eng/verify-wgpu-native-d3d12-patch.ps1 -SourceDirectory <source> -Apply` to that clean
   checkout. The bridge patch uses zero context; the verifier now normalizes CRLF and applies it
   with `--unidiff-zero`.
3. Run `cargo fetch --locked` against that checkout. Copy the verified registry source
   `wgpu-hal-29.0.3` into a separate directory beneath artifacts; never edit Cargo's shared registry.
4. Run `eng/build-wgpu-shader-cache.ps1 -NativeSource <source> -HalSource <copy> -Stage`, selecting
   Python/Cargo paths if needed. The script applies idempotent checked anchors and uses the committed
   `eng/native/WgpuShaderCache.Cargo.lock`. Default RIDs are x64, x86 and ARM64. Use an explicit RID
   subset for development only. Complete packaging requires all three; incomplete builds are not
   silently replaced with upstream DLLs. Existing generated C bindings remain valid.
5. `eng/verify-wgpu-shader-cache.ps1` verifies source/lock/DLL provenance before runtime packaging.
   Update the license report with `eng/update-wgpu-cache-licenses.py --native-source <source>
   --registry <Cargo registry source directory>` if the extra lock graph changes.

The Windows runtime artifacts include only a generic cache and provenance, not renderer bytecode.
Public runtime archives/manifest publication remain a separate release action.

## Generate the optional bundle

Build the headless `Mu3D.OpenPbr.Rendering.Tests` executable against the patched runtime in an
isolated output directory. Use an empty, absolute capture directory and an empty disk cache for
new generations. Set these process environment variables:

- `MU3D_SHADER_CACHE_CAPTURE`: capture directory for exact compiler inputs, DXIL and embedded-source hashes.
- `MU3D_SHADER_CACHE_DIR`: writable throwaway cache directory.
- `MU3D_SHADER_CACHE_SEEDS`: empty/nonexistent directory so old packaged seeds do not suppress capture.
- `MU3D_DX12_SHADER_MODEL`: each of `6_0` through `6_8`, sequentially. Hardware/compiler ceilings
  still apply; capture requires hardware supporting the requested highest model.

Run the test executable with `--native --dx12 --cache-warmup-only --cache-log` for each model.
This performs compiler work on the maintainer machine before shipping, not in consumer app builds.
All five rendering modes use these three shader families. Keep the generated `.input` files and logs
as build evidence, not package payload. `source-manifest.json` prevents combining renderer versions.

Run `eng/openpbr/package_precompiled.py --capture <capture> --all-feature-variants --compiler <x64/dxcompiler.dll>
--compiler <x86/dxcompiler.dll> --compiler <arm64/dxcompiler.dll>` using the pinned DXC 1.8.2505.32
files. It also compiles the opposite ShaderF16 device-feature flag for models 6.2–6.8 with the
pinned offline compiler; OpenPBR arithmetic stays FP32 in both variants. This avoids tying the
bundle to the generating machine's device-feature support. It validates compiler hashes and capture integrity, writes the optional package's generated
bytecode and manifest, and rejects unexpected old output entries rather than removing files.
Use `--check` before packaging; regenerate after shader/backend/compiler changes. These files are
committed maintainer artifacts, like WGSL/LUT, so consumers need no compiler or GPU during build.

Package the renderer, then run `eng/verify-openpbr-optional.py --package <nupkg>` and a NuGet-only
consumer build. The package uses `buildTransitive/Mu3D.Rendering.OpenPbr.targets`; all entries must
land directly under `Mu3D/ShaderCache` adjacent to the application executable on Windows. Do not
import that target into the net10.0 renderer project: it would leak content through mobile project
references. Repository application/test projects import it explicitly instead. Deployment is
restricted to executable projects so a portable class library cannot forward Windows content
into a downstream mobile application.

## Acceptance

`eng/openpbr/test_shader_cache.py --host <test executable> --output <artifact directory>` checks
eight scenarios across independent processes, including bundled startup without writable cache.
Run the full headless rendering suite afterward. Cache logs separate DXC and driver PSO times.
Never report an OS-driver-cold result unless that condition was actually measured. An unseen
GPU/driver still needs its device-specific compilation even with precompiled DXIL.

Diagnostic-only process switches: `MU3D_SHADER_CACHE_DISABLE=1` bypasses this cache;
`MU3D_SHADER_CACHE_DIR` and `MU3D_SHADER_CACHE_SEEDS` override absolute paths. Invalid/unwritable
paths cannot prevent rendering. Normal applications need no environment variables or registration.
Android and Apple retain their existing backends/system behavior.

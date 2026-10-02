# Mu3D Web/WASM validation

Non-packable host for a library Web target. Shared Core/Graphics/Toolkit runs in WASM; thin
Canvas/JS handlers adapt lifecycle/input/bindings. The Emdawn device bridge is diagnostic-only,
with source-linked shared WGPU adapter code. Native bindings, the v29.0.1.1 pin and supported
1.0 platform matrix remain unchanged. This host uses the standalone WebAssembly SDK without a
Blazor UI host; HostContracts additionally exercises the core Blazor component renderer headlessly.

Validate Web features with trimmed Release AOT and compare the affected browser paths with
interpreted execution. Node and recording-device suites check shared logic and host contracts;
actual browser rendering, interaction and physical HDR require separate checks.
Public usage/defaults/ownership live in [Mu3D.Web](../../src/Mu3D.Web/README.md) and
[Mu3D.Web.Toolkit](../../src/Mu3D.Web.Toolkit/README.md).

## Suites and test ownership

| Suite | Boundary |
| --- | --- |
| Smoke | Core scene/mesh/camera, Creative FP16 values, static C P/Invoke and asynchronous reverse callback |
| Creative | Existing Creative test source (215 checks) |
| Graphics | Existing resource/command/output/device-loss contracts with test devices |
| HostContracts | Same desktop Viewer sources and shared Toolkit anchor checks; Canvas lifecycle/serialization and real Gallery route snapshot, query replacement and owner disposal |
| WgpuAbi | 20 sampled wasm32 layouts/constants, exact 64-bit future sentinel and C-flattened reverse callback |
| Emdawn | Actual bridge linking and asynchronous adapter request; browser success or Node unavailable path |
| Render | Actual shared SceneRenderer/PBR/Toolkit plus original Lighting/Occlusion and five OpenPBR Gallery modes/preparation → FP16 source → HDR/explicit SDR Canvas and GPU readback |
| CodecContracts (separate project) | Actual pinned JPEG/UltraHDR/KTX WASM P/Invoke layouts, HDR round trip, BC/ETC2/ASTC transcodes, caches/cancellation, all 24 native Model Lab imports/animation/variants and both bundled HDRI sources |

Core/Toolkit owns algorithms, helper rendering and statistics mathematics. Viewer owns host
property/input/selection/anchor mapping, submitted-frame diagnostics and JSON. JS tests own
browser adapter/DOM/lifecycle behavior with shared fixtures. HostContracts reuses those C# sources
in both WASM modes; no separate feature consumer is added. Helper recording checks share
HelperRenderChecks.cs; include it when source-linking an individual helper fixture elsewhere.
The Gallery renderer fixture explicitly preserves its closed component set. HostContracts applies
method-scoped IL2072 annotations to three framework reflection methods; production Gallery and
other suites do not use these test-only suppressions.

Intermediates are isolated by suite and Interpreted/Aot flavor. AOT strips intermediate method
bodies, so an interpreter build must use its own intermediates. Static asset compression is
disabled for correctness probes; these outputs do not establish production download size.

## Build and run shared contracts

Use the repository-pinned SDK/workloads. Install wasm-tools once if it is absent, then run:

```sh
dotnet workload install wasm-tools --disable-parallel
bash eng/validate-web-wasm.sh
node --test tests/Mu3D.Web.Viewer.Tests/*.test.mjs
```

The script restores each suite separately, publishes and runs Smoke/Creative/Graphics/HostContracts
under Node. It selects the optional repository-local SDK or `dotnet` on PATH;
MU3D_DOTNET overrides the SDK executable. MU3D_EMDAWN_ROOT additionally enables WgpuAbi.
No browser, GPU or MAUI UI is launched. Logs: artifacts/validation/web-wasm; outputs:
`artifacts/web-wasm/<suite>/wwwroot`. Run .NET builds serially.

For the same host contracts with trimming/AOT:

```sh
dotnet restore tests/Mu3D.Web.Validation/Mu3D.Web.Validation.csproj \
  --disable-parallel -p:Mu3DWebSuite=HostContracts -p:NuGetAudit=false
dotnet publish tests/Mu3D.Web.Validation/Mu3D.Web.Validation.csproj \
  -c Release --no-restore --disable-build-servers -m:1 -nr:false \
  -p:UseSharedCompilation=false -p:NuGetAudit=false -p:Mu3DWebSuite=HostContracts \
  -p:PublishTrimmed=true -p:RunAOTCompilation=true -p:DisableParallelAot=true \
  -p:DisableParallelEmccCompile=true -o artifacts/web-wasm/HostContractsAot
node artifacts/web-wasm/HostContractsAot/wwwroot/main.mjs
```

SmokeAot is Smoke with RunAOTCompilation=true and Mu3DWebRootCore=true. Rooting all Core methods
checks renderer compilation; it does not execute GPU rendering or represent a trimmed consumer size.
The GPU-independent suites do not execute OpenPBR/MAUI/printing rendering. The complete
[Gallery host](../../samples/Mu3D.Gallery/Web/README.md) runs the actual OpenPBR and other shared presenters; its browser rendering checks remain
separate from the codec and host contracts.

Build and execute the real codec/importer boundary in both WASM modes after
`python3 eng/build-web-codecs.py` prepares the pinned archives:

```sh
dotnet restore tests/Mu3D.Web.Validation/CodecContracts/Mu3D.Web.CodecContracts.csproj --disable-parallel
env FROZEN_CACHE= dotnet publish tests/Mu3D.Web.Validation/CodecContracts/Mu3D.Web.CodecContracts.csproj \
  -c Release --no-restore --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false \
  -p:PublishTrimmed=true -p:TrimMode=full -p:WasmCachePath="$PWD/artifacts/web-wasm/emdawn-cache" \
  -o artifacts/web-wasm/CodecContracts
cd artifacts/web-wasm/CodecContracts
node --input-type=module -e 'import {dotnet} from "./wwwroot/_framework/dotnet.js"; const runtime = await dotnet.create(); process.exitCode = await runtime.runMain(runtime.getConfig().mainAssemblyName, []);'
```

For AOT add `RunAOTCompilation=true`, `DisableParallelAot=true` and
`DisableParallelEmccCompile=true`, use `artifacts/web-wasm/CodecContractsAot`, and run the same
Node entry there. No fixture capability profile used by this suite claims a real browser GPU
supports the requested compressed format. The executed imports, HDR values and transcodes are
real; texture upload, Canvas rendering, physical HDR and browser input are separate checks.

From the repository root, profile the same matrix twice in one runtime after publishing each mode:

```sh
node tests/Mu3D.Web.Validation/CodecContracts/measure-memory.mjs artifacts/web-wasm/CodecContracts artifacts/validation/web-wasm/codec-memory-interpreted.json
node tests/Mu3D.Web.Validation/CodecContracts/measure-memory.mjs artifacts/web-wasm/CodecContractsAot artifacts/validation/web-wasm/codec-memory-aot.json
```

Run serially without a concurrent build for comparable CPU times. The profile checks retired
asset/image owners and allocation bounds, and records raw decoded-array weak references separately.
AOT uses conservative native-stack roots: an unowned large array may survive an initial collection,
so the regression checks retention and WASM capacity growth across identical cycles. GC runs only
in this test host, after an asynchronous selection boundary or after Main fully returns; it is
not Gallery frame-loop policy. Managed heap, WASM linear capacity and Node RSS are distinct
measurements. This CPU workload does not measure browser FPS, GPU memory or mobile suitability.

## Browser bridge and Render publication

The source-only Dawn/Emdawn package is pinned to v20260928.195327, SHA-256
c142ad66d1f2cea912a14477850404088b08f93d18851d85acee1e417010f2a1. Explicit maintainer preparation:

```sh
python3 eng/probe-emdawn-web.py --download
python3 eng/probe-emdawn-web.py --stack-dependency-compat
```

The unmodified port fails linking on $stackSave/$stackRestore with .NET's Emscripten3.1.56.
The second command makes an isolated compatibility copy changing only those dependency names
under artifacts/web-wasm/emdawn-compat. Installed SDK/archive/native bindings stay unchanged;
this is not an upstream compatibility guarantee or consumer-time download step.

Publish the actual scene with the existing compatibility port/cache:

```sh
dotnet restore tests/Mu3D.Web.Validation/Mu3D.Web.Validation.csproj \
  --disable-parallel -p:Mu3DWebSuite=Render -p:NuGetAudit=false
dotnet publish tests/Mu3D.Web.Validation/Mu3D.Web.Validation.csproj \
  -c Release --no-restore --disable-build-servers -m:1 -nr:false \
  -p:UseSharedCompilation=false -p:NuGetAudit=false -p:Mu3DWebSuite=Render \
  -p:PublishTrimmed=true -p:WasmCachePath="$PWD/artifacts/web-wasm/emdawn-cache" \
  -p:EmdawnWebGpuRoot="$PWD/artifacts/web-wasm/emdawn-compat/emdawnwebgpu_pkg" \
  -o artifacts/web-wasm/RenderPerf
```

Render is the normal output destination. RenderPerf is the interpreted comparison; for RenderAot
add RunAOTCompilation=true, DisableParallelAot=true and DisableParallelEmccCompile=true and change
the output destination. Restore with the same suite property before publishing. Browser-only
bootstrap/readback partials differ; shared renderer/resource/command code is source-linked.

Retain the intentional compiler/ABI controls:

- Gallery's shared Infrastructure/BrowserWgpuBindings.targets emits explicit-Cdecl DllImport intermediates for 228
  imports. LibraryImport+UnmanagedCallConv(Cdecl) triggered Mono10.0.12 macOS ARM64 sgen-alloc.c:409.
  The transformation fails closed on changed source shapes. Native generated files are untouched.
- python3 eng/repro-web-aot-callconv.py checks the original failing attribute pair plus passing
  default-LibraryImport and explicit-Cdecl-DllImport controls. No native function is executed.
- WgpuAbi with --flat-callbacks passes. Without it the known by-value WGPUStringView callback gets
  length0 instead of8; browser ?raw=1 selects that failure. The shim passes pointer/length via nint
  under disabled runtime marshalling. Sampled matching layouts do not prove the full ABI.
- Emdawn uses the same port/cache publication flags with Mu3DWebSuite=Emdawn. Node without
  navigator.gpu reports Unavailable with the full actual bridge error; that is not GPU success.

## Serve current, matching builds

```sh
python3 eng/serve-web-validation.py
```

Use http://127.0.0.1:8765/Render/?demo=viewer#mu3d-canvas or /Render/ for static HDR. The source HTML
shows a local-service link for file URLs because browser modules cannot load via file://; the
source directory has no _framework payload. The loopback server allowlists published suites;
missing builds return404, --port selects another port, Ctrl+C stops it. Render overlays the current
runner; RenderPerf/RenderAot use their own published assets. After managed changes republish the
matching assemblies; a new JS runner over an old WASM assembly is insufficient. Static-only changes
must update matching runner files/modules in every affected output.

Viewer demonstrates shared PBR, Orbit/Gizmo, pen reports, application selection/Outline, node-local
HTML content and the native-equivalent statistics panel. Register Gizmo/raw input before selection
and Orbit; cancel drags before resize/camera/tool changes. Paused Gizmo capture keeps the existing
viewport clock active so FPS continues updating, then returns to on-demand. Ordinary tap jitter
uses the shared 6 CSS-pixel threshold. History/collection/visibility remain distinct.

The startup GPU check reads eight 1-pixel source/Canvas samples (2KiB) independent of resolution;
animation has no per-frame readback. CPU colors/transforms remain FP32, scene targets FP16. HDR
Canvas is rgba16float/srgb/opaque/extended with exactly one extended-sRGB transfer; explicit
bgra8unorm/standard SDR preview clips only the display pass. Source RGB0.25/1/2/4 and alpha1 remain
unchanged at 0/-2EV. At0EV Canvas RGB is approximately0.5366/0.9995/1.3525/1.8242; explicit SDR
approximately0.5373/1/1/1. Physical device-pixel sizing takes precedence over CSS×reportedDPR.

Successful submission drives immutable node projections and FrameStatisticsCollector. Startup,
resize/refresh and broken continuity prime cadence; independent on-demand idle periods do not
count as continuous FPS. Statistics JSON uses source generation and transfers full snapshots only
on version changes. CPU stages/actual supplied counts are reported; GPU/presentation/display
measurements and unknown counts stay null/—. Overlays add no render timer or GPU queries.

## HDR diagnostics and measurements

/Render/hdr-diagnostic.html provides an independent direct-WebGPU baseline. It separates ordinary
native controls, redraw, same-size reconfiguration, FP16 tone mapping and SDR/HDR format changes;
optional alpha/loop/readback/layout comparisons remain available. Refresh between experiments.
No numeric readback, requested format, capability query or SDR screenshot proves physical HDR.

/RenderPerf/benchmark.html and /RenderAot/benchmark.html keep a fixed manual measurement loop:
960×576, 1/60-second step, 120 warmup, 180 ordinary and 300 instrumented calls. Alternate fresh
pages only after both builds finish. Results retain raw round-trip/input JSON/output JSON/overlay/
RAF intervals plus managed scene-update/render/anchors/report/serialization stages, CPU renderer
stages and current-thread allocations. Measurement includes probe overhead/back-pressure and
excludes JS/native/GPU allocation/outer marshaler. Clock quantization can report zero short stages;
synchronous string echo is a separate lower-bound control. CPU wall time/cadence is not GPU time,
display refresh or throughput; loopback readiness is not cold-network startup. Compare fresh
measurements from matching builds, retaining the browser, hardware and build configuration.

Core's --renderer-cache-checks / --renderer-cache-measure <json> and Toolkit's warmed helper tests
remain runnable. MU3D_GIZMO_ALLOCATION_EVIDENCE, MU3D_GRID_ALLOCATION_EVIDENCE and
MU3D_AXES_ALLOCATION_EVIDENCE write optional JSON. Geometry/ordering/failure/ownership checks stay
independent; shared fixtures only supply mechanics. Desktop recording budgets are not browser FPS.

For browser validation, compare the affected routes in AOT and interpreted builds, including first
load, resize, materials/textures, device-loss recovery and transparent composition. Record browser
engine, hardware, download size and performance separately from physical HDR-display measurements.

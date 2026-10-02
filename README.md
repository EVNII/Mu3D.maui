# Mu3D

Mu3D is an HDR-first, backend-independent 3D graphics library for .NET MAUI. Its default
graphics backend uses precompiled `wgpu-native` binaries through generated C# bindings.

Mu3D provides declarative scenes, glTF assets, HDR color management, OpenPBR rendering and optional
camera, gizmo, playback and diagnostic tools. Native Gallery uses MAUI/XAML; Web Gallery uses
Blazor WebAssembly with the same renderer, Toolkit and example logic through thin Canvas handlers.
CPU colors and transforms retain FP32 semantics, while normal HDR attachments use FP16.

See [developer documentation](https://evnii.github.io/Mu3D.maui/v0.1/) and the
[native Gallery guide](samples/Mu3D.Gallery/README.md), alongside the
[Web Gallery build guide](samples/Mu3D.Gallery/Web/README.md).

This is a preview source release. NuGet package publication is not enabled yet. The public
repository is [EVNII/Mu3D.maui](https://github.com/EVNII/Mu3D.maui); its source snapshots exclude
private development history, internal planning documents and AI instructions.

## Packages

Native implementation packages are separated from their assembly-free runtime carriers:

- `Mu3D.Native.Wgpu.Runtime` — complete ABI-locked wgpu-native RID matrix.
- `Mu3D.Native.Ktx.Runtime` — complete pinned libktx RID matrix.
- `Mu3D.Native.UltraHdr.Runtime.Jpeg` — JPEG-only libultrahdr/libjpeg-turbo RID matrix; no HEIF.

Applications reference the managed package; its published form selects the exact matching runtime
carrier transitively. Runtime carriers contain no Mu3D managed assembly, PDB, SourceLink data or
source package.

- `Mu3D.Maui` — public MAUI package and controls.
- `Mu3D.Native.Wgpu` — default wgpu-native backend.
- `Mu3D.Native.Ktx` — optional pinned libktx adapter for glTF KTX2/BasisU textures; applications
  may replace or omit it.
- `Mu3D.Native.UltraHdr` — optional pinned JPEG/HEIF adapters with Ultra HDR and ISO gain-map
  capability.
- `Mu3D.Graphics` — backend-independent graphics API.
- `Mu3D.Color` — color spaces, HDR output and transforms.
- `Mu3D.Core` — scene and renderer domain model.
- `Mu3D.Formats.Gltf` — optional glTF 2.0/GLB import; applications may replace its injected image
  decoder and do not need the package for procedural or otherwise imported scenes.
- `Mu3D.Toolkit` — optional UI/backend-independent orbit and transform-gizmo controllers plus
  gizmo-own-handle hit testing, HDR-linear handle rendering, revocable control arbitration, rolling
  frame/render statistics, playback transport contracts and a one-shot frame-request boundary.

- `Mu3D.Maui.Toolkit` — optional MAUI pan/pinch orbit, supported-platform wheel/focused-key input,
  primary-pointer transform-gizmo capture, shared viewport overlays, playback/output toolbars,
  throttled-statistics behaviors and diagnostics view.

Toolkit features are statically composed, not reflection-discovered plugins. The high-level
`Mu3DSceneView` is part of the main `Mu3D.Maui` package; `Mu3DView` remains the low-level custom-draw
and manual escape hatch.

Native-layer debugging is opt-in through one or more platform packages:

- `Mu3D.Native.Wgpu.DebugRuntime.Android`
- `Mu3D.Native.Wgpu.DebugRuntime.iOS`
- `Mu3D.Native.Wgpu.DebugRuntime.MacCatalyst`
- `Mu3D.Native.Wgpu.DebugRuntime.Windows`

These packages are for diagnosing Rust/native behavior. A managed Debug build without one of
these explicit references continues to use the smaller Release native runtime.

## Build

The repository requires the .NET SDK pinned by `global.json` (minimum 10.0.401), with workload set
10.0.401. Keep the SDK and workload set aligned: SDK 10.0.400 requests the 10.0.11 AOT compiler,
whereas this workload set installs 10.0.12.
`rollForward: latestPatch` accepts newer 10.0.4xx servicing releases but deliberately does not jump
to another feature band. `global.json` searches an optional isolated SDK installation at
`artifacts/toolchain/dotnet` before the system installation. When using that local installation,
run `artifacts/toolchain/dotnet/dotnet workload restore samples/Mu3D.Gallery/Mu3D.Gallery.csproj` once
to install workloads for that SDK root.

Gallery's Mac Catalyst target explicitly selects platform SDK 27.0 from that workload set for
Xcode 27.0 and requires Mac Catalyst 17.0. The public libraries retain their existing platform
minimums. The Apple SDK is the opt-in `27.0.10539-xcode27.0` preview; only its informational
`XCODE_27_0_PREVIEW` warning is suppressed, and Xcode compatibility validation remains enabled.
Gallery registers `MauiUISceneDelegate` because SDK 27 requires the scene lifecycle. A scoped
manifest-input target also ensures Info.plist edits reach incremental Mac Catalyst builds.

```shell
dotnet restore Mu3D.slnx
dotnet build src/Mu3D.Core/Mu3D.Core.csproj --no-restore
dotnet run --project tests/Mu3D.Core.Tests --configuration Release
dotnet run --project tests/Mu3D.Toolkit.Tests --configuration Release
dotnet run --project tests/Mu3D.Maui.Toolkit.Tests --configuration Release
dotnet build src/Mu3D.Maui/Mu3D.Maui.csproj -f net10.0-android --no-restore
dotnet build src/Mu3D.Maui.Toolkit/Mu3D.Maui.Toolkit.csproj -f net10.0-android --no-restore
dotnet build samples/Mu3D.Gallery/Mu3D.Gallery.csproj -f net10.0-android --no-restore
```

Use the corresponding `net10.0-ios`, `net10.0-maccatalyst`, or Windows target framework
to validate another platform. Apple application packaging may additionally require signing.

The MAUI projects follow the Visual Studio template's host-conditioned target list: Android
is present on every host, iOS and Mac Catalyst are omitted only on Linux, and Windows is included
only on Windows. This lets Linux restore/build Android without attempting Apple workloads, while
Windows retains the Apple targets used with Pair to Mac. `Microsoft.Maui.Controls` is pinned to
`10.0.90`; Gallery's debug logging package is pinned to `10.0.11`, matching the reference Visual
Studio projects. Prefer an explicit `-f` target when building a single platform.

The default native backend version is pinned in `eng/Mu3D.WgpuNativeVersion.props` and is
not a consumer-facing override. Changing it requires regenerating and publishing a matched
backend package.

## MAUI control usage

Register Mu3D once in `MauiProgram`:

```csharp
builder.UseMauiApp<App>().UseMu3D();
```

The normal XAML path does not create or resize a native session in application code:

```xml
<mu3d:Mu3DView x:Name="SurfaceView"
                 Draw="OnSurfaceDraw" />
```

`Mu3DView` automatically owns the default backend session, native-surface recreation, physical-
pixel/DPI resize and disposal. The application supplies only the commands for a frame:

```csharp
private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
{
    using GraphicsCommandEncoder encoder = e.Device.CreateCommandEncoder("sample frame");
    using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
        new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(
                e.Target,
                clearColor: new GraphicsClearColor(0.02f, 0.04f, 0.12f, 1.0f)),
            "sample surface pass")))
    {
    }
    using GraphicsCommandBuffer commands = encoder.Finish("sample commands");
    e.Device.Queue.Submit(commands);
}
```

The first ready surface and every resize request an on-demand draw automatically. Call
`SurfaceView.InvalidateSurface()` after scene state changes. Engines that intentionally own their
swapchain lifecycle can set `SurfaceManagement="Manual"` and use the explicit native/size events;
manual mode is not required for an ordinary application.

`Mu3DSceneView` now accepts an application-owned `Scene` and `Camera`, owns the automatic surface,
`SceneRenderer` and depth attachment, follows physical resize and exposes `InvalidateScene()` for
mutated scene state. Its default pipeline selects a stable `RenderOutputId` through an explicit
per-view `RenderOutputRegistry`; downstream output factories are registered directly without
reflection discovery. Built-in output passes do not mutate renderer-wide compatibility state. Its
ordered `Features` collection attaches statically referenced viewport features with the native
Handler, releases their attachment leases in reverse order and lets each context register owned
passes around the default scene pass.
For concise MAUI pages, the same view accepts a nested `Scene3D` content object. The first facade
slice declares a `PerspectiveCamera3D`, `Sphere3D`, `Cone3D`, `DirectionalLight3D`,
`PbrMaterial3D` and `UnlitMaterial3D` entirely in XAML; its wrappers retain explicit Core-object
escape hatches and automatically coalesce a frame after bindable changes.
Applications can assign an explicitly constructed `RenderPipeline` to compose scene, helper and
overlay passes in order; pass descriptors declare color/depth Clear/Load behavior, outputs, size
dependency and a shared linear working space. The view borrows custom pipelines and never disposes
their passes. The optional MAUI Toolkit maps pan/pinch, pointer wheel/trackpad scroll and optional
focused hardware keys to an application-owned `OrbitController`; its declarative `MapTool` preset
uses world-up-plane Pan and per-device mouse, trackpad and touchscreen Rotate/Pan mappings. Its
`NavigationCommand` is the preferred MAUI button, menu, gesture and keyboard-accelerator boundary,
while the device-separated input objects and optional native rotate/dolly keys remain strongly
typed XAML properties. The separate `FlyController`/`FlyTool` mode moves and looks without
an orbit target through the same application-owned MAUI command philosophy. The Toolkit can also
collect successful frame statistics without owning selection or editor policy. Capability
properties report the current platform adapter boundary. Toolkit can hit-test its own transform
handles, and the opt-in MAUI behavior maps accepted mouse/touch drags into them while leaving target
selection, undo and pen semantics to the application.
`TransformGizmoRenderPass` can then be explicitly ordered after `SceneRenderPass` to draw HDR-linear
physical-pixel handles while loading the scene color and depth attachments. Gallery's **Transform
Gizmo** page demonstrates this full composition. Keep using
`Mu3DView.Draw` for unrestricted custom command encoding or swapchain policy.

## Developer documentation

The repository pins DocFX 2.78.5 and builds reviewed public articles plus API metadata from staged
Release DLL/XML pairs. The public allowlist lives under `docs-public`; the internal `docs` tree is
never site input. Build and validate the static site locally with:

```shell
dotnet run --project tools/Mu3D.Docs/Mu3D.Docs.csproj -- build
```

The command writes ignored output to `artifacts/docs/_site`, treats DocFX warnings as errors and
then checks the public boundary, Gallery feature catalog and generated relative links. To test the
Gallery's Debug **Docs** action, serve that already-built directory on its configured port:

```shell
dotnet docfx serve artifacts/docs/_site --port 8080
```

Every focused Examples page and Advanced **Material Conformance** has one stable feature ID,
reviewed public article and standard Toolbar **Docs** action. The build validator reads the actual
Shell registrations and rejects a missing/duplicate page action, route mismatch, orphan article,
TOC mismatch or runtime-catalog difference.
Debug builds use `http://localhost:8080/v0.1/`. Other configurations use the versioned Pages URL,
which can be overridden with `Mu3DDocumentationBaseUrl`. The public Pages site is hosted by
`EVNII/Mu3D.maui`, with versioned documentation under `/Mu3D.maui/v0.1/` and both Gallery modes.

The actual Gallery also has an experimental Blazor WebAssembly host, with shared native example
logic and assets. Its [build and Pages guide](samples/Mu3D.Gallery/Web/README.md) uses the same local
scripts as the manual **Web Gallery Pages** Action. Local publication avoids hosted AOT builds;
the Action remains ready for later use, without running on ordinary pushes.

API staging includes the optional `Mu3D.Color.Printing` package. The documentation and managed-pack
commands build all public packages; this does not add Printing to normal library dependencies.
Applications opt in with a project/package reference. Gallery enables its CMYK example by default;
`-p:EnableMu3DPrinting=false` excludes the extension, page and source assets from its build.

JPEG decoding is application-replaceable. Implement `Mu3D.Assets.IEncodedImageDecoder` and pass
the instance through `GltfImportOptions.ImageDecoder`; `Mu3D.Native.UltraHdr.JpegImageDecoder`
is an optional JPEG implementation with Ultra HDR/ISO gain-map capability, not a global service
selected by the renderer or glTF package. `HeifImageDecoder` is a separate HEIF/HEIC/AVIF adapter
and can wrap another application-owned decoder when both MIME families are required.
The normal codec package is JPEG-only. Its C# project property
`Mu3DUltraHdrEnableHeif` defaults to `false`; setting it to `true` selects a separately built and
verified `release-heif` native feature set during maintainer packing and never runs CMake in an
application build.

```csharp
GltfAsset asset = GltfImporter.ImportWithOptions(
    encodedGltf,
    new GltfImportOptions { ImageDecoder = myApplicationDecoder });

IEncodedImageDecoder jpegAndHeif = new HeifImageDecoder(new JpegImageDecoder());
```

## Local package release without GitHub Actions

Package publication is deferred for this preview. Maintainer scripts produce separate runtime and
managed outputs for local validation. Package versioning is centralized in
`eng/Mu3D.PackageVersion.props`.

First stage every verified native RID under the declared `artifacts/<dependency>/<version>/release`
layout. A runtime package is deliberately not created from only the current host architecture.
Then run locally:

```powershell
./eng/pack-runtime-packages.ps1
./eng/pack-managed-packages.ps1
./eng/test-package-consumer.ps1
```

The runtime command requires all twelve RIDs, rejects source, PDB and managed `lib/`/`ref/`
content, enforces the public-feed size limit and records package SHA-256 values. Managed packages
restore the exact runtime version from that verified local feed. The consumer test creates a clean
MAUI project outside the source graph and checks that NuGet restore places the Windows wgpu DLL in
its output.

wgpu publication also requires a reviewed `licenses/wgpu-native-dependencies.html` generated from
the exact pinned Cargo.lock. Top-level MIT/Apache files do not replace the transitive Rust license
audit, and the runtime project fails packing while that report is absent.

Gallery keeps its direct native-staging fallback until the first complete local feed is available.
Use `-p:Mu3DUseRuntimePackages=true` with `artifacts/packages/public-runtime` as a Restore source to
exercise the same package path as a downstream application. Personal Apple signing identities are
not committed; each developer or release environment supplies its own signing properties.

Public package upload remains a separate release step. Source synchronization and Pages deployment
do not invoke it or upload NuGet packages.

## Maintainer native asset checks

```shell
dotnet run --project tools/Mu3D.WgpuGen -- \
  validate-manifest eng/wgpu-native-assets.json eng/Mu3D.WgpuNativeVersion.props
```

`Mu3D.WgpuGen` also verifies downloaded archives by RID/configuration and generates checked-in
backend metadata. These commands are maintainer and CI operations; they never run in consuming
MAUI applications.

The complete internal raw ABI is reproduced from the two headers in a verified archive. Restore
the repository-pinned generator before generating or checking drift:

```shell
dotnet tool restore
dotnet run --project tools/Mu3D.WgpuGen -- \
  generate-bindings eng/wgpu-native-assets.json eng/Mu3D.WgpuNativeVersion.props \
  path/to/include/webgpu/webgpu.h path/to/include/webgpu/wgpu.h \
  src/Mu3D.Native.Wgpu/Generated/WgpuNative.Raw.g.cs

dotnet run --project tools/Mu3D.WgpuGen -- \
  verify-bindings eng/wgpu-native-assets.json eng/Mu3D.WgpuNativeVersion.props \
  path/to/include/webgpu/webgpu.h path/to/include/webgpu/wgpu.h \
  src/Mu3D.Native.Wgpu/Generated/WgpuNative.Raw.g.cs

dotnet run --project tools/Mu3D.WgpuGen -- \
  verify-native-exports src/Mu3D.Native.Wgpu/Generated/WgpuNative.Raw.g.cs \
  path/to/native/library
```

Native export verification reads PE/DLL export directories directly in managed code and uses
`nm` for Unix libraries. Set the maintainer-only `MU3D_NATIVE_SYMBOL_TOOL` environment variable
when CI must use a target-specific `llvm-nm`, such as the Android NDK tool.

The optional KTX workflow publishes both a complete NuGet package and a single verified 12-RID
runtime artifact. After that workflow succeeds, authenticate GitHub CLI and stage its native files
for Mu3D Gallery with:

```shell
gh auth login -h github.com
./eng/download-ktx-runtimes.sh
dotnet run --project samples/Mu3D.Gallery/Mu3D.Gallery.csproj \
  -f net10.0-maccatalyst -r maccatalyst-arm64
```

Gallery automatically enables the Apple or Windows KTX harness when the verified native library for
the selected RID is present. Other platforms remain opt-in with `-p:Mu3DEnableKtxHarness=true` after
their complete runtime set has been staged.

The optional libultrahdr/libjpeg-turbo workflow follows the same 12-RID contract. After a successful
`native-codecs-platforms.yml` run, stage its verified JPEG-only matrix with:

```shell
bash ./eng/download-ultrahdr-runtimes.sh
```

When hosted Actions are unavailable, a Windows maintainer can build and verify every Windows KTX
and JPEG/Ultra HDR runtime locally:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\build-windows-native-runtimes.ps1
```

The script uses the repository-pinned libktx v4.4.2, libultrahdr v2.0.1 and libjpeg-turbo 3.1.0
commits, downloads a SHA-256-pinned NASM tool, builds `win-x86`, `win-x64` and `win-arm64`, checks
the PE machine and every managed native export, and writes
`artifacts/native-build/windows/SHA256SUMS.windows`. It requires Visual Studio 2022 or 2026 with
Desktop C++ tools; cross-building `win-arm64` additionally requires the latest
`Microsoft.VisualStudio.Component.VC.Tools.ARM64` component. A subset can be rebuilt with
`-RuntimeIdentifier win-x86,win-x64` without removing hashes for already staged Windows slices.
No GitHub Actions run or consumer-side CMake step is involved.

On a Windows x64 development machine, the staged binaries are discovered automatically by Gallery:

```powershell
dotnet build samples/Mu3D.Gallery/Mu3D.Gallery.csproj `
  -f net10.0-windows10.0.19041.0 -c Release
```

The output contains the selected RID's `wgpu_native.dll`, `ktx.dll`, `uhdr.dll` and
`turbojpeg.dll`. Windows executable architectures remain separate; an MSIX bundle, rather than a
made-up universal PE DLL, combines architectures for distribution.

The artifact and NuGet package contain separate native slices rather than one impossible universal
binary: Android may package all four ABI-specific `.so` pairs into a universal APK (or let an AAB
split them), Windows selects one PE architecture and may combine application packages in an MSIX
bundle, and Apple keeps device/simulator/Mac Catalyst RIDs distinct while the final Mac app may be
published as Universal 2. CI checks architecture, required exports, complete paths and SHA-256
before packing. Third-party sources are checked out at exact commits only in isolated CI jobs; they
are not Git submodules and are not part of a consuming checkout.

Pass an explicit workflow run ID to the script to reproduce an older retained artifact. Without an
argument it selects the newest successful `native-ktx-platforms.yml` run. The script downloads
`Mu3D.Native.Ktx.Runtimes.PlatformMatrixVerified`, validates its recorded SHA-256 file and all twelve
expected runtime paths, then stages it under `artifacts/ktx/v4.4.2/release/runtimes`. Set
`MU3D_GITHUB_REPOSITORY` only when downloading from a fork.

All immutable upstream runtimes for one configuration can be downloaded, verified and staged in
one fail-closed operation:

```shell
dotnet run --project tools/Mu3D.WgpuGen -- \
  prepare-runtimes eng/wgpu-native-assets.json \
  eng/Mu3D.WgpuNativeVersion.props release artifacts/downloads artifacts/wgpu-native
```

This prepares all twelve baseline RIDs. Mac Catalyst archives come from Mu3D's pinned native
distribution release; Android, iOS and the baseline Windows archives come from the pinned upstream
release. The upstream Windows DLLs support the direct opaque Surface, but they do not contain ADR
0022's three D3D12 bridge exports and must not be used to publish the transparent Windows mask
carrier.
For a private Mu3D repository, maintainers set `MU3D_GITHUB_TOKEN` to a token with read-only
contents access. Public distribution assets require no token.

Windows additionally requires the pinned Microsoft DXC compiler and validator; FXC cannot
compile the OpenPBR transport shaders. After baseline runtime preparation, run:

```powershell
pwsh -File eng/prepare-windows-dxc.ps1
```

This maintainer-only script verifies the official DXC 1.8.2505.32 NuGet archive and all six
DLL hashes in `eng/dxc-assets.json`, then stages x64/x86/ARM64 `dxcompiler.dll` and `dxil.dll`
beside wgpu. `build-windows-native-runtimes.ps1` also performs this step. Source samples
and the native/OpenPBR test executables copy the matching Windows assets automatically;
NuGet applications receive them from `Mu3D.Native.Wgpu.Runtime`. No consumer build downloads
DXC. To cover the Windows presentation compiler without opening a window, run the OpenPBR
rendering tests with `--native --dx12` (and optionally `--dump-shaders` for generated HLSL).

Before building the patched Windows runtimes, verify or apply the version-specific patch against
the exact pinned source checkout:

```powershell
./eng/verify-wgpu-native-d3d12-patch.ps1 `
  -SourceDirectory artifacts/wgpu-native-src/v29.0.1.1 `
  -Apply
```

The script fails if the checkout commit, patch contents or four modified upstream files drift. After
building `win-x64`, `win-x86` and `win-arm64`, verify each DLL without loading the non-host
architecture:

```powershell
dotnet run --project tools/Mu3D.WgpuGen --no-restore -- `
  verify-windows-d3d12-bridge `
  artifacts/wgpu-native/v29.0.1.1/release/runtimes/win-x64/native/wgpu_native.dll
```

Run the same command for `win-x86` and `win-arm64`. The complete Windows native-runtime script also
checks those exports. Packaging remains blocked until the three patched archives are published as
immutable Mu3D distribution assets and their URLs, sizes and hashes replace the baseline Windows
entries in `eng/wgpu-native-assets.json`.

To prepare only one platform family, especially the much larger opt-in Debug libraries:

```shell
dotnet run --project tools/Mu3D.WgpuGen -- \
  prepare-platform-runtimes eng/wgpu-native-assets.json \
  eng/Mu3D.WgpuNativeVersion.props ios debug artifacts/downloads artifacts/wgpu-native
```

Accepted platform groups are `android`, `ios` (device and simulators), `maccatalyst`, and `windows`.

To deploy Mu3D Gallery's explicit native device probe, first stage the pinned Release runtimes:

```shell
dotnet run --project tools/Mu3D.WgpuGen -- \
  prepare-platform-runtimes eng/wgpu-native-assets.json \
  eng/Mu3D.WgpuNativeVersion.props android release artifacts/downloads artifacts/wgpu-native
dotnet build samples/Mu3D.Gallery/Mu3D.Gallery.csproj -f net10.0-android
```

Use platform `ios`, `maccatalyst`, or `windows` and an explicit runtime identifier when appropriate.
Mu3D Gallery links the staged pinned Release runtime by default on every supported platform. Android
provides all four upstream ABI inputs and packages those selected by the build's runtime identifiers;
Windows selects one of `win-x64`, `win-x86`, or `win-arm64` and copies it to the output as
`wgpu_native.dll`. Its MAUI handler bridges WinUI 3's `SwapChainPanel` through
`ISwapChainPanelNative` to the D3D12 backend. The probe creates and releases a real adapter/device,
checks ShaderF16, and creates an RGBA16Float texture.
It deliberately reports that surface/HDR presentation was not probed; that requires the next
physical-display validation stage.

Mac Catalyst development builds enable the Release native harness by default, including when
running `dotnet run` from `samples/Mu3D.Gallery`. Prepare its pinned assets once before the first run:

```shell
dotnet run --project tools/Mu3D.WgpuGen -- \
  prepare-platform-runtimes eng/wgpu-native-assets.json \
  eng/Mu3D.WgpuNativeVersion.props maccatalyst release artifacts/downloads artifacts/wgpu-native
```

The build selects `maccatalyst-arm64` or `maccatalyst-x64` from `$(RuntimeIdentifier)` and fails
before launch if the exact ABI-pinned static library is unavailable.

Mu3D Gallery also contains a Surface Probe page. Call `.UseMu3D()` while constructing the MAUI app
to register its platform handler. Android supplies a retained `ANativeWindow`; iOS and Mac Catalyst
use a `UIView` whose backing layer is a real `CAMetalLayer`. The page queries surface capabilities,
applies the explicit HDR/SDR fallback policy, and configures an `RGBA16Float` extended-range surface
when advertised. After configuration, its extended-range test button acquires a surface texture,
clears it with an FP32 linear color containing a `2.0` blue component, submits the render pass and
presents the frame. Configuration and GPU presentation are reported separately from independently
measured physical HDR display verification.

The packed iOS DebugRuntime consumer fixture is intentionally not part of the normal solution
build because it consumes locally produced NuGet packages. CI first packs the DebugRuntime and
the test-only `BasePackageFixture.csproj` backend fixture into a local feed, restores
`tests/Mu3D.DebugRuntime.Tests/PackageConsumer/IosConsumer.proj`, and runs the
`VerifyDebugRuntimePackage` target to prove that exactly one Debug static library is selected.
The sibling `AndroidConsumer.csproj` fixture proves that the packed Android package selects four
Debug ABI libraries and removes the Release copy-local runtime before an Android build.
After building the fixture, `Mu3D.DebugRuntime.Tests verify-android-aar` byte-compares every AAR
library with its manifest-verified staged Debug runtime.
`WindowsConsumer.csproj` performs the equivalent local-feed replacement test for `win-x64` and
copies the selected native file to the required output name `wgpu_native.dll`.
The `verify-windows-output` test command byte-compares that output with the staged Debug DLL.
`MacCatalystConsumer.proj` is run after the fixed-commit macabi CI build has produced both
architectures. That workflow also prepares every upstream Release runtime, packs the complete
`Mu3D.Native.Wgpu` package, and verifies that the opt-in Mac Catalyst package replaces its
Release linker input.

Normal push and pull-request CI runs binding drift, managed ABI/ownership, DebugRuntime MSBuild,
and MAUI compilation checks. The much larger Android/iOS/Windows Debug archives are validated by
the manually dispatched `Native Debug Runtimes` workflow so ordinary changes do not repeatedly
download hundreds of megabytes of native diagnostics binaries.

The manually dispatched `Native Lifecycle` workflow loads the manifest-verified Windows Release
DLL and calls the real instance, adapter, and device APIs using the D3D12 fallback adapter. The
same test executable accepts `native-lifecycle` without `--force-fallback` on a GPU-equipped host.

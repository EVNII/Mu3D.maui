# Mu3D Web Gallery

Non-packable Blazor WebAssembly host in the existing Gallery family. Native Gallery keeps
MAUI/XAML and native presentation; there is no Blazor Hybrid/WebView conversion. Razor supplies
browser navigation and controls. One .NET runtime uses the shared Core, renderer and Toolkit;
thin Canvas/input handlers connect the existing experimental WebGPU bridge.

Native and Web use the same portable `GalleryCatalog`: **44 entries** (43 with
`EnableMu3DPrinting=false`) in Basics, Rendering, Toolkit, Assets and About, with shared case IDs,
order and search policy. This includes all 38 focused cases, Advanced Material Conformance,
Emissive Material, Translucent HDR Canvas, Painting Color Spaces, Procedural 3D Feed and Licenses. OpenPBR/MaterialX,
white furnace and Device/Surface Probe retain their actual examples. The old `pbr-material` URL
redirects to the `pbr-material-textures` example.

The persistent Razor `GalleryShell` supplies a desktop sidebar, compact category navigation,
global search and contextual Docs/Source actions. Home, category lists and Source resolve through
the same catalog; route content remains lazy with asynchronous Canvas/GPU teardown. Native
navigation uses AdaptiveShell, while browser controls and input remain Web host adapters.

UI-independent example state/presenters are shared with native Gallery. Declarative examples read
their bounded scene/tool settings from the actual native XAML assets; this is not a general MAUI
XAML runtime. Assets are the exact native Raw tree, fetched lazily over HTTP. Native source drawers
and lazy Web Source pages use the shared source catalog, distinguishing current Adaptive pages
from retained `Legacy` fixtures and including their helpers. Shared-device Canvas groups support
Scene Assets and color comparisons, while Product Feed retains the actual six-model
loading/lifecycle policy. The additional Web cases reuse the native emissive-material operation,
translucent ACEScg canvas and 24-product procedural feed policy. Translucent Canvas uses explicit
Blend and premultiplied surface alpha; the original opaque HDR Canvas example remains available.
Sliders apply changes during input, and scene-linear HDR remains the default with explicit
display-transform selection.

JPEG gain maps and KTX/Basis use the pinned native codec implementations compiled to wasm32;
they do not substitute browser RGBA8 JPEG decoding or replacement assets. LibraryImport bindings
are source-linked with static module lookup, without desktop runtime RID packages/DLL resolvers.
This is an experimental browser host; the supported native platforms retain their own presentation
and UI adapters. Public usage documentation lives in [docs-public](../../../docs-public/index.md)
and is generated at [the versioned documentation site](https://evnii.github.io/Mu3D.maui/v0.1/).

Build both interpreted and trimmed AOT modes. Full trimming excludes unused native extensions;
App.razor's explicit dependencies preserve routable Razor components and JS callbacks in both
modes. The [Web validation guide](../../../tests/Mu3D.Web.Validation/README.md) covers codec/import,
host-contract and browser-rendering checks. CPU import measurements, browser FPS and physical HDR
display measurements describe different boundaries.

Mono interpreting prebuilt application IL and AOT are both supported execution modes. The Web
port introduces no runtime user-authored C# scripting, dynamic compilation or executable hot-code
authoring/loading system. Live scene/material values and supported declarative graphs retain their
normal contracts. Stability, consistent native/shared behavior and measured performance apply in
both modes; application-selected AOT remains compatible with every offered Web feature.

## Publish and serve

Run from the repository root with its pinned SDK and wasm-tools workload. Optional local ICC
inputs use the same `Mu3DPrintProfileDirectory` property as native Gallery; third-party profiles
are not committed, and the browser also accepts an explicit ICC upload. The shared local/Actions
build helper prepares the existing Emdawn compatibility port and clean official codec checkouts
at the commits pinned in `eng/Mu3D.{UltraHdr,Ktx}NativeVersion.props`:

```sh
# Prerequisites: pinned .NET SDK/wasm-tools, Python 3, Git, CMake and Ninja.
# In this repository, global.json selects SDK/workload set 10.0.401.
dotnet workload install wasm-tools --skip-manifest-update
python3 eng/build-web-gallery.py --prepare --mode both
python3 eng/serve-web-validation.py --port 8765
```

Open [interpreted Gallery](http://127.0.0.1:8765/Gallery/) or
[AOT Gallery](http://127.0.0.1:8765/GalleryAot/). Publish serially; intermediates are separated by
build mode. The project-owned Emscripten cache must populate the pinned Emdawn port once with
the codec-compatible WASM exception flags; `FROZEN_CACHE=` permits that local build without
modifying the SDK cache. Use HTTP: opening source HTML via `file://` cannot load modules or the .NET payload.
The server supports direct example routes such as `/Gallery/examples/orbit-controls`.
For subsequent builds, omit `--prepare`. `--mode aot` or `--mode interpreted` rebuilds just one
mode. To include supplied ICC inputs in a local Gallery verification build, pass their directory
explicitly to both modes:

```sh
python3 eng/build-web-gallery.py --mode both --print-profile-directory /path/to/profiles
```

This forwards `Mu3DPrintProfileDirectory` to restore and publish. The directory must contain
`.icc` files; the Gallery picker discovers its five documented regional filenames. Defaults do
not download or bundle third-party profiles. An existing local verification directory may be used,
including `artifacts/validation/next-step/profiles` when those test inputs are present.

Existing dirty or incorrectly pinned codec checkouts are preserved and rejected. The helper
selects `MU3D_DOTNET`, the repository-local SDK or `PATH`; `--dotnet` overrides it.
After publishing, it reuses the desktop host-check consumer to read the actual trimmed Gallery
assembly metadata. Every shared catalog entry and the navigation/alias routes must retain a
public Razor component with a `RouteAttribute`. A missing page fails the build helper, even if
the HTTP server returns its SPA startup page successfully. Rendering and live controls still
require browser acceptance.

## GitHub Pages

The deployment target is the public [EVNII/Mu3D.maui](https://github.com/EVNII/Mu3D.maui) repository:

- [AOT Gallery](https://evnii.github.io/Mu3D.maui/gallery/)
- [Interpreted Gallery](https://evnii.github.io/Mu3D.maui/gallery-interpreted/)
- [Developer documentation](https://evnii.github.io/Mu3D.maui/v0.1/)

The published app includes the Gallery source drawer and its exact assets. Public documentation
comes from `docs-public`; generated output goes into `artifacts`.
The Pages publisher excludes optional local `assets/PrintProfiles` from staged public copies;
the local publication inputs retain those files. On the public site, load an ICC through the
browser file picker.

After building both modes, build the allowlisted public docs if updating them, then stage locally:

```sh
dotnet run --project tools/Mu3D.Docs/Mu3D.Docs.csproj -- build
python3 eng/publish-web-pages.py \
  --aot artifacts/web-wasm/GalleryAot --interpreted artifacts/web-wasm/Gallery \
  --repository EVNII/Mu3D.maui --base-path /Mu3D.maui/ \
  --docs artifacts/docs/_site
```

Review `artifacts/pages-site`; rerun the same publisher with `--deploy` to commit and push the
generated content through an isolated temporary `gh-pages` worktree. A checkout of the public
repository uses its default `origin` remote. If your checkout's `origin` points to a separate
development repository, configure a `public` remote for `EVNII/Mu3D.maui` and select it explicitly:

```sh
python3 eng/publish-web-pages.py \
  --aot artifacts/web-wasm/GalleryAot --interpreted artifacts/web-wasm/Gallery \
  --docs artifacts/docs/_site --remote public \
  --repository EVNII/Mu3D.maui --base-path /Mu3D.maui/ --deploy
```

The initial administrator
setup also passes `--enable-pages`, configuring **Deploy from a branch → gh-pages → / (root)**.
Without `--docs`, deployment retains the existing public documentation and version trees.
The publisher verifies boot hashes, exact native Raw assets, size limits and non-overlapping
staging roots. Main never contains generated site output.

The deployment supplies explicit bases `/Mu3D.maui/gallery/` and `/Mu3D.maui/gallery-interpreted/`; root
`404.html` restores direct example/source routes before Blazor boots. Rename the repository or
use another site prefix by passing its trailing-slash `--base-path` and matching `--repository`.
The **Web Gallery Pages** workflow is `workflow_dispatch` only. It calls the same build/publish
helpers, retains locally published docs, and explicitly requests the final Pages deployment.
Building locally avoids the hosted WASM/AOT build; GitHub still performs the final Pages deployment.
After requesting deployment, check the Pages build status and the published files before reporting
the site as updated.

HDR requests FP16 extended-sRGB Canvas output; explicit SDR preview clips only at the display
boundary. Format configuration and numeric checks do not prove physical HDR. Every Web feature
requires trimmed AOT compatibility and affected-path browser comparison with interpretation.

## Codec and shared logic checks

`tests/Mu3D.Web.Validation/CodecContracts` runs the same managed JPEG/UltraHDR/KTX imports against
actual wasm32 static libraries, both interpreted and AOT. It checks C-header ABI layouts, the exact
native HDR JPEG operation, malformed-input recovery, Basis targets and native ETC2 data fallback,
bounded loading/cache/retention/cancellation, Shoe/Fox instances/variants/skinning, the complete
24-model Advanced import matrix and both native HDRI sources. It does not execute GPU rendering.
Shared Core/Toolkit/host tests cover the common logic; browser-specific JS tests cover Canvas
scheduling, input, lifecycle and DOM bindings. Run affected Gallery routes in interpreted and AOT
builds to verify rendering and live controls. Physical HDR, device-loss recovery and browser/display
coverage require their own checks.

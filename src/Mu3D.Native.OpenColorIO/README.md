# Mu3D.Native.OpenColorIO

Optional explicit OpenColorIO 2.5.2 CPU processing. The managed package depends only on
`Mu3D.Color`; it does not select a global OCIO configuration, replace ICC image profiles, change
document pixels, own a renderer, or select presentation/HDR surface policy.

```csharp
using Mu3D.Native.OpenColorIO;

using var config = OcioConfiguration.LoadFile("/explicit/path/config.ocio");
using var processor = config.CreateColorSpaceProcessor("ACEScg", "ACES2065-1");
var result = processor.ApplyRgb(new System.Numerics.Vector3(0.18f));
```

Configurations may come from explicit files, YAML strings with an asset working directory, or
exact built-in names. Enumeration includes all color spaces, displays, attached views and Looks,
independent of active-display filters. Context variables use declared config defaults; the API
does not select `$OCIO` or load process variable overrides. Config files may reference local LUTs;
the caller supplies and retains those assets. A compiled processor survives configuration disposal.

Display/view processors return the view's actual output encoding. `SourceIdentifier`,
`OutputIdentifier`, configuration cache ID and compiled processor cache ID keep this explicit.
Null Look override retains configured Looks, an empty override bypasses them, and a nonempty
override applies the requested OCIO Look expression before the display view.

`CreateDisplayViewToLinearProcessor` additionally converts the explicitly named view output to an
explicit linear OCIO destination. Output and destination must share the same OCIO reference-space
type, avoiding a hidden inverse tone/view transform. The caller must verify the destination is
linear; names alone are insufficient. `AsLinearTransform` is an explicit assertion that both
OCIO endpoint identities match supplied Mu3D linear identities, **not** transfer decoding. This
adapter can be passed to `LinearRgbLut3D.Bake` with an explicit finite domain and range policy;
validate off-grid approximation error before GPU use. Keep the processor alive until baking ends.

RGB processing retains finite HDR and negative values when the chosen OCIO transform does.
RGBA batches are straight alpha, validated/staged before mutation, and preserve alpha bits exactly.
The explicit transform may intentionally map or clamp RGB. Dynamic processor properties and arbitrary
GPU shader export are outside this adapter's current contract.

## Native runtime boundary

The native implementation has been built and tested separately on **macOS arm64** and
**Mac Catalyst arm64**. It uses Mu3D C ABI 1 around privately linked OpenColorIO 2.5.2. Native artifacts
are produced by explicit maintainer recipes. The default pack contains no binaries; an optional
verified runtime staging property produces a local package containing both supported native RIDs.
iOS, Android, Windows and other architectures remain unsupported and fail explicitly.

Place the maintainer-built `libmu3d_opencolorio.dylib` beside the application, or in its
`runtimes/osx-arm64/native` directory. Loading validates the exact OCIO release and wrapper ABI;
missing runtime files produce an actionable error. No system/Homebrew library is substituted.
Application builds never download source, fetch binaries, run CMake, or compile C++.

Mac Catalyst uses its own complete `libmu3d_opencolorio.a` archive, built with the macabi target for
every dependency. A package built with `Mu3DOpenColorIORuntimeRoot` automatically supplies that
archive for `maccatalyst-arm64`. With a managed-only package, set the application MSBuild property
`Mu3DOpenColorIONativeArchive` to the maintainer-produced archive; the package's `buildTransitive`
target adds the static native reference, C++ runtime and twelve retained C entry points.
A repository project-reference consumer must also
explicitly import `src/Mu3D.Native.OpenColorIO/buildTransitive/Mu3D.Native.OpenColorIO.targets`.
The loader resolves these symbols in the application's main image and validates ABI/release.
Do not substitute the macOS dylib. No monitor frameworks or UI APIs are used in the Catalyst build.

The Catalyst archive's 256 objects and the executed native test binaries were verified as
`MACCATALYST` (minimum 15.0, SDK 27.0). Native C ABI smoke and 384 independent reference components
passed. Full .NET MAUI linking/signing/presentation for this optional adapter remains unverified.
The earlier Xcode workload mismatch was repaired for Gallery, but Gallery's portable display views
do not exercise this native OCIO consumer path. The macOS .NET adapter separately
passed 419 checks. Native platform evidence does not imply the remaining application checks passed.

Maintainer source, source/dependency hashes, local build command and the independent C++ oracle are
in `eng/opencolorio`. BSD/MIT/zlib dependency notices are included in `ThirdParty` and the package.

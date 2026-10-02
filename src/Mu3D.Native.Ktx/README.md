# Mu3D.Native.Ktx

Optional Khronos libktx adapter for `IEncodedTextureTranscoder`. The native ABI is pinned to
KTX-Software v4.4.2 (`4d6fc70eaf62ad0558e63e8d97eb9766118327a6`).
Distribution builds use libktx's reader/transcoder-only `ktx_read` target, rename that binary to
the package ABI name `libktx`, and disable ETC unpack, tools, tests and upload helpers.
Apple packages rely on explicit native-symbol anchors and ordinary archive extraction; the upstream
merged archive must not be force-loaded because doing so can pull duplicate ASTC objects.
Mu3D's audited Apple build patch preserves `ktx_read`'s own members when merging the upstream ASTC
dependency; without it, upstream v4.4.2 accidentally substitutes the full writer/encoder archive.
Release builds use the SSE2 ASTC baseline on x64 instead of silently requiring AVX2.

The package validates the glTF `KHR_texture_basisu` 2D, orientation, swizzle, alpha and DFD color
requirements before transcoding. It selects ASTC 4x4, BC7 or ETC2 RGBA from explicit device
capabilities. Non-color UASTC data falls back to decoded RGBA when neither ASTC nor BC7 is enabled,
as recommended by the Khronos extension.

`Ktx2ImageDecoder` supplies that RGBA8 fallback and can wrap an application's existing JPEG/image
decoder, so one `GltfImportOptions.ImageDecoder` handles both KTX2 and the application's other MIME
types without adding a dependency on a particular JPEG package.

`Ktx2TextureLoader` is the bounded asynchronous path for standalone material textures. It limits
both encoded input and the decoded FP32/compressed mip payload, validates KTX2 dimensions before
native processing, and performs decode/transcode work away from the caller's synchronization
context. Its stream overload leaves a caller-owned stream open. Its keyed-resolver overload owns and
closes the returned stream and can borrow an application-owned `Ktx2SourceCache`; a warm cache avoids
opening the source, but still obeys the current load's source/output limits. Cancellation covers
source acquisition/read and the boundaries around native processing; an already executing libktx
call is allowed to finish rather than being interrupted unsafely.

The cache contains exact encoded KTX2 copies only. `Ktx2TextureAsset` always owns the selected
decoded FP32 image or GPU-compressed mip chain. Exact encoded bytes are independently released by
default, or copied into `RetainedEncodedSource` when `RetainEncodedSource` is enabled. Clearing the
application cache therefore cannot invalidate an already loaded texture or its optional source copy.

```csharp
Ktx2TextureAsset texture = await Ktx2TextureLoader.LoadAsync(
    "products/albedo@revision-7",
    token => OpenApprovedKtx2StreamAsync(token),
    productTextureSourceCache,
    new Ktx2TextureLoadOptions
    {
        MaximumSourceByteCount = 8 * 1024 * 1024,
        MaximumOutputByteCount = 64 * 1024 * 1024,
        RetainEncodedSource = false,
        Name = "Product albedo",
    },
    cancellationToken);
```

This package is optional. `Mu3D.Core` and `Mu3D.Formats.Gltf` do not reference it, and applications
may supply a different KTX2/BasisU implementation.

Release packaging covers Android arm/x86 families, iOS device and simulators, Mac Catalyst arm64/x64
and Windows arm64/x86/x64. Android and Windows use renamed shared reader binaries; Apple uses static
archives retained through exact native-symbol anchors.

Native distributions include `THIRD-PARTY-NOTICES.md` and the applicable complete license texts.
The specially licensed upstream ETC decoder is not included because release builds set
`KTX_FEATURE_ETC_UNPACK=OFF`; Basis Universal ETC2 transcoding remains available.

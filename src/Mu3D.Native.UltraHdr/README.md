# Mu3D.Native.UltraHdr

Optional implementations of `Mu3D.Assets.IEncodedImageDecoder` backed by pinned Google
libultrahdr v2.0.1 and libjpeg-turbo 3.1.0. `JpegImageDecoder` handles ordinary JPEG plus Ultra HDR
and ISO 21496-1 JPEG gain maps. `HeifImageDecoder` separately owns HEIF/HEIC/AVIF and may wrap an
application-owned fallback decoder. Applications may omit this package or inject their own decoder
through `GltfImportOptions.ImageDecoder`; Mu3D does not install a process-global image decoder.

Ultra HDR is a gain-map capability, not the public decoder's container identity. To compose the two
provided container adapters explicitly:

```csharp
IEncodedImageDecoder decoder = new HeifImageDecoder(new JpegImageDecoder());
```

The normal native feature set is built with `UHDR_ENABLE_HEIF=OFF`. Maintainers can select the
separate prebuilt `release-heif` feature set while compiling/packing the C# project with
`Mu3DUltraHdrEnableHeif=true`; the property defaults to `false`. This switch never downloads source
or runs CMake in a consuming application. A HEIF-enabled package can only be packed after a
complete, separately verified `release-heif` runtime matrix has been staged.

One NuGet package contains architecture-specific assets for all supported RIDs: Android arm,
arm64, x86 and x64; iOS arm64 and arm64/x64 simulators; Mac Catalyst arm64/x64; and Windows
arm64/x86/x64. These are not one cross-architecture native binary. Android universal APKs may carry
all four independent ABI libraries and Android App Bundles may split them. Windows selects one PE
architecture; a multi-architecture MSIX bundle contains separate application packages. Apple
keeps device, simulator and Mac Catalyst RID archives separate, while the final Mac application may
be published as Universal 2.

The native libraries are maintainer-built from exact commits in an isolated CI workspace. They are
verified for target architecture, required exports and SHA-256 before NuGet packing. Third-party
source trees are not Git submodules and are not present in consumer builds.

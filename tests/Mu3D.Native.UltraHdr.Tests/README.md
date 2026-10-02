# Ultra HDR Apple linkage regression

The managed codec resolves Apple static functions from the main executable.
A successful native archive build is not enough: native dead stripping/export filtering
must retain every managed import in the final app. Gallery's local-archive harness and
the runtime NuGet both consume the shared
`Mu3D.Native.UltraHdr.Symbols.props`; the runtime package must include that file.

Run from the repository root after building Gallery:

```sh
python3 eng/verify-ultrahdr-apple-symbols.py
python3 eng/verify-ultrahdr-apple-symbols.py --binary samples/GalleryApp/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/GalleryApp.app/Contents/MacOS/Mu3D.GalleryApp
```

The guard compares all LibraryImport declarations against the shared symbol roots.
With --binary it also checks both defined external symbols and the dyld export table,
the latter being the lookup path used by NativeLibrary.GetMainProgramHandle.

`NativeRoundTrip.apple.c` is a maintainer-only, headless runtime test. Compile it for
Mac Catalyst against the exact same libuhdr.a and libturbojpeg.a used by Gallery,
with the header from pinned libultrahdr commit
`a532a8c1418890cff7ce1e90cbe59ba6ebc6fa6d`. Example:

```sh
xcrun clang -target arm64-apple-ios17.0-macabi \
  -isysroot "$(xcrun --sdk macosx --show-sdk-path)" \
  -I/path/to/pinned/libultrahdr \
  tests/Mu3D.Native.UltraHdr.Tests/NativeRoundTrip.apple.c \
  -Wl,-force_load,artifacts/ultrahdr/v2.0.1/release/runtimes/maccatalyst-arm64/native/libuhdr.a \
  -Wl,-force_load,artifacts/ultrahdr/v2.0.1/release/runtimes/maccatalyst-arm64/native/libturbojpeg.a \
  -lc++ -o /private/tmp/mu3d-ultrahdr-roundtrip
/private/tmp/mu3d-ultrahdr-roundtrip
```

The test uses the encoder settings and gain-map dimension queries that were previously
missing in Gallery's roots. It exercises full/half-size, multichannel/single-channel and
best-quality/realtime settings, decodes each JPEG, and requires finite HDR output above one.
It opens no UI. It does not establish Gallery button interaction or calibrated display
accuracy; those remain separate checks. Consumer builds do not run this compiler or fetch headers.

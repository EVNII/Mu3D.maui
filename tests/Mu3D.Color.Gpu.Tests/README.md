# GPU color LUT verification

The default executable runs deterministic recording-backend checks, including FP32 table upload,
explicit metadata/range policy, resource ownership, matching input/output dimensions, target
precision and submission without readback. `--native` additionally creates a headless wgpu device
and compares CPU and GPU results for all four Float16/Float32 source/destination combinations.
It does not create a window or presentation surface.

```sh
dotnet build tests/Mu3D.Color.Gpu.Tests/Mu3D.Color.Gpu.Tests.csproj -c Release \
  --disable-build-servers -p:UseSharedCompilation=false -p:BuildInParallel=false -m:1 -nodeReuse:false
dotnet tests/Mu3D.Color.Gpu.Tests/bin/Release/net10.0/Mu3D.Color.Gpu.Tests.dll
dotnet tests/Mu3D.Color.Gpu.Tests/bin/Release/net10.0/Mu3D.Color.Gpu.Tests.dll --native
```

The native option requires the **exact pinned v29.0.1.1** dynamic library beside the test executable
or in the host's native-library search path, plus GPU access. Do not substitute a different ABI.
Normal builds do not download diagnostic libraries. On macOS, this is an upstream macOS diagnostic
binary, not the separately built Mac Catalyst `macabi` shipping archive.

The 2026-09-17 macOS arm64 diagnostic used the immutable official release asset
`wgpu-macos-aarch64-release.zip`, SHA256
`a5797a37b1adf720bcd5dcffb291edbbd5b7b14be0a3874c28e6393a655a7a3e`, verified against the
[pinned GitHub release](https://github.com/gfx-rs/wgpu-native/releases/tag/v29.0.1.1).
Its `lib/libwgpu_native.dylib` was placed only in the ignored test-output directory. Sandbox GPU
enumeration returned no Metal adapters; a reviewed unsandboxed headless invocation passed.

Numerical comparison includes negative RGB, HDR values, explicit domain clamping, partially
transparent pixels and zero-alpha pixels with preserved hidden RGB. FP32 tolerance is 0.00001;
FP16 tolerance is 0.004, including output quantization. Alpha is copied before storage rounding.
These checks validate native numerical texture operations, not physical display HDR engagement or
the Android/iOS/Mac Catalyst/Windows presentation matrix.

# Third-party notices

This package redistributes precompiled wgpu-native v29.0.1.1 binaries from commit
`6aed50955d934ac36049ba8d002034841633ae02`. wgpu-native and its Rust dependency graph are provided
under their respective upstream licenses. The top-level wgpu-native project is dual-licensed under
MIT or Apache-2.0. Mu3D's Windows runtime applies the checked-in, version-specific D3D12 composition
bridge patch. It adds three borrowed native-object exports used only by Mu3D's internal Windows
backend; it does not change Mu3D's public managed API or permit a consumer-selected native ABI.

Windows also applies Mu3D's versioned wgpu-hal 29.0.3 DXIL cache patch. Its complete additional
dependency lock is `eng/native/WgpuShaderCache.Cargo.lock`; the transitive license report must
include that graph (including sha2 and its dependencies), not only upstream Cargo.lock.
The cache contains no OpenPBR shader assets; those belong to the optional renderer package.

Upstream project: https://github.com/gfx-rs/wgpu-native

Public packaging additionally requires `licenses/wgpu-native-dependencies.html`, generated and
reviewed from the exact pinned Cargo.lock. The package build fails while that transitive report is
absent; this top-level notice is not used as a substitute for the dependency audit.

Windows redistributes `dxcompiler.dll` and `dxil.dll` from Microsoft's official
`Microsoft.Direct3D.DXC` NuGet package, version 1.8.2505.32. The package and individual
binaries are SHA-256 pinned in `eng/dxc-assets.json` in the maintainer repository.
The original MIT, LLVM and Microsoft license texts and redistributable-file list
are included as `licenses/DXC-*`. These components retain their upstream terms.

Upstream project: https://github.com/microsoft/DirectXShaderCompiler
Distribution: https://www.nuget.org/packages/Microsoft.Direct3D.DXC/1.8.2505.32

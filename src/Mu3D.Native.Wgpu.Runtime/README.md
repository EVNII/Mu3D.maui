# Mu3D.Native.Wgpu.Runtime

Runtime-only native assets for the ABI-locked Mu3D wgpu backend. Applications normally reference
`Mu3D.Maui` or `Mu3D.Native.Wgpu`; they do not reference this carrier package directly.

The patched Windows backend includes a generic, versioned DXIL disk cache and can read optional
precompiled bundles adjacent to the application. It contains no OpenPBR shader payload and does
not depend on or initialize `Mu3D.Rendering.OpenPbr`. The optional renderer owns those assets.
Maintainer packaging verifies each Windows architecture's source/lock/DLL provenance; all three
Windows architectures must be rebuilt before publishing an updated runtime carrier.

The package contains shared libraries for Android and Windows and static archives for iOS, iOS
Simulator and Mac Catalyst. It contains no Mu3D managed assembly, symbols, SourceLink data or
source code. Native assets are pinned to wgpu-native v29.0.1.1 at commit
`6aed50955d934ac36049ba8d002034841633ae02`. Windows runtime packages that support Mu3D's
compositor-owned transparent mask are built from that commit plus the repository's versioned,
Windows-only D3D12 composition bridge patch; Android and Apple assets remain upstream builds.

Windows assets also include Microsoft's DirectX Shader Compiler (DXC) 1.8.2505.32:
`dxcompiler.dll` and `dxil.dll` for x64, x86 and ARM64. Mu3D explicitly selects DXC
for D3D12; the legacy FXC compiler cannot compile the OpenPBR transport shader.
No developer SDK or compiler download is required at application build or run time.

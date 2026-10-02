[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $NativeSource,
    [Parameter(Mandatory = $true)][string] $HalSource,
    [string] $Python = "python",
    [string] $Cargo = "cargo",
    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string[]] $RuntimeIdentifier = @("win-x64", "win-x86", "win-arm64"),
    [switch] $Stage
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if ($env:OS -ne "Windows_NT") { throw "Build Windows runtimes on Windows." }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$source = [IO.Path]::GetFullPath($NativeSource)
$hal = [IO.Path]::GetFullPath($HalSource)
foreach ($path in @($source, $hal)) {
    if (-not $path.StartsWith((Join-Path $root "artifacts") + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Use isolated maintainer sources beneath repository artifacts."
    }
}
$commit = (& git -C $source rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne "6aed50955d934ac36049ba8d002034841633ae02") { throw "Native source pin mismatch." }
if (-not (Get-Content (Join-Path $source "src/lib.rs") -Raw).Contains("fn wgpuDeviceGetNativeD3D12Device")) {
    throw "Apply/verify the existing D3D12 composition patch before preparing the cache patch."
}
& $Python (Join-Path $PSScriptRoot "prepare-wgpu-shader-cache.py") --native-source $source --hal-source $hal
if ($LASTEXITCODE -ne 0) { throw "Cache patch preparation failed." }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "native/WgpuShaderCache.Cargo.lock") -Destination (Join-Path $source "Cargo.lock") -Force
$targets = @{ "win-x64" = "x86_64-pc-windows-msvc"; "win-x86" = "i686-pc-windows-msvc"; "win-arm64" = "aarch64-pc-windows-msvc" }
foreach ($rid in $RuntimeIdentifier) {
    $target = $targets[$rid]
    & $Cargo build --release --locked --manifest-path (Join-Path $source "Cargo.toml") --target $target
    if ($LASTEXITCODE -ne 0) { throw "Native build failed for $rid; do not package a mixed runtime set." }
    $dll = Join-Path $source "target/$target/release/wgpu_native.dll"
    if ($Stage) {
        $destination = Join-Path $root "artifacts/wgpu-native/v29.0.1.1/release/runtimes/$rid/native"
        [void](New-Item -ItemType Directory -Force $destination)
        Copy-Item -LiteralPath $dll -Destination (Join-Path $destination "wgpu_native.dll") -Force
        $evidence = [ordered]@{
            schema = 1; nativeCommit = $commit; halVersion = "29.0.3"; rid = $rid
            dllSha256 = (Get-FileHash -Algorithm SHA256 $dll).Hash.ToLowerInvariant()
            cacheSourceSha256 = (Get-FileHash -Algorithm SHA256 (Join-Path $PSScriptRoot "native/WgpuDx12ShaderCache.rs")).Hash.ToLowerInvariant()
            patchScriptSha256 = (Get-FileHash -Algorithm SHA256 (Join-Path $PSScriptRoot "prepare-wgpu-shader-cache.py")).Hash.ToLowerInvariant()
            lockSha256 = (Get-FileHash -Algorithm SHA256 (Join-Path $source "Cargo.lock")).Hash.ToLowerInvariant()
        }
        $evidence | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $destination "mu3d-shader-cache.json")
    }
}

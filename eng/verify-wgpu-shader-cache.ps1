[CmdletBinding()]
param([string[]] $RuntimeIdentifier = @("win-x64", "win-x86", "win-arm64"))
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$expectedSource = (Get-FileHash (Join-Path $PSScriptRoot "native/WgpuDx12ShaderCache.rs") -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedScript = (Get-FileHash (Join-Path $PSScriptRoot "prepare-wgpu-shader-cache.py") -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedLock = (Get-FileHash (Join-Path $PSScriptRoot "native/WgpuShaderCache.Cargo.lock") -Algorithm SHA256).Hash.ToLowerInvariant()
foreach ($rid in $RuntimeIdentifier) {
    $directory = Join-Path $root "artifacts/wgpu-native/v29.0.1.1/release/runtimes/$rid/native"
    $manifest = Join-Path $directory "mu3d-shader-cache.json"
    if (-not (Test-Path -LiteralPath $manifest)) { throw "Missing verified shader-cache runtime for $rid. Run build-wgpu-shader-cache.ps1 on Windows; do not mix older runtimes." }
    $evidence = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    $actual = (Get-FileHash (Join-Path $directory "wgpu_native.dll") -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($evidence.schema -ne 1 -or $evidence.rid -ne $rid -or $evidence.nativeCommit -ne "6aed50955d934ac36049ba8d002034841633ae02" -or
        $evidence.halVersion -ne "29.0.3" -or $evidence.dllSha256 -ne $actual -or $evidence.cacheSourceSha256 -ne $expectedSource -or
        $evidence.patchScriptSha256 -ne $expectedScript -or $evidence.lockSha256 -ne $expectedLock) {
        throw "Shader-cache source/binary provenance mismatch for $rid. Rebuild before packaging."
    }
}
Write-Host "Verified pinned shader-cache runtimes: $($RuntimeIdentifier -join ', ')"

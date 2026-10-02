[CmdletBinding()]
param(
    [string] $SourceDirectory,
    [switch] $Apply
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$propsPath = Join-Path $repositoryRoot "eng/Mu3D.WgpuNativeVersion.props"
[xml] $props = Get-Content -LiteralPath $propsPath -Raw
$version = [string] $props.Project.PropertyGroup.Mu3DWgpuNativeVersion
$commit = [string] $props.Project.PropertyGroup.Mu3DWgpuNativeCommit
if ([string]::IsNullOrWhiteSpace($version) -or [string]::IsNullOrWhiteSpace($commit)) {
    throw "The pinned wgpu-native version or commit is missing from $propsPath."
}

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $repositoryRoot "artifacts/wgpu-native-src/$version"
}
$source = [System.IO.Path]::GetFullPath($SourceDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $source ".git") -PathType Container)) {
    throw "wgpu-native source checkout is missing at $source."
}

$patch = Join-Path $repositoryRoot "eng/patches/wgpu-native-$version-d3d12-composition.patch"
if (-not (Test-Path -LiteralPath $patch -PathType Leaf)) {
    throw "The version-specific D3D12 patch is missing: $patch"
}
$patchDirectory = Join-Path $repositoryRoot "artifacts/native-patches"
[void](New-Item -ItemType Directory -Force $patchDirectory)
$patchToApply = Join-Path $patchDirectory "wgpu-d3d12-composition-lf.patch"
[IO.File]::WriteAllText($patchToApply, (Get-Content -LiteralPath $patch -Raw).Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))

$git = (Get-Command git -ErrorAction Stop).Source
$safeDirectory = "safe.directory=$($source.Replace('\', '/'))"

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,
        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    & $git -c $safeDirectory -C $source @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Test-GitApply {
    & $git -c $safeDirectory -C $source apply --unidiff-zero --check $patchToApply *> $null
    return $LASTEXITCODE -eq 0
}

function Get-NormalizedSourceDiff {
    param([string[]] $Paths)

    $lines = @(& $git -c $safeDirectory -C $source diff --no-ext-diff --unified=0 -- @Paths)
    if ($LASTEXITCODE -ne 0) {
        throw "Reading the wgpu-native source diff failed with exit code $LASTEXITCODE."
    }
    return ([string]::Join("`n", $lines).TrimEnd() + "`n")
}

$actualCommit = (& $git -c $safeDirectory -C $source rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $commit) {
    throw "wgpu-native source is '$actualCommit'; expected exact pinned commit '$commit'."
}

$patchText = Get-Content -LiteralPath $patch -Raw
$requiredExports = @(
    "wgpuDeviceGetNativeD3D12Device",
    "wgpuDeviceGetNativeD3D12CommandQueue",
    "wgpuTextureGetNativeD3D12Resource"
)
foreach ($export in $requiredExports) {
    if (-not $patchText.Contains($export, [System.StringComparison]::Ordinal)) {
        throw "The patch does not contain required bridge export '$export'."
    }
}

$expectedPaths = @("Cargo.lock", "Cargo.toml", "ffi/wgpu.h", "src/lib.rs")
$actualPaths = @(& $git -c $safeDirectory -C $source diff --name-only | Sort-Object)
$untracked = @(& $git -c $safeDirectory -C $source ls-files --others --exclude-standard)
$normalizedPatch = $patchText.Replace("`r`n", "`n").TrimEnd() + "`n"
$isCleanPatchBase = $actualPaths.Count -eq 0 -and $untracked.Count -eq 0 -and (Test-GitApply)
$isExactlyApplied =
    [string]::Join("`n", $actualPaths) -eq [string]::Join("`n", $expectedPaths) -and
    $untracked.Count -eq 0 -and
    (Get-NormalizedSourceDiff $expectedPaths) -eq $normalizedPatch
if ($Apply -and $isCleanPatchBase) {
    $statusBefore = @(& $git -c $safeDirectory -C $source status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $statusBefore.Count -ne 0) {
        throw "Refusing to apply the D3D12 patch to a dirty wgpu-native checkout."
    }
    Invoke-Git @("apply", "--unidiff-zero", $patchToApply) "Applying the pinned wgpu-native D3D12 patch"
    $actualPaths = @(& $git -c $safeDirectory -C $source diff --name-only | Sort-Object)
    $isExactlyApplied =
        [string]::Join("`n", $actualPaths) -eq [string]::Join("`n", $expectedPaths) -and
        (Get-NormalizedSourceDiff $expectedPaths) -eq $normalizedPatch
}

if (-not $isExactlyApplied) {
    if ($isCleanPatchBase -and -not $Apply) {
        Write-Host "Verified clean $version source at $commit; the D3D12 patch applies without drift."
        Write-Host "Rerun with -Apply before building Windows runtimes."
        exit 0
    }
    throw "The checkout is neither the clean patch base nor the exact patched source. Rebase the version-specific patch explicitly."
}

Invoke-Git @("diff", "--check") "Checking the patched source diff"
if ($LASTEXITCODE -ne 0 -or [string]::Join("`n", $actualPaths) -ne [string]::Join("`n", $expectedPaths)) {
    throw "The patched checkout contains unexpected tracked changes: $([string]::Join(', ', $actualPaths))"
}
if ($LASTEXITCODE -ne 0 -or $untracked.Count -ne 0) {
    throw "The patched checkout contains unexpected untracked files: $([string]::Join(', ', $untracked))"
}

Write-Host "Verified exact $version D3D12 bridge patch at $commit."

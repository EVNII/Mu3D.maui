[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-ElfIdentity {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 20 -or
        $bytes[0] -ne 0x7f -or
        $bytes[1] -ne [byte][char]'E' -or
        $bytes[2] -ne [byte][char]'L' -or
        $bytes[3] -ne [byte][char]'F') {
        throw "'$Path' is not an ELF binary."
    }

    if ($bytes[5] -ne 1) {
        throw "'$Path' is not a little-endian ELF binary."
    }

    [pscustomobject]@{
        Class = [int]$bytes[4]
        Machine = [int]$bytes[18] -bor ([int]$bytes[19] -shl 8)
    }
}

function Assert-AndroidElf {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Rid
    )

    $expected = @{
        "android-arm"   = @{ Class = 1; Machine = 40 }
        "android-arm64" = @{ Class = 2; Machine = 183 }
        "android-x86"   = @{ Class = 1; Machine = 3 }
        "android-x64"   = @{ Class = 2; Machine = 62 }
    }

    if (-not $expected.ContainsKey($Rid)) {
        throw "Unsupported Android RID '$Rid'."
    }

    $identity = Get-ElfIdentity -Path $Path
    $wanted = $expected[$Rid]
    if ($identity.Class -ne $wanted.Class -or $identity.Machine -ne $wanted.Machine) {
        throw "ELF identity mismatch for '$Rid': class=$($identity.Class), machine=$($identity.Machine)."
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$manifestPath = Join-Path $repositoryRoot "eng/wgpu-native-assets.json"
$versionPropsPath = Join-Path $repositoryRoot "eng/Mu3D.WgpuNativeVersion.props"

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Missing manifest '$manifestPath'."
}

if (-not (Test-Path -LiteralPath $versionPropsPath -PathType Leaf)) {
    throw "Missing version lock '$versionPropsPath'."
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
[xml]$versionProps = Get-Content -Raw -LiteralPath $versionPropsPath
$version = [string]$versionProps.Project.PropertyGroup.Mu3DWgpuNativeVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Mu3DWgpuNativeVersion is missing from '$versionPropsPath'."
}

$assets = @($manifest.artifacts | Where-Object {
    $_.platform -eq "android" -and $_.configuration -eq "release"
} | Sort-Object rid)

$requiredRids = @("android-arm", "android-arm64", "android-x86", "android-x64")
if ($assets.Count -ne $requiredRids.Count) {
    throw "Expected exactly four Android Release assets; found $($assets.Count)."
}

foreach ($rid in $requiredRids) {
    if (@($assets | Where-Object rid -eq $rid).Count -ne 1) {
        throw "Manifest must contain exactly one Android Release asset for '$rid'."
    }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("mu3d-wgpu-android-" + [guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

try {
    foreach ($asset in $assets) {
        $uri = [uri][string]$asset.url
        if ($uri.Scheme -ne "https" -or $uri.Host -ne "github.com") {
            throw "Refusing non-official wgpu-native URL '$uri'."
        }

        $archivePath = Join-Path $temporaryRoot ([string]$asset.archive)
        Write-Host "Downloading $($asset.rid) from $uri"
        Invoke-WebRequest -Uri $uri -OutFile $archivePath

        $actualSize = (Get-Item -LiteralPath $archivePath).Length
        if ($actualSize -ne [long]$asset.size) {
            throw "Size mismatch for '$($asset.archive)': expected $($asset.size), got $actualSize."
        }

        $actualArchiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
        $expectedArchiveHash = ([string]$asset.sha256).ToLowerInvariant()
        if ($actualArchiveHash -ne $expectedArchiveHash) {
            throw "SHA-256 mismatch for '$($asset.archive)': expected $expectedArchiveHash, got $actualArchiveHash."
        }

        $extractPath = Join-Path $temporaryRoot ([string]$asset.rid)
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath
        $libraries = @(Get-ChildItem -LiteralPath $extractPath -Recurse -File -Filter "libwgpu_native.so")
        if ($libraries.Count -ne 1) {
            throw "Expected exactly one libwgpu_native.so in '$($asset.archive)'; found $($libraries.Count)."
        }

        Assert-AndroidElf -Path $libraries[0].FullName -Rid ([string]$asset.rid)

        $destinationDirectory = Join-Path $repositoryRoot "artifacts/wgpu-native/$version/release/runtimes/$($asset.rid)/native"
        [System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
        $destinationPath = Join-Path $destinationDirectory "libwgpu_native.so"
        Copy-Item -LiteralPath $libraries[0].FullName -Destination $destinationPath -Force

        $runtimeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationPath).Hash.ToLowerInvariant()
        Write-Host "Staged $($asset.rid): $runtimeHash  $destinationPath"
    }

    $runtimeRoot = Join-Path $repositoryRoot "artifacts/wgpu-native/$version/release/runtimes"
    $hashLines = foreach ($rid in $requiredRids) {
        $relativePath = "$rid/native/libwgpu_native.so"
        $path = Join-Path $runtimeRoot $relativePath
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
        "$hash  $relativePath"
    }

    [System.IO.File]::WriteAllLines(
        (Join-Path $runtimeRoot "SHA256SUMS.android"),
        $hashLines,
        [System.Text.UTF8Encoding]::new($false))
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host "All four official wgpu-native Android Release RIDs are staged and verified."

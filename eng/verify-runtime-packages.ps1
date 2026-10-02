[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [string] $Version
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml] $versionProps = Get-Content -LiteralPath (Join-Path $repositoryRoot "eng/Mu3D.PackageVersion.props")
    $Version = [string] $versionProps.Project.PropertyGroup.Mu3DPackageVersion.'#text'
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = [string] $versionProps.Project.PropertyGroup.Mu3DPackageVersion
    }
}
$resolvedPackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
if (-not (Test-Path -LiteralPath $resolvedPackageDirectory -PathType Container)) {
    throw "Runtime package directory does not exist: $resolvedPackageDirectory"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

$wgpuFiles = @(
    "android-arm/native/libwgpu_native.so",
    "android-arm64/native/libwgpu_native.so",
    "android-x86/native/libwgpu_native.so",
    "android-x64/native/libwgpu_native.so",
    "ios-arm64/native/libwgpu_native.a",
    "iossimulator-arm64/native/libwgpu_native.a",
    "iossimulator-x64/native/libwgpu_native.a",
    "maccatalyst-arm64/native/libwgpu_native.a",
    "maccatalyst-x64/native/libwgpu_native.a",
    "win-arm64/native/wgpu_native.dll",
    "win-x86/native/wgpu_native.dll",
    "win-x64/native/wgpu_native.dll",
    "win-x64/native/dxcompiler.dll",
    "win-x64/native/dxil.dll",
    "win-x86/native/dxcompiler.dll",
    "win-x86/native/dxil.dll",
    "win-arm64/native/dxcompiler.dll",
    "win-arm64/native/dxil.dll"
)

$ktxFiles = @(
    "android-arm/native/libktx.so",
    "android-arm64/native/libktx.so",
    "android-x86/native/libktx.so",
    "android-x64/native/libktx.so",
    "ios-arm64/native/libktx.a",
    "iossimulator-arm64/native/libktx.a",
    "iossimulator-x64/native/libktx.a",
    "maccatalyst-arm64/native/libktx.a",
    "maccatalyst-x64/native/libktx.a",
    "win-arm64/native/ktx.dll",
    "win-x86/native/ktx.dll",
    "win-x64/native/ktx.dll"
)

$codecFiles = @()
foreach ($rid in @("android-arm", "android-arm64", "android-x86", "android-x64")) {
    $codecFiles += "$rid/native/libuhdr.so"
    $codecFiles += "$rid/native/libturbojpeg.so"
}
foreach ($rid in @("ios-arm64", "iossimulator-arm64", "iossimulator-x64", "maccatalyst-arm64", "maccatalyst-x64")) {
    $codecFiles += "$rid/native/libuhdr.a"
    $codecFiles += "$rid/native/libturbojpeg.a"
}
foreach ($rid in @("win-arm64", "win-x86", "win-x64")) {
    $codecFiles += "$rid/native/uhdr.dll"
    $codecFiles += "$rid/native/turbojpeg.dll"
}

$contracts = @(
    [pscustomobject]@{
        Id = "Mu3D.Native.Wgpu.Runtime"
        Targets = @(
            "buildTransitive/Mu3D.Native.Wgpu.Runtime.targets",
            "buildTransitive/Mu3D.Native.Wgpu.targets"
        )
        LicenseReport = "licenses/wgpu-native-dependencies.html"
        Files = $wgpuFiles
    },
    [pscustomobject]@{
        Id = "Mu3D.Native.Ktx.Runtime"
        Targets = @(
            "buildTransitive/Mu3D.Native.Ktx.Runtime.targets",
            "buildTransitive/Mu3D.Native.Ktx.targets"
        )
        LicenseReport = "licenses/KTX-Software-LICENSE.md"
        Files = $ktxFiles
    },
    [pscustomobject]@{
        Id = "Mu3D.Native.UltraHdr.Runtime.Jpeg"
        Targets = @(
            "buildTransitive/Mu3D.Native.UltraHdr.Runtime.Jpeg.targets",
            "buildTransitive/Mu3D.Native.UltraHdr.targets"
        )
        LicenseReport = "licenses/libultrahdr-MIT.txt"
        Files = $codecFiles
    }
)

$maximumNuGetOrgBytes = 250000000
foreach ($contract in $contracts) {
    $packagePath = Join-Path $resolvedPackageDirectory "$($contract.Id).$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Required runtime package is missing: $packagePath"
    }

    $packageInfo = Get-Item -LiteralPath $packagePath
    if ($packageInfo.Length -gt $maximumNuGetOrgBytes) {
        throw "$($contract.Id) is $($packageInfo.Length) bytes and exceeds the 250 MB public-feed limit."
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($packageInfo.FullName)
    try {
        $entries = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace("\", "/")
            [void] $entries.Add($name)

            if ($name.EndsWith(".pdb", [System.StringComparison]::OrdinalIgnoreCase) -or
                $name.EndsWith(".cs", [System.StringComparison]::OrdinalIgnoreCase) -or
                $name.StartsWith("src/", [System.StringComparison]::OrdinalIgnoreCase) -or
                $name.StartsWith("lib/", [System.StringComparison]::OrdinalIgnoreCase) -or
                $name.StartsWith("ref/", [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "$($contract.Id) leaks a managed/source artifact: $name"
            }
        }

        $requiredFiles = @("README.md", "THIRD-PARTY-NOTICES.md", $contract.LicenseReport) + $contract.Targets
        if ($contract.Id -eq 'Mu3D.Native.Wgpu.Runtime') {
            foreach ($rid in @('win-x64', 'win-x86', 'win-arm64')) {
                $marker = $archive.GetEntry("runtimes/$rid/native/mu3d-shader-cache.json")
                if ($null -eq $marker) { throw "Missing shader-cache runtime provenance for $rid" }
                $reader = [IO.StreamReader]::new($marker.Open())
                try { $cacheEvidence = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                $binary = $archive.GetEntry("runtimes/$rid/native/wgpu_native.dll")
                if ($null -eq $binary) { throw "Missing native runtime for $rid" }
                $stream = $binary.Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
                finally { $stream.Dispose() }
                if ($cacheEvidence.schema -ne 1 -or $cacheEvidence.rid -ne $rid -or $cacheEvidence.dllSha256 -ne $hash -or
                    $cacheEvidence.nativeCommit -ne '6aed50955d934ac36049ba8d002034841633ae02' -or $cacheEvidence.halVersion -ne '29.0.3') {
                    throw "Native shader-cache provenance mismatch for $rid"
                }
            }
            $requiredFiles += @('licenses/DXC-LICENCE-MIT.txt', 'licenses/DXC-LICENSE-LLVM.txt',
                'licenses/DXC-LICENSE-MS.txt', 'licenses/DXC-distributable_files.txt')
            $dxc = Get-Content -Raw (Join-Path $PSScriptRoot 'dxc-assets.json') | ConvertFrom-Json
            foreach ($file in $dxc.files.PSObject.Properties) {
                $parts = $file.Name.Split('/')
                $entryPath = "runtimes/win-$($parts[0])/native/$($parts[1])"
                $entry = $archive.GetEntry($entryPath)
                if ($null -eq $entry) { throw "Missing pinned DXC asset: $entryPath" }
                $stream = $entry.Open()
                $sha = [System.Security.Cryptography.SHA256]::Create()
                try {
                    $actual = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
                    if ($actual -ne $file.Value) { throw "DXC package payload hash mismatch: $entryPath" }
                } finally {
                    $stream.Dispose()
                    $sha.Dispose()
                }
            }
        }
        foreach ($required in $requiredFiles) {
            if (-not $entries.Contains($required)) {
                throw "$($contract.Id) is missing package contract file '$required'."
            }
        }
        if (-not ($entries | Where-Object { $_.StartsWith("licenses/", [System.StringComparison]::OrdinalIgnoreCase) })) {
            throw "$($contract.Id) does not contain third-party license texts."
        }
        foreach ($nativeFile in $contract.Files) {
            $packageEntry = "runtimes/$nativeFile"
            if (-not $entries.Contains($packageEntry)) {
                throw "$($contract.Id) is missing '$packageEntry'."
            }
        }

        $expectedRuntimeEntries = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($nativeFile in $contract.Files) {
            [void] $expectedRuntimeEntries.Add("runtimes/$nativeFile")
        }
        foreach ($entryName in $entries) {
            $isBuildTarget = $false
            foreach ($targetFile in $contract.Targets) {
                if ($entryName.Equals($targetFile, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $isBuildTarget = $true
                    break
                }
            }
            $isNuGetMetadata =
                $entryName.Equals("_rels/.rels", [System.StringComparison]::OrdinalIgnoreCase) -or
                $entryName.Equals("[Content_Types].xml", [System.StringComparison]::OrdinalIgnoreCase) -or
                ($entryName.EndsWith(".nuspec", [System.StringComparison]::OrdinalIgnoreCase) -and
                    -not $entryName.Contains("/")) -or
                $entryName.StartsWith("package/services/metadata/", [System.StringComparison]::OrdinalIgnoreCase)
            $isPackagePayload =
                $entryName.Equals("README.md", [System.StringComparison]::OrdinalIgnoreCase) -or
                $entryName.Equals("THIRD-PARTY-NOTICES.md", [System.StringComparison]::OrdinalIgnoreCase) -or
                $isBuildTarget -or
                $entryName.StartsWith("licenses/", [System.StringComparison]::OrdinalIgnoreCase) -or
                $expectedRuntimeEntries.Contains($entryName)
            if (-not $isNuGetMetadata -and -not $isPackagePayload) {
                throw "$($contract.Id) contains unexpected package content '$entryName'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $hash = (Get-FileHash -LiteralPath $packageInfo.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "$hash  $($packageInfo.Name)"
}

Write-Host "All runtime packages are complete, runtime-only and within the public-feed size limit."

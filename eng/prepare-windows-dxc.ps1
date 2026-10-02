[CmdletBinding()]
param()

# Maintainer-only preparation. Application builds never download a shader compiler.
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest = Get-Content -Raw (Join-Path $PSScriptRoot 'dxc-assets.json') | ConvertFrom-Json
[xml] $versionProps = Get-Content (Join-Path $PSScriptRoot 'Mu3D.WgpuNativeVersion.props')
$wgpuVersion = [string] $versionProps.Project.PropertyGroup.Mu3DWgpuNativeVersion
$cache = Join-Path $repositoryRoot 'artifacts/downloads'
[void] (New-Item -ItemType Directory -Force $cache)
$package = Join-Path $cache "microsoft.direct3d.dxc.$($manifest.version).nupkg"
if (-not (Test-Path -LiteralPath $package)) {
    Invoke-WebRequest -Uri $manifest.url -OutFile $package
}
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $manifest.sha256) {
    throw "DXC package hash mismatch: $package"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    foreach ($file in $manifest.files.PSObject.Properties) {
        if ($file.Name -notmatch '^(x64|x86|arm64)/(dxcompiler|dxil)\.dll$') {
            throw "Unexpected DXC manifest path: $($file.Name)"
        }
        $architecture = $Matches[1]
        $entry = $archive.GetEntry("build/native/bin/$($file.Name)")
        if ($null -eq $entry) { throw "Missing DXC archive entry: $($file.Name)" }
        $stream = $entry.Open()
        $memory = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($memory)
            $bytes = $memory.ToArray()
        } finally {
            $stream.Dispose()
            $memory.Dispose()
        }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose() }
        if ($hash -ne $file.Value) { throw "DXC file hash mismatch: $($file.Name)" }
        $destination = Join-Path $repositoryRoot "artifacts/wgpu-native/$wgpuVersion/release/runtimes/win-$architecture/native"
        [void] (New-Item -ItemType Directory -Force $destination)
        [IO.File]::WriteAllBytes((Join-Path $destination ([IO.Path]::GetFileName($file.Name))), $bytes)
        Write-Host "$hash  win-$($file.Name)"
    }
} finally {
    $archive.Dispose()
}

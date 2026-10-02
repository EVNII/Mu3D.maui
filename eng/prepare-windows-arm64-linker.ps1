[CmdletBinding()]
param(
    [string] $Linker,
    [string] $WindowsSdkLibraries = "${env:ProgramFiles(x86)}/Windows Kits/10/Lib/10.0.26100.0"
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if ($env:OS -ne "Windows_NT") { throw "Prepare the ARM64 linker on Windows." }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$tools = Join-Path $root "artifacts/toolchain"
if ([string]::IsNullOrWhiteSpace($Linker)) {
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    $vs = (& $vswhere -latest -property installationPath).Trim()
    $Linker = Join-Path $vs "VC/Tools/MSVC/14.51.36231/bin/Hostx64/x64/link.exe"
}
if (-not (Test-Path -LiteralPath $Linker)) { throw "Provide an installed compatible MSVC host linker using -Linker." }
$payloads = @(
    @{
        Directory = "msvc-arm64"; Name = "Microsoft.VC.14.51.CRT.ARM64.Desktop.base.vsix"
        Sha256 = "c30a1064ef39efd3e969a1cac2bb56173de42aee558efbf7b5c1ca8910228687"
        Url = "https://download.visualstudio.microsoft.com/download/pr/16ac3a9f-21a9-4b80-a1cb-097bd79ea455/c30a1064ef39efd3e969a1cac2bb56173de42aee558efbf7b5c1ca8910228687/Microsoft.VC.14.51.CRT.ARM64.Desktop.base.vsix"
    },
    @{
        Directory = "msvc-arm64-store"; Name = "Microsoft.VC.14.51.CRT.ARM64.Store.base.vsix"
        Sha256 = "904230c4a5c5d34b84c9c962f9435a8909cdaa2d0ebe3d0c55472de4e45db081"
        Url = "https://download.visualstudio.microsoft.com/download/pr/16ac3a9f-21a9-4b80-a1cb-097bd79ea455/904230c4a5c5d34b84c9c962f9435a8909cdaa2d0ebe3d0c55472de4e45db081/Microsoft.VC.14.51.CRT.ARM64.Store.base.vsix"
    }
)
foreach ($payload in $payloads) {
    $destination = Join-Path $tools $payload.Directory
    [void](New-Item -ItemType Directory -Force $destination)
    $archive = Join-Path $destination $payload.Name
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest $payload.Url -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $payload.Sha256) {
        throw "Official ARM64 CRT payload hash mismatch: $archive"
    }
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $destination, $true)
}
$libraries = @(
    (Join-Path $tools "msvc-arm64-store/Contents/VC/Tools/MSVC/14.51.36231/lib/arm64"),
    (Join-Path $tools "msvc-arm64/Contents/VC/Tools/MSVC/14.51.36231/lib/arm64"),
    (Join-Path $WindowsSdkLibraries "um/arm64"),
    (Join-Path $WindowsSdkLibraries "ucrt/arm64")
)
foreach ($path in $libraries) { if (-not (Test-Path -LiteralPath $path)) { throw "Missing ARM64 target library directory: $path" } }
$wrapper = Join-Path $tools "msvc-arm64-link.cmd"
# Only the target link process receives this LIB; Cargo's x64 build helpers keep their normal environment.
# No installer, machine/user environment change, or modification of Visual Studio is performed.
$lines = @('@echo off', ('set "LIB=' + ($libraries -join ';') + '"'), ('"' + $Linker + '" %*'), 'exit /b %ERRORLEVEL%')
[IO.File]::WriteAllLines($wrapper, $lines, [Text.Encoding]::ASCII)
Write-Host "Prepared pinned ARM64 CRT libraries in repository artifacts; Visual Studio installation is unchanged."
Write-Output $wrapper

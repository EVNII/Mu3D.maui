[CmdletBinding()]
param(
    [string] $RuntimeFeed,
    [string] $OutputDirectory,
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($RuntimeFeed)) {
    $RuntimeFeed = Join-Path $repositoryRoot "artifacts/packages/public-runtime"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts/packages/private-managed"
}
$resolvedRuntimeFeed = [System.IO.Path]::GetFullPath($RuntimeFeed)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)

& (Join-Path $PSScriptRoot "verify-runtime-packages.ps1") -PackageDirectory $resolvedRuntimeFeed
[void] (New-Item -ItemType Directory -Force -Path $resolvedOutput)

$projects = @(
    "src/Mu3D.Color/Mu3D.Color.csproj",
    "src/Mu3D.Color.Printing/Mu3D.Color.Printing.csproj",
    "src/Mu3D.Native.OpenColorIO/Mu3D.Native.OpenColorIO.csproj",
    "src/Mu3D.Graphics/Mu3D.Graphics.csproj",
    "src/Mu3D.Core/Mu3D.Core.csproj",
    "src/Mu3D.Creative/Mu3D.Creative.csproj",
    "src/Mu3D.Formats.MaterialX/Mu3D.Formats.MaterialX.csproj",
    "src/Mu3D.Rendering.OpenPbr/Mu3D.Rendering.OpenPbr.csproj",
    "src/Mu3D.Native.Wgpu/Mu3D.Native.Wgpu.csproj",
    "src/Mu3D.Native.Ktx/Mu3D.Native.Ktx.csproj",
    "src/Mu3D.Native.UltraHdr/Mu3D.Native.UltraHdr.csproj",
    "src/Mu3D.Formats.Gltf/Mu3D.Formats.Gltf.csproj",
    "src/Mu3D.Toolkit/Mu3D.Toolkit.csproj",
    "src/Mu3D.Maui/Mu3D.Maui.csproj",
    "src/Mu3D.Maui.Toolkit/Mu3D.Maui.Toolkit.csproj"
)

Push-Location $repositoryRoot
try {
    foreach ($project in $projects) {
        & dotnet restore $project `
            -p:Mu3DUseRuntimeProjectReferences=false `
            --source $resolvedRuntimeFeed `
            --source "https://api.nuget.org/v3/index.json" `
            --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Managed package restore failed for $project."
        }

        & dotnet pack $project `
            --configuration $Configuration `
            --output $resolvedOutput `
            --no-restore `
            -p:Mu3DUseRuntimeProjectReferences=false `
            --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Managed package creation failed for $project."
        }
    }
}
finally {
    Pop-Location
}

if (Get-ChildItem -LiteralPath $resolvedOutput -Filter "*.snupkg" -File) {
    throw "The private managed feed unexpectedly contains symbol packages."
}
Write-Host "Private managed feed: $resolvedOutput"

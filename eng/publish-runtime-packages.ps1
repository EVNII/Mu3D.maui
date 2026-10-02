[CmdletBinding()]
param(
    [string] $PackageDirectory,
    [string] $Source = "https://api.nuget.org/v3/index.json",
    [switch] $ConfirmPublicRelease
)

$ErrorActionPreference = "Stop"
if (-not $ConfirmPublicRelease) {
    throw "Public publication requires the explicit -ConfirmPublicRelease switch."
}
if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) {
    throw "Set NUGET_API_KEY in the local environment; never commit it to the repository."
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repositoryRoot "artifacts/packages/public-runtime"
}
$resolvedPackages = [System.IO.Path]::GetFullPath($PackageDirectory)

& (Join-Path $PSScriptRoot "verify-runtime-packages.ps1") -PackageDirectory $resolvedPackages

foreach ($package in Get-ChildItem -LiteralPath $resolvedPackages -Filter "Mu3D.Native.*.nupkg" -File | Sort-Object Name) {
    & dotnet nuget push $package.FullName --source $Source --api-key $env:NUGET_API_KEY --skip-duplicate
    if ($LASTEXITCODE -ne 0) {
        throw "Publication failed for $($package.Name)."
    }
}

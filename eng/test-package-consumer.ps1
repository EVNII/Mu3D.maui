[CmdletBinding()]
param(
    [string] $RuntimeFeed,
    [string] $ManagedFeed,
    [string] $Framework = "net10.0-windows10.0.19041.0",
    [string] $RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($RuntimeFeed)) {
    $RuntimeFeed = Join-Path $repositoryRoot "artifacts/packages/public-runtime"
}
if ([string]::IsNullOrWhiteSpace($ManagedFeed)) {
    $ManagedFeed = Join-Path $repositoryRoot "artifacts/packages/private-managed"
}
$resolvedRuntimeFeed = [System.IO.Path]::GetFullPath($RuntimeFeed)
$resolvedManagedFeed = [System.IO.Path]::GetFullPath($ManagedFeed)
$consumerRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts/package-consumer-smoke"))
[xml] $versionProps = Get-Content -LiteralPath (Join-Path $repositoryRoot "eng/Mu3D.PackageVersion.props")
$packageVersion = [string] $versionProps.Project.PropertyGroup.Mu3DPackageVersion.'#text'
if ([string]::IsNullOrWhiteSpace($packageVersion)) {
    $packageVersion = [string] $versionProps.Project.PropertyGroup.Mu3DPackageVersion
}

if (-not $consumerRoot.StartsWith((Join-Path $repositoryRoot "artifacts"), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to recreate package consumer outside the repository artifacts directory."
}
if (Test-Path -LiteralPath $consumerRoot) {
    Remove-Item -LiteralPath $consumerRoot -Recurse -Force
}

& dotnet new maui --name Mu3D.PackageConsumerSmoke --output $consumerRoot --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Unable to create the clean MAUI package consumer."
}

$consumerProject = Join-Path $consumerRoot "Mu3D.PackageConsumerSmoke.csproj"
& dotnet add $consumerProject package Mu3D.Maui `
    --version $packageVersion `
    --source $resolvedManagedFeed `
    --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Unable to add Mu3D.Maui to the clean package consumer."
}

& dotnet restore $consumerProject `
    --source $resolvedRuntimeFeed `
    --source $resolvedManagedFeed `
    --source "https://api.nuget.org/v3/index.json" `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Clean package-consumer restore failed."
}

& dotnet build $consumerProject `
    --configuration Release `
    --framework $Framework `
    --no-restore `
    -p:RuntimeIdentifierOverride=$RuntimeIdentifier `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Clean package-consumer build failed."
}

$nativeLibrary = Get-ChildItem -LiteralPath (Join-Path $consumerRoot "bin/Release") -Recurse -Filter "wgpu_native.dll" -File |
    Select-Object -First 1
if ($Framework.Contains("windows") -and -not $nativeLibrary) {
    throw "The clean Windows package consumer output does not contain wgpu_native.dll."
}
Write-Host "Clean downstream package consumer passed: $consumerRoot"

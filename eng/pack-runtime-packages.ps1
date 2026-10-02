[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts/packages/public-runtime"
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[void] (New-Item -ItemType Directory -Force -Path $resolvedOutput)

$projects = @(
    "src/Mu3D.Native.Wgpu.Runtime/Mu3D.Native.Wgpu.Runtime.csproj",
    "src/Mu3D.Native.Ktx.Runtime/Mu3D.Native.Ktx.Runtime.csproj",
    "src/Mu3D.Native.UltraHdr.Runtime.Jpeg/Mu3D.Native.UltraHdr.Runtime.Jpeg.csproj"
)

Push-Location $repositoryRoot
try {
    foreach ($project in $projects) {
        & dotnet pack $project --configuration $Configuration --output $resolvedOutput --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Runtime package creation failed for $project. Complete every declared RID before retrying."
        }
    }
}
finally {
    Pop-Location
}

& (Join-Path $PSScriptRoot "verify-runtime-packages.ps1") -PackageDirectory $resolvedOutput

$hashLines = Get-ChildItem -LiteralPath $resolvedOutput -Filter "Mu3D.Native.*.nupkg" -File |
    Sort-Object Name |
    ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }
$hashLines | Set-Content -LiteralPath (Join-Path $resolvedOutput "SHA256SUMS.runtime-packages") -Encoding ascii
Write-Host "Verified runtime feed: $resolvedOutput"

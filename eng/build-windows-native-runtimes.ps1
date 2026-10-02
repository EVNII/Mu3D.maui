[CmdletBinding()]
param(
    [ValidateSet("win-x86", "win-x64", "win-arm64")]
    [string[]] $RuntimeIdentifier = @("win-x86", "win-x64", "win-arm64")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($env:OS -ne "Windows_NT") {
    throw "Windows native runtimes must be built on Windows."
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$workingRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "native-build/windows"))
$sourceRoot = Join-Path $workingRoot "sources"
$buildRoot = Join-Path $workingRoot "build"
$toolsRoot = Join-Path $workingRoot "tools"

foreach ($path in @($workingRoot, $sourceRoot, $buildRoot, $toolsRoot)) {
    if (-not $path.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use a native-build path outside the repository artifacts directory: $path"
    }
    [void] (New-Item -ItemType Directory -Force -Path $path)
}

function Read-PropsValue {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    [xml] $document = Get-Content -LiteralPath $Path -Raw
    $node = $document.SelectSingleNode("/Project/PropertyGroup/$Name")
    if (-not $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "MSBuild property '$Name' is missing from $Path."
    }
    return $node.InnerText.Trim()
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Find-VisualStudioTool {
    param(
        [Parameter(Mandatory = $true)]
        [string] $InstallationPath,

        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $path = Join-Path $InstallationPath $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Visual Studio tool is missing: $path"
    }
    return $path
}

function Ensure-PinnedSource {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $Repository,

        [Parameter(Mandatory = $true)]
        [string] $Commit,

        [Parameter(Mandatory = $true)]
        [string] $GitPath
    )

    $destination = Join-Path $sourceRoot $Name
    [void] (New-Item -ItemType Directory -Force -Path $destination)
    $safeDirectory = "safe.directory=$($destination.Replace('\', '/'))"

    if (-not (Test-Path -LiteralPath (Join-Path $destination ".git") -PathType Container)) {
        Invoke-Checked $GitPath @("-C", $destination, "init") "Initializing $Name source checkout"
        Invoke-Checked $GitPath @("-C", $destination, "remote", "add", "origin", $Repository) `
            "Adding the $Name upstream"
    }

    Invoke-Checked $GitPath @("-c", $safeDirectory, "-C", $destination, "fetch", "--depth", "1", "origin", $Commit) `
        "Fetching pinned $Name commit"
    Invoke-Checked $GitPath @(
        "-c", $safeDirectory,
        "-c", "filter.lfs.process=",
        "-c", "filter.lfs.smudge=",
        "-c", "filter.lfs.clean=",
        "-c", "filter.lfs.required=false",
        "-C", $destination,
        "checkout", "--force", "--detach", "FETCH_HEAD"
    ) "Checking out pinned $Name commit"

    $actualCommit = (& $GitPath -c $safeDirectory -C $destination rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $Commit) {
        throw "$Name source is '$actualCommit', expected '$Commit'."
    }

    return $destination
}

function Ensure-Nasm {
    $version = "2.16.03"
    $expectedSha256 = "3ee4782247bcb874378d02f7eab4e294a84d3d15f3f6ee2de2f47a46aa7226e6"
    $archive = Join-Path $toolsRoot "nasm-$version-win64.zip"
    $executable = Join-Path $toolsRoot "nasm-$version/nasm.exe"

    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
        Invoke-WebRequest `
            -Uri "https://www.nasm.us/pub/nasm/releasebuilds/$version/win64/nasm-$version-win64.zip" `
            -OutFile $archive
    }

    $actualSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        throw "NASM archive hash is '$actualSha256', expected '$expectedSha256'."
    }

    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        Expand-Archive -LiteralPath $archive -DestinationPath $toolsRoot -Force
    }
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "The verified NASM archive did not contain $executable."
    }

    return $executable
}

function Get-PeMachine {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) {
            throw "$Path is not a PE file."
        }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadUInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "$Path has an invalid PE signature."
        }
        return $reader.ReadUInt16()
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-NativeLibrary {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [UInt16] $ExpectedMachine,

        [Parameter(Mandatory = $true)]
        [string[]] $RequiredExports,

        [Parameter(Mandatory = $true)]
        [string] $DumpbinPath
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Native build output is missing: $Path"
    }
    $actualMachine = Get-PeMachine $Path
    if ($actualMachine -ne $ExpectedMachine) {
        throw "$Path has PE machine 0x$($actualMachine.ToString('x4')); expected 0x$($ExpectedMachine.ToString('x4'))."
    }

    $exports = (& $DumpbinPath /exports $Path) -join "`n"
    if ($LASTEXITCODE -ne 0) {
        throw "dumpbin could not inspect $Path."
    }
    foreach ($symbol in $RequiredExports) {
        if ($exports -notmatch "(?m)\s$([regex]::Escape($symbol))\s*$") {
            throw "$Path is missing required export '$symbol'."
        }
    }
}

$ktxVersionProps = Join-Path $repositoryRoot "eng/Mu3D.KtxNativeVersion.props"
$codecVersionProps = Join-Path $repositoryRoot "eng/Mu3D.UltraHdrNativeVersion.props"
$wgpuVersionProps = Join-Path $repositoryRoot "eng/Mu3D.WgpuNativeVersion.props"
$ktxVersion = Read-PropsValue $ktxVersionProps "Mu3DKtxNativeVersion"
$ktxCommit = Read-PropsValue $ktxVersionProps "Mu3DKtxNativeCommit"
$ultraHdrVersion = Read-PropsValue $codecVersionProps "Mu3DUltraHdrNativeVersion"
$ultraHdrCommit = Read-PropsValue $codecVersionProps "Mu3DUltraHdrNativeCommit"
$jpegTurboVersion = Read-PropsValue $codecVersionProps "Mu3DJpegTurboNativeVersion"
$jpegTurboCommit = Read-PropsValue $codecVersionProps "Mu3DJpegTurboNativeCommit"
$wgpuVersion = Read-PropsValue $wgpuVersionProps "Mu3DWgpuNativeVersion"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw "Visual Studio Installer's vswhere.exe was not found."
}
$visualStudioPath = (& $vswhere -latest -products * -property installationPath).Trim()
if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
    throw "A complete Visual Studio C++ installation was not found."
}

$cmake = Find-VisualStudioTool $visualStudioPath "Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"
$git = Find-VisualStudioTool $visualStudioPath "Common7/IDE/CommonExtensions/Microsoft/TeamFoundation/Team Explorer/Git/cmd/git.exe"
$bash = Find-VisualStudioTool $visualStudioPath "Common7/IDE/CommonExtensions/Microsoft/TeamFoundation/Team Explorer/Git/usr/bin/sh.exe"
$dumpbin = Get-ChildItem -LiteralPath (Join-Path $visualStudioPath "VC/Tools/MSVC") -Recurse -Filter dumpbin.exe -File |
    Where-Object { $_.FullName -match "\\Hostx64\\x64\\dumpbin\.exe$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $dumpbin) {
    throw "The Visual Studio x64-hosted dumpbin.exe was not found."
}

$cmakeHelp = (& $cmake --help) -join "`n"
$generator = if ($cmakeHelp -match "Visual Studio 18 2026") {
    "Visual Studio 18 2026"
}
elseif ($cmakeHelp -match "Visual Studio 17 2022") {
    "Visual Studio 17 2022"
}
else {
    throw "CMake does not expose a supported Visual Studio generator."
}

$architectures = @{
    "win-x86" = [pscustomobject]@{ CMake = "Win32"; Machine = [UInt16] 0x014c; CompilerFolder = "x86" }
    "win-x64" = [pscustomobject]@{ CMake = "x64"; Machine = [UInt16] 0x8664; CompilerFolder = "x64" }
    "win-arm64" = [pscustomobject]@{ CMake = "ARM64"; Machine = [UInt16] 0xaa64; CompilerFolder = "arm64" }
}

foreach ($rid in $RuntimeIdentifier) {
    $architecture = $architectures[$rid]
    $compiler = Get-ChildItem -LiteralPath (Join-Path $visualStudioPath "VC/Tools/MSVC") -Recurse -Filter cl.exe -File |
        Where-Object { $_.FullName -match "\\Hostx64\\$([regex]::Escape($architecture.CompilerFolder))\\cl\.exe$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $compiler) {
        if ($rid -eq "win-arm64") {
            throw "win-arm64 requires Visual Studio component Microsoft.VisualStudio.Component.VC.Tools.ARM64. Install it in Visual Studio Installer, then rerun this script."
        }
        throw "Visual Studio compiler for $rid is missing."
    }
}

$ktxSource = Ensure-PinnedSource "ktx" "https://github.com/KhronosGroup/KTX-Software.git" $ktxCommit $git
$ultraHdrSource = Ensure-PinnedSource "ultrahdr" "https://github.com/google/libultrahdr.git" $ultraHdrCommit $git
$jpegTurboSource = Ensure-PinnedSource "jpeg-turbo" "https://github.com/libjpeg-turbo/libjpeg-turbo.git" $jpegTurboCommit $git
$requiresNasm = @($RuntimeIdentifier | Where-Object { $_ -ne "win-arm64" }).Count -gt 0
$nasm = if ($requiresNasm) { Ensure-Nasm } else { $null }

$ktxPatch = Join-Path $repositoryRoot "eng/native/KtxWindowsX86CallingConvention.patch"
$ktxSafeDirectory = "safe.directory=$($ktxSource.Replace('\', '/'))"
Invoke-Checked $git @("-c", $ktxSafeDirectory, "-C", $ktxSource, "apply", "--check", $ktxPatch) `
    "Checking the pinned KTX Windows calling-convention patch"
Invoke-Checked $git @("-c", $ktxSafeDirectory, "-C", $ktxSource, "apply", $ktxPatch) `
    "Applying the pinned KTX Windows calling-convention patch"

$ktxExports = @(
    "ktxTexture2_CreateFromMemory", "ktxTexture2_Destroy", "ktxTexture2_NeedsTranscoding",
    "ktxTexture2_GetTransferFunction_e", "ktxTexture2_GetPrimaries_e",
    "ktxTexture2_GetPremultipliedAlpha", "ktxTexture2_TranscodeBasis",
    "ktxTexture2_GetImageOffset", "ktxTexture_GetData", "ktxTexture_GetDataSize",
    "ktxHashList_FindValue"
)
$ultraHdrExports = @(
    "uhdr_create_decoder", "uhdr_release_decoder", "uhdr_dec_set_image",
    "uhdr_dec_set_out_img_format", "uhdr_dec_set_out_color_transfer", "uhdr_dec_probe",
    "uhdr_dec_get_gainmap_width", "uhdr_dec_get_gainmap_height", "uhdr_decode",
    "uhdr_get_decoded_image", "is_uhdr_image", "uhdr_create_encoder", "uhdr_release_encoder",
    "uhdr_enc_set_raw_image", "uhdr_enc_set_quality", "uhdr_enc_set_using_multi_channel_gainmap",
    "uhdr_enc_set_gainmap_scale_factor", "uhdr_enc_set_target_display_peak_brightness",
    "uhdr_enc_set_preset", "uhdr_enc_set_output_format", "uhdr_encode", "uhdr_get_encoded_stream"
)
$turboJpegExports = @(
    "tj3Init", "tj3Destroy", "tj3GetErrorStr", "tj3Get", "tj3Set", "tj3Compress8", "tj3Free",
    "tj3DecompressHeader", "tj3Decompress8"
)

foreach ($rid in $RuntimeIdentifier) {
    $architecture = $architectures[$rid]
    Write-Host "Building $rid native runtimes with $generator."

    $ktxBuild = Join-Path $buildRoot "ktx/$rid"
    [void] (New-Item -ItemType Directory -Force -Path $ktxBuild)
    $ktxConfigure = @(
        "-S", $ktxSource, "-B", $ktxBuild, "-G", $generator, "-A", $architecture.CMake,
        "-DBASH_EXECUTABLE=$bash", "-DKTX_GIT_VERSION_FULL=$ktxVersion", "-DBUILD_SHARED_LIBS=ON",
        "-DCMAKE_C_FLAGS=/Gz", "-DCMAKE_CXX_FLAGS=/Gz", "-DKTX_MU3D_PUBLIC_EXPORTS_ONLY=ON",
        "-DKTX_MU3D_WINDOWS_EXPORTS_FILE=$(Join-Path $repositoryRoot 'eng/native/KtxWindowsX86PublicExports.def')",
        "-DKTX_FEATURE_TOOLS=OFF", "-DKTX_FEATURE_TESTS=OFF", "-DKTX_FEATURE_LOADTEST_APPS=OFF",
        "-DKTX_FEATURE_GL_UPLOAD=OFF", "-DKTX_FEATURE_VK_UPLOAD=OFF", "-DKTX_FEATURE_ETC_UNPACK=OFF",
        "-DKTX_FEATURE_KTX1=ON", "-DBASISU_SUPPORT_OPENCL=OFF", "-DBASISU_SUPPORT_SSE=OFF",
        "-DASTCENC_ISA_SSE2=ON"
    )
    Invoke-Checked $cmake $ktxConfigure "Configuring KTX for $rid"
    Invoke-Checked $cmake @("--build", $ktxBuild, "--config", "Release", "--target", "ktx_read", "--parallel") `
        "Building KTX for $rid"

    $ktxOutput = Join-Path $ktxBuild "Release/ktx_read.dll"
    Assert-NativeLibrary $ktxOutput $architecture.Machine $ktxExports $dumpbin
    $ktxDestination = Join-Path $repositoryRoot "artifacts/ktx/$ktxVersion/release/runtimes/$rid/native/ktx.dll"
    [void] (New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ktxDestination))
    Copy-Item -LiteralPath $ktxOutput -Destination $ktxDestination -Force

    $jpegBuild = Join-Path $buildRoot "jpeg-turbo/$rid"
    [void] (New-Item -ItemType Directory -Force -Path $jpegBuild)
    $jpegConfigure = @(
        "-S", $jpegTurboSource, "-B", $jpegBuild, "-G", $generator, "-A", $architecture.CMake,
        "-DENABLE_SHARED=ON", "-DENABLE_STATIC=ON", "-DWITH_TURBOJPEG=ON", "-DWITH_CRT_DLL=ON"
    )
    if ($rid -eq "win-arm64") {
        # libjpeg-turbo 3.1.0 has no Windows ARM64 SIMD backend. Its Visual Studio platform
        # detection also compares lowercase "arm64" against CMake's uppercase "ARM64" and would
        # otherwise probe the x86 NASM path before disabling SIMD with a misleading warning.
        $jpegConfigure += "-DWITH_SIMD=OFF"
    }
    else {
        $jpegConfigure += "-DCMAKE_ASM_NASM_COMPILER=$nasm"
    }
    Invoke-Checked $cmake $jpegConfigure "Configuring libjpeg-turbo for $rid"
    Invoke-Checked $cmake @("--build", $jpegBuild, "--config", "Release", "--target", "jpeg-static", "turbojpeg", "--parallel") `
        "Building libjpeg-turbo for $rid"

    $jpegInstall = Join-Path $buildRoot "jpeg-turbo-install/$rid"
    [void] (New-Item -ItemType Directory -Force -Path $jpegInstall)
    Invoke-Checked $cmake @(
        "--install", $jpegBuild, "--config", "Release", "--component", "include", "--prefix", $jpegInstall
    ) "Staging libjpeg-turbo headers for $rid"

    $ultraHdrBuild = Join-Path $buildRoot "ultrahdr/$rid"
    [void] (New-Item -ItemType Directory -Force -Path $ultraHdrBuild)
    $jpegLibrary = (Join-Path $jpegBuild "Release/jpeg-static.lib").Replace('\', '/')
    $jpegIncludeDirectory = (Join-Path $jpegInstall "include").Replace('\', '/')
    $ultraHdrConfigure = @(
        "-S", $ultraHdrSource, "-B", $ultraHdrBuild, "-G", $generator, "-A", $architecture.CMake,
        "-DBUILD_SHARED_LIBS=ON", "-DUHDR_BUILD_DEPS=OFF", "-DUHDR_BUILD_TESTS=OFF",
        "-DUHDR_BUILD_EXAMPLES=OFF", "-DUHDR_BUILD_BENCHMARK=OFF", "-DUHDR_ENABLE_HEIF=OFF",
        "-DUHDR_ENABLE_INSTALL=OFF", "-DUHDR_WRITE_ISO=ON", "-DUHDR_WRITE_XMP=OFF",
        "-DJPEG_LIBRARY=$jpegLibrary", "-DJPEG_INCLUDE_DIR=$jpegIncludeDirectory"
    )
    Invoke-Checked $cmake $ultraHdrConfigure "Configuring libultrahdr for $rid"
    Invoke-Checked $cmake @("--build", $ultraHdrBuild, "--config", "Release", "--target", "uhdr", "--parallel") `
        "Building libultrahdr for $rid"

    $ultraHdrOutput = Join-Path $ultraHdrBuild "Release/uhdr.dll"
    $turboJpegOutput = Join-Path $jpegBuild "Release/turbojpeg.dll"
    Assert-NativeLibrary $ultraHdrOutput $architecture.Machine $ultraHdrExports $dumpbin
    Assert-NativeLibrary $turboJpegOutput $architecture.Machine $turboJpegExports $dumpbin

    $codecDestination = Join-Path $repositoryRoot "artifacts/ultrahdr/$ultraHdrVersion/release/runtimes/$rid/native"
    [void] (New-Item -ItemType Directory -Force -Path $codecDestination)
    Copy-Item -LiteralPath $ultraHdrOutput -Destination (Join-Path $codecDestination "uhdr.dll") -Force
    Copy-Item -LiteralPath $turboJpegOutput -Destination (Join-Path $codecDestination "turbojpeg.dll") -Force

    $wgpuPath = Join-Path $repositoryRoot "artifacts/wgpu-native/$wgpuVersion/release/runtimes/$rid/native/wgpu_native.dll"
    Assert-NativeLibrary $wgpuPath $architecture.Machine @(
        "wgpuCreateInstance",
        "wgpuDeviceGetNativeD3D12Device",
        "wgpuDeviceGetNativeD3D12CommandQueue",
        "wgpuTextureGetNativeD3D12Resource"
    ) $dumpbin

}

& (Join-Path $PSScriptRoot 'prepare-windows-dxc.ps1')

$hashRecords = [System.Collections.Generic.List[string]]::new()
foreach ($rid in $architectures.Keys | Sort-Object) {
    $runtimePaths = @(
        (Join-Path $repositoryRoot "artifacts/wgpu-native/$wgpuVersion/release/runtimes/$rid/native/wgpu_native.dll"),
        (Join-Path $repositoryRoot "artifacts/wgpu-native/$wgpuVersion/release/runtimes/$rid/native/dxcompiler.dll"),
        (Join-Path $repositoryRoot "artifacts/wgpu-native/$wgpuVersion/release/runtimes/$rid/native/dxil.dll"),
        (Join-Path $repositoryRoot "artifacts/ktx/$ktxVersion/release/runtimes/$rid/native/ktx.dll"),
        (Join-Path $repositoryRoot "artifacts/ultrahdr/$ultraHdrVersion/release/runtimes/$rid/native/uhdr.dll"),
        (Join-Path $repositoryRoot "artifacts/ultrahdr/$ultraHdrVersion/release/runtimes/$rid/native/turbojpeg.dll")
    )
    foreach ($path in $runtimePaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            continue
        }
        if (-not $path.StartsWith($repositoryRoot + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Verified output is outside the repository: $path"
        }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $relativePath = $path.Substring($repositoryRoot.Length + 1).Replace('\', '/')
        $hashRecords.Add("$hash  $relativePath")
        Write-Host "$hash  $relativePath"
    }
}

$hashPath = Join-Path $workingRoot "SHA256SUMS.windows"
$hashRecords | Sort-Object | Set-Content -LiteralPath $hashPath -Encoding ascii
Write-Host "Verified Windows native runtimes. Hash manifest: $hashPath"

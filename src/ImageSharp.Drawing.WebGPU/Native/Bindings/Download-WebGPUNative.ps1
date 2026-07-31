<#
.SYNOPSIS
Downloads the native libraries and headers used by the WebGPU backend.

.DESCRIPTION
Downloads the configured official wgpu-native release archives, verifies each archive against its
published SHA-256 digest, and copies the native libraries into the project's runtimes directory
using standard .NET runtime identifiers. The configured Microsoft DirectX Shader Compiler package
supplies the Windows shader compiler runtime. Library filenames remain unchanged from upstream.

The Windows x64 archive supplies the checked-in webgpu.h and wgpu.h files used for binding
generation. This is safe because every platform archive for the configured release contains
identical headers.

Downloaded archives and extracted files are cached beneath obj/WebGPUNative. They are generation
inputs only and are not included in packages.

.EXAMPLE
dotnet msbuild ImageSharp.Drawing.WebGPU.csproj -t:DownloadWebGPUNative -p:Configuration=Release

.NOTES
Run the script through the DownloadWebGPUNative MSBuild target so its repository entry point stays
consistent with binding generation.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$releaseVersion = "v29.0.1.1"
$releaseBaseUri = "https://github.com/gfx-rs/wgpu-native/releases/download/$releaseVersion"
$dxcPackageVersion = "1.8.2505.32"
$dxcPackageSha256 = "C6E82B70C14552F1DD58E4A79C93EEAB1567EEB0A9EE63A51564C410429BCE3E"
$dxcPackageName = "microsoft.direct3d.dxc.$dxcPackageVersion.nupkg"
$dxcPackageUri = "https://api.nuget.org/v3-flatcontainer/microsoft.direct3d.dxc/$dxcPackageVersion/$dxcPackageName"
$projectDirectory = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$cacheDirectory = Join-Path $projectDirectory "obj\WebGPUNative\$releaseVersion"
$dxcCacheDirectory = Join-Path $projectDirectory "obj\WebGPUNative\DXC\$dxcPackageVersion"
$runtimeDirectory = Join-Path $projectDirectory "runtimes"
$headerDirectory = Join-Path $PSScriptRoot "Headers"

# GitHub's release architecture names differ from .NET runtime identifiers. This table is also the
# authoritative supported-runtime matrix for the downloaded assets. Digests are the SHA-256 values
# published for the configured release.
$assets = @(
    @{
        RuntimeIdentifier = "win-x64"
        ArchiveName = "wgpu-windows-x86_64-msvc-release.zip"
        LibraryName = "wgpu_native.dll"
        Sha256 = "7e67d7445c42aeb85e30f88930fd8d7d83ee769e3390aeb1ada75ebf3cf78132"
    },
    @{
        RuntimeIdentifier = "win-x86"
        ArchiveName = "wgpu-windows-i686-msvc-release.zip"
        LibraryName = "wgpu_native.dll"
        Sha256 = "ad59d4eadfcfe667999a37e096cc551ecf3f56c387b5a7fd5f61baebf105f54a"
    },
    @{
        RuntimeIdentifier = "win-arm64"
        ArchiveName = "wgpu-windows-aarch64-msvc-release.zip"
        LibraryName = "wgpu_native.dll"
        Sha256 = "4a876421a8c1e5fe72f849b3722214280fe485cb1c56f77f8b0c82414be5b29f"
    },
    @{
        RuntimeIdentifier = "linux-x64"
        ArchiveName = "wgpu-linux-x86_64-release.zip"
        LibraryName = "libwgpu_native.so"
        Sha256 = "95a4d90c071005a98d03eab348beaa6b07e16eb00d1dcdb9f8348f75eb97ec5a"
    },
    @{
        RuntimeIdentifier = "linux-arm64"
        ArchiveName = "wgpu-linux-aarch64-release.zip"
        LibraryName = "libwgpu_native.so"
        Sha256 = "015fcdf1dbae82e614a783cc38017e5399ae0927a889fe9b69c9b664bc61b47a"
    },
    @{
        RuntimeIdentifier = "osx-x64"
        ArchiveName = "wgpu-macos-x86_64-release.zip"
        LibraryName = "libwgpu_native.dylib"
        Sha256 = "8e2f7378548ddd0e2cf21e7d864dda46e953f0af724855a33778b85ead206d41"
    },
    @{
        RuntimeIdentifier = "osx-arm64"
        ArchiveName = "wgpu-macos-aarch64-release.zip"
        LibraryName = "libwgpu_native.dylib"
        Sha256 = "a5797a37b1adf720bcd5dcffb291edbbd5b7b14be0a3874c28e6393a655a7a3e"
    },
    @{
        RuntimeIdentifier = "android-x64"
        ArchiveName = "wgpu-android-x86_64-release.zip"
        LibraryName = "libwgpu_native.so"
        Sha256 = "ef16fc0644bf0e308a39ac4516742da8e22d8c201d3a542cc5baf533d272c491"
    },
    @{
        RuntimeIdentifier = "android-x86"
        ArchiveName = "wgpu-android-i686-release.zip"
        LibraryName = "libwgpu_native.so"
        Sha256 = "593b94875bc4fcc1506ea0b6714dd12b96b7c852921caa63f45eb61517793312"
    },
    @{
        RuntimeIdentifier = "android-arm64"
        ArchiveName = "wgpu-android-aarch64-release.zip"
        LibraryName = "libwgpu_native.so"
        Sha256 = "721741f1b05a20c1738166bedf7a5efb2ba4b382da689526d3fc33de22bdd573"
    },
    @{
        RuntimeIdentifier = "android-arm"
        ArchiveName = "wgpu-android-armv7-release.zip"
        LibraryName = "libwgpu_native.so"
        Sha256 = "f9d76c77b3fda3f7121476884eb16ec067f7dada83276298a3cc8bf6a8403d60"
    },
    @{
        RuntimeIdentifier = "ios-arm64"
        ArchiveName = "wgpu-ios-aarch64-release.zip"
        LibraryName = "libwgpu_native.a"
        Sha256 = "e36c9913b9e5095a530fa9121c50b16a4e3dd020e1eebf601f2f47ce24d56941"
    },
    @{
        RuntimeIdentifier = "iossimulator-arm64"
        ArchiveName = "wgpu-ios-aarch64-simulator-release.zip"
        LibraryName = "libwgpu_native.a"
        Sha256 = "750e706765bef3744313745194774d095c916fc21d2a0e7d4d7b0bc4d0c92789"
    },
    @{
        RuntimeIdentifier = "iossimulator-x64"
        ArchiveName = "wgpu-ios-x86_64-simulator-release.zip"
        LibraryName = "libwgpu_native.a"
        Sha256 = "94f67e1b268e8dd31b8e59b32f211ac469f09ed7950fceee52bd84f0623da3d9"
    })

New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $dxcCacheDirectory -Force | Out-Null

foreach ($asset in $assets)
{
    $archivePath = Join-Path $cacheDirectory $asset.ArchiveName
    $archiveUri = "$releaseBaseUri/$($asset.ArchiveName)"

    # A cached archive is reusable only when it matches the release digest. A missing or damaged
    # cache entry is downloaded again before extraction.
    $archiveIsCurrent = (Test-Path $archivePath) -and
        ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -eq $asset.Sha256)

    if (!$archiveIsCurrent)
    {
        Write-Host "Downloading $($asset.ArchiveName)"
        Invoke-WebRequest -Uri $archiveUri -OutFile $archivePath -MaximumRedirection 5
    }

    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash

    # Archive contents are an external boundary. Reject a digest mismatch before any native file
    # can enter the runtime asset tree.
    if ($actualHash -ne $asset.Sha256)
    {
        throw "SHA-256 verification failed for '$($asset.ArchiveName)'."
    }

    $extractDirectory = Join-Path $cacheDirectory $asset.RuntimeIdentifier
    New-Item -ItemType Directory -Path $extractDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDirectory -Force

    $libraryCandidates = @(Get-ChildItem -LiteralPath $extractDirectory -Recurse -File -Filter $asset.LibraryName)

    # Each official archive must contain exactly one runtime library with the upstream name. This
    # prevents an archive layout change from silently selecting an import library or stale file.
    if ($libraryCandidates.Count -ne 1)
    {
        throw "Expected exactly one '$($asset.LibraryName)' in '$($asset.ArchiveName)' but found $($libraryCandidates.Count)."
    }

    $runtimeAssetDirectory = Join-Path $runtimeDirectory "$($asset.RuntimeIdentifier)\native"
    New-Item -ItemType Directory -Path $runtimeAssetDirectory -Force | Out-Null
    Copy-Item -LiteralPath $libraryCandidates[0].FullName -Destination (Join-Path $runtimeAssetDirectory $asset.LibraryName) -Force
}

# Every platform archive for the configured release contains the same C headers, so the verified
# Windows x64 archive is the single source for binding generation.
$windowsHeaderRoot = Join-Path $cacheDirectory "win-x64\include\webgpu"
$headerNames = @("webgpu.h", "wgpu.h")

foreach ($headerName in $headerNames)
{
    $headerSource = Join-Path $windowsHeaderRoot $headerName

    if (!(Test-Path $headerSource))
    {
        throw "The verified Windows x64 archive does not contain '$headerName' at the expected path."
    }

    Copy-Item -LiteralPath $headerSource -Destination (Join-Path $headerDirectory $headerName) -Force
}

$dxcPackagePath = Join-Path $dxcCacheDirectory $dxcPackageName
$dxcPackageIsCurrent = (Test-Path $dxcPackagePath) -and
    ((Get-FileHash -LiteralPath $dxcPackagePath -Algorithm SHA256).Hash -eq $dxcPackageSha256)

if (!$dxcPackageIsCurrent)
{
    Write-Host "Downloading $dxcPackageName"
    Invoke-WebRequest -Uri $dxcPackageUri -OutFile $dxcPackagePath -MaximumRedirection 5
}

$actualDxcPackageHash = (Get-FileHash -LiteralPath $dxcPackagePath -Algorithm SHA256).Hash

# The compiler binaries execute inside the application process, so reject a package whose content
# does not match the configured Microsoft package before copying any file into the runtime tree.
if ($actualDxcPackageHash -ne $dxcPackageSha256)
{
    throw "SHA-256 verification failed for '$dxcPackageName'."
}

$dxcExtractDirectory = Join-Path $dxcCacheDirectory "package"
New-Item -ItemType Directory -Path $dxcExtractDirectory -Force | Out-Null

# NuGet packages are ZIP archives with a different extension. Use the framework ZIP API so the
# downloader does not need to rename the verified package or depend on NuGet tooling being present.
[System.IO.Compression.ZipFile]::ExtractToDirectory($dxcPackagePath, $dxcExtractDirectory, $true)

$dxcRuntimeAssets = @(
    @{
        RuntimeIdentifier = "win-x64"
        PackageArchitecture = "x64"
    },
    @{
        RuntimeIdentifier = "win-x86"
        PackageArchitecture = "x86"
    },
    @{
        RuntimeIdentifier = "win-arm64"
        PackageArchitecture = "arm64"
    })

$dxcLibraryNames = @("dxcompiler.dll", "dxil.dll")

foreach ($dxcRuntimeAsset in $dxcRuntimeAssets)
{
    $dxcPackageBinDirectory = Join-Path $dxcExtractDirectory "build\native\bin\$($dxcRuntimeAsset.PackageArchitecture)"
    $runtimeAssetDirectory = Join-Path $runtimeDirectory "$($dxcRuntimeAsset.RuntimeIdentifier)\native"
    New-Item -ItemType Directory -Path $runtimeAssetDirectory -Force | Out-Null

    foreach ($dxcLibraryName in $dxcLibraryNames)
    {
        $dxcLibrarySource = Join-Path $dxcPackageBinDirectory $dxcLibraryName

        # The configured compiler package contract supplies both the compiler and its validator for
        # each supported Windows architecture; a partial runtime would fail only after deployment.
        if (!(Test-Path $dxcLibrarySource))
        {
            throw "The verified DXC package does not contain '$dxcLibraryName' for '$($dxcRuntimeAsset.PackageArchitecture)'."
        }

        Copy-Item -LiteralPath $dxcLibrarySource -Destination (Join-Path $runtimeAssetDirectory $dxcLibraryName) -Force
    }
}

Write-Host "Downloaded and verified $($assets.Count) wgpu-native runtime assets for $releaseVersion and the configured DXC runtime."

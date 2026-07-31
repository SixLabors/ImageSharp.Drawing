<#
.SYNOPSIS
Regenerates the internal C# bindings for the WebGPU backend.

.DESCRIPTION
Restores the repository-pinned ClangSharp generator and translates the checked-in wgpu-native
headers into Native/Bindings/Generated/WebGPUNative.g.cs. The script never downloads or replaces
headers, so updating the native API remains a separate and reviewable source change.

On Windows, ClangSharp also needs the C standard-library headers supplied by MSVC and the Windows
SDK. The script discovers those installations and passes their include directories only to
libclang; machine-specific paths are not written to the generated source.

.EXAMPLE
dotnet msbuild ImageSharp.Drawing.WebGPU.csproj -t:GenerateWebGPUBindings -p:Configuration=Release

.NOTES
Run the script through the GenerateWebGPUBindings MSBuild target so the documented repository
entry point and the generator use the same project working directory.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Paths in generate.rsp are project-relative. Resolve the project from this script rather than
# relying on the caller's current directory.
$projectDirectory = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$toolManifestPath = Join-Path $projectDirectory ".config\dotnet-tools.json"

# Keep stable generator options in the response file so the command line, reviewable settings,
# and local regeneration path cannot drift apart.
$generatorArguments = @(
    "tool",
    "run",
    "ClangSharpPInvokeGenerator",
    "@Native/Bindings/generate.rsp")

$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

if ($runningOnWindows)
{
    # libclang does not ship the Microsoft C standard-library headers. The official Windows
    # wgpu-native headers transitively include stdint.h, stddef.h, and math.h, so generation must
    # parse them using the installed MSVC and Windows SDK definitions.
    $vsWherePath = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"

    if (-not (Test-Path $vsWherePath))
    {
        throw "Visual Studio Installer's vswhere.exe is required to locate the MSVC headers."
    }

    # Ask Visual Studio Installer for the newest installation that actually contains the C++
    # toolchain instead of assuming an edition-specific installation directory.
    $visualStudioDirectory = & $vsWherePath `
        -latest `
        -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($visualStudioDirectory))
    {
        throw "Visual Studio C++ Build Tools are required to generate the WebGPU bindings."
    }

    $msvcToolsDirectory = Join-Path $visualStudioDirectory "VC\Tools\MSVC"

    # Side-by-side Visual Studio servicing can leave multiple toolsets installed. Parsing version
    # directory names selects the active newest headers without depending on enumeration order.
    $msvcVersionDirectory = Get-ChildItem $msvcToolsDirectory -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if ($null -eq $msvcVersionDirectory)
    {
        throw "No MSVC toolset was found under '$msvcToolsDirectory'."
    }

    # The Windows Kits registry value is the supported machine-wide root. SDK versions are also
    # installed side by side, so select the newest version containing the required UCRT headers.
    $windowsKitsRoot = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots").KitsRoot10
    $windowsSdkIncludeDirectory = Join-Path $windowsKitsRoot "Include"
    $windowsSdkVersionDirectory = Get-ChildItem $windowsSdkIncludeDirectory -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName "ucrt\stddef.h") } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if ($null -eq $windowsSdkVersionDirectory)
    {
        throw "No Windows SDK containing the Universal C Runtime headers was found."
    }

    # These arguments affect parsing only. ClangSharp emits declarations from the WebGPU headers,
    # so absolute toolchain paths do not become part of the checked-in binding file.
    $generatorArguments += @(
        "--include-directory",
        (Join-Path $msvcVersionDirectory.FullName "include"),
        "--include-directory",
        (Join-Path $windowsSdkVersionDirectory.FullName "ucrt"),
        "--include-directory",
        (Join-Path $windowsSdkVersionDirectory.FullName "shared"))
}

# generate.rsp deliberately uses project-relative paths to keep its output stable across machines.
Push-Location $projectDirectory

try
{
    # Restore from the project-scoped manifest so an ambient global tool or ClangSharp upgrade
    # cannot silently change generated declarations or native ABI layouts.
    & dotnet tool restore --tool-manifest $toolManifestPath

    # dotnet is an external process, so PowerShell's error preference does not convert a non-zero
    # exit code into an exception. Fail the MSBuild target explicitly at this process boundary.
    if ($LASTEXITCODE -ne 0)
    {
        throw "ClangSharpPInvokeGenerator restore failed with exit code $LASTEXITCODE."
    }

    & dotnet @generatorArguments

    if ($LASTEXITCODE -ne 0)
    {
        throw "WebGPU binding generation failed with exit code $LASTEXITCODE."
    }
}
finally
{
    Pop-Location
}

param(
    [string]$version = '1.0.0',
    [string]$targetFramework = 'ALL'
)

$skipFullFramework = 'false'

# If we are trying to build only netcoreapp versions for testings then skip building the full framework targets
if ("$targetFramework".StartsWith("netcoreapp")) {
    $skipFullFramework = 'true'
}

Write-Host "Building version '${version}'"
dotnet restore /p:packageversion=$version /p:DisableImplicitNuGetFallbackFolder=true /p:skipFullFramework=$skipFullFramework

$repositoryUrl = "https://github.com/SixLabors/ImageSharp.Drawing/"

if ("$env:GITHUB_REPOSITORY" -ne "") {
    $repositoryUrl = "https://github.com/$env:GITHUB_REPOSITORY"
}

Write-Host "Building projects"
dotnet build -c Release /p:packageversion=$version /p:skipFullFramework=$skipFullFramework /p:RepositoryUrl=$repositoryUrl

if ($LASTEXITCODE ) { Exit $LASTEXITCODE }

Write-Host "Packaging projects"

dotnet pack ./src/ImageSharp.Drawing/ -c Release --output "$PSScriptRoot/artifacts" --no-build  /p:packageversion=$version /p:skipFullFramework=$skipFullFramework /p:RepositoryUrl=$repositoryUrl
if ($LASTEXITCODE ) { Exit $LASTEXITCODE }

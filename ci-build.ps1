param(
  [Parameter(Mandatory = $true, Position = 0)]
  [string]$targetFramework
)

$solution = Join-Path $PSScriptRoot "ImageSharp.Drawing.CI.slnf"

dotnet clean $solution -c Release

$repositoryUrl = "https://github.com/$env:GITHUB_REPOSITORY"

# Building for a specific framework.
dotnet build $solution -c Release -f $targetFramework /p:RepositoryUrl=$repositoryUrl

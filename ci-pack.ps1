$solution = Join-Path $PSScriptRoot "ImageSharp.Drawing.CI.slnf"

dotnet clean $solution -c Release

$repositoryUrl = "https://github.com/$env:GITHUB_REPOSITORY"

# Building for packing and publishing.
dotnet pack $solution -c Release --output "$PSScriptRoot/artifacts" /p:RepositoryUrl=$repositoryUrl

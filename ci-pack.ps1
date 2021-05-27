$repositoryUrl = "https://github.com/$env:GITHUB_REPOSITORY"

# Building for packing and publishing.
dotnet build -c Release /p:RepositoryUrl=$repositoryUrl
dotnet pack -c Release --no-build --output "$PSScriptRoot/artifacts" /p:RepositoryUrl=$repositoryUrl

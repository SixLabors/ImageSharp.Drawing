$env:CI = "true"
$env:SIXLABORS_TESTING = "True"

$solution = Join-Path $PSScriptRoot "ImageSharp.Drawing.sln"

# Build (ci-build.ps1 net8.0)
dotnet clean $solution -c Release
dotnet build $solution -c Release -f net8.0

# Pack (ci-pack.ps1)
dotnet clean $solution -c Release
dotnet pack $solution -c Release --output "$PSScriptRoot/artifacts"

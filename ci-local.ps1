$env:CI = "true"
$env:SIXLABORS_TESTING = "True"

$solution = Join-Path $PSScriptRoot "ImageSharp.Drawing.slnx"

# Build (ci-build.ps1 net10.0)
dotnet clean $solution -c Release
dotnet build $solution -c Release -f net10.0

# Pack (ci-pack.ps1)
dotnet clean $solution -c Release
dotnet pack $solution -c Release --output "$PSScriptRoot/artifacts"

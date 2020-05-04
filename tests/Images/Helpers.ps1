function Install-ImageSharp {
    Param(
        [Parameter(Mandatory)]
        [string]$Version,
        [ValidateSet('NuGet', 'MyGet')]
        [string]$Source = 'NuGet'
    )

    $ImageSharpVersion = $Version
    $ImageSharpPackageName = "SixLabors.ImageSharp"
    
    
    $tmpLibLocation = Join-Path $env:Temp $ImageSharpPackageName $ImageSharpVersion
    # if the package has not yet been downloaded downloaded into a temp directory and carry on
    if ((Test-Path $tmpLibLocation) -eq $false) {
        $RepositoryDownloadRoot = "https://www.nuget.org/api/v2/package" # nuget.org for when we get a good one out officailly
        if ($Source -eq "MyGet") {
            $RepositoryDownloadRoot = "https://www.myget.org/F/sixlabors/api/v2/package" # myget feed for access to the latest. should we just try both? leave for now
        }
        
        $downloadUrl = "$RepositoryDownloadRoot/$ImageSharpPackageName/$ImageSharpVersion"
        
        $fileLocation = Join-Path $tmpLibLocation "package.zip"
        $_ = New-Item $tmpLibLocation -ItemType Directory
        Invoke-WebRequest $downloadUrl -OutFile $fileLocation     
        Expand-Archive $fileLocation -DestinationPath $tmpLibLocation    
    }
    
    Add-Type -LiteralPath (Join-Path $tmpLibLocation "lib\netstandard1.3\SixLabors.ImageSharp.dll" )
}

function Get-ImageSharpImage {
    Param(
        [Parameter(Mandatory)]
        [string]$Path
    )
    [SixLabors.ImageSharp.Image]$img = [SixLabors.ImageSharp.Image]::Load($Path)

    return $img
}

function Set-ImageSharpImage {
    Param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [SixLabors.ImageSharp.Image]$Image,
        [SixLabors.ImageSharp.Formats.IImageEncoder]$Encoder
    )

    if ($null -eq $Encoder) {
        [SixLabors.ImageSharp.ImageExtensions]::Save($Image, $Path, [SixLabors.ImageSharp.Formats.IImageEncoder]$Encoder)
    }
    else {
        [SixLabors.ImageSharp.ImageExtensions]::Save($Image, $Path, [SixLabors.ImageSharp.Formats.IImageEncoder]$pngEncoder)
    }

    return (Get-Item -Path $Path)
}
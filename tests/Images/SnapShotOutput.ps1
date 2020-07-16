. "$PSScriptRoot\Helpers.ps1" # include all the helper methods

Install-ImageSharp -Version "1.0.0-rc0003" -Source 'NuGet'

$pngEncoder = [SixLabors.ImageSharp.Formats.Png.PngEncoder]::new()
$pngEncoder.CompressionLevel = 'BestCompression'
$pngEncoder.FilterMethod = 'Adaptive'

$pngs = (Get-ChildItem "$PSScriptRoot\ActualOutput\*.png" -Recurse -File)

$compressedCounter = 0
$copiedCounter = 0
foreach ($sourceFile in $pngs) {
    $path = $sourceFile.FullName
    try {
        $img = Get-ImageSharpImage $path
        [string]$pathOutput = $path.Replace("\ActualOutput\", "\ReferenceOutput\")
        $newFile = $img | Set-ImageSharpImage -Path $pathOutput -Encoder $pngEncoder
        $oldFile = Get-Item  $path
        if ($newFile.Length -gt $oldFile.Length) {
            Copy-Item $oldFile -Destination $pathOutput
            $copiedCounter += 1
        }
        else {
            $compressedCounter += 1
        }
    }
    finally {
        $img.Dispose()
    }

}

$all = $copiedCounter + $compressedCounter
Write-Host "Copied     $copiedCounter"
Write-Host "Compressed $compressedCounter"
Write-Host "Processed  $all"

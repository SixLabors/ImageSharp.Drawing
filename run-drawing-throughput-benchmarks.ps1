<#
Runs a fixed set of DrawingThroughputBenchmark cases and emits the results as a
markdown pipe table.

Run from the repository root:

    .\run-drawing-throughput-benchmarks.ps1

The script writes the same markdown table to:

    .\drawing-throughput-benchmark-results.md

Scenario meanings:

- SingleImage:
  Keeps concurrent requests at 1 and varies drawing parallelism only. Use this
  to measure scaling of a single render request.
- ServiceThroughput:
  Varies the split between outer request concurrency and inner drawing
  parallelism. Use this to see which balance gives the best host throughput.

Notes:

- The case matrix lives in $cases below. Edit that list to add or remove runs.
- The script expects the benchmark to print TotalSeconds and MegaPixelsPerSec.
#>

$proj = "tests/ImageSharp.Drawing.ManualBenchmarks/ImageSharp.Drawing.ManualBenchmarks.csproj"
$outputPath = Join-Path $PSScriptRoot "drawing-throughput-benchmark-results.md"

# Fixed benchmark matrix for the two investigation modes described above.
$cases = @(
    [pscustomobject]@{
        Scenario = "SingleImage"
        Size = "Large"
        Width = 2000
        Height = 2000
        Seconds = 10
        ConcurrentRequests = 1
        Parallelism = 1
    }
    [pscustomobject]@{
        Scenario = "SingleImage"
        Size = "Large"
        Width = 2000
        Height = 2000
        Seconds = 10
        ConcurrentRequests = 1
        Parallelism = 8
    }
    [pscustomobject]@{
        Scenario = "SingleImage"
        Size = "Large"
        Width = 2000
        Height = 2000
        Seconds = 10
        ConcurrentRequests = 1
        Parallelism = 16
    }
    [pscustomobject]@{
        Scenario = "SingleImage"
        Size = "Small"
        Width = 200
        Height = 200
        Seconds = 10
        ConcurrentRequests = 1
        Parallelism = 1
    }
    [pscustomobject]@{
        Scenario = "SingleImage"
        Size = "Small"
        Width = 200
        Height = 200
        Seconds = 10
        ConcurrentRequests = 1
        Parallelism = 8
    }
    [pscustomobject]@{
        Scenario = "SingleImage"
        Size = "Small"
        Width = 200
        Height = 200
        Seconds = 10
        ConcurrentRequests = 1
        Parallelism = 16
    }
    [pscustomobject]@{
        Scenario = "ServiceThroughput"
        Size = "Large"
        Width = 2000
        Height = 2000
        Seconds = 10
        ConcurrentRequests = 8
        Parallelism = 1
    }
    [pscustomobject]@{
        Scenario = "ServiceThroughput"
        Size = "Large"
        Width = 2000
        Height = 2000
        Seconds = 10
        ConcurrentRequests = 4
        Parallelism = 2
    }
    [pscustomobject]@{
        Scenario = "ServiceThroughput"
        Size = "Large"
        Width = 2000
        Height = 2000
        Seconds = 10
        ConcurrentRequests = 2
        Parallelism = 4
    }
    [pscustomobject]@{
        Scenario = "ServiceThroughput"
        Size = "Large"
        Width = 2000
        Height = 2000
        Seconds = 10
        ConcurrentRequests = 1
        Parallelism = 8
    }
)

function Parse-Decimal([string]$text)
{
    # Benchmark output may use either decimal separator depending on locale.
    $normalized = $text.Replace(",", ".")
    return [double]::Parse($normalized, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Run-Case($case)
{
    Write-Host ("Running {0} {1}: {2}x{3}, c={4}, p={5}" -f `
        $case.Scenario, `
        $case.Size, `
        $case.Width, `
        $case.Height, `
        $case.ConcurrentRequests, `
        $case.Parallelism)

    $output = & dotnet run --project $proj -c Release -- `
        -m Tiger `
        -w $case.Width `
        -h $case.Height `
        -s $case.Seconds `
        -c $case.ConcurrentRequests `
        -p $case.Parallelism 2>&1

    $exitCode = $LASTEXITCODE
    $outputText = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine

    if ($exitCode -ne 0)
    {
        throw "Benchmark run failed with exit code $exitCode.$([Environment]::NewLine)$outputText"
    }

    $secondsMatch = [regex]::Match($outputText, "TotalSeconds:\s*([0-9]+(?:[.,][0-9]+)?)")
    $megaPixelsMatch = [regex]::Match($outputText, "MegaPixelsPerSec:\s*([0-9]+(?:[.,][0-9]+)?)")

    if (!$secondsMatch.Success -or !$megaPixelsMatch.Success)
    {
        throw "Failed to parse benchmark output.$([Environment]::NewLine)$outputText"
    }

    [pscustomobject]@{
        Scenario = $case.Scenario
        Size = $case.Size
        Width = $case.Width
        Height = $case.Height
        ConcurrentRequests = $case.ConcurrentRequests
        Parallelism = $case.Parallelism
        TotalSeconds = Parse-Decimal $secondsMatch.Groups[1].Value
        MegaPixelsPerSec = Parse-Decimal $megaPixelsMatch.Groups[1].Value
    }
}

$results = @()
foreach ($case in $cases)
{
    $results += Run-Case $case
}

$lines = @(
    "| Scenario | Size | Width | Height | Concurrent Requests | Drawing Parallelism | Total Seconds | MegaPixelsPerSec |"
    "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |"
)

foreach ($result in $results)
{
    $lines += ("| {0} | {1} | {2} | {3} | {4} | {5} | {6:F2} | {7:F2} |" -f `
        $result.Scenario, `
        $result.Size, `
        $result.Width, `
        $result.Height, `
        $result.ConcurrentRequests, `
        $result.Parallelism, `
        $result.TotalSeconds, `
        $result.MegaPixelsPerSec)
}

$markdown = $lines -join [Environment]::NewLine
Set-Content -Path $outputPath -Value $markdown

Write-Host ""
Write-Host $markdown
Write-Host ""
Write-Host "Saved markdown results to $outputPath"

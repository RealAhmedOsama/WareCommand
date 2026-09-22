[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:5188',
    [int]$Requests = 20,
    [int]$Concurrency = 4,
    [string]$Environment = 'LocalQualification',
    [string]$DatasetFingerprint = 'http-smoke-v1',
    [string]$EvidencePath,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$endpoints = @('/health/live', '/manifest.webmanifest')

if ($PlanOnly) {
    Write-Output 'performance-profile=local-http-smoke-v1'
    Write-Output "requests=$Requests"
    Write-Output "concurrency=$Concurrency"
    foreach ($endpoint in $endpoints) {
        Write-Output "endpoint=$endpoint"
    }
    exit 0
}

if ($Requests -lt 1 -or $Requests -gt 10000) {
    throw 'Requests must be between 1 and 10000.'
}

if ($Concurrency -lt 1 -or $Concurrency -gt 128) {
    throw 'Concurrency must be between 1 and 128.'
}

if ($Environment -match '^(production|staging)$') {
    throw 'This local qualification runner refuses Production and Staging environments.'
}

$baseUri = [Uri]::new($BaseUrl)
if (-not $baseUri.IsAbsoluteUri -or $baseUri.UserInfo -or $baseUri.Query -or $baseUri.Fragment) {
    throw 'BaseUrl must be an absolute URL without credentials, query strings, or fragments.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$startedAt = [DateTimeOffset]::UtcNow
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$baseUrlRoot = $BaseUrl.TrimEnd('/')
$records = @(
    1..$Requests | ForEach-Object -Parallel {
        $routes = $using:endpoints
        $root = $using:baseUrlRoot
        $endpoint = $routes[($_ - 1) % $routes.Count]
        $timer = [System.Diagnostics.Stopwatch]::StartNew()
        $statusCode = 0
        $succeeded = $false
        $errorType = $null
        try {
            $response = Invoke-WebRequest -Uri "$root$endpoint" -Method Get -TimeoutSec 30 -UseBasicParsing
            $statusCode = [int]$response.StatusCode
            $succeeded = $statusCode -ge 200 -and $statusCode -lt 400
        }
        catch {
            $errorType = $_.Exception.GetType().Name
        }
        finally {
            $timer.Stop()
        }

        [pscustomobject]@{
            endpoint = $endpoint
            statusCode = $statusCode
            succeeded = $succeeded
            durationMilliseconds = [math]::Round($timer.Elapsed.TotalMilliseconds, 3)
            errorType = $errorType
        }
    } -ThrottleLimit $Concurrency
)
$stopwatch.Stop()
$completedAt = [DateTimeOffset]::UtcNow
$successfulRecordCount = @($records | Where-Object succeeded).Count
if ($successfulRecordCount -eq 0) {
    throw 'No successful local samples were recorded; refusing to report a green performance run.'
}

function Get-Percentile {
    param(
        [Parameter(Mandatory)][double[]]$Values,
        [Parameter(Mandatory)][double]$Percentile
    )

    if ($Values.Count -eq 0) {
        return 0
    }

    $sorted = @($Values | Sort-Object)
    $index = [math]::Ceiling(($Percentile / 100) * $sorted.Count) - 1
    $index = [math]::Max(0, [math]::Min($sorted.Count - 1, $index))
    return [math]::Round([double]$sorted[$index], 3)
}

$results = @(
    foreach ($endpoint in $endpoints) {
        $items = @($records | Where-Object endpoint -eq $endpoint)
        $durations = [double[]]@($items | ForEach-Object durationMilliseconds)
        $successes = @($items | Where-Object succeeded)
        [pscustomobject]@{
            endpoint = $endpoint
            sampleCount = $items.Count
            successCount = $successes.Count
            errorCount = $items.Count - $successes.Count
            p50Milliseconds = Get-Percentile -Values $durations -Percentile 50
            p95Milliseconds = Get-Percentile -Values $durations -Percentile 95
            p99Milliseconds = Get-Percentile -Values $durations -Percentile 99
            errorTypes = @($items | Where-Object { -not $_.succeeded -and $_.errorType } |
                Select-Object -ExpandProperty errorType -Unique)
        }
    }
)

$revision = (& git -C $repositoryRoot rev-parse HEAD 2>$null | Out-String).Trim()
$hardwareProfile = "windows-$([Environment]::ProcessorCount)cpu"
$evidence = [pscustomobject]@{
    schema = 'wms-performance-local-v1'
    metadata = [pscustomobject]@{
        environment = $Environment
        datasetFingerprint = $DatasetFingerprint
        hardwareProfile = $hardwareProfile
        applicationRevision = $revision
        sampleCount = $Requests
        concurrency = $Concurrency
        startedAtUtc = $startedAt
        completedAtUtc = $completedAt
    }
    baseUrl = $baseUri.GetLeftPart([System.UriPartial]::Authority)
    elapsedMilliseconds = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
    throughputPerSecond = if ($stopwatch.Elapsed.TotalSeconds -gt 0) {
        [math]::Round($successfulRecordCount / $stopwatch.Elapsed.TotalSeconds, 3)
    }
    else {
        0
    }
    routes = $results
    payloadCapture = 'none'
}

$json = $evidence | ConvertTo-Json -Depth 8
$byteCount = [Text.Encoding]::UTF8.GetByteCount($json)
if ($byteCount -gt 1000000) {
    throw "Performance evidence exceeded the 1000000-byte bound: $byteCount."
}

Write-Output $json
if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $evidenceDirectory = Split-Path -Parent $EvidencePath
    if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
    }
    Set-Content -Path $EvidencePath -Value $json -Encoding utf8
}

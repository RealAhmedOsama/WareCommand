$ErrorActionPreference = 'Stop'

$script = Join-Path $PSScriptRoot 'verify-performance-qualification.ps1'
$output = & pwsh -NoProfile -File $script -PlanOnly 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Performance plan contract failed to execute: $output"
}

foreach ($required in @(
        'performance-profile=local-http-smoke-v1',
        'requests=20',
        'concurrency=4',
        'endpoint=/health/live',
        'endpoint=/manifest.webmanifest')) {
    if ($output -notmatch [regex]::Escape($required)) {
        throw "Performance plan is missing '$required'. Output: $output"
    }
}

$source = Get-Content -Raw $script
if ($source -notmatch 'payloadCapture = ''none''') {
    throw 'Performance runner must declare that response payloads are not captured.'
}
if ($source -notmatch 'ForEach-Object -Parallel') {
    throw 'Performance runner must use bounded parallel execution.'
}

Write-Host 'Performance qualification contract passed.'

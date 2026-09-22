$ErrorActionPreference = 'Stop'

$script = Join-Path $PSScriptRoot 'verify-resilience-qualification.ps1'
$output = & pwsh -NoProfile -File $script -PlanOnly 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Resilience plan contract failed to execute: $output"
}

foreach ($required in @(
        'resilience-profile=local-contract-v1',
        'environment=LocalQualification',
        'scenario=timeout-after-commit',
        'scenario=worker-restart-durable-job',
        'scenario=provider-permanent-failure-dead-letter',
        'scenario=backup-restore-reconcile')) {
    if ($output -notmatch [regex]::Escape($required)) {
        throw "Resilience plan is missing '$required'. Output: $output"
    }
}

$productionOutput = & pwsh -NoProfile -File $script -Environment Production 2>&1 | Out-String
if ($LASTEXITCODE -eq 0 -or $productionOutput -notmatch 'refuses Production') {
    throw 'Resilience runner must refuse Production.'
}

Write-Host 'Resilience qualification contract passed.'

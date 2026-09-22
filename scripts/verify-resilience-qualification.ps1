[CmdletBinding()]
param(
    [string]$Environment = 'LocalQualification',
    [string]$DatasetFingerprint = 'resilience-contract-v1',
    [string]$EvidencePath,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$scenarios = @(
    [pscustomobject]@{ id = 'timeout-after-commit'; recovery = 'IdempotentReplay'; classification = 'Ambiguous'; maxRetries = 1 }
    [pscustomobject]@{ id = 'worker-restart-durable-job'; recovery = 'ResumeDurableWork'; classification = 'Retryable'; maxRetries = 3 }
    [pscustomobject]@{ id = 'provider-permanent-failure-dead-letter'; recovery = 'DeadLetter'; classification = 'Permanent'; maxRetries = 0 }
    [pscustomobject]@{ id = 'backup-restore-reconcile'; recovery = 'RestoreAndReconcile'; classification = 'DependencyUnavailable'; maxRetries = 0 }
)

if ($PlanOnly) {
    Write-Output 'resilience-profile=local-contract-v1'
    Write-Output "environment=$Environment"
    foreach ($scenario in $scenarios) {
        Write-Output "scenario=$($scenario.id)"
    }
    exit 0
}

if ($Environment -match '^(production|staging)$') {
    throw 'This resilience runner refuses Production and Staging environments.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot 'Wms.Application.Tests/Wms.Application.Tests.csproj'
$testOutput = & dotnet test $testProject -c Release --no-restore --logger 'console;verbosity=minimal' --filter 'FullyQualifiedName~ResiliencePolicyTests' 2>&1 | Out-String
$testExitCode = $LASTEXITCODE
if ($testExitCode -ne 0) {
    throw "Resilience policy tests failed with exit code $testExitCode."
}

$summaryMatch = [regex]::Match($testOutput, 'Passed!\s+- Failed:\s+(?<failed>\d+), Passed:\s+(?<passed>\d+), Skipped:\s+(?<skipped>\d+), Total:\s+(?<total>\d+)')
if (-not $summaryMatch.Success -or [int]$summaryMatch.Groups['failed'].Value -ne 0) {
    throw 'Resilience policy test output did not contain a zero-failure summary.'
}

$revision = (& git -C $repositoryRoot rev-parse HEAD 2>$null | Out-String).Trim()
$evidence = [pscustomobject]@{
    schema = 'wms-resilience-local-v1'
    metadata = [pscustomobject]@{
        environment = $Environment
        datasetFingerprint = $DatasetFingerprint
        applicationRevision = $revision
        faultInjectionMode = 'contract-only'
        productionFaultInjectionEnabled = $false
    }
    scenarios = $scenarios
    contractTests = [pscustomobject]@{
        passed = [int]$summaryMatch.Groups['passed'].Value
        failed = [int]$summaryMatch.Groups['failed'].Value
        skipped = [int]$summaryMatch.Groups['skipped'].Value
        total = [int]$summaryMatch.Groups['total'].Value
    }
    cleanup = 'no runtime database, provider, queue, or external artifact was created'
    externalGates = @(
        'disposable PostgreSQL timeout/conflict and restart hooks',
        'provider/webhook/storage/printer fault injection',
        'backup restore rehearsal and operator reconciliation'
    )
}

$json = $evidence | ConvertTo-Json -Depth 8
if ([Text.Encoding]::UTF8.GetByteCount($json) -gt 1000000) {
    throw 'Resilience evidence exceeded the 1000000-byte bound.'
}

Write-Output $testOutput.Trim()
Write-Output $json
if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $evidenceDirectory = Split-Path -Parent $EvidencePath
    if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
    }
    Set-Content -Path $EvidencePath -Value $json -Encoding utf8
}

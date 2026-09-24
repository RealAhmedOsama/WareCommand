[CmdletBinding()]
param(
    [string]$Environment = 'LocalQualification',
    [string]$DatasetFingerprint = 'resilience-runtime-v2',
    [int]$Port = 55432,
    [string]$EvidencePath,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$scenarios = @(
    [pscustomobject]@{ id = 'work-command-response-loss-after-commit'; recovery = 'IdempotentReplay'; classification = 'Ambiguous'; maxRetries = 1 }
    [pscustomobject]@{ id = 'provider-response-loss-after-commit'; recovery = 'IdempotentReplay'; classification = 'Ambiguous'; maxRetries = 1 }
    [pscustomobject]@{ id = 'worker-restart-durable-outbox'; recovery = 'ResumeDurableWork'; classification = 'Retryable'; maxRetries = 3 }
    [pscustomobject]@{ id = 'transient-postgresql-failure'; recovery = 'ReclaimAndResume'; classification = 'Retryable'; maxRetries = 2 }
    [pscustomobject]@{ id = 'provider-dead-letter-authorized-replay'; recovery = 'AuditAndReplay'; classification = 'Permanent'; maxRetries = 2 }
    [pscustomobject]@{ id = 'populated-backup-restore-reconcile'; recovery = 'RestoreAndReconcile'; classification = 'DependencyUnavailable'; maxRetries = 0 }
)

if ($PlanOnly) {
    Write-Output 'resilience-profile=local-runtime-v2'
    Write-Output "environment=$Environment"
    Write-Output "postgres-port=$Port"
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
$runtimeRunner = Join-Path $PSScriptRoot 'verify-postgresql.ps1'
$testOutput = & dotnet test $testProject -c Release --no-restore --logger 'console;verbosity=minimal' --filter 'FullyQualifiedName~ResiliencePolicyTests' 2>&1 | Out-String
$testExitCode = $LASTEXITCODE
if ($testExitCode -ne 0) {
    throw "Resilience policy tests failed with exit code $testExitCode."
}

$summaryMatch = [regex]::Match($testOutput, 'Passed!\s+- Failed:\s+(?<failed>\d+), Passed:\s+(?<passed>\d+), Skipped:\s+(?<skipped>\d+), Total:\s+(?<total>\d+)')
if (-not $summaryMatch.Success -or [int]$summaryMatch.Groups['failed'].Value -ne 0) {
    throw 'Resilience policy test output did not contain a zero-failure summary.'
}

$runtimeOutput = & $runtimeRunner -Group resilience -Port $Port 2>&1 | Out-String
$runtimeExitCode = $LASTEXITCODE
if ($runtimeExitCode -ne 0) {
    throw "Disposable PostgreSQL runtime resilience qualification failed with exit code $runtimeExitCode.`n$runtimeOutput"
}

$runtimeSchemaMarker = '"schema": "wms-postgresql-provider-v1"'
$runtimeSchemaIndex = $runtimeOutput.LastIndexOf($runtimeSchemaMarker, [StringComparison]::Ordinal)
if ($runtimeSchemaIndex -lt 0) {
    throw 'PostgreSQL runtime qualification output did not include provider evidence.'
}
$runtimeJsonStart = $runtimeOutput.LastIndexOf('{', $runtimeSchemaIndex)
if ($runtimeJsonStart -lt 0) {
    throw 'PostgreSQL runtime qualification evidence was malformed.'
}
try {
    $runtimeProviderEvidence = $runtimeOutput.Substring($runtimeJsonStart).Trim() | ConvertFrom-Json
}
catch {
    throw 'PostgreSQL runtime qualification evidence could not be parsed.'
}
if ($runtimeProviderEvidence.group -ne 'resilience' -or
    @($runtimeProviderEvidence.resilienceReports).Count -ne 3) {
    throw 'PostgreSQL runtime evidence did not contain all three resilience rehearsals.'
}

$revision = (& git -C $repositoryRoot rev-parse HEAD 2>$null | Out-String).Trim()
$evidence = [pscustomobject]@{
    schema = 'wms-resilience-local-v1'
    metadata = [pscustomobject]@{
        environment = $Environment
        datasetFingerprint = $DatasetFingerprint
        applicationRevision = $revision
        faultInjectionMode = 'disposable PostgreSQL plus test-only loopback HTTP provider'
        productionFaultInjectionEnabled = $false
    }
    scenarios = $scenarios
    contractTests = [pscustomobject]@{
        passed = [int]$summaryMatch.Groups['passed'].Value
        failed = [int]$summaryMatch.Groups['failed'].Value
        skipped = [int]$summaryMatch.Groups['skipped'].Value
        total = [int]$summaryMatch.Groups['total'].Value
    }
    runtimeProviderEvidence = $runtimeProviderEvidence
    boundaries = @(
        'production RTO/RPO and real-data recovery were not measured',
        'production credentials, key storage, offsite copies, and provider infrastructure were not used'
    )
    cleanup = $runtimeProviderEvidence.cleanup
}

$json = $evidence | ConvertTo-Json -Depth 8
if ([Text.Encoding]::UTF8.GetByteCount($json) -gt 1000000) {
    throw 'Resilience evidence exceeded the 1000000-byte bound.'
}

Write-Output $testOutput.Trim()
Write-Output $runtimeOutput.Trim()
Write-Output $json
if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $evidenceDirectory = Split-Path -Parent $EvidencePath
    if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
    }
    Set-Content -Path $EvidencePath -Value $json -Encoding utf8
}

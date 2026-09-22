[CmdletBinding()]
param(
    [string]$Environment = 'LocalQualification',
    [string]$EvidencePath,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$gates = @(
    [pscustomobject]@{ name = 'release-preflight'; script = 'scripts/verify-release-preflight.ps1' }
    [pscustomobject]@{ name = 'security-boundaries'; script = 'scripts/verify-security-boundaries.ps1' }
    [pscustomobject]@{ name = 'migration-lifecycle'; script = 'scripts/verify-migrations.ps1' }
    [pscustomobject]@{ name = 'support-and-security-tests'; script = $null }
)

if ($PlanOnly) {
    Write-Output 'evidence-packet-schema=wms-release-local-v1'
    Write-Output "environment=$Environment"
    foreach ($gate in $gates) {
        Write-Output "gate=$($gate.name)"
    }
    Write-Output 'decision=GO_WITH_RESTRICTIONS'
    exit 0
}

if ($Environment -match '^(production|staging)$') {
    throw 'This local evidence packet refuses Production and Staging environments.'
}

function Invoke-Gate {
    param([Parameter(Mandatory)][string]$Name, [string]$ScriptPath)

    if ([string]::IsNullOrWhiteSpace($ScriptPath)) {
        $project = Join-Path $repositoryRoot 'Wms.Application.Tests/Wms.Application.Tests.csproj'
        $output = & dotnet test $project -c Release --no-restore --logger 'console;verbosity=minimal' --filter 'FullyQualifiedName~SupportBundlePolicyTests|FullyQualifiedName~SecurityBoundaryTests' 2>&1 | Out-String
    }
    else {
        $output = & pwsh -NoProfile -File (Join-Path $repositoryRoot $ScriptPath) 2>&1 | Out-String
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Evidence gate '$Name' failed."
    }

    [pscustomobject]@{
        name = $Name
        status = 'passed'
    }
}

$gateResults = @(
    foreach ($gate in $gates) {
        Invoke-Gate -Name $gate.name -ScriptPath $gate.script
    }
)

$revision = (& git -C $repositoryRoot rev-parse HEAD 2>$null | Out-String).Trim()
$packet = [ordered]@{
    schema = 'wms-release-local-v1'
    decision = 'GO_WITH_RESTRICTIONS'
    metadata = [ordered]@{
        environment = $Environment
        applicationRevision = $revision
        generatedAtUtc = [DateTimeOffset]::UtcNow
    }
    gates = $gateResults
    redaction = [ordered]@{
        responsePayloads = 'not captured'
        credentials = 'not captured'
        cookies = 'not captured'
        bearerTokens = 'not captured'
        dataProtectionKeys = 'not captured'
        databaseDumps = 'not captured'
    }
    restrictions = @(
        'No production deployment, migration, or rollback was performed.'
        'Backup artifact/restore verification and target-environment readiness remain owner gates.'
        'Live provider, device/printer, independent security review, and capacity approval remain external gates.'
    )
}

$json = $packet | ConvertTo-Json -Depth 8
if ([Text.Encoding]::UTF8.GetByteCount($json) -gt 1000000) {
    throw 'Release evidence packet exceeded the 1000000-byte bound.'
}

Write-Output $json
if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $evidenceDirectory = Split-Path -Parent $EvidencePath
    if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
    }
    Set-Content -Path $EvidencePath -Value $json -Encoding utf8
}

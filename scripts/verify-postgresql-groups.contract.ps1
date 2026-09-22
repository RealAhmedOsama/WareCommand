$ErrorActionPreference = 'Stop'

$script = Join-Path $PSScriptRoot 'verify-postgresql.ps1'
$output = & pwsh -NoProfile -File $script -PlanOnly 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "PostgreSQL group plan contract failed to execute: $output"
}

foreach ($required in @(
        'provider-group=core',
        'provider-group=harness',
        'filter=FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests',
        'filter=FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests_Harness')) {
    if ($output -notmatch [regex]::Escape($required)) {
        throw "PostgreSQL group plan is missing '$required'. Output: $output"
    }
}

Write-Host 'PostgreSQL group plan contract passed.'

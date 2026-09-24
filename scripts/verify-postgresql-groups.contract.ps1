$ErrorActionPreference = 'Stop'

$script = Join-Path $PSScriptRoot 'verify-postgresql.ps1'
$output = & pwsh -NoProfile -File $script -PlanOnly 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "PostgreSQL group plan contract failed to execute: $output"
}

foreach ($required in @(
        'provider-group=core',
    'provider-group=harness',
        'provider-group=dashboard',
        'provider-group=data-generation',
        'provider-group=journeys',
        'provider-group=resilience',
        'provider-group=browser',
        'filter=FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests',
        'filter=FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests_Harness',
        'filter=FullyQualifiedName~Wms.ASP.Tests.PostgreSqlDashboardFlowTests',
        'filter=FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlDataGenerationTests',
        'filter=FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlJourneyTests',
        'filter=FullyQualifiedName~Wms.ASP.Tests.PostgreSqlRuntimeResilienceTests',
        'filter=FullyQualifiedName~Wms.ASP.Tests.PostgreSqlBrowserJourneyTests')) {
    if ($output -notmatch [regex]::Escape($required)) {
        throw "PostgreSQL group plan is missing '$required'. Output: $output"
    }
}

if ($output -match 'provider-group=performance') {
    throw 'The full PostgreSQL performance qualification must stay outside the default provider group.'
}

$performanceOutput = & pwsh -NoProfile -File $script -PlanOnly -Group performance -PerformanceSamples 20 -PerformanceRepeats 2 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "PostgreSQL performance plan contract failed to execute: $performanceOutput"
}
foreach ($required in @(
        'provider-group=performance',
        'filter=FullyQualifiedName~Wms.ASP.Tests.PostgreSqlPerformanceQualificationTests',
        'performance-repeats=2',
        'performance-samples-per-level=20',
        'extended-contention=False')) {
    if ($performanceOutput -notmatch [regex]::Escape($required)) {
        throw "PostgreSQL performance plan is missing '$required'. Output: $performanceOutput"
    }
}

Write-Host 'PostgreSQL group plan contract passed.'

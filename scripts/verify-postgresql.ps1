[CmdletBinding()]
param(
    [int]$Port = 55432,
    [string]$ContainerName = "warecommand-postgres-test-$([Guid]::NewGuid().ToString('N'))",
    [ValidateSet('all', 'core', 'harness', 'dashboard', 'data-generation')]
    [string]$Group = 'all',
    [switch]$PlanOnly,
    [string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Get-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )

    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd([char[]]@('\', '/'))
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPrefix = $fullRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Provider test project '$fullPath' is outside the repository root."
    }

    return $fullPath.Substring($rootPrefix.Length).Replace('\', '/')
}

$testProject = Join-Path $repositoryRoot 'Wms.Infrastructure.Tests/Wms.Infrastructure.Tests.csproj'
$dashboardTestProject = Join-Path $repositoryRoot 'Wms.ASP.Tests/Wms.ASP.Tests.csproj'
$providerGroups = [ordered]@{
    core = [pscustomobject]@{
        testProject = $testProject
        filter = 'FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests&FullyQualifiedName!~_Harness'
        expectedClass = 'Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests.'
    }
    harness = [pscustomobject]@{
        testProject = $testProject
        filter = 'FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests_Harness'
        expectedClass = 'Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests_Harness.'
    }
    dashboard = [pscustomobject]@{
        testProject = $dashboardTestProject
        filter = 'FullyQualifiedName~Wms.ASP.Tests.PostgreSqlDashboardFlowTests'
        expectedClass = 'Wms.ASP.Tests.PostgreSqlDashboardFlowTests.'
    }
    'data-generation' = [pscustomobject]@{
        testProject = $testProject
        filter = 'FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlDataGenerationTests'
        expectedClass = 'Wms.Infrastructure.Tests.Integration.PostgreSqlDataGenerationTests.'
    }
}
$selectedGroupNames = if ($Group -eq 'all') { @('core', 'harness', 'dashboard', 'data-generation') } else { @($Group) }

if ($PlanOnly) {
    foreach ($groupName in $selectedGroupNames) {
        Write-Output "provider-group=$groupName"
        Write-Output "test-project=$($providerGroups[$groupName].testProject)"
        Write-Output "filter=$($providerGroups[$groupName].filter)"
    }
    exit 0
}

$previousConnectionString = $env:WARECOMMAND_TEST_POSTGRES_CONNECTION
$previousDataGenerationEvidencePath = $env:WARECOMMAND_DATA_GENERATION_EVIDENCE_PATH
$password = $env:WARECOMMAND_TEST_POSTGRES_PASSWORD
$locationPushed = $false
$containerStarted = $false
$dataGenReportPath = $null
if ($selectedGroupNames -contains 'data-generation') {
    $dataGenReportPath = Join-Path ([System.IO.Path]::GetTempPath()) "warecommand-data-generation-$([Guid]::NewGuid().ToString('N')).json"
    $env:WARECOMMAND_DATA_GENERATION_EVIDENCE_PATH = $dataGenReportPath
}
if ([string]::IsNullOrWhiteSpace($password)) {
    $password = [Guid]::NewGuid().ToString('N')
}

function Invoke-Docker {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker is required for PostgreSQL integration verification.'
    }

    Push-Location $repositoryRoot
    $locationPushed = $true

    $existingContainers = @(& docker ps --all --format '{{.Names}}')
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect Docker containers before PostgreSQL verification.'
    }

    if ($existingContainers -contains $ContainerName) {
        throw "Refusing to reuse or remove existing Docker container '$ContainerName'."
    }

    Invoke-Docker @(
        'run', '--detach', '--rm', '--name', $ContainerName,
        '--env', 'POSTGRES_DB=warecommand_test',
        '--env', 'POSTGRES_USER=warecommand_test',
        '--env', "POSTGRES_PASSWORD=$password",
        '--publish', "$Port`:5432",
        'postgres:17'
    ) | Out-Null
    $containerStarted = $true

    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Seconds 1
        $health = (& docker exec $ContainerName pg_isready -U warecommand_test -d warecommand_test 2>$null | Out-String).Trim()
        if ($LASTEXITCODE -eq 0 -and $health -match 'accepting connections') {
            $ready = $true
            break
        }
    }

    if (-not $ready) {
        throw "PostgreSQL container did not become ready: $ContainerName"
    }

    $env:WARECOMMAND_TEST_POSTGRES_CONNECTION =
        "Host=127.0.0.1;Port=$Port;Database=warecommand_test;Username=warecommand_test;Password=$password"

    $results = @()
    foreach ($groupName in $selectedGroupNames) {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $groupSpec = $providerGroups[$groupName]
        $groupTestProject = $groupSpec.testProject
        $filter = $groupSpec.filter
        $listedTests = (& dotnet test $groupTestProject -c Release --no-restore --list-tests --logger 'console;verbosity=minimal' --filter $filter 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL integration group '$groupName' could not enumerate tests."
        }
        if ($listedTests -notmatch [regex]::Escape($groupSpec.expectedClass)) {
            throw "PostgreSQL integration group '$groupName' selected no tests for '$filter'."
        }

        & dotnet test $groupTestProject -c Release --no-restore --logger 'console;verbosity=minimal' --filter $filter
        $exitCode = $LASTEXITCODE
        $stopwatch.Stop()
        if ($exitCode -ne 0) {
            throw "PostgreSQL integration group '$groupName' failed with exit code $exitCode."
        }

        $results += [pscustomobject]@{
            group = $groupName
            testProject = (Get-RepositoryRelativePath -Root $repositoryRoot -Path $groupTestProject)
            filter = $filter
            durationSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
            exitCode = $exitCode
        }
    }

    $revision = (& git rev-parse HEAD 2>$null | Out-String).Trim()
    $dataGenerationReports = @()
    if ($null -ne $dataGenReportPath -and (Test-Path -LiteralPath $dataGenReportPath -PathType Leaf)) {
        $dataGenerationEvidence = Get-Content -LiteralPath $dataGenReportPath -Raw | ConvertFrom-Json
        $dataGenerationReports = @($dataGenerationEvidence.reports)
    }
    $evidence = [pscustomobject]@{
        schema = 'wms-postgresql-provider-v1'
        revision = $revision
        postgresImage = 'postgres:17'
        group = $Group
        groups = $results
        dataGenerationReports = $dataGenerationReports
        cleanup = 'owned container removed in finally; fixture-owned schema dropped by test fixture'
    }
    $json = $evidence | ConvertTo-Json -Depth 6
    Write-Output $json
    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $evidenceDirectory = Split-Path -Parent $EvidencePath
        if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
            New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
        }
        Set-Content -Path $EvidencePath -Value $json -Encoding utf8
    }
}
finally {
    if ($containerStarted) {
        & docker rm --force $ContainerName 2>$null | Out-Null
    }
    if ($null -ne $previousConnectionString) {
        $env:WARECOMMAND_TEST_POSTGRES_CONNECTION = $previousConnectionString
    }
    else {
        Remove-Item Env:WARECOMMAND_TEST_POSTGRES_CONNECTION -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousDataGenerationEvidencePath) {
        $env:WARECOMMAND_DATA_GENERATION_EVIDENCE_PATH = $previousDataGenerationEvidencePath
    }
    else {
        Remove-Item Env:WARECOMMAND_DATA_GENERATION_EVIDENCE_PATH -ErrorAction SilentlyContinue
    }
    if ($null -ne $dataGenReportPath) {
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
        $fullReportPath = [System.IO.Path]::GetFullPath($dataGenReportPath)
        if ($fullReportPath.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($fullReportPath).StartsWith('warecommand-data-generation-', [System.StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath $fullReportPath -PathType Leaf)) {
            Remove-Item -LiteralPath $fullReportPath -Force -ErrorAction SilentlyContinue
        }
    }
    if ($locationPushed) {
        Pop-Location
    }
}

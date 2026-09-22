[CmdletBinding()]
param(
    [int]$Port = 55432,
    [string]$ContainerName = "warecommand-postgres-test-$([Guid]::NewGuid().ToString('N'))",
    [ValidateSet('all', 'core', 'harness')]
    [string]$Group = 'all',
    [switch]$PlanOnly,
    [string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot 'Wms.Infrastructure.Tests/Wms.Infrastructure.Tests.csproj'
$providerGroups = [ordered]@{
    core = [pscustomobject]@{
        filter = 'FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests&FullyQualifiedName!~_Harness'
        expectedClass = 'Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests.'
    }
    harness = [pscustomobject]@{
        filter = 'FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests_Harness'
        expectedClass = 'Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests_Harness.'
    }
}
$selectedGroupNames = if ($Group -eq 'all') { @('core', 'harness') } else { @($Group) }

if ($PlanOnly) {
    foreach ($groupName in $selectedGroupNames) {
        Write-Output "provider-group=$groupName"
        Write-Output "filter=$($providerGroups[$groupName].filter)"
    }
    exit 0
}

$previousConnectionString = $env:WARECOMMAND_TEST_POSTGRES_CONNECTION
$password = $env:WARECOMMAND_TEST_POSTGRES_PASSWORD
$locationPushed = $false
$containerStarted = $false
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
        $filter = $groupSpec.filter
        $listedTests = (& dotnet test $testProject -c Release --no-restore --list-tests --logger 'console;verbosity=minimal' --filter $filter 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL integration group '$groupName' could not enumerate tests."
        }
        if ($listedTests -notmatch [regex]::Escape($groupSpec.expectedClass)) {
            throw "PostgreSQL integration group '$groupName' selected no tests for '$filter'."
        }

        & dotnet test $testProject -c Release --no-restore --logger 'console;verbosity=minimal' --filter $filter
        $exitCode = $LASTEXITCODE
        $stopwatch.Stop()
        if ($exitCode -ne 0) {
            throw "PostgreSQL integration group '$groupName' failed with exit code $exitCode."
        }

        $results += [pscustomobject]@{
            group = $groupName
            filter = $filter
            durationSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
            exitCode = $exitCode
        }
    }

    $revision = (& git rev-parse HEAD 2>$null | Out-String).Trim()
    $evidence = [pscustomobject]@{
        schema = 'wms-postgresql-provider-v1'
        revision = $revision
        postgresImage = 'postgres:17'
        group = $Group
        groups = $results
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
    if ($locationPushed) {
        Pop-Location
    }
}

[CmdletBinding()]
param(
    [int]$Port = 55432,
    [string]$ContainerName = "warecommand-postgres-test-$([Guid]::NewGuid().ToString('N'))"
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot 'Wms.Infrastructure.Tests/Wms.Infrastructure.Tests.csproj'
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

    & dotnet test $testProject -c Release --no-restore --logger 'console;verbosity=minimal' --filter 'FullyQualifiedName~PostgreSqlIntegrationTests'
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL integration tests failed with exit code $LASTEXITCODE."
    }

    Write-Host "PostgreSQL integration verification passed using container $ContainerName."
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

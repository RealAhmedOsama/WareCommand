[CmdletBinding()]
param(
    [string]$ConnectionString = $env:WARECOMMAND_POSTGRES_CONNECTION,
    [switch]$Apply,
    [string]$OutputScript = 'artifacts/migrations/postgresql-idempotent.sql'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$previousConnectionString = $env:WARECOMMAND_POSTGRES_CONNECTION
$locationPushed = $false

function Invoke-DotnetEf {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet ef @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet ef $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        throw 'A PostgreSQL connection string is required through -ConnectionString or WARECOMMAND_POSTGRES_CONNECTION.'
    }

    Push-Location $repositoryRoot
    $locationPushed = $true
    $env:WARECOMMAND_POSTGRES_CONNECTION = $ConnectionString

    $commonArguments = @(
        '--project', 'Wms.Infrastructure/Wms.Infrastructure.csproj',
        '--startup-project', 'Wms.ASP/Wms.ASP.csproj',
        '--context', 'WmsDbContext'
    )

    if ($Apply) {
        Invoke-DotnetEf (@('database', 'update') + $commonArguments)
        Write-Host 'PostgreSQL migrations applied. Application startup will now verify the schema and refuse pending migrations.'
    }
    else {
        $outputPath = [System.IO.Path]::GetFullPath($OutputScript)
        $outputDirectory = Split-Path -Parent $outputPath
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
            New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
        }

        Invoke-DotnetEf (@('migrations', 'script', '--idempotent', '--output', $outputPath) + $commonArguments)
        Write-Host "Idempotent PostgreSQL migration script written to $outputPath. Review it before applying."
    }
}
finally {
    if ($null -ne $previousConnectionString) {
        $env:WARECOMMAND_POSTGRES_CONNECTION = $previousConnectionString
    }
    else {
        Remove-Item Env:WARECOMMAND_POSTGRES_CONNECTION -ErrorAction SilentlyContinue
    }
    if ($locationPushed) {
        Pop-Location
    }
}

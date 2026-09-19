[CmdletBinding()]
param()

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
    Push-Location $repositoryRoot
    $locationPushed = $true
    if ([string]::IsNullOrWhiteSpace($env:WARECOMMAND_POSTGRES_CONNECTION)) {
        $env:WARECOMMAND_POSTGRES_CONNECTION = 'Host=localhost;Database=warecommand;Username=warecommand;Password=ci-placeholder'
    }

    $commonArguments = @(
        '--project', 'Wms.Infrastructure/Wms.Infrastructure.csproj',
        '--startup-project', 'Wms.ASP/Wms.ASP.csproj',
        '--context', 'WmsDbContext'
    )

    Invoke-DotnetEf (@('migrations', 'has-pending-model-changes') + $commonArguments)

    $migrationFiles = Get-ChildItem -LiteralPath 'Wms.Infrastructure/Database/Migrations' -Filter '*.cs' |
        Where-Object Name -notlike '*ModelSnapshot.cs'
    if ($migrationFiles.Count -eq 0) {
        throw 'No checked-in EF migration files were found.'
    }

    $initializer = Get-Content -LiteralPath 'Wms.Infrastructure/Database/WmsDatabaseInitializer.cs' -Raw
    if ($initializer.Contains('Database.MigrateAsync', [StringComparison]::Ordinal)) {
        throw 'PostgreSQL application startup must not apply migrations implicitly.'
    }

    Write-Host 'Migration lifecycle verification passed.'
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

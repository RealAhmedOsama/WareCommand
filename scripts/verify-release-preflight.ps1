[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$locationPushed = $false

function Assert-File {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required release-qualification file '$Path' is missing."
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Needle
    )

    $content = Get-Content -LiteralPath $Path -Raw
    if ($content.IndexOf($Needle, [StringComparison]::Ordinal) -lt 0) {
        throw "Release contract '$Needle' was not found in '$Path'."
    }
}

try {
    Push-Location $repositoryRoot
    $locationPushed = $true

    @(
        'DEPLOYMENT.md',
        'SECURITY.md',
        'docker-compose.yml',
        'Wms.Infrastructure/Database/Migrations',
        'scripts/backup-postgresql.ps1',
        'scripts/restore-postgresql.ps1',
        'scripts/migrate-postgresql.ps1',
        'scripts/verify-migrations.ps1',
        'docs/operations/BACKUP_DISASTER_RECOVERY.md',
        'docs/operations/RELEASE_QUALIFICATION.md'
    ) | ForEach-Object {
        if (Test-Path -LiteralPath $_ -PathType Container) {
            return
        }

        Assert-File $_
    }

    Assert-Contains 'DEPLOYMENT.md' 'The web host does not apply migrations on startup.'
    Assert-Contains 'DEPLOYMENT.md' 'backup-postgresql.ps1'
    Assert-Contains 'DEPLOYMENT.md' 'health/ready'
    Assert-Contains 'Wms.Infrastructure/Database/WmsDatabaseInitializer.cs' 'pending'
    Assert-Contains 'SECURITY.md' 'secret manager'

    $migrationFiles = Get-ChildItem -LiteralPath 'Wms.Infrastructure/Database/Migrations' -Filter '*.cs' |
        Where-Object Name -notlike '*ModelSnapshot.cs'
    if ($migrationFiles.Count -eq 0) {
        throw 'No checked-in EF migrations are available for release review.'
    }

    Write-Host "Release preflight contract passed with $($migrationFiles.Count) checked-in migration files."
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
}

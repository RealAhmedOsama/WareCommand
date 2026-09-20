[CmdletBinding()]
param(
    [string]$RootPath = $env:WARECOMMAND_BACKUP_ROOT_PATH,
    [string]$OffsitePath = $env:WARECOMMAND_BACKUP_OFFSITE_PATH,
    [string]$EncryptionKeyFile = $env:WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE,
    [int]$RetentionDays = 30,
    [int]$MinimumRetained = 2
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RootPath)) {
    $RootPath = Join-Path $repositoryRoot 'artifacts/backups'
}
if ([string]::IsNullOrWhiteSpace($EncryptionKeyFile)) {
    throw 'An encryption key file is required through -EncryptionKeyFile or WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE.'
}
Push-Location $repositoryRoot
try {
    $arguments = @(
        'prune', '--root', $RootPath, '--key-file', $EncryptionKeyFile,
        '--retention-days', $RetentionDays.ToString(),
        '--minimum-retained', $MinimumRetained.ToString())
    if (-not [string]::IsNullOrWhiteSpace($OffsitePath)) {
        $arguments += @('--offsite', $OffsitePath)
    }
    & dotnet run --project 'Wms.Backup/Wms.Backup.csproj' --configuration Release --no-restore -- @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "The backup retention command failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

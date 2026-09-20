[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactPath,
    [string]$RootPath = $env:WARECOMMAND_BACKUP_ROOT_PATH,
    [string]$EncryptionKeyFile = $env:WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RootPath)) {
    $RootPath = Split-Path -Parent $ArtifactPath
}
if ([string]::IsNullOrWhiteSpace($EncryptionKeyFile)) {
    throw 'An encryption key file is required through -EncryptionKeyFile or WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE.'
}
Push-Location $repositoryRoot
try {
    & dotnet run --project 'Wms.Backup/Wms.Backup.csproj' --configuration Release --no-restore -- verify --artifact $ArtifactPath --root $RootPath --key-file $EncryptionKeyFile
    if ($LASTEXITCODE -ne 0) {
        throw "The backup verification failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

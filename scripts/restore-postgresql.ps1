[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactPath,
    [Parameter(Mandatory)][string]$TargetConnectionString,
    [string]$RootPath = $env:WARECOMMAND_BACKUP_ROOT_PATH,
    [string]$EncryptionKeyFile = $env:WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE,
    [switch]$AllowNonEmptyTarget
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RootPath)) {
    $RootPath = Split-Path -Parent $ArtifactPath
}
if ([string]::IsNullOrWhiteSpace($EncryptionKeyFile)) {
    throw 'An encryption key file is required through -EncryptionKeyFile or WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE.'
}
if ($AllowNonEmptyTarget) {
    $confirmation = Read-Host 'Type RESTORE-WARECOMMAND-DATA to authorize a non-empty target restore'
    if ($confirmation -cne 'RESTORE-WARECOMMAND-DATA') {
        throw 'Non-empty target restore was not confirmed.'
    }
}

$previousTarget = $env:WARECOMMAND_RESTORE_TARGET_CONNECTION
Push-Location $repositoryRoot
try {
    $env:WARECOMMAND_RESTORE_TARGET_CONNECTION = $TargetConnectionString
    $arguments = @('restore', '--artifact', $ArtifactPath, '--root', $RootPath, '--key-file', $EncryptionKeyFile)
    if ($AllowNonEmptyTarget) {
        $arguments += @('--allow-non-empty-target', '--confirm', 'RESTORE-WARECOMMAND-DATA')
    }
    & dotnet run --project 'Wms.Backup/Wms.Backup.csproj' --configuration Release --no-restore -- @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "The backup restore command failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -ne $previousTarget) {
        $env:WARECOMMAND_RESTORE_TARGET_CONNECTION = $previousTarget
    }
    else {
        Remove-Item Env:WARECOMMAND_RESTORE_TARGET_CONNECTION -ErrorAction SilentlyContinue
    }
    Pop-Location
}

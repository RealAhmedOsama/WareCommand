[CmdletBinding()]
param(
    [string]$ConnectionString = $env:WARECOMMAND_POSTGRES_CONNECTION,
    [string]$RootPath = $env:WARECOMMAND_BACKUP_ROOT_PATH,
    [string]$OffsitePath = $env:WARECOMMAND_BACKUP_OFFSITE_PATH,
    [string]$EncryptionKeyFile = $env:WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE,
    [switch]$VerifyAfter
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$previousConnectionString = $env:WARECOMMAND_POSTGRES_CONNECTION
$locationPushed = $false

function Invoke-BackupCommand {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet run --project 'Wms.Backup/Wms.Backup.csproj' --configuration Release --no-restore -- @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "The WareCommand backup command failed with exit code $LASTEXITCODE."
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        throw 'A PostgreSQL connection string is required through -ConnectionString or WARECOMMAND_POSTGRES_CONNECTION.'
    }
    if ([string]::IsNullOrWhiteSpace($EncryptionKeyFile)) {
        throw 'An encryption key file is required through -EncryptionKeyFile or WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE.'
    }
    if ([string]::IsNullOrWhiteSpace($RootPath)) {
        $RootPath = Join-Path $repositoryRoot 'artifacts/backups'
    }

    Push-Location $repositoryRoot
    $locationPushed = $true
    $env:WARECOMMAND_POSTGRES_CONNECTION = $ConnectionString
    $arguments = @('backup', '--root', $RootPath, '--key-file', $EncryptionKeyFile)
    if (-not [string]::IsNullOrWhiteSpace($OffsitePath)) {
        $arguments += @('--offsite', $OffsitePath)
    }
    Invoke-BackupCommand $arguments

    if ($VerifyAfter) {
        $artifact = Get-ChildItem -LiteralPath $RootPath -Filter '*.wcbak' -File |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -eq $artifact) {
            throw "No backup artifact was found under '$RootPath' after the backup command."
        }
        Invoke-BackupCommand @('verify', '--artifact', $artifact.FullName, '--root', $RootPath, '--key-file', $EncryptionKeyFile)
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

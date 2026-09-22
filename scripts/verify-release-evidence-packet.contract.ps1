$ErrorActionPreference = 'Stop'

$script = Join-Path $PSScriptRoot 'verify-release-evidence-packet.ps1'
$output = & pwsh -NoProfile -File $script -PlanOnly 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Release evidence plan contract failed to execute: $output"
}

foreach ($required in @(
        'evidence-packet-schema=wms-release-local-v1',
        'gate=release-preflight',
        'gate=security-boundaries',
        'gate=migration-lifecycle',
        'gate=support-and-security-tests',
        'decision=GO_WITH_RESTRICTIONS')) {
    if ($output -notmatch [regex]::Escape($required)) {
        throw "Release evidence plan is missing '$required'. Output: $output"
    }
}

$source = Get-Content -Raw $script
foreach ($required in @('responsePayloads', 'credentials', 'cookies', 'bearerTokens', 'dataProtectionKeys', 'databaseDumps')) {
    if ($source -notmatch [regex]::Escape($required)) {
        throw "Release evidence packet is missing redaction coverage for '$required'."
    }
}

Write-Host 'Release evidence packet contract passed.'

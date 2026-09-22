[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$locationPushed = $false

function Assert-Contains {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Needle
    )

    $content = Get-Content -LiteralPath $Path -Raw
    if ($content.IndexOf($Needle, [StringComparison]::Ordinal) -lt 0) {
        throw "Security boundary '$Needle' was not found in '$Path'."
    }
}

try {
    Push-Location $repositoryRoot
    $locationPushed = $true

    Assert-Contains 'SECURITY.md' 'external security review'
    Assert-Contains 'Wms.ASP/Program.cs' 'AutoValidateAntiforgeryTokenAttribute'
    Assert-Contains 'Wms.ASP/Program.cs' 'UseRateLimiter'
    Assert-Contains 'Wms.Application/Connectors/ConnectorContracts.cs' 'SensitiveMarkers'
    Assert-Contains 'Wms.Application/B2bDocuments/B2bDocumentContracts.cs' 'B2bDocumentIdentity'
    Assert-Contains 'Wms.Application/BulkExchange/BulkExchangeContracts.cs' 'SanitizeForSpreadsheet'
    Assert-Contains 'Wms.Infrastructure/Integrations/IntegrationOutboxDispatcher.cs' 'WebhookSignature'

    $rawViewFiles = Get-ChildItem -LiteralPath 'Wms.ASP/Views' -Recurse -Filter '*.cshtml'
    foreach ($view in $rawViewFiles) {
        $rawLines = Select-String -LiteralPath $view.FullName -Pattern 'Html\.Raw\('
        foreach ($rawLine in $rawLines) {
            $line = $rawLine.Line
            if ($line -notmatch 'Json\.Serialize' -and $line -notmatch "class='badge") {
                throw "Unreviewed Html.Raw usage found in '$($view.FullName)' at line $($rawLine.LineNumber)."
            }
        }
    }

    Write-Host 'Security boundary verification passed.'
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
}

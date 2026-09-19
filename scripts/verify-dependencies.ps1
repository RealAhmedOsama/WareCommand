[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot 'Warehouse Management System.sln'
$locationPushed = $false
$legacyXunitPackages = [System.Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($package in @(
        'xunit',
        'xunit.assert',
        'xunit.core',
        'xunit.extensibility.core',
        'xunit.extensibility.execution')) {
    $legacyXunitPackages.Add($package) | Out-Null
}

function Invoke-DotnetPackageCheck {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$CheckName)

    $output = & dotnet @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String).Trim()
    if ($exitCode -ne 0) {
        throw "$CheckName failed with exit code $exitCode.`n$text"
    }

    return $text
}

try {
    Push-Location $repositoryRoot
    $locationPushed = $true

    $vulnerabilityOutput = Invoke-DotnetPackageCheck @(
        'list', $solution, 'package', '--vulnerable', '--include-transitive') 'NuGet vulnerability check'
    if ($vulnerabilityOutput -match '(?im)has the following vulnerable packages') {
        throw "NuGet vulnerability check found vulnerable packages.`n$vulnerabilityOutput"
    }

    $deprecatedOutput = Invoke-DotnetPackageCheck @(
        'list', $solution, 'package', '--deprecated', '--include-transitive') 'NuGet deprecation check'
    $deprecatedPackageNames = [System.Text.RegularExpressions.Regex]::Matches(
            $deprecatedOutput,
            '(?m)^\s*>\s+(?<package>\S+)') |
        ForEach-Object { $_.Groups['package'].Value } |
        Sort-Object -Unique
    $unexpectedDeprecatedPackages = $deprecatedPackageNames |
        Where-Object { -not $legacyXunitPackages.Contains($_) }

    if ($unexpectedDeprecatedPackages.Count -gt 0) {
        throw "NuGet deprecation check found packages outside the documented xUnit v2 legacy allowlist: $($unexpectedDeprecatedPackages -join ', ').`n$deprecatedOutput"
    }

    if ($deprecatedPackageNames.Count -gt 0) {
        Write-Warning (
            "Deprecated-package check found only the documented xUnit v2 legacy packages: " +
            ($deprecatedPackageNames -join ', ') +
            ". Migrate the test framework before removing this allowlist.")
    }

    Write-Host 'NuGet vulnerability and deprecation checks passed.'
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
}

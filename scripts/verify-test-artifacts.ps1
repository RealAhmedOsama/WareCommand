[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('linux', 'windows')]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [string]$ResultsRoot
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($ResultsRoot)) {
    $ResultsRoot = Join-Path $repositoryRoot $ResultsRoot
}
New-Item -ItemType Directory -Force -Path $ResultsRoot | Out-Null

$projects = @(
    'Wms.Domain.Tests',
    'Wms.Application.Tests',
    'Wms.Infrastructure.Tests',
    'Wms.ASP.Tests',
    'Wms.Architecture.Tests',
    'Wms.DataMigration.Tests'
)
$errors = [System.Collections.Generic.List[string]]::new()
$coverageFiles = [System.Collections.Generic.List[string]]::new()
$totalPassed = [int64]0
$totalFailed = [int64]0
$totalSkipped = [int64]0
$providerGatedSkipped = [int64]0
$validTrxReports = 0
$validCoverageReports = 0
$mergedCoverage = $null

function Get-CounterValue {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$Counters,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [switch]$Required
    )

    $value = $Counters.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        if ($Required) {
            throw "TRX counter '$Name' is missing."
        }
        return [int64]0
    }

    $parsed = [int64]0
    if (-not [int64]::TryParse($value, [ref]$parsed) -or $parsed -lt 0) {
        throw "TRX counter '$Name' is invalid."
    }
    return $parsed
}

foreach ($project in $projects) {
    $projectDirectory = Join-Path $ResultsRoot $project
    if (-not (Test-Path -LiteralPath $projectDirectory -PathType Container)) {
        $errors.Add("Missing test result directory for $project.")
        continue
    }

    $trxFiles = @(Get-ChildItem -LiteralPath $projectDirectory -Filter '*.trx' -File -Recurse)
    if ($trxFiles.Count -ne 1) {
        $errors.Add("$project produced $($trxFiles.Count) TRX reports; exactly one is required.")
    }
    else {
        try {
            $trx = [System.Xml.XmlDocument]::new()
            $trx.Load($trxFiles[0].FullName)
            if ($trx.DocumentElement.LocalName -ne 'TestRun') {
                throw 'Unexpected TRX root element.'
            }

            $counters = $trx.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
            if ($null -eq $counters -or -not ($counters -is [System.Xml.XmlElement])) {
                throw 'TRX result counters are missing.'
            }

            $total = Get-CounterValue -Counters $counters -Name 'total' -Required
            if ($total -le 0) {
                throw 'TRX contains no discovered tests.'
            }

            $unitResults = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
            if ($unitResults.Count -ne $total) {
                throw "TRX has $($unitResults.Count) result rows for $total discovered tests."
            }

            foreach ($unitResult in $unitResults) {
                $outcome = $unitResult.GetAttribute('outcome')
                switch ($outcome) {
                    'Passed' {
                        $totalPassed++
                    }
                    { $_ -in @('NotExecuted', 'Inconclusive') } {
                        $totalSkipped++
                        if ($unitResult.InnerText.Contains('WARECOMMAND_TEST_POSTGRES_CONNECTION')) {
                            $providerGatedSkipped++
                        }
                    }
                    { $_ -in @('Failed', 'Error', 'Timeout', 'Aborted', 'PassedButRunAborted', 'NotRunnable', 'Disconnected', 'Warning') } {
                        $totalFailed++
                    }
                    default {
                        throw "TRX contains an unclassified test outcome '$outcome'."
                    }
                }
            }
            $validTrxReports++
        }
        catch {
            $errors.Add("Invalid TRX report for ${project}: $($_.Exception.Message)")
        }
    }

    $coberturaFiles = @(Get-ChildItem -LiteralPath $projectDirectory -Filter 'coverage.cobertura.xml' -File -Recurse)
    if ($coberturaFiles.Count -ne 1) {
        $errors.Add("$project produced $($coberturaFiles.Count) Cobertura reports; exactly one is required.")
        continue
    }

    try {
        if ($coberturaFiles[0].Length -le 0) {
            throw 'Coverage report is empty.'
        }
        $coverage = [System.Xml.XmlDocument]::new()
        $coverage.Load($coberturaFiles[0].FullName)
        if ($coverage.DocumentElement.LocalName -ne 'coverage') {
            throw 'Unexpected Cobertura root element.'
        }

        $linesValid = [int64]0
        $linesCovered = [int64]0
        if (
            -not [int64]::TryParse($coverage.DocumentElement.GetAttribute('lines-valid'), [ref]$linesValid) -or
            -not [int64]::TryParse($coverage.DocumentElement.GetAttribute('lines-covered'), [ref]$linesCovered) -or
            $linesValid -lt 0 -or
            $linesCovered -lt 0 -or
            $linesCovered -gt $linesValid
        ) {
            throw 'Cobertura line counters are invalid.'
        }

        $coverageFiles.Add($coberturaFiles[0].FullName)
        $validCoverageReports++
    }
    catch {
        $errors.Add("Invalid Cobertura report for ${project}: $($_.Exception.Message)")
    }
}

if ($coverageFiles.Count -gt 0 -and $errors.Count -eq 0) {
    try {
        $mergedDirectory = Join-Path $ResultsRoot 'merged-coverage'
        New-Item -ItemType Directory -Force -Path $mergedDirectory | Out-Null
        $coverageInput = $coverageFiles -join ';'
        $reportArguments = @(
            "-reports:$coverageInput",
            "-targetdir:$mergedDirectory",
            '-reporttypes:Cobertura;TextSummary'
        )
        Push-Location $repositoryRoot
        try {
            & dotnet tool run reportgenerator -- @reportArguments
            $reportGeneratorExit = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
        if ($reportGeneratorExit -ne 0) {
            throw "ReportGenerator exited with code $reportGeneratorExit."
        }

        $mergedReports = @(Get-ChildItem -LiteralPath $mergedDirectory -Filter '*.xml' -File)
        if ($mergedReports.Count -ne 1) {
            throw "ReportGenerator produced $($mergedReports.Count) merged XML reports."
        }

        $mergedDocument = [System.Xml.XmlDocument]::new()
        $mergedDocument.Load($mergedReports[0].FullName)
        if ($mergedDocument.DocumentElement.LocalName -ne 'coverage') {
            throw 'The merged report is not Cobertura XML.'
        }

        $mergedLinesValid = [int64]0
        $mergedLinesCovered = [int64]0
        if (
            -not [int64]::TryParse($mergedDocument.DocumentElement.GetAttribute('lines-valid'), [ref]$mergedLinesValid) -or
            -not [int64]::TryParse($mergedDocument.DocumentElement.GetAttribute('lines-covered'), [ref]$mergedLinesCovered) -or
            $mergedLinesValid -le 0 -or
            $mergedLinesCovered -le 0 -or
            $mergedLinesCovered -gt $mergedLinesValid
        ) {
            throw 'Merged coverage has no valid executed source-line evidence.'
        }
        $mergedCoverage = [pscustomobject]@{
            Path = $mergedReports[0].FullName
            LinesCovered = $mergedLinesCovered
            LinesValid = $mergedLinesValid
            LineRate = [double]::Parse(
                $mergedDocument.DocumentElement.GetAttribute('line-rate'),
                [System.Globalization.CultureInfo]::InvariantCulture
            )
        }
    }
    catch {
        $errors.Add("Merged coverage validation failed: $($_.Exception.Message)")
    }
}

$otherSkipped = [Math]::Max([int64]0, $totalSkipped - $providerGatedSkipped)
if ($providerGatedSkipped -gt $totalSkipped) {
    $errors.Add('Identified provider-gated skips exceed all skipped tests.')
}
$evidenceStatus = if ($errors.Count -eq 0) { 'complete' } else { 'incomplete' }
$coverageSummary = 'Merged Cobertura report unavailable.'
if ($null -ne $mergedCoverage) {
    $coveragePercent = [Math]::Round($mergedCoverage.LineRate * 100, 2)
    $coverageSummary = "$coveragePercent% line coverage ($($mergedCoverage.LinesCovered)/$($mergedCoverage.LinesValid) lines)"
}

$summary = @(
    "# $Platform test and coverage evidence",
    '',
    "- Evidence status: **$evidenceStatus**",
    "- TRX reports validated: $validTrxReports/$($projects.Count)",
    "- Cobertura reports validated: $validCoverageReports/$($projects.Count)",
    "- Passed: $totalPassed",
    "- Failed or aborted: $totalFailed",
    "- Skipped: $totalSkipped",
    "- PostgreSQL provider-gated skips identified by reason: $providerGatedSkipped",
    "- Other skips: $otherSkipped",
    "- Measured coverage from executed tests: $coverageSummary",
    '- Skipped tests do not contribute coverage; no percentage threshold is enforced.',
    '- Per-project reports are merged once per platform so duplicate source lines are counted once.'
)
if ($null -ne $mergedCoverage) {
    $summary += "- Merged report: $([System.IO.Path]::GetRelativePath($repositoryRoot, $mergedCoverage.Path))"
}
if ($errors.Count -gt 0) {
    $summary += ''
    $summary += '## Evidence errors'
    foreach ($errorMessage in $errors) {
        $summary += "- $errorMessage"
    }
}

$summaryPath = Join-Path $ResultsRoot 'test-summary.md'
[System.IO.File]::WriteAllLines($summaryPath, $summary, [System.Text.UTF8Encoding]::new($false))
Write-Host ($summary -join [Environment]::NewLine)

if ($errors.Count -gt 0) {
    Write-Host '::error::Test-result or coverage evidence is incomplete.'
    exit 1
}

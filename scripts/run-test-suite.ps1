[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('linux', 'windows')]
    [string]$Platform,

    [switch]$NoBuild,

    [string]$RunId,

    [string]$ResultsRoot
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ResultsRoot)) {
    $ResultsRoot = Join-Path $repositoryRoot "artifacts/test-results/$Platform"
}
elseif (-not [System.IO.Path]::IsPathRooted($ResultsRoot)) {
    $ResultsRoot = Join-Path $repositoryRoot $ResultsRoot
}
$ResultsRoot = [System.IO.Path]::GetFullPath($ResultsRoot)

if ([string]::IsNullOrWhiteSpace($RunId)) {
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ID)) {
        $RunId = $env:GITHUB_RUN_ID
    }
    else {
        $RunId = "local-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))-$([Guid]::NewGuid().ToString('N'))"
    }
}
if ($RunId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_.-]*$') {
    throw 'RunId must start with a letter or number and contain only letters, numbers, periods, underscores, or hyphens.'
}
$RunResultsRoot = Join-Path $ResultsRoot $RunId
if (Test-Path -LiteralPath $RunResultsRoot) {
    throw "Test evidence directory already exists for RunId '$RunId'; use a fresh run id."
}

$projects = @(
    'Wms.Domain.Tests/Wms.Domain.Tests.csproj',
    'Wms.Application.Tests/Wms.Application.Tests.csproj',
    'Wms.Infrastructure.Tests/Wms.Infrastructure.Tests.csproj',
    'Wms.ASP.Tests/Wms.ASP.Tests.csproj',
    'Wms.Architecture.Tests/Wms.Architecture.Tests.csproj',
    'Wms.DataMigration.Tests/Wms.DataMigration.Tests.csproj'
)
$firstFailure = 0

try {
    Push-Location $repositoryRoot
    New-Item -ItemType Directory -Force -Path $RunResultsRoot | Out-Null
    Write-Host "Test evidence directory: $RunResultsRoot"

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        $relativeResultsRoot = [System.IO.Path]::GetRelativePath($repositoryRoot, $RunResultsRoot).Replace('\', '/')
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "results_root=$relativeResultsRoot"
    }

    foreach ($project in $projects) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
        $projectResults = Join-Path $RunResultsRoot $projectName
        New-Item -ItemType Directory -Force -Path $projectResults | Out-Null

        $arguments = @(
            'test',
            $project,
            '-c', 'Release',
            '--no-restore',
            '--nologo',
            '--logger', 'console;verbosity=minimal',
            '--logger', "trx;LogFileName=$projectName.trx",
            '--collect', 'XPlat Code Coverage',
            '--settings', 'coverage.runsettings',
            '--results-directory', $projectResults
        )
        if ($NoBuild) {
            $arguments += '--no-build'
        }

        Write-Host "Running $project on $Platform"
        & dotnet @arguments
        $projectExitCode = $LASTEXITCODE

        $coverageReports = @(Get-ChildItem -LiteralPath $projectResults -Filter 'coverage.cobertura.xml' -File -Recurse)
        if ($coverageReports.Count -gt 0) {
            try {
                $coverageHashes = @(
                    $coverageReports |
                        ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } |
                        Sort-Object -Unique
                )
                if ($coverageHashes.Count -eq 1) {
                    $canonicalCoverage = Join-Path $projectResults 'coverage.cobertura.xml'
                    Copy-Item -LiteralPath $coverageReports[0].FullName -Destination $canonicalCoverage -Force

                    $trxFiles = @(Get-ChildItem -LiteralPath $projectResults -Filter '*.trx' -File)
                    if ($trxFiles.Count -eq 1) {
                        $trx = [System.Xml.XmlDocument]::new()
                        $trx.PreserveWhitespace = $true
                        $trx.Load($trxFiles[0].FullName)
                        $coverageLinks = @(
                            $trx.SelectNodes("//*[local-name()='UriAttachment']/*[local-name()='A'][contains(@href, 'coverage.cobertura.xml')]")
                        )
                        if ($coverageLinks.Count -gt 0) {
                            foreach ($coverageLink in $coverageLinks) {
                                $coverageLink.SetAttribute('href', 'coverage.cobertura.xml')
                            }

                            $writerSettings = [System.Xml.XmlWriterSettings]::new()
                            $writerSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
                            $writerSettings.Indent = $false
                            $writer = [System.Xml.XmlWriter]::Create($trxFiles[0].FullName, $writerSettings)
                            try {
                                $trx.Save($writer)
                            }
                            finally {
                                $writer.Dispose()
                            }
                        }
                    }

                    foreach ($coverageReport in $coverageReports) {
                        Remove-Item -LiteralPath $coverageReport.FullName -Force
                    }
                    Write-Host "Normalized $($coverageReports.Count) identical coverage attachment(s) for $projectName."
                }
                else {
                    Write-Host "Coverage collector produced $($coverageHashes.Count) different reports for $projectName; evidence validation will reject the ambiguity."
                }
            }
            catch {
                Write-Host "Coverage report normalization failed for ${projectName}: $($_.Exception.Message)"
            }
        }

        if ($projectExitCode -ne 0) {
            Write-Host "Test command failed for $project with exit code $projectExitCode."
            if ($firstFailure -eq 0) {
                $firstFailure = $projectExitCode
            }
        }
    }
}
finally {
    Pop-Location
}

if ($firstFailure -ne 0) {
    exit $firstFailure
}

Write-Host "All six test projects completed on $Platform."

[CmdletBinding()]
param(
    [int]$Port = 55432,
    [string]$ContainerName = "warecommand-postgres-test-$([Guid]::NewGuid().ToString('N'))",
    [ValidateSet('all', 'core', 'harness', 'dashboard', 'data-generation', 'journeys', 'resilience', 'browser', 'performance')]
    [string]$Group = 'all',
    [string]$TestName,
    [ValidateRange(10, 100)]
    [int]$PerformanceSamples = 40,
    [ValidateRange(2, 5)]
    [int]$PerformanceRepeats = 2,
    [switch]$ExtendedContention,
    [switch]$PlanOnly,
    [string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Get-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )

    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd([char[]]@('\', '/'))
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPrefix = $fullRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Provider test project '$fullPath' is outside the repository root."
    }

    return $fullPath.Substring($rootPrefix.Length).Replace('\', '/')
}

$testProject = Join-Path $repositoryRoot 'Wms.Infrastructure.Tests/Wms.Infrastructure.Tests.csproj'
$dashboardTestProject = Join-Path $repositoryRoot 'Wms.ASP.Tests/Wms.ASP.Tests.csproj'
$providerGroups = [ordered]@{
    core = [pscustomobject]@{
        testProject = $testProject
        filter = 'FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests&FullyQualifiedName!~_Harness'
        expectedClass = 'Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests.'
    }
    harness = [pscustomobject]@{
        testProject = $testProject
        filter = 'FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests_Harness'
        expectedClass = 'Wms.Infrastructure.Tests.Integration.PostgreSqlIntegrationTests_Harness.'
    }
    dashboard = [pscustomobject]@{
        testProject = $dashboardTestProject
        filter = 'FullyQualifiedName~Wms.ASP.Tests.PostgreSqlDashboardFlowTests'
        expectedClass = 'Wms.ASP.Tests.PostgreSqlDashboardFlowTests.'
    }
    'data-generation' = [pscustomobject]@{
        testProject = $testProject
        filter = 'FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlDataGenerationTests'
        expectedClass = 'Wms.Infrastructure.Tests.Integration.PostgreSqlDataGenerationTests.'
    }
    journeys = [pscustomobject]@{
        testProject = $testProject
        filter = 'FullyQualifiedName~Wms.Infrastructure.Tests.Integration.PostgreSqlJourneyTests'
        expectedClass = 'Wms.Infrastructure.Tests.Integration.PostgreSqlJourneyTests.'
    }
    resilience = [pscustomobject]@{
        testProject = $dashboardTestProject
        filter = 'FullyQualifiedName~Wms.ASP.Tests.PostgreSqlRuntimeResilienceTests'
        expectedClass = 'Wms.ASP.Tests.PostgreSqlRuntimeResilienceTests.'
    }
    browser = [pscustomobject]@{
        testProject = $dashboardTestProject
        filter = 'FullyQualifiedName~Wms.ASP.Tests.PostgreSqlBrowserJourneyTests'
        expectedClass = 'Wms.ASP.Tests.PostgreSqlBrowserJourneyTests.'
    }
    performance = [pscustomobject]@{
        testProject = $dashboardTestProject
        filter = 'FullyQualifiedName~Wms.ASP.Tests.PostgreSqlPerformanceQualificationTests'
        expectedClass = 'Wms.ASP.Tests.PostgreSqlPerformanceQualificationTests.'
    }
}
$selectedGroupNames = if ($Group -eq 'all') { @('core', 'harness', 'dashboard', 'data-generation', 'journeys', 'resilience', 'browser') } else { @($Group) }
if (-not [string]::IsNullOrWhiteSpace($TestName) -and $TestName -notmatch '^[A-Za-z0-9_]+$') {
    throw 'TestName must be a test method identifier.'
}
if (-not [string]::IsNullOrWhiteSpace($TestName) -and $Group -ne 'resilience') {
    throw 'TestName can only be used with the resilience provider group.'
}

if ($PlanOnly) {
    foreach ($groupName in $selectedGroupNames) {
        Write-Output "provider-group=$groupName"
        Write-Output "test-project=$($providerGroups[$groupName].testProject)"
        $plannedFilter = $providerGroups[$groupName].filter
        if (-not [string]::IsNullOrWhiteSpace($TestName)) {
            $plannedFilter += "&FullyQualifiedName~$TestName"
        }
        Write-Output "filter=$plannedFilter"
        if ($groupName -eq 'performance') {
            Write-Output "performance-repeats=$PerformanceRepeats"
            Write-Output "performance-samples-per-level=$PerformanceSamples"
            Write-Output "extended-contention=$($ExtendedContention.IsPresent)"
        }
    }
    exit 0
}

$previousConnectionString = $env:WARECOMMAND_TEST_POSTGRES_CONNECTION
$previousTestContainerName = $env:WARECOMMAND_TEST_POSTGRES_CONTAINER
$previousDataGenerationEvidencePath = $env:WARECOMMAND_DATA_GENERATION_EVIDENCE_PATH
$previousJourneyEvidencePath = $env:WARECOMMAND_POSTGRES_JOURNEY_EVIDENCE_PATH
$previousResilienceEvidencePath = $env:WARECOMMAND_POSTGRES_RESILIENCE_EVIDENCE_PATH
$previousPerformanceEvidencePath = $env:WARECOMMAND_PERFORMANCE_EVIDENCE_PATH
$previousPerformanceRepeats = $env:WARECOMMAND_PERF_REPEATS
$previousPerformanceSamples = $env:WARECOMMAND_PERF_SAMPLES
$previousPerformanceExtendedContention = $env:WARECOMMAND_PERF_EXTENDED_CONTENTION
$previousBrowserArtifactDirectory = $env:WARECOMMAND_BROWSER_ARTIFACT_DIRECTORY
$previousVerificationRevision = $env:WARECOMMAND_VERIFICATION_REVISION
$password = $env:WARECOMMAND_TEST_POSTGRES_PASSWORD
$locationPushed = $false
$containerStarted = $false
$containerId = $null
$dataGenReportPath = $null
$journeyReportPath = $null
$resilienceReportPath = $null
$performanceReportPath = $null
$performanceEvidence = $null
$verificationFailure = $null
if ($selectedGroupNames -contains 'data-generation' -or
    $selectedGroupNames -contains 'dashboard' -or
    $selectedGroupNames -contains 'browser' -or
    $selectedGroupNames -contains 'journeys' -or
    $selectedGroupNames -contains 'resilience') {
    $dataGenReportPath = Join-Path ([System.IO.Path]::GetTempPath()) "warecommand-data-generation-$([Guid]::NewGuid().ToString('N')).json"
    $env:WARECOMMAND_DATA_GENERATION_EVIDENCE_PATH = $dataGenReportPath
}
if ($selectedGroupNames -contains 'journeys') {
    $journeyReportPath = Join-Path ([System.IO.Path]::GetTempPath()) "warecommand-postgresql-journeys-$([Guid]::NewGuid().ToString('N')).json"
    $env:WARECOMMAND_POSTGRES_JOURNEY_EVIDENCE_PATH = $journeyReportPath
}
if ($selectedGroupNames -contains 'resilience') {
    $resilienceReportPath = Join-Path ([System.IO.Path]::GetTempPath()) "warecommand-postgresql-resilience-$([Guid]::NewGuid().ToString('N')).json"
    $env:WARECOMMAND_POSTGRES_RESILIENCE_EVIDENCE_PATH = $resilienceReportPath
}
if ($selectedGroupNames -contains 'performance') {
    $performanceReportPath = Join-Path ([System.IO.Path]::GetTempPath()) "warecommand-postgresql-performance-$([Guid]::NewGuid().ToString('N')).json"
    $env:WARECOMMAND_PERFORMANCE_EVIDENCE_PATH = $performanceReportPath
    $env:WARECOMMAND_PERF_REPEATS = $PerformanceRepeats.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $env:WARECOMMAND_PERF_SAMPLES = $PerformanceSamples.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $env:WARECOMMAND_PERF_EXTENDED_CONTENTION = $ExtendedContention.IsPresent.ToString().ToLowerInvariant()
}
if ($selectedGroupNames -contains 'browser') {
    $browserArtifactBase = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
        [System.IO.Path]::GetTempPath()
    }
    else {
        $env:RUNNER_TEMP
    }
    $env:WARECOMMAND_BROWSER_ARTIFACT_DIRECTORY = Join-Path $browserArtifactBase 'warecommand-browser-evidence'
}
if ([string]::IsNullOrWhiteSpace($password)) {
    $password = [Guid]::NewGuid().ToString('N')
}

function Invoke-Docker {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker is required for PostgreSQL integration verification.'
    }

    Push-Location $repositoryRoot
    $locationPushed = $true
    $env:WARECOMMAND_VERIFICATION_REVISION = (& git rev-parse HEAD 2>$null | Out-String).Trim()

    $existingContainers = @(& docker ps --all --format '{{.Names}}')
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect Docker containers before PostgreSQL verification.'
    }

    if ($existingContainers -contains $ContainerName) {
        throw "Refusing to reuse or remove existing Docker container '$ContainerName'."
    }

    Invoke-Docker @(
        'run', '--detach', '--rm', '--name', $ContainerName,
        '--env', 'POSTGRES_DB=warecommand_test',
        '--env', 'POSTGRES_USER=warecommand_test',
        '--env', "POSTGRES_PASSWORD=$password",
        '--publish', "$Port`:5432",
        'postgres:17'
    ) | Out-Null
    $containerStarted = $true
    $env:WARECOMMAND_TEST_POSTGRES_CONTAINER = $ContainerName
    $containerId = (& docker inspect --format '{{.Id}}' $ContainerName 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        throw "Unable to read the identity of the owned PostgreSQL container '$ContainerName'."
    }

    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Seconds 1
        $health = (& docker exec $ContainerName pg_isready -U warecommand_test -d warecommand_test 2>$null | Out-String).Trim()
        if ($LASTEXITCODE -eq 0 -and $health -match 'accepting connections') {
            $ready = $true
            break
        }
    }

    if (-not $ready) {
        throw "PostgreSQL container did not become ready: $ContainerName"
    }

    $env:WARECOMMAND_TEST_POSTGRES_CONNECTION =
        "Host=127.0.0.1;Port=$Port;Database=warecommand_test;Username=warecommand_test;Password=$password"

    if ($selectedGroupNames -contains 'browser') {
        $browserInstallScript = Join-Path $repositoryRoot 'Wms.ASP.Tests/bin/Release/net10.0/playwright.ps1'
        if (-not (Test-Path -LiteralPath $browserInstallScript -PathType Leaf)) {
            & dotnet build $dashboardTestProject -c Release --no-restore --nologo -v:minimal
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $browserInstallScript -PathType Leaf)) {
                throw 'The Playwright browser installer was not produced by the ASP test build.'
            }
        }

        $browserInstallArguments = @('-NoProfile', '-File', $browserInstallScript, 'install', 'chromium')
        if (-not $IsWindows) {
            $browserInstallArguments = @('-NoProfile', '-File', $browserInstallScript, 'install', '--with-deps', 'chromium')
        }
        & pwsh @browserInstallArguments
        if ($LASTEXITCODE -ne 0) {
            throw 'Playwright could not install the pinned Chromium browser for PostgreSQL browser qualification.'
        }
    }

    $results = @()
    foreach ($groupName in $selectedGroupNames) {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $groupSpec = $providerGroups[$groupName]
        $groupTestProject = $groupSpec.testProject
        $filter = $groupSpec.filter
        if (-not [string]::IsNullOrWhiteSpace($TestName)) {
            $filter += "&FullyQualifiedName~$TestName"
        }
        $listedTests = (& dotnet test $groupTestProject -c Release --no-restore --list-tests --logger 'console;verbosity=minimal' --filter $filter 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL integration group '$groupName' could not enumerate tests."
        }
        if ($listedTests -notmatch [regex]::Escape($groupSpec.expectedClass)) {
            throw "PostgreSQL integration group '$groupName' selected no tests for '$filter'."
        }
        if (-not [string]::IsNullOrWhiteSpace($TestName) -and
            $listedTests -notmatch [regex]::Escape($TestName)) {
            throw "PostgreSQL integration group '$groupName' selected no test named '$TestName'."
        }

        & dotnet test $groupTestProject -c Release --no-restore --logger 'console;verbosity=minimal' --filter $filter
        $exitCode = $LASTEXITCODE
        $stopwatch.Stop()
        if ($exitCode -ne 0) {
            $verificationFailure = "PostgreSQL integration group '$groupName' failed with exit code $exitCode."
        }

        $results += [pscustomobject]@{
            group = $groupName
            testProject = (Get-RepositoryRelativePath -Root $repositoryRoot -Path $groupTestProject)
            filter = $filter
            durationSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
            exitCode = $exitCode
        }
        if ($null -ne $verificationFailure) {
            break
        }
    }

    $revision = (& git rev-parse HEAD 2>$null | Out-String).Trim()
    $dataGenerationReports = @()
    if ($null -ne $dataGenReportPath -and (Test-Path -LiteralPath $dataGenReportPath -PathType Leaf)) {
        $dataGenerationEvidence = Get-Content -LiteralPath $dataGenReportPath -Raw | ConvertFrom-Json
        $dataGenerationReports = @($dataGenerationEvidence.reports)
    }
    $journeyReports = @()
    if ($null -ne $journeyReportPath -and (Test-Path -LiteralPath $journeyReportPath -PathType Leaf)) {
        $journeyEvidence = Get-Content -LiteralPath $journeyReportPath -Raw | ConvertFrom-Json
        $journeyReports = @($journeyEvidence.reports)
    }
    $resilienceReports = @()
    if ($null -ne $resilienceReportPath -and (Test-Path -LiteralPath $resilienceReportPath -PathType Leaf)) {
        $resilienceReports = @(Get-Content -LiteralPath $resilienceReportPath -Raw | ConvertFrom-Json)
    }
    if ($null -ne $performanceReportPath -and (Test-Path -LiteralPath $performanceReportPath -PathType Leaf)) {
        $performanceEvidence = Get-Content -LiteralPath $performanceReportPath -Raw | ConvertFrom-Json
    }
    $performanceConfiguration = $null
    if ($Group -eq 'performance') {
        $performanceRequestedConcurrencyLevels = @(1, 4, 20)
        if ($ExtendedContention.IsPresent) {
            $performanceRequestedConcurrencyLevels = @(1, 4, 20, 50, 100)
        }
        $performanceConfiguration = [pscustomobject]@{
            repeats = $PerformanceRepeats
            samplesPerConcurrencyLevel = $PerformanceSamples
            extendedContentionRequested = $ExtendedContention.IsPresent
            requestedConcurrencyLevels = $performanceRequestedConcurrencyLevels
        }
    }
    $evidence = [pscustomobject]@{
        schema = 'wms-postgresql-provider-v1'
        revision = $revision
        postgresImage = 'postgres:17'
        testResources = [pscustomobject]@{
            postgresContainerName = $ContainerName
            postgresContainerId = $containerId
        }
        group = $Group
        groups = $results
        dataGenerationReports = $dataGenerationReports
        journeyReports = $journeyReports
        resilienceReports = $resilienceReports
        performanceConfiguration = $performanceConfiguration
        performanceEvidence = $performanceEvidence
        cleanup = 'owned container removed in finally; fixture-owned schema dropped by test fixture'
    }
    $json = $evidence | ConvertTo-Json -Depth 12
    Write-Output $json
    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $evidenceDirectory = Split-Path -Parent $EvidencePath
        if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
            New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
        }
        Set-Content -Path $EvidencePath -Value $json -Encoding utf8
    }
    if ($null -ne $verificationFailure) {
        throw $verificationFailure
    }
}
finally {
    if ($containerStarted) {
        & docker rm --force $ContainerName 2>$null | Out-Null
    }
    if ($null -ne $previousConnectionString) {
        $env:WARECOMMAND_TEST_POSTGRES_CONNECTION = $previousConnectionString
    }
    else {
        Remove-Item Env:WARECOMMAND_TEST_POSTGRES_CONNECTION -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousTestContainerName) {
        $env:WARECOMMAND_TEST_POSTGRES_CONTAINER = $previousTestContainerName
    }
    else {
        Remove-Item Env:WARECOMMAND_TEST_POSTGRES_CONTAINER -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousDataGenerationEvidencePath) {
        $env:WARECOMMAND_DATA_GENERATION_EVIDENCE_PATH = $previousDataGenerationEvidencePath
    }
    else {
        Remove-Item Env:WARECOMMAND_DATA_GENERATION_EVIDENCE_PATH -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousJourneyEvidencePath) {
        $env:WARECOMMAND_POSTGRES_JOURNEY_EVIDENCE_PATH = $previousJourneyEvidencePath
    }
    else {
        Remove-Item Env:WARECOMMAND_POSTGRES_JOURNEY_EVIDENCE_PATH -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousResilienceEvidencePath) {
        $env:WARECOMMAND_POSTGRES_RESILIENCE_EVIDENCE_PATH = $previousResilienceEvidencePath
    }
    else {
        Remove-Item Env:WARECOMMAND_POSTGRES_RESILIENCE_EVIDENCE_PATH -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousPerformanceEvidencePath) {
        $env:WARECOMMAND_PERFORMANCE_EVIDENCE_PATH = $previousPerformanceEvidencePath
    }
    else {
        Remove-Item Env:WARECOMMAND_PERFORMANCE_EVIDENCE_PATH -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousPerformanceRepeats) {
        $env:WARECOMMAND_PERF_REPEATS = $previousPerformanceRepeats
    }
    else {
        Remove-Item Env:WARECOMMAND_PERF_REPEATS -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousPerformanceSamples) {
        $env:WARECOMMAND_PERF_SAMPLES = $previousPerformanceSamples
    }
    else {
        Remove-Item Env:WARECOMMAND_PERF_SAMPLES -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousPerformanceExtendedContention) {
        $env:WARECOMMAND_PERF_EXTENDED_CONTENTION = $previousPerformanceExtendedContention
    }
    else {
        Remove-Item Env:WARECOMMAND_PERF_EXTENDED_CONTENTION -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousBrowserArtifactDirectory) {
        $env:WARECOMMAND_BROWSER_ARTIFACT_DIRECTORY = $previousBrowserArtifactDirectory
    }
    else {
        Remove-Item Env:WARECOMMAND_BROWSER_ARTIFACT_DIRECTORY -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousVerificationRevision) {
        $env:WARECOMMAND_VERIFICATION_REVISION = $previousVerificationRevision
    }
    else {
        Remove-Item Env:WARECOMMAND_VERIFICATION_REVISION -ErrorAction SilentlyContinue
    }
    if ($null -ne $dataGenReportPath) {
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
        $fullReportPath = [System.IO.Path]::GetFullPath($dataGenReportPath)
        if ($fullReportPath.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($fullReportPath).StartsWith('warecommand-data-generation-', [System.StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath $fullReportPath -PathType Leaf)) {
            Remove-Item -LiteralPath $fullReportPath -Force -ErrorAction SilentlyContinue
        }
    }
    if ($null -ne $journeyReportPath) {
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
        $fullReportPath = [System.IO.Path]::GetFullPath($journeyReportPath)
        if ($fullReportPath.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($fullReportPath).StartsWith('warecommand-postgresql-journeys-', [System.StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath $fullReportPath -PathType Leaf)) {
            Remove-Item -LiteralPath $fullReportPath -Force -ErrorAction SilentlyContinue
        }
    }
    if ($null -ne $resilienceReportPath) {
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
        $fullReportPath = [System.IO.Path]::GetFullPath($resilienceReportPath)
        if ($fullReportPath.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($fullReportPath).StartsWith('warecommand-postgresql-resilience-', [System.StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath $fullReportPath -PathType Leaf)) {
            Remove-Item -LiteralPath $fullReportPath -Force -ErrorAction SilentlyContinue
        }
    }
    if ($null -ne $performanceReportPath) {
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
        $fullReportPath = [System.IO.Path]::GetFullPath($performanceReportPath)
        if ($fullReportPath.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($fullReportPath).StartsWith('warecommand-postgresql-performance-', [System.StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath $fullReportPath -PathType Leaf)) {
            Remove-Item -LiteralPath $fullReportPath -Force -ErrorAction SilentlyContinue
        }
    }
    if ($locationPushed) {
        Pop-Location
    }
}

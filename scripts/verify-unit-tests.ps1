[CmdletBinding()]
param(
    [ValidateRange(1, 10)]
    [int]$Repetitions = 2
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projects = @(
    'Wms.Domain.Tests/Wms.Domain.Tests.csproj',
    'Wms.Application.Tests/Wms.Application.Tests.csproj'
)
$projectOrder = [System.Collections.Generic.List[string]]::new()

try {
    Push-Location $repositoryRoot

    for ($run = 1; $run -le $Repetitions; $run++) {
        $projectOrder.Clear()
        $projects | ForEach-Object { $projectOrder.Add($_) }
        $shuffled = $projectOrder | Sort-Object { Get-Random }

        Write-Host "Deterministic unit-test run $run/$Repetitions ($($shuffled -join ', '))"
        foreach ($project in $shuffled) {
            & dotnet test $project `
                -c Release `
                --no-build `
                --no-restore `
                --nologo `
                --logger 'console;verbosity=minimal'
            if ($LASTEXITCODE -ne 0) {
                throw "Unit tests failed for $project during repetition $run."
            }
        }
    }

    Write-Host "Repeated unit-test verification passed ($Repetitions repetitions)."
}
finally {
    Pop-Location
}

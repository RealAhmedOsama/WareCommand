[CmdletBinding()]
param(
    [int]$WebPort = 5232
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot 'Warehouse Management System.sln'
$webProject = Join-Path $repositoryRoot 'Wms.ASP'
$webAssembly = Join-Path $webProject 'bin/Release/net10.0/Wms.ASP.dll'
$winFormsExecutable = Join-Path $repositoryRoot 'Warehouse Management System/bin/Release/net10.0-windows/Wms.WinForms.exe'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("warecommand-baseline-" + [Guid]::NewGuid().ToString('N'))
$webDatabase = Join-Path $tempRoot 'web.db'
$webLog = Join-Path $tempRoot 'web.log'
$webErrorLog = Join-Path $tempRoot 'web-error.log'
$webProcess = $null
$winFormsProcess = $null

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    Push-Location $repositoryRoot

    Invoke-Dotnet @('restore', $solution)
    Invoke-Dotnet @('build', $solution, '-c', 'Debug', '--no-restore')
    Invoke-Dotnet @('build', $solution, '-c', 'Release', '--no-restore')
    Invoke-Dotnet @('test', $solution, '-c', 'Release', '--no-build', '--no-restore', '--logger', 'console;verbosity=minimal')

    $previousConnectionString = $env:ConnectionStrings__DefaultConnection
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousDatabaseProvider = $env:Wms__DatabaseProvider
$previousSeedProfile = $env:Wms__SeedProfile
$previousBootstrapEnabled = $env:Authentication__Bootstrap__Enabled
$previousBootstrapUserName = $env:Authentication__Bootstrap__UserName
$previousBootstrapEmail = $env:Authentication__Bootstrap__Email
$previousBootstrapDisplayName = $env:Authentication__Bootstrap__DisplayName
$previousBootstrapEmployeeCode = $env:Authentication__Bootstrap__EmployeeCode
$previousBootstrapPassword = $env:WARECOMMAND_ADMIN_BOOTSTRAP_PASSWORD
$previousCookieSecure = $env:Authentication__CookieSecure
$env:ConnectionStrings__DefaultConnection = "Data Source=$webDatabase"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Wms__DatabaseProvider = 'Sqlite'
$env:Wms__SeedProfile = 'Demo'
$env:Authentication__Bootstrap__Enabled = 'true'
$env:Authentication__Bootstrap__UserName = 'baseline-admin'
$env:Authentication__Bootstrap__Email = 'baseline-admin@localhost'
$env:Authentication__Bootstrap__DisplayName = 'Baseline Administrator'
$env:Authentication__Bootstrap__EmployeeCode = 'BASELINE-ADMIN'
$env:WARECOMMAND_ADMIN_BOOTSTRAP_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) + 'aA1!'
$env:Authentication__CookieSecure = 'false'

    if (-not (Test-Path -LiteralPath $webAssembly)) {
        throw "MVC assembly not found: $webAssembly"
    }

    # Start-Process joins ArgumentList before invoking dotnet; quote the DLL
    # explicitly because the repository path contains spaces.
    $webArguments = @('"' + $webAssembly + '"', '--urls', "http://127.0.0.1:$WebPort")
    $webProcess = Start-Process -FilePath 'dotnet' -ArgumentList $webArguments -WorkingDirectory $webProject -RedirectStandardOutput $webLog -RedirectStandardError $webErrorLog -PassThru
    $webReady = $false

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Seconds 1
        if ($webProcess.HasExited) {
            throw "MVC process exited before becoming ready. See $webLog and $webErrorLog."
        }

        try {
            $probe = Invoke-WebRequest -Uri "http://127.0.0.1:$WebPort/" -UseBasicParsing -TimeoutSec 2
            if ([int]$probe.StatusCode -eq 200) {
                $webReady = $true
                break
            }
        }
        catch {
            # The listener may still be starting.
        }
    }

    if (-not $webReady) {
        throw "MVC process did not become ready within 30 seconds. See $webLog and $webErrorLog."
    }

    $webSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $anonymousResponse = Invoke-WebRequest -Uri "http://127.0.0.1:$WebPort/Items" -WebSession $webSession -UseBasicParsing -MaximumRedirection 0 -SkipHttpErrorCheck -ErrorAction SilentlyContinue -TimeoutSec 10
    if ([int]$anonymousResponse.StatusCode -ne 302) {
        throw "Anonymous MVC /Items request returned HTTP $($anonymousResponse.StatusCode), expected 302."
    }
    Write-Host "MVC anonymous /Items -> $($anonymousResponse.StatusCode)"

    $loginPage = Invoke-WebRequest -Uri "http://127.0.0.1:$WebPort/Account/Login" -WebSession $webSession -UseBasicParsing -TimeoutSec 10
    $tokenMatch = [regex]::Match(
        $loginPage.Content,
        'name="__RequestVerificationToken"[^>]*value="([^"]+)"',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $tokenMatch.Success) {
        throw 'MVC login page did not render an antiforgery token.'
    }

    $loginResponse = Invoke-WebRequest -Uri "http://127.0.0.1:$WebPort/Account/Login" -Method Post -WebSession $webSession -UseBasicParsing -MaximumRedirection 0 -SkipHttpErrorCheck -ErrorAction SilentlyContinue -TimeoutSec 10 -Body @{
        UserName = 'baseline-admin'
        Password = $env:WARECOMMAND_ADMIN_BOOTSTRAP_PASSWORD
        RememberMe = 'true'
        __RequestVerificationToken = [System.Net.WebUtility]::HtmlDecode($tokenMatch.Groups[1].Value)
    }
    if ([int]$loginResponse.StatusCode -ne 302) {
        throw "MVC bootstrap login returned HTTP $($loginResponse.StatusCode), expected 302."
    }
    Write-Host "MVC bootstrap login -> $($loginResponse.StatusCode)"

    foreach ($path in @('/', '/Dashboard', '/Items', '/Inventory')) {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$WebPort$path" -WebSession $webSession -UseBasicParsing -TimeoutSec 10
        if ([int]$response.StatusCode -ne 200) {
            throw "MVC smoke request $path returned HTTP $($response.StatusCode)."
        }
        Write-Host "MVC $path -> $($response.StatusCode)"
    }

    Stop-Process -Id $webProcess.Id -Force
    $webProcess = $null

    if (-not (Test-Path -LiteralPath $winFormsExecutable)) {
        throw "WinForms executable not found: $winFormsExecutable"
    }

    $winFormsProcess = Start-Process -FilePath $winFormsExecutable -WorkingDirectory (Split-Path $winFormsExecutable) -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 7
    if ($winFormsProcess.HasExited) {
        throw "WinForms process exited before the smoke check completed."
    }
    $liveProcess = Get-Process -Id $winFormsProcess.Id -ErrorAction Stop
    if ($liveProcess.MainWindowHandle -eq 0) {
        Write-Host "WinForms -> live process (login dialog is required before warehouse forms; hidden smoke launch did not expose a main-window handle)"
    }
    else {
        Write-Host "WinForms -> live (PID $($liveProcess.Id), title '$($liveProcess.MainWindowTitle)')"
    }
    Stop-Process -Id $liveProcess.Id -Force
    $winFormsProcess = $null

    Write-Host 'Baseline verification passed.'
}
finally {
    if ($webProcess -and -not $webProcess.HasExited) {
        Stop-Process -Id $webProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($winFormsProcess -and -not $winFormsProcess.HasExited) {
        Stop-Process -Id $winFormsProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousConnectionString) {
        $env:ConnectionStrings__DefaultConnection = $previousConnectionString
    }
    else {
        Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousEnvironment) {
        $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    }
    else {
        Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    }
    if ($null -ne $previousDatabaseProvider) {
        $env:Wms__DatabaseProvider = $previousDatabaseProvider
    }
    else {
        Remove-Item Env:Wms__DatabaseProvider -ErrorAction SilentlyContinue
    }
    $environmentOverrides = @{
        'Wms__SeedProfile' = $previousSeedProfile
        'Authentication__Bootstrap__Enabled' = $previousBootstrapEnabled
        'Authentication__Bootstrap__UserName' = $previousBootstrapUserName
        'Authentication__Bootstrap__Email' = $previousBootstrapEmail
        'Authentication__Bootstrap__DisplayName' = $previousBootstrapDisplayName
        'Authentication__Bootstrap__EmployeeCode' = $previousBootstrapEmployeeCode
        'WARECOMMAND_ADMIN_BOOTSTRAP_PASSWORD' = $previousBootstrapPassword
        'Authentication__CookieSecure' = $previousCookieSecure
    }
    foreach ($override in $environmentOverrides.GetEnumerator()) {
        if ($null -ne $override.Value) {
            Set-Item "Env:$($override.Key)" $override.Value
        }
        else {
            Remove-Item "Env:$($override.Key)" -ErrorAction SilentlyContinue
        }
    }
    Pop-Location
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

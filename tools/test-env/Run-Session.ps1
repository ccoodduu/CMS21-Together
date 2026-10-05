#Requires -Version 5.1
<#
Runs one test scenario: starts the local server and the test installs, runs scenarios\<Scenario>.ps1,
collects logs, dumps and screenshots in tools\runs\<timestamp>_<Scenario>, then shuts everything down.

The test installs share the real game's save folder and registry settings with the Steam install,
so both are backed up before the run and restored afterwards, whatever happens.
#>
param(
    [string]$Scenario = "connect",
    [string[]]$Instances = @("A", "B"),
    [string]$Window = "960x540",
    [switch]$Sound,
    [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"
$TestRoot = "$env:USERPROFILE\CMS21-TestInstalls"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$gameProcess = "Car Mechanic Simulator 2021"
$saveDir = "$env:USERPROFILE\AppData\LocalLow\Red Dot Games\Car Mechanic Simulator 2021"
$registryKey = "HKCU\Software\Red Dot Games\Car Mechanic Simulator 2021"

Import-Module (Join-Path $PSScriptRoot "HarnessClient.psm1") -Force

$scenarioFile = Join-Path $PSScriptRoot "scenarios\$Scenario.ps1"
if (-not (Test-Path -LiteralPath $scenarioFile)) { throw "Unknown scenario: $Scenario" }
if (Get-Process -Name $gameProcess -ErrorAction SilentlyContinue) { throw "The game is already running; refusing to start a test run." }
if (Get-Process -Name "CMS21_Together_Server" -ErrorAction SilentlyContinue) { throw "A Together server is already running." }

$runDir = Join-Path $repo ("tools\runs\{0}_{1}" -f (Get-Date -Format "yyyyMMdd-HHmmss"), $Scenario)
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$backupDir = Join-Path $runDir "_backup"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
Copy-Item -LiteralPath $saveDir -Destination (Join-Path $backupDir "LocalLow") -Recurse
cmd /c "reg export `"$registryKey`" `"$(Join-Path $backupDir 'settings.reg')`" /y >nul 2>&1"
if ($LASTEXITCODE -ne 0) { throw "Registry backup failed" }

function Restore-GameState {
    Get-ChildItem -LiteralPath $saveDir -Recurse -File -Include *.cms21b | Remove-Item -Force
    Copy-Item -Path (Join-Path $backupDir "LocalLow\*") -Destination $saveDir -Recurse -Force
    $regFile = Join-Path $backupDir "settings.reg"
    cmd /c "reg delete `"$registryKey`" /f >nul 2>&1 & reg import `"$regFile`" >nul 2>&1"
    if ($LASTEXITCODE -ne 0) { Write-Host "WARNING: registry restore failed, import $regFile manually" -ForegroundColor Red }
    Write-Host "Restored saves and registry settings"
}

$result = [ordered]@{ scenario = $Scenario; passed = $false; notes = @(); started = (Get-Date).ToString("s") }
$server = $null
try {
    $serverDir = Join-Path $TestRoot "Server"
    $server = Start-Process -FilePath (Join-Path $serverDir "CMS21_Together_Server.exe") -WorkingDirectory $serverDir -PassThru

    $size = $Window.Split('x')
    foreach ($name in $Instances) {
        $dir = Join-Path $TestRoot $name
        Remove-Item -LiteralPath (Join-Path $dir "UserData\TestHarness\status.json") -ErrorAction SilentlyContinue
        $arguments = @(
            "--melonloader.disablestartscreen", "--melonloader.agfoffline",
            "-screen-fullscreen", "0", "-screen-width", $size[0], "-screen-height", $size[1],
            "--harness.name=$name", "--harness.window=$Window"
        )
        if (-not $Sound) { $arguments += "--harness.mute" }
        Start-Process -FilePath (Join-Path $dir "$gameProcess.exe") -WorkingDirectory $dir -ArgumentList $arguments | Out-Null
        Write-Host "Started instance $name"
    }

    $ctx = [pscustomobject]@{ RunDir = $runDir; Instances = $Instances; Result = $result }
    & $scenarioFile -Ctx $ctx
}
catch {
    $result.notes += "ERROR: $($_.Exception.Message)"
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    if (-not $KeepRunning) {
        foreach ($name in $Instances) {
            try { Send-HarnessCommand -Instance $name -Verb quit -TimeoutSec 5 | Out-Null } catch { }
        }
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Process -Name $gameProcess -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Get-Process -Name $gameProcess -ErrorAction SilentlyContinue | Stop-Process -Force
        if ($server) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 1
    }

    foreach ($name in $Instances) {
        $log = Join-Path $TestRoot "$name\MelonLoader\Latest.log"
        if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $runDir "client_$name.log") }
    }
    $serverLog = Join-Path $TestRoot "Server\Log\Latest.txt"
    if (Test-Path -LiteralPath $serverLog) { Copy-Item -LiteralPath $serverLog -Destination (Join-Path $runDir "server.log") }

    if (-not $KeepRunning) { Restore-GameState } else { Write-Host "KeepRunning: restore saves later with the backup in $backupDir" }

    $result.finished = (Get-Date).ToString("s")
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDir "result.json") -Encoding utf8
    Write-Host ("RESULT {0}: {1}" -f $Scenario, $(if ($result.passed) { "PASSED" } else { "FAILED" }))
    $result.notes | ForEach-Object { Write-Host "  $_" }
    Write-Host "Run folder: $runDir"
}

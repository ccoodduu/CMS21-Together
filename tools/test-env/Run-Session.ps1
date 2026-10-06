#Requires -Version 5.1
<#
Runs one test scenario in one test lane: resets the lane's profiles from the seed, starts the lane's server and
test installs, runs scenarios\<Scenario>.ps1, collects logs, dumps and screenshots in
tools\runs\<timestamp>_L<lane>_<Scenario>, then shuts the lane down.

Each install has its own save folder and registry key (see TestLanes.psm1), so the real game's saves are never
written. The run checks that with a fingerprint of the real save folder and registry key before and after.
#>
param(
    [string]$Scenario = "connect",
    [int]$Lane = 1,
    [string]$Window = "960x540",
    [switch]$Sound,
    [switch]$KeepRunning,
    [int]$MinFreeMemoryGb = 12
)

$ErrorActionPreference = "Stop"
$TestRoot = "$env:USERPROFILE\CMS21-TestInstalls"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$gameProcess = "Car Mechanic Simulator 2021"
$steamLog = "C:\Program Files (x86)\Steam\logs\console_log.txt"

Import-Module (Join-Path $PSScriptRoot "HarnessClient.psm1") -Force
Import-Module (Join-Path $PSScriptRoot "TestLanes.psm1") -Force

$laneInfo = Get-TestLane $Lane
$Instances = $laneInfo.Instances
$serverDir = $laneInfo.ServerDir

$scenarioFile = Join-Path $PSScriptRoot "scenarios\$Scenario.ps1"
if (-not (Test-Path -LiteralPath $scenarioFile)) { throw "Unknown scenario: $Scenario" }
$launchFile = Join-Path $PSScriptRoot "scenarios\$Scenario.launch.psd1"
$launchArgs = if (Test-Path -LiteralPath $launchFile) { Import-PowerShellDataFile -LiteralPath $launchFile } else { $null }

# Two game instances use most of the PC's 32 GB; two lanes at once ran it out of memory and hung clients.
# Lanes therefore take turns with the game: the mutex is held for the whole run.
$gameMutex = New-Object System.Threading.Mutex($false, "Global\CMS21TogetherGameLane")
try {
    if (-not $gameMutex.WaitOne([TimeSpan]::FromMinutes(30))) { throw "Another lane has been running the game for 30 minutes; giving up." }
} catch [System.Threading.AbandonedMutexException] { }
$freeGb = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
if ($freeGb -lt $MinFreeMemoryGb) {
    $gameMutex.ReleaseMutex()
    throw ("Only {0:N1} GB RAM free (need {1} GB for two game instances); not starting." -f $freeGb, $MinFreeMemoryGb)
}
if (Get-LaneGameProcesses $laneInfo) { throw "A game instance of lane $Lane is already running; refusing to start a test run." }
foreach ($name in $Instances) { Assert-InstanceIsolated $name }

$runDir = Join-Path $repo ("tools\runs\{0}_L{1}_{2}" -f (Get-Date -Format "yyyyMMdd-HHmmss"), $Lane, $Scenario)
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$serverSaves = Join-Path $serverDir "Saves"
$serverConfig = Join-Path $serverDir "server_config.ini"
$backupDir = Join-Path $runDir "_backup"
New-Item -ItemType Directory -Force -Path (Join-Path $backupDir "Server") | Out-Null
if (Test-Path -LiteralPath $serverSaves) { Copy-Item -LiteralPath $serverSaves -Destination (Join-Path $backupDir "Server\Saves") -Recurse }
Copy-Item -LiteralPath $serverConfig -Destination (Join-Path $backupDir "Server\server_config.ini")
Initialize-TestServer -ServerDir $serverDir -CommandFile (Join-Path $runDir "server_commands.txt") -ConnectAddress $laneInfo.ConnectAddress

function Restore-ServerState {
    if (Test-Path -LiteralPath $serverSaves) { Remove-Item -LiteralPath $serverSaves -Recurse -Force }
    $savedSaves = Join-Path $backupDir "Server\Saves"
    if (Test-Path -LiteralPath $savedSaves) { Copy-Item -LiteralPath $savedSaves -Destination $serverSaves -Recurse }
    Copy-Item -LiteralPath (Join-Path $backupDir "Server\server_config.ini") -Destination $serverConfig -Force
    Write-Host "Restored server saves and config"
}

function Stop-LaneGames {
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-LaneGameProcesses $laneInfo) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    Get-LaneGameProcesses $laneInfo | Stop-Process -Force
}

function Test-SteamKick([int]$FromLine) {
    if (-not (Test-Path -LiteralPath $steamLog)) { return $false }
    $lines = @(Get-Content -LiteralPath $steamLog -ErrorAction SilentlyContinue)
    if ($lines.Count -le $FromLine) { return $false }
    return [bool]($lines[$FromLine..($lines.Count - 1)] | Select-String -SimpleMatch "waiting for user response to KickingOtherSession")
}

$realFingerprint = Get-RealProfileFingerprint
$result = [ordered]@{ scenario = $Scenario; lane = $Lane; passed = $false; notes = @(); started = (Get-Date).ToString("s") }
$runStart = Get-Date
try {
    foreach ($name in $Instances) { Reset-InstanceProfile $name }
    Start-TestServer | Out-Null

    $steamLogStart = if (Test-Path -LiteralPath $steamLog) { @(Get-Content -LiteralPath $steamLog).Count } else { 0 }
    $size = $Window.Split('x')
    foreach ($name in $Instances) {
        $dir = Join-Path $TestRoot $name
        $harnessDir = Join-Path $dir "UserData\TestHarness"
        Remove-Item -LiteralPath (Join-Path $harnessDir "status.json") -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $harnessDir -Filter "reply_*.json" -ErrorAction SilentlyContinue | Remove-Item -Force
        Remove-Item -LiteralPath (Join-Path $harnessDir "command.txt") -ErrorAction SilentlyContinue
        $arguments = @(
            "--melonloader.disablestartscreen", "--melonloader.agfoffline",
            "-screen-fullscreen", "0", "-screen-width", $size[0], "-screen-height", $size[1],
            "--harness.name=$name", "--harness.window=$Window"
        )
        if (-not $Sound) { $arguments += "--harness.mute" }
        $role = @("A", "B")[[array]::IndexOf($Instances, $name)]
        if ($launchArgs -and $launchArgs.ContainsKey($role)) {
            $arguments += @($launchArgs[$role] | ForEach-Object { $_.Replace("{port}", "$($laneInfo.Port)") })
        }
        Start-Process -FilePath (Join-Path $dir "$gameProcess.exe") -WorkingDirectory $dir -ArgumentList $arguments | Out-Null
        Write-Host "Started instance $name"
    }

    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        if (Test-SteamKick $steamLogStart) {
            & (Join-Path $PSScriptRoot "Cancel-SteamLaunch.ps1") | Out-Null
            throw "Steam asks to end a session on another device (KickingOtherSession); run aborted. Set Steam offline on the other device."
        }
        if (@($Instances | Where-Object { Get-HarnessStatus $_ }).Count -eq $Instances.Count) { break }
        Start-Sleep -Seconds 2
    }

    $ctx = [pscustomobject]@{ RunDir = $runDir; Instances = $Instances; Result = $result; ServerDir = $serverDir; Lane = $laneInfo }
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
        Stop-LaneGames
        Stop-TestServer
        Start-Sleep -Seconds 1
    }

    foreach ($name in $Instances) {
        $log = Join-Path $TestRoot "$name\MelonLoader\Latest.log"
        if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $runDir "client_$name.log") }
        $playerLog = Join-Path (Get-InstanceSaveDir $name) "Player.log"
        if (Test-Path -LiteralPath $playerLog) { Copy-Item -LiteralPath $playerLog -Destination (Join-Path $runDir "player_$name.log") }
    }
    $serverLogs = @(Get-ChildItem -LiteralPath (Join-Path $serverDir "Log") -Filter "Log_*.txt" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $runStart } | Sort-Object Name)
    for ($i = 0; $i -lt $serverLogs.Count; $i++) {
        $target = if ($i -eq $serverLogs.Count - 1) { "server.log" } else { "server_$($i + 1).log" }
        Copy-Item -LiteralPath $serverLogs[$i].FullName -Destination (Join-Path $runDir $target)
    }
    if (Test-Path -LiteralPath $serverSaves) { Copy-Item -LiteralPath $serverSaves -Destination (Join-Path $runDir "server_saves_after") -Recurse }

    if (-not $KeepRunning) { Restore-ServerState } else { Write-Host "KeepRunning: server saves backup in $backupDir" }

    foreach ($name in $Instances) {
        $clientLog = Join-Path $runDir "client_$name.log"
        if ((Test-Path -LiteralPath $clientLog) -and (Select-String -LiteralPath $clientLog -Pattern "HarmonyException" -Quiet)) {
            $result.passed = $false
            $result.notes += "HARMONY PATCH ERROR in client $name (a failed patch can disable every later patch)"
        }
    }

    if ((Get-RealProfileFingerprint) -ne $realFingerprint) {
        $result.passed = $false
        $result.notes += "REAL SAVE FOLDER OR REGISTRY CHANGED DURING THE RUN"
        Write-Host "WARNING: the real save folder or registry key changed during the run" -ForegroundColor Red
    }

    $result.finished = (Get-Date).ToString("s")
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDir "result.json") -Encoding utf8
    Write-Host ("RESULT L{0} {1}: {2}" -f $Lane, $Scenario, $(if ($result.passed) { "PASSED" } else { "FAILED" }))
    $result.notes | ForEach-Object { Write-Host "  $_" }
    Write-Host "Run folder: $runDir"
    $gameMutex.ReleaseMutex()
}

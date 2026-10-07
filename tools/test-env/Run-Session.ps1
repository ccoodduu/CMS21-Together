#Requires -Version 5.1
<#
Runs one test scenario in one test lane: resets the lane's profiles from the seed, starts the lane's server and
test installs, runs scenarios\<Scenario>.ps1, collects logs, dumps and screenshots in
tools\runs\<timestamp>_L<lane>_<Scenario>, then shuts the lane down.

Batch mode (-Scenarios a,b,c) starts the games once and runs the scenarios one after the other with the same clients.
Before each scenario every client goes back to the main menu and gets harness-reset, and the server is restarted from
the state saved at the batch start (tools\runs\<timestamp>_L<lane>_batch). Each scenario still gets its own run
folder, with the part of each client's log written during it, and its own RESULT line (marked "(batch)").
Scenarios with a .launch.psd1 or a "# run-all: fresh" line run as single sessions after the batch, and so does the
rest of a batch whose clients do not get back to the menu within 90 s.

Lane locks: the run holds the lock of every lane it uses (lane 3: lanes 1 and 2) from start to end, waiting up to
-LaneWaitMinutes for a busy lane. -Deploy runs Deploy-Mod.ps1 for the lane inside the locks. -ScenarioArgs is a
hashtable splatted into the scenario after -Ctx. -Headless C,D starts those instances with -batchmode -nographics.

Each install has its own save folder and registry key (see TestLanes.psm1), so the real game's saves are never
written. The run checks that with a fingerprint of the real save folder and registry key before and after.
#>
param(
    [string]$Scenario = "connect",
    [string[]]$Scenarios,
    [int]$Lane = 1,
    [string]$Window = "960x540",
    [switch]$Sound,
    [switch]$KeepRunning,
    [int]$MinCommitHeadroomGb = 0,
    [int]$MinFreeMemoryGb = 0,
    [hashtable]$ScenarioArgs = @{},
    [switch]$Deploy,
    [switch]$NoBuild,
    [double]$LaneWaitMinutes = 120,
    [string[]]$Headless = @()
)

$ErrorActionPreference = "Stop"
$TestRoot = "$env:USERPROFILE\CMS21-TestInstalls"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$thisScript = $PSCommandPath

Import-Module (Join-Path $PSScriptRoot "HarnessClient.psm1") -Force
Import-Module (Join-Path $PSScriptRoot "TestLanes.psm1") -Force

$laneInfo = Get-TestLane $Lane
$Instances = $laneInfo.Instances
$serverDir = $laneInfo.ServerDir
if ($MinCommitHeadroomGb -le 0) { $MinCommitHeadroomGb = $laneInfo.MinCommitHeadroomGb }
if ($MinFreeMemoryGb -le 0) { $MinFreeMemoryGb = $laneInfo.MinFreeMemoryGb }
$Headless = @($Headless | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ })
foreach ($name in $Headless) { if ($Instances -notcontains $name) { throw "-Headless $name is not an instance of lane $Lane ($($Instances -join ', '))" } }

$batchMode = $Scenarios.Count -gt 0
$names = if ($batchMode) { @($Scenarios) } else { @($Scenario) }
foreach ($name in $names) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot "scenarios\$name.ps1"))) { throw "Unknown scenario: $name" }
}
if ($batchMode -and $KeepRunning) { throw "-KeepRunning works only with a single -Scenario." }

# Each game instance commits 8-10 GB (mostly Direct3D) but touches only 2-4 GB, so the Windows commit limit, not RAM,
# decides how many lanes fit. A lane takes the launch mutex, waits for enough commit headroom, starts its games and
# releases the mutex once they reach the main menu, so the next lane measures the headroom with them loaded.
$gameMutex = New-Object System.Threading.Mutex($false, "Global\CMS21TogetherGameLane")
$holdingMutex = $false
function Exit-LaunchLock {
    if ($script:holdingMutex) { $script:holdingMutex = $false; $gameMutex.ReleaseMutex() }
}
function Enter-LaunchLock {
    try {
        if (-not $gameMutex.WaitOne([TimeSpan]::FromMinutes(30))) { throw "Another lane has been starting its games for 30 minutes; giving up." }
    } catch [System.Threading.AbandonedMutexException] { }
    $script:holdingMutex = $true
    try {
        Wait-MemoryHeadroom -MinCommitHeadroomGb $MinCommitHeadroomGb -MinFreeMemoryGb $MinFreeMemoryGb | Out-Null
        if (Get-LaneGameProcesses $laneInfo) { throw "A game instance of lane $Lane is already running; refusing to start a test run." }
        foreach ($name in $Instances) { Assert-InstanceIsolated $name }
    } catch {
        Exit-LaunchLock
        throw
    }
}

function New-RunDir([string]$Name) {
    $dir = Join-Path $repo ("tools\runs\{0}_L{1}_{2}" -f (Get-Date -Format "yyyyMMdd-HHmmss"), $Lane, $Name)
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

$serverSaves = Join-Path $serverDir "Saves"
$serverConfig = Join-Path $serverDir "server_config.ini"
$runMarker = Join-Path $serverDir "harness_run_in_progress.txt"
$backupDir = $null

function Restore-InterruptedRun {
    if (-not (Test-Path -LiteralPath $runMarker)) { return }
    $interrupted = (Get-Content -LiteralPath $runMarker -Raw).Trim()
    if (Test-Path -LiteralPath (Join-Path $interrupted "Server\server_config.ini")) {
        if (Test-Path -LiteralPath $serverSaves) { Remove-Item -LiteralPath $serverSaves -Recurse -Force }
        $interruptedSaves = Join-Path $interrupted "Server\Saves"
        if (Test-Path -LiteralPath $interruptedSaves) { Copy-Item -LiteralPath $interruptedSaves -Destination $serverSaves -Recurse }
        Copy-Item -LiteralPath (Join-Path $interrupted "Server\server_config.ini") -Destination $serverConfig -Force
        Write-Host "Restored the server state an interrupted run left behind (backup $interrupted)"
    }
    Remove-Item -LiteralPath $runMarker -Force
}

function Save-ServerBackup([string]$Dir) {
    $script:backupDir = $Dir
    New-Item -ItemType Directory -Force -Path (Join-Path $Dir "Server") | Out-Null
    if (Test-Path -LiteralPath $serverSaves) { Copy-Item -LiteralPath $serverSaves -Destination (Join-Path $Dir "Server\Saves") -Recurse }
    Copy-Item -LiteralPath $serverConfig -Destination (Join-Path $Dir "Server\server_config.ini")
    Set-Content -LiteralPath $runMarker -Value $Dir -Encoding utf8
}

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

function Stop-LaneSession {
    foreach ($name in $Instances) {
        try { Send-HarnessCommand -Instance $name -Verb quit -TimeoutSec 5 | Out-Null } catch { }
    }
    Stop-LaneGames
    Stop-TestServer
    Start-Sleep -Seconds 1
}

function Start-LaneGames($LaunchArgs) {
    $launches = foreach ($name in $Instances) {
        $role = @("A", "B", "C", "D")[[array]::IndexOf($Instances, $name)]
        $extra = if ($LaunchArgs -and $LaunchArgs.ContainsKey($role)) { @($LaunchArgs[$role]) } else { @() }
        Start-HarnessInstance -Lane $Lane -Instance $name -Window $Window -Sound:$Sound -Headless:($Headless -contains $name) `
            -ExtraArguments $extra -NoWait -SkipGate
    }
    try { Wait-HarnessInstances -Launches @($launches) | Out-Null }
    finally { Exit-LaunchLock }
}

function Save-ServerLogs([string]$RunDir, [datetime]$Since) {
    $serverLogs = @(Get-ChildItem -LiteralPath (Join-Path $serverDir "Log") -Filter "Log_*.txt" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $Since } | Sort-Object Name)
    for ($i = 0; $i -lt $serverLogs.Count; $i++) {
        $target = if ($i -eq $serverLogs.Count - 1) { "server.log" } else { "server_$($i + 1).log" }
        Copy-Item -LiteralPath $serverLogs[$i].FullName -Destination (Join-Path $RunDir $target)
    }
    $perfLogs = @(Get-ChildItem -LiteralPath (Join-Path $serverDir "Log") -Filter "perf_*.jsonl" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $Since } | Sort-Object Name)
    if ($perfLogs.Count -gt 0) {
        $perfLines = [string[]]@($perfLogs | ForEach-Object { [System.IO.File]::ReadAllLines($_.FullName) })
        [System.IO.File]::WriteAllLines((Join-Path $RunDir "perf_server.jsonl"), $perfLines)
    }
    $desyncRecords = @(Get-ChildItem -LiteralPath (Join-Path $serverDir "Log\desync") -Filter "*.json" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $Since })
    if ($desyncRecords.Count -gt 0) {
        $target = Join-Path $RunDir "desync"
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        $desyncRecords | Copy-Item -Destination $target
    }
    if (Test-Path -LiteralPath $serverSaves) { Copy-Item -LiteralPath $serverSaves -Destination (Join-Path $RunDir "server_saves_after") -Recurse }
}

function New-RunResult([string]$Name) {
    $deployed = Get-LaneDeployedBuilds $laneInfo
    if ($deployed.Mixed) { Write-Host "WARNING: the installs and server of lane $Lane carry different builds (see deployed in result.json)" -ForegroundColor Yellow }
    $result = [ordered]@{ scenario = $Name; lane = $Lane; passed = $false; notes = @(); started = (Get-Date).ToString("s"); deployed = $deployed.Builds }
    if ($deployed.Mixed) { $result.notes += "WARNING: mixed builds in lane $Lane (deployed in result.json)" }
    if ($ScenarioArgs.Count -gt 0) { $result.scenarioArgs = $ScenarioArgs }
    if ($Headless.Count -gt 0) { $result.headless = $Headless }
    return $result
}

function New-ScenarioContext([string]$RunDir, $Result, [bool]$Batch) {
    [pscustomobject]@{
        RunDir = $RunDir; Instances = $Instances; Result = $Result; ServerDir = $serverDir; Lane = $laneInfo; Batch = $Batch
        Launch = [pscustomobject]@{ Lane = $Lane; Window = $Window; Sound = [bool]$Sound; Headless = $Headless }
    }
}

$realFingerprint = $null

function Complete-Run($Result, [string]$RunDir, [string[]]$HarmonyAtLaunch = @()) {
    foreach ($name in $Instances) {
        $clientLog = Join-Path $RunDir "client_$name.log"
        if (($HarmonyAtLaunch -contains $name) -or ((Test-Path -LiteralPath $clientLog) -and (Select-String -LiteralPath $clientLog -Pattern "HarmonyException" -Quiet))) {
            $Result.passed = $false
            $Result.notes += "HARMONY PATCH ERROR in client $name (a failed patch can disable every later patch)"
        }
    }

    $fingerprint = Get-RealProfileFingerprint
    if ($fingerprint -ne $realFingerprint) {
        $Result.passed = $false
        $Result.notes += "REAL SAVE FOLDER OR REGISTRY CHANGED DURING THE RUN"
        Write-Host "WARNING: the real save folder or registry key changed during the run" -ForegroundColor Red
    }
    $script:realFingerprint = $fingerprint

    $Result.finished = (Get-Date).ToString("s")
    $Result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $RunDir "result.json") -Encoding utf8
    Write-Host ("RESULT L{0} {1}: {2}{3}" -f $Lane, $Result.scenario, $(if ($Result.passed) { "PASSED" } else { "FAILED" }), $(if ($Result.batch) { " (batch)" } else { "" }))
    $Result.notes | ForEach-Object { Write-Host "  $_" }
    Write-Host "Run folder: $RunDir"
}

function Invoke-FreshSession([string]$Name) {
    try {
        & $thisScript -Scenario $Name -Lane $Lane -Window $Window -Sound:$Sound -MinCommitHeadroomGb $MinCommitHeadroomGb `
            -MinFreeMemoryGb $MinFreeMemoryGb -ScenarioArgs $ScenarioArgs -LaneWaitMinutes $LaneWaitMinutes -Headless $Headless
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ("RESULT L{0} {1}: FAILED" -f $Lane, $Name)
    }
}

$clientLogs = @{}
$logOffsets = @{}
$logCreated = @{}
foreach ($name in $Instances) {
    $clientLogs["client_$name"] = Join-Path $TestRoot "$name\MelonLoader\Latest.log"
    $clientLogs["player_$name"] = Join-Path (Get-InstanceSaveDir $name) "Player.log"
}
foreach ($key in @($clientLogs.Keys)) { $logOffsets[$key] = [long]0; $logCreated[$key] = $null }

# The games keep their logs open, so each scenario gets the bytes written since the previous copy. A game relaunched
# inside a scenario writes a new log, which is copied from its start.
function Save-ClientLogSlices([string]$Dir, [string]$Suffix = "") {
    foreach ($key in @($clientLogs.Keys)) {
        $path = $clientLogs[$key]
        if (-not (Test-Path -LiteralPath $path)) { continue }
        try {
            $created = (Get-Item -LiteralPath $path).CreationTimeUtc
            if ($null -ne $logCreated[$key] -and $created -ne $logCreated[$key]) { $logOffsets[$key] = [long]0 }
            $logCreated[$key] = $created
            $source = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
            try {
                if ($source.Length -lt $logOffsets[$key]) { $logOffsets[$key] = [long]0 }
                $source.Position = $logOffsets[$key]
                $target = [System.IO.File]::Create((Join-Path $Dir "$key$Suffix.log"))
                try { $source.CopyTo($target) } finally { $target.Dispose() }
                $logOffsets[$key] = $source.Position
            } finally { $source.Dispose() }
        } catch {
            Write-Host "Could not copy $path : $($_.Exception.Message)"
        }
    }
}

function Reset-LaneClients {
    if (@(Get-LaneGameProcesses $laneInfo).Count -lt $Instances.Count) { throw "A game instance of lane $Lane is no longer running." }
    $inMenu = { param($s) $s -and $s.scene -eq "Menu" -and $s.playable -and -not $s.connected }
    $sent = $false
    foreach ($name in $Instances) {
        if (& $inMenu (Get-HarnessStatus $name)) { continue }
        try { Send-HarnessCommand -Instance $name -Verb to-menu -TimeoutSec 30 | Out-Null } catch { Write-Host "to-menu on $name : $($_.Exception.Message)" }
        $sent = $true
    }
    if ($sent) { Start-Sleep -Seconds 2 }
    $deadline = (Get-Date).AddSeconds(90)
    while ($true) {
        $waiting = @($Instances | Where-Object { -not (& $inMenu (Get-HarnessStatus $_)) })
        if ($waiting.Count -eq 0) { break }
        if ((Get-Date) -gt $deadline) { throw "$($waiting -join ', ') did not get back to the main menu within 90 s." }
        Start-Sleep -Milliseconds 500
    }
    $reset = [ordered]@{}
    foreach ($name in $Instances) {
        $reset[$name] = @((Send-HarnessCommand -Instance $name -Verb harness-reset -TimeoutSec 30).reset)
        if ($reset[$name].Count -gt 0) { Write-Host "harness-reset $name : $($reset[$name] -join '; ')" }
    }
    return $reset
}

function Invoke-BatchScenario([string]$Name, $HarnessReset) {
    $runDir = New-RunDir $Name
    Copy-Item -LiteralPath $backupDir -Destination (Join-Path $runDir "_backup") -Recurse
    Restore-ServerState
    Initialize-TestServer -ServerDir $serverDir -CommandFile (Join-Path $runDir "server_commands.txt") -ConnectAddress $laneInfo.ConnectAddress

    $result = New-RunResult $Name
    $result.batch = $true
    $result.harnessReset = $HarnessReset
    $scenarioStart = Get-Date
    try {
        Start-TestServer | Out-Null
        $ctx = New-ScenarioContext $runDir $result $true
        & (Join-Path $PSScriptRoot "scenarios\$Name.ps1") -Ctx $ctx @ScenarioArgs
    }
    catch {
        $where = @($_.ScriptStackTrace -split "`n" | Select-Object -First 2) -join " <- "
        $result.notes += "ERROR: $($_.Exception.Message) (at $where)"
        Write-Host "ERROR: $($_.Exception.Message) (at $where)" -ForegroundColor Red
    }
    finally {
        Stop-TestServer
        Start-Sleep -Seconds 1
        Save-ClientLogSlices $runDir
        Save-ServerLogs $runDir $scenarioStart
        Complete-Run $result $runDir $harmonyAtLaunch
    }
}

$laneLocks = Enter-LaneLocks -Lane $Lane -WaitMinutes $LaneWaitMinutes
try {
    if ($Deploy) { & (Join-Path $PSScriptRoot "Deploy-Mod.ps1") -Lane $Lane -NoBuild:$NoBuild | Out-Host }

    if (-not $batchMode) {
        $scenarioFile = Join-Path $PSScriptRoot "scenarios\$Scenario.ps1"
        $launchFile = Join-Path $PSScriptRoot "scenarios\$Scenario.launch.psd1"
        $launchArgs = if (Test-Path -LiteralPath $launchFile) { Import-PowerShellDataFile -LiteralPath $launchFile } else { $null }

        Write-Host "SCENARIO L$Lane $Scenario"
        Enter-LaunchLock
        try {
            $runDir = New-RunDir $Scenario
            Restore-InterruptedRun
            Save-ServerBackup (Join-Path $runDir "_backup")
            Initialize-TestServer -ServerDir $serverDir -CommandFile (Join-Path $runDir "server_commands.txt") -ConnectAddress $laneInfo.ConnectAddress
            $realFingerprint = Get-RealProfileFingerprint
        } catch {
            Exit-LaunchLock
            throw
        }
        $result = New-RunResult $Scenario
        $runStart = Get-Date
        try {
            foreach ($name in $Instances) { Reset-InstanceProfile $name }
            Start-TestServer | Out-Null
            Start-LaneGames $launchArgs

            $ctx = New-ScenarioContext $runDir $result $false
            & $scenarioFile -Ctx $ctx @ScenarioArgs
        }
        catch {
            $where = @($_.ScriptStackTrace -split "`n" | Select-Object -First 2) -join " <- "
            $result.notes += "ERROR: $($_.Exception.Message) (at $where)"
            Write-Host "ERROR: $($_.Exception.Message) (at $where)" -ForegroundColor Red
        }
        finally {
            if (-not $KeepRunning) { Stop-LaneSession }

            foreach ($name in $Instances) {
                $log = Join-Path $TestRoot "$name\MelonLoader\Latest.log"
                if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $runDir "client_$name.log") }
                $playerLog = Join-Path (Get-InstanceSaveDir $name) "Player.log"
                if (Test-Path -LiteralPath $playerLog) { Copy-Item -LiteralPath $playerLog -Destination (Join-Path $runDir "player_$name.log") }
            }
            Save-ServerLogs $runDir $runStart

            if (-not $KeepRunning) { Restore-ServerState; Remove-Item -LiteralPath $runMarker -Force -ErrorAction SilentlyContinue } else { Write-Host "KeepRunning: server saves backup in $backupDir" }

            Complete-Run $result $runDir
            Exit-LaunchLock
        }
    } else {
        $freshNames = @($names | Where-Object { Test-ScenarioNeedsFreshGame (Join-Path $PSScriptRoot "scenarios\$_.ps1") })
        $batchNames = New-Object System.Collections.Generic.List[string]
        $names | Where-Object { $freshNames -notcontains $_ } | ForEach-Object { $batchNames.Add($_) }
        if ($freshNames.Count -gt 0) { Write-Host "Single sessions after the batch: $($freshNames -join ', ')" }

        $batchTotal = $batchNames.Count
        $harmonyAtLaunch = @()
        $stopReason = $null
        if ($batchTotal -gt 0) {
            Enter-LaunchLock
            try {
                $batchDir = New-RunDir "batch"
                Restore-InterruptedRun
                Save-ServerBackup (Join-Path $batchDir "_backup")
                Initialize-TestServer -ServerDir $serverDir -CommandFile (Join-Path $batchDir "server_commands.txt") -ConnectAddress $laneInfo.ConnectAddress
                $realFingerprint = Get-RealProfileFingerprint
            } catch {
                Exit-LaunchLock
                throw
            }
            try {
                try {
                    foreach ($name in $Instances) { Reset-InstanceProfile $name }
                    Start-LaneGames $null
                    Save-ClientLogSlices $batchDir "_launch"
                    $harmonyAtLaunch = @($Instances | Where-Object { Select-String -LiteralPath (Join-Path $batchDir "client_$($_)_launch.log") -Pattern "HarmonyException" -Quiet -ErrorAction SilentlyContinue })
                } catch {
                    $stopReason = "the games did not start: $($_.Exception.Message)"
                }
                while (-not $stopReason -and $batchNames.Count -gt 0) {
                    $name = $batchNames[0]
                    Write-Host ("SCENARIO L{0} {1}" -f $Lane, $name)
                    Write-Host ("Batch {0}/{1}" -f ($batchTotal - $batchNames.Count + 1), $batchTotal)
                    try { $harnessReset = Reset-LaneClients }
                    catch { $stopReason = $_.Exception.Message; break }
                    $batchNames.RemoveAt(0)
                    Invoke-BatchScenario $name $harnessReset
                }
            }
            finally {
                try { Stop-LaneSession } catch { Write-Host "ERROR stopping the lane: $($_.Exception.Message)" -ForegroundColor Red }
                Save-ClientLogSlices $batchDir "_end"
                Restore-ServerState
                Remove-Item -LiteralPath $runMarker -Force -ErrorAction SilentlyContinue
                Exit-LaunchLock
            }
            if ($stopReason) { Write-Host "Batch stopped: $stopReason; the remaining $($batchNames.Count) scenario(s) run as single sessions." -ForegroundColor Red }
            Write-Host ("BATCH L{0}: {1} of {2} scenarios ran in the batch, folder {3}" -f $Lane, ($batchTotal - $batchNames.Count), $batchTotal, $batchDir)
        }

        foreach ($name in @($batchNames) + $freshNames) { Invoke-FreshSession $name }
    }
}
finally {
    Exit-LaneLocks $laneLocks
}

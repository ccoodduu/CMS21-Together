# Hosting from the game: A starts the lane's server through mp-host (new session on Expert, old save moved aside),
# joins it as admin, B joins and plays on Expert; mp-host stop saves, B sees the shutdown and the process is gone;
# a second start continues on Expert; quitting A stops the server.
param($Ctx)

$a, $b = $Ctx.Instances
$exe = Join-Path $Ctx.ServerDir "CMS21_Together_Server.exe"
$saveFile = Join-Path $Ctx.ServerDir "Saves\server_save.json"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "$Name in session in the garage" -Condition {
        param($s) $s.joinStatus -eq "InSession" -and $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Menu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 120 -What "$Name in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

function Get-LaneServerProcesses {
    @(Get-Process -Name "CMS21_Together_Server" -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ieq $exe })
}

function Wait-NoServer([int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-LaneServerProcesses).Count -gt 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    return (Get-LaneServerProcesses).Count -eq 0
}

$oldSaveDirs = @(Get-ChildItem -LiteralPath $Ctx.ServerDir -Directory -Filter "Saves_old_*" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
$hadSave = Test-Path -LiteralPath $saveFile

try {
    foreach ($name in $Ctx.Instances) { Wait-Menu $name }
    Stop-TestServer

    Send-HarnessCommand -Instance $a -Verb mp-ui -Arguments "open host" | Out-Null
    Start-Sleep -Seconds 1
    Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "host-panel"
    Send-HarnessCommand -Instance $a -Verb mp-ui -Arguments "close" | Out-Null

    $host1 = Send-HarnessCommand -Instance $a -Verb mp-host -Arguments "start serverPath=`"$exe`" port=$($Ctx.Lane.Port) new=true difficulty=Expert"
    Check ($host1.state -eq "Starting") "mp-host start begins (state $($host1.state), $($host1.message))"
    Wait-InGarage $a
    $sessionA = (Send-HarnessCommand -Instance $a -Verb dump).session
    Check ($sessionA.host.state -eq "Running") "host state Running (got $($sessionA.host.state))"
    Check ($sessionA.serverInfo.isAdmin -eq $true) "the host is admin of its server"
    Check ($sessionA.serverInfo.difficulty -eq "Expert" -and $sessionA.gameDifficulty -eq "Expert") "new session on Expert (server $($sessionA.serverInfo.difficulty), game $($sessionA.gameDifficulty))"
    $newSaveDirs = @(Get-ChildItem -LiteralPath $Ctx.ServerDir -Directory -Filter "Saves_old_*" | Where-Object { $oldSaveDirs -notcontains $_.FullName })
    if ($hadSave) {
        Check ($newSaveDirs.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $newSaveDirs[0].FullName "server_save.json"))) "start over kept the old save in Saves_old_* ($($newSaveDirs.Count) folders)"
    }

    Send-HarnessCommand -Instance $b -Verb mp-join -Arguments $Ctx.Lane.ConnectAddress | Out-Null
    Wait-InGarage $b
    $sessionB = (Send-HarnessCommand -Instance $b -Verb dump).session
    Check ($sessionB.serverInfo.difficulty -eq "Expert" -and $sessionB.gameDifficulty -eq "Expert") "B plays on Expert (server $($sessionB.serverInfo.difficulty), game $($sessionB.gameDifficulty))"
    Check ($sessionB.serverInfo.isAdmin -eq $false) "B is not admin"

    $savedBefore = (Get-Item -LiteralPath $saveFile).LastWriteTimeUtc
    Start-Sleep -Seconds 2
    Send-HarnessCommand -Instance $a -Verb mp-host -Arguments "stop" | Out-Null
    $s = Wait-HarnessStatus -Instance $b -TimeoutSec 60 -What "B sees the shutdown" -Condition { param($s) $s.joinStatus -eq "Disconnected" -or $s.joinStatus -eq "Failed" }
    Check ($s.lastDisconnect.reason -eq "ServerShutdown") "B ends Disconnected/ServerShutdown (got $($s.joinStatus)/$($s.lastDisconnect.reason))"
    Check (Wait-NoServer 20) "no server process from the lane's server dir after mp-host stop"
    Check ((Get-Item -LiteralPath $saveFile).LastWriteTimeUtc -gt $savedBefore) "the stop saved the session"
    Check ((Send-HarnessCommand -Instance $a -Verb mp-host -Arguments "status").state -eq "Idle") "host state Idle after stop"
    Wait-Menu $a
    Wait-Menu $b
    Send-HarnessCommand -Instance $b -Verb mp-ui -Arguments "ok" | Out-Null

    Send-HarnessCommand -Instance $a -Verb mp-host -Arguments "start serverPath=`"$exe`" port=$($Ctx.Lane.Port)" | Out-Null
    Wait-InGarage $a
    $sessionA = (Send-HarnessCommand -Instance $a -Verb dump).session
    Check ($sessionA.serverInfo.difficulty -eq "Expert") "continuing keeps Expert (got $($sessionA.serverInfo.difficulty))"
    Check ($sessionA.serverInfo.isAdmin -eq $true) "the host is admin again (new key per start)"

    $quitAt = Get-Date
    try { Send-HarnessCommand -Instance $a -Verb quit -TimeoutSec 5 | Out-Null } catch { }
    Check (Wait-NoServer 15) "quitting the game stops the server within 15 s ($([int]((Get-Date) - $quitAt).TotalSeconds) s)"
}
finally {
    Get-ChildItem -LiteralPath $Ctx.ServerDir -Directory -Filter "Saves_old_*" -ErrorAction SilentlyContinue |
        Where-Object { $oldSaveDirs -notcontains $_.FullName } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

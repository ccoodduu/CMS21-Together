# session-persistence-and-rejoin 7.4 (and 5.3): A and B connect and change the shared stats, A walks to another spot,
# B leaves (the server saves). The server is killed with A in the garage: A must reach the menu within 12 s. After a
# restart both reconnect and get the state and A's position from before the kill. Then "stop" with both connected
# sends both to the menu with the shutdown reason and writes a newer save. --check-save still reads the v1 fixture.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-InMenu([string]$Name, [int]$TimeoutSec) {
    try {
        Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "menu, disconnected" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected }
    } catch { $null }
}

function Distance($p, $q) { [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }

$savePath = Join-Path $Ctx.ServerDir "Saves\server_save.json"

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b

$before = Send-HarnessCommand -Instance $a -Verb dump
Send-HarnessCommand -Instance $a -Verb stats-add -Arguments "4 250" | Out-Null
Send-HarnessCommand -Instance $b -Verb stats-add -Arguments "3 150" | Out-Null
$deadline = (Get-Date).AddSeconds(20)
do { Start-Sleep -Milliseconds 500; $changed = Send-HarnessCommand -Instance $a -Verb dump } while ($changed.stats.scrap -ne $before.stats.scrap + 7 -and (Get-Date) -lt $deadline)
Check ($changed.stats.scrap -eq $before.stats.scrap + 7) "both stats changes reached A ($($before.stats.scrap) -> $($changed.stats.scrap))"

$start = $changed.local.position
Send-HarnessCommand -Instance $a -Verb teleport -Arguments ([string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0},{1},{2},{3}", $start.x - 1.0, $start.y + 1.0, $start.z + 1.0, 45)) | Out-Null
Start-Sleep -Seconds 3

$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
$saved = $null
try { $saved = Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 } catch { }
Check ($null -ne $saved) "the server saved after B left"
Wait-InMenu $b 60 | Out-Null
$dumpBeforeKill = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "before-kill"

Stop-TestServer
$killedAt = Get-Date
Write-Host "Server killed"
$lost = Wait-InMenu $a 30
$elapsed = ((Get-Date) - $killedAt).TotalSeconds
Check ($null -ne $lost -and $elapsed -le 12) "A reached the menu $([math]::Round($elapsed, 1)) s after the kill"
if ($lost) { Check ($lost.joinStatus -eq "Disconnected") "A's join status after the kill is Disconnected ($($lost.joinStatus), $($lost.lastDisconnect.reason): $($lost.lastDisconnect.message))" }
Send-HarnessCommand -Instance $a -Verb mp-ui -Arguments "ok" | Out-Null

Start-TestServer | Out-Null
Write-Host "Server restarted"
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
Start-Sleep -Seconds 3

$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "after-restart"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "after-restart"
foreach ($pair in @(@($a, $dumpA), @($b, $dumpB))) {
    $diff = Compare-HarnessDumps $dumpBeforeKill $pair[1] -Sections @("stats", "inventory")
    Check ($diff.Count -eq 0) "$($pair[0]) has the state from before the kill (differ: $($diff -join ', '))"
}
$offset = Distance $dumpA.local.position $dumpBeforeKill.local.position
Check ($offset -lt 0.5) "A is back at its position from before the kill ($([math]::Round($offset, 3)) m away)"

$saveBefore = (Get-Item -LiteralPath $savePath).LastWriteTimeUtc
Start-Sleep -Seconds 1
$mark = Get-ServerLogMark
Send-ServerCommand "stop"
$stopSaved = $null
try { $stopSaved = Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 } catch { }
Check ($null -ne $stopSaved) "stop saved the session"
foreach ($name in $Ctx.Instances) {
    $menu = Wait-InMenu $name 60
    Check ($null -ne $menu -and $menu.lastDisconnect.reason -eq "ServerShutdown") "$name reached the menu with the shutdown reason ($($menu.lastDisconnect.reason): $($menu.lastDisconnect.message))"
    try { Send-HarnessCommand -Instance $name -Verb mp-ui -Arguments "ok" | Out-Null } catch { }
}
$saveAfter = (Get-Item -LiteralPath $savePath).LastWriteTimeUtc
Check ($saveAfter -gt $saveBefore) "the save is newer after stop ($($saveBefore.ToString('o')) -> $($saveAfter.ToString('o')))"

$fixture = Join-Path $PSScriptRoot "..\fixtures\server_save_v1.json"
$checkOutput = & (Join-Path $Ctx.ServerDir "CMS21_Together_Server.exe") --check-save $fixture | Out-String
Check ($LASTEXITCODE -eq 0) "--check-save reads the v1 fixture (exit $LASTEXITCODE)"
Set-Content -LiteralPath (Join-Path $Ctx.RunDir "check-save.txt") -Value $checkOutput

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

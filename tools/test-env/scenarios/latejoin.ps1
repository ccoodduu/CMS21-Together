# areas: connect, persistence, cars, parts, smoke
# session-persistence-and-rejoin 7.3 (and 5.1, 5.2): A connects alone, changes the shared stats, spawns a car and
# unmounts a part. B joins late and, while its garage is still loading, sends a stats change that the server must drop.
# B must end with A's stats, inventory and cars, both must see each other and the server must drop nothing else.
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$loader = 0
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$TimeoutSec = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready within $TimeoutSec s"
}

function Dropped-Lines([int]$After) {
    @(Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $After | Where-Object { $_ -match "is not in session .*: dropped " })
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
$markA = Get-ServerLogMark
Connect-HarnessInstance $a; Wait-InGarage $a
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments "Off" | Out-Null

$before = Send-HarnessCommand -Instance $a -Verb dump
Send-HarnessCommand -Instance $a -Verb stats-add -Arguments "5 200" | Out-Null
$deadline = (Get-Date).AddSeconds(20)
do { Start-Sleep -Milliseconds 500; $changed = Send-HarnessCommand -Instance $a -Verb dump } while ($changed.stats.scrap -eq $before.stats.scrap -and (Get-Date) -lt $deadline)
Check ($changed.stats.scrap -eq $before.stats.scrap + 5) "stats-add reached A ($($before.stats.scrap) -> $($changed.stats.scrap))"

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "$loader $car 0" | Out-Null
$deadline = (Get-Date).AddSeconds(60)
do { Start-Sleep -Milliseconds 300; $loaded = Send-HarnessCommand -Instance $a -Verb car-loaded -Arguments "$loader" } until ($loaded.loaded -or (Get-Date) -gt $deadline)
Wait-Ready $a | Out-Null
$unmounted = Send-HarnessCommand -Instance $a -Verb part-unmount -Arguments "$loader"
Write-Host "A unmounted $($unmounted.key) ($($unmounted.id))"
Start-Sleep -Seconds 2
$readyA = Wait-Ready $a
Check (@(Dropped-Lines $markA).Count -eq 0) "nothing was dropped while A joined and played alone"

$aBefore = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "before-latejoin"
$markB = Get-ServerLogMark
Connect-HarnessInstance $b
$early = $null
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    $s = Get-HarnessStatus $b
    if ($s -and $s.syncAcked) { break }
    if ($s -and $s.connectionValid) {
        try { $early = Send-HarnessCommand -Instance $b -Verb send-early-stats -Arguments "3 100"; break } catch { Write-Host "send-early-stats: $($_.Exception.Message)" }
    }
    Start-Sleep -Milliseconds 300
}
Check ($null -ne $early) "B sent a stats change before its snapshot was acknowledged ($early)"
Wait-InGarage $b
$readyB = Wait-Ready $b
Start-Sleep -Seconds 3

$dropped = @(Dropped-Lines $markB)
Check (@($dropped | Where-Object { $_ -match "dropped StatsAction" }).Count -ge 1) "the server dropped B's early stats change"
$others = @($dropped | Where-Object { $_ -notmatch "dropped StatsAction" })
Check ($others.Count -eq 0) "nothing else was dropped during B's join ($($others -join ' | '))"

$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "after-latejoin"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "after-latejoin"
Check ($dumpA.stats.scrap -eq $aBefore.stats.scrap -and $dumpA.stats.exp -eq $aBefore.stats.exp) "the dropped change did not reach the shared stats (scrap $($aBefore.stats.scrap) -> $($dumpA.stats.scrap), exp $($aBefore.stats.exp) -> $($dumpA.stats.exp))"
$diff = Compare-HarnessDumps $dumpA $dumpB -Sections @("stats", "inventory", "cars")
Check ($diff.Count -eq 0) "B's stats, inventory and cars equal A's (differ: $($diff -join ', '))"
$finalA = Wait-Ready $a
$finalB = Wait-Ready $b
Write-Host "part state hashes: A before the join $($readyA.stateHash), A now $($finalA.stateHash), B when ready $($readyB.stateHash), B now $($finalB.stateHash)"
if ($finalB.stateHash -ne $finalA.stateHash) {
    foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb part-state -Arguments "$loader $(Join-Path $Ctx.RunDir "parts_$name.txt")" | Out-Null }
}
Check ($finalB.stateHash -eq $finalA.stateHash) "same part state on A and B ($($finalA.stateHash) / $($finalB.stateHash))"
Check ($readyB.stateHash -eq $finalB.stateHash) "B had the final part state when its car was ready ($($readyB.stateHash) / $($finalB.stateHash))"
Check ($dumpA.syncAcked -and $dumpB.syncAcked) "both acknowledged their snapshot"

foreach ($pair in @(@($a, $b), @($b, $a))) {
    $seen = $null
    try { $seen = Wait-HarnessStatus -Instance $pair[0] -TimeoutSec 20 -What "sees the other player" -Condition { param($s) $s.remotePlayers -ge 1 } } catch { }
    Check ($null -ne $seen) "$($pair[0]) sees $($pair[1])"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

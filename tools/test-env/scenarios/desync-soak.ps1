# run-all: skip
# desync-detection-and-resync task 2.6 false-alarm check: A and B work on one car for 10 minutes (unmount/mount on
# alternating clients, scrap/XP changes, B with 250 ms incoming latency) while the server compares digests every 5 s;
# no desync may be confirmed.
param($Ctx, [int]$Minutes = 10)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "0"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader 0 not Ready"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }
Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null

Send-HarnessCommand -Instance $b -Verb net-delay -Arguments "250" | Out-Null
$mark = Get-ServerLogMark
$end = (Get-Date).AddMinutes($Minutes)
$step = 0
while ((Get-Date) -lt $end) {
    $who = if ($step % 2 -eq 0) { $a } else { $b }
    try {
        $unmount = Send-HarnessCommand -Instance $who -Verb part-fast-unmount -Arguments "0"
        Start-Sleep -Seconds 3
        Send-HarnessCommand -Instance $who -Verb part-fast-mount -Arguments "0 $($unmount.key)" | Out-Null
    } catch { Write-Host "step $step on $who`: $($_.Exception.Message)" }
    Send-HarnessCommand -Instance $who -Verb stats-add -Arguments "1 5" | Out-Null
    Start-Sleep -Seconds 4
    $step++
}
Start-Sleep -Seconds 15

$log = @(Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $mark)
$repairs = @($log | Where-Object { $_ -match "\[Desync\] .*resending|is persistent" })
$waiting = @($log | Where-Object { $_ -match "\[Desync\] .*mismatch \(waiting" })
Write-Host "$step steps; $($waiting.Count) first-round mismatches (absorbed by the two-round rule), $($repairs.Count) confirmed"
$repairs | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" }
Check ($repairs.Count -eq 0) "no desync was confirmed in $Minutes minutes of normal play ($($repairs.Count))"
$Ctx.Result.notes += "$step steps, $($waiting.Count) first-round mismatches"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# run-all: skip
# areas: resync, parts, economy, details, tools, jobs
# desync-detection-and-resync task 2.6 and state-merges-and-contention task 9.4, false-alarm check: A and B work on one
# car for 10 minutes (unmount/mount, car details, the tire changer, the warehouse, orders, scrap/XP changes on
# alternating clients, B with 250 ms incoming latency) while the server compares every digest key every 5 s; no desync
# may be confirmed, also not for a key whose resend is still off (logged as "log only"), and no key may stall.
param($Ctx, [int]$Minutes = 10)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Cmd $Name car-ready "0"
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
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
Cmd $a car-spawn "0 $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null

Cmd $b net-delay "250" | Out-Null
$mark = Get-ServerLogMark
$end = (Get-Date).AddMinutes($Minutes)
$step = 0
$tireUid = 0
$stored = @()
while ((Get-Date) -lt $end) {
    $who = if ($step % 2 -eq 0) { $a } else { $b }
    try {
        switch ($step % 4) {
            0 {
                $unmount = Cmd $who part-fast-unmount "0"
                Start-Sleep -Seconds 3
                Cmd $who part-fast-mount "0 $($unmount.key)" | Out-Null
            }
            1 { Cmd $who cardetails-randomize "0" | Out-Null }
            2 {
                if ($tireUid -eq 0) {
                    $group = Cmd $who give-group "wheel"
                    Start-Sleep -Seconds 1
                    Cmd $who tool-put "TireChanger $($group.UID)" | Out-Null
                    $tireUid = $group.UID
                } else {
                    $taken = Cmd $who tool-take "TireChanger"
                    if (-not $taken.refused) { $tireUid = 0 }
                }
            }
            3 {
                if ($stored.Count -lt 3) {
                    $item = Cmd $who give-item "tarczaHamulcowa_1 0.5"
                    Start-Sleep -Seconds 1
                    Cmd $who warehouse-move "$($item.UID) to" | Out-Null
                    $stored += $item.UID
                } else {
                    Cmd $who warehouse-move "$($stored[0]) from" | Out-Null
                    $stored = @($stored | Select-Object -Skip 1)
                }
                Cmd $a econ-fee "spill 0" | Out-Null
            }
        }
    } catch { Write-Host "step $step on $who`: $($_.Exception.Message)" }
    Cmd $who stats-add "1 5" | Out-Null
    if ($step % 10 -eq 5) {
        $generator = @($Ctx.Instances | Where-Object { (Get-HarnessStatus $_).isOrderGenerator })
        if ($generator.Count -gt 0) { try { Cmd $generator[0] orders-generate | Out-Null } catch { Write-Host "orders step $step`: $($_.Exception.Message)" } }
    }
    Start-Sleep -Seconds 4
    $step++
}
Start-Sleep -Seconds 15

$log = @(Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $mark)
$repairs = @($log | Where-Object { $_ -match "\[Desync\] .*(resending|is persistent|log only)" })
$waiting = @($log | Where-Object { $_ -match "\[Desync\] .*mismatch \(waiting" })
$stalls = @($log | Where-Object { $_ -match "\[WARN\] \[Desync\] .* has not been ready" })
Write-Host "$step steps; $($waiting.Count) first-round mismatches (absorbed by the two-round rule), $($repairs.Count) confirmed, $($stalls.Count) stalls"
$repairs | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
$stalls | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" }
$keys = "world", "inventory", "car-placement", "workshop-tools", "warehouse", "garage", "jobs", "cars", "car-details"
$waitingPerKey = @($keys | ForEach-Object { $key = $_; "$key $(@($waiting | Where-Object { $_ -match "\[Desync\] $([regex]::Escape($key))(:\d+)? for client" }).Count)" })
$confirmedPerKey = @($keys | ForEach-Object { $key = $_; "$key $(@($repairs | Where-Object { $_ -match "\[Desync\] $([regex]::Escape($key))(:\d+)? " }).Count)" })
Write-Host "first-round mismatches per key: $($waitingPerKey -join ', ')"
Write-Host "confirmed per key: $($confirmedPerKey -join ', ')"
Check ($repairs.Count -eq 0) "no desync was confirmed in $Minutes minutes of normal play ($($repairs.Count))"
Check ($stalls.Count -eq 0) "no key stalled ($($stalls.Count))"
$Ctx.Result.notes += "$step steps, $($waiting.Count) first-round mismatches ($($waitingPerKey -join ', ')); confirmed: $($confirmedPerKey -join ', ')"

$records = @(Get-ChildItem -LiteralPath (Join-Path $Ctx.ServerDir "Log\desync") -Filter "*.json" -ErrorAction SilentlyContinue)
if ($records.Count -gt 0) { Copy-Item -LiteralPath $records.FullName -Destination $Ctx.RunDir }
Remove-Item -LiteralPath (Join-Path $Ctx.ServerDir "Log\desync") -Recurse -Force -ErrorAction SilentlyContinue

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# desync-detection-and-resync (c): B's car part state is corrupted locally (no packet), B resyncs (the F7 path) and
# the garage reload brings B back to the server's state; a second resync right away is refused by the cooldown.
param($Ctx)

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
$readyA = Wait-Ready $a
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$corrupt = Send-HarnessCommand -Instance $b -Verb part-corrupt -Arguments "0"
$corruptB = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "0"
Check ($corruptB.stateHash -ne $readyA.stateHash) "B's car differs after the local corruption of $($corrupt.key)"

$mark = Get-ServerLogMark
$result = Send-HarnessCommand -Instance $b -Verb resync
Check ($result -eq "reloading") "B's resync starts ($result)"
$manual = try { Wait-ServerLog -Pattern "manual resync by .*differing at that moment: .*cars:0" -After $mark -TimeoutSec 10 } catch { $null }
Check ([bool]$manual) "the server logs the manual resync with the differing car ($manual)"
Start-Sleep -Seconds 3
Wait-InGarage $b
$readyB = Wait-Ready $b
$readyA = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "0"
Check ($readyB.stateHash -eq $readyA.stateHash) "after the resync B's car matches A's ($($readyA.stateHash) / $($readyB.stateHash))"

$again = Send-HarnessCommand -Instance $b -Verb resync
Check ("$again" -match "^Wait \d+ s") "a second resync right away is refused ($again)"
Check ((Get-HarnessStatus $a).joinStatus -eq "InSession") "A stayed in the session"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

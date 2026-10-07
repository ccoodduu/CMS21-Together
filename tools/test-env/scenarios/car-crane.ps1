# areas: parts, cars, tools
# sync-car-parts engine crane: A takes the engine out with the crane (NotificationCenter.ActionUnMountGroup), B sees
# every engine part unmounted and gets the engine group in its inventory; A puts the same engine back
# (InsertEngineToCar), B sees the parts mounted and the group gone. The group travels inside the part change.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
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
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Wait-Same([string]$What, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 500
        $ra = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "$loader"
        $rb = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "$loader"
        if ($ra.stateHash -eq $rb.stateHash) { return @($ra, $rb) }
    } while ((Get-Date) -lt $deadline)
    Send-HarnessCommand -Instance $a -Verb part-state -Arguments "$loader $(Join-Path $Ctx.RunDir "parts_${What}_A.txt")" | Out-Null
    Send-HarnessCommand -Instance $b -Verb part-state -Arguments "$loader $(Join-Path $Ctx.RunDir "parts_${What}_B.txt")" | Out-Null
    return @($ra, $rb)
}

function Group-Uids($Dump, [string]$Id) {
    @($Dump.inventory.groups | Where-Object { $_.ID -eq $Id } | ForEach-Object { $_.UID })
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "$loader $car 0" | Out-Null
$readyA = Wait-Ready $a
Wait-Ready $b | Out-Null
$same = Wait-Same "spawn"
Check ($same[0].stateHash -eq $same[1].stateHash) "B has A's car in the same state"

$out = Send-HarnessCommand -Instance $a -Verb crane-out -Arguments "$loader"
Write-Host "A takes out $($out.engine) (group $($out.group))"
Check ($out.group -ne 0) "the crane put an engine group into A's inventory"
Start-Sleep -Seconds 2
$same = Wait-Same "crane-out"
Check ($same[0].unmounted -gt $readyA.unmounted + 5) "the engine parts are unmounted on A ($($readyA.unmounted) -> $($same[0].unmounted))"
Check ($same[0].stateHash -eq $same[1].stateHash) "B sees the engine out ($($same[0].stateHash) / $($same[1].stateHash))"
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "engine-out"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "engine-out"
Check ((Group-Uids $dumpB $out.engine) -contains $out.group) "B has the engine group $($out.group) ($((Group-Uids $dumpB $out.engine) -join ','))"
$diff = Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory")
Check ($diff.Count -eq 0) "inventories are equal with the engine out"
$serverLog = Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") -Raw
Check ($serverLog -match "\[Cars\] Change .* inventory \+1 -0") "the engine group travelled inside the part change"

$in = Send-HarnessCommand -Instance $a -Verb crane-in -Arguments "$loader"
Write-Host "A puts back $($in.engine) (group $($in.group))"
Start-Sleep -Seconds 3
$same = Wait-Same "crane-in"
Check ($same[0].unmounted -eq $readyA.unmounted) "the engine parts are mounted again on A ($($same[0].unmounted))"
Check ($same[0].stateHash -eq $same[1].stateHash) "B sees the engine back in ($($same[0].stateHash) / $($same[1].stateHash))"
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "engine-in"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "engine-in"
Check ((Group-Uids $dumpA $out.engine).Count -eq 0 -and (Group-Uids $dumpB $out.engine).Count -eq 0) "the engine group is gone on both"
$diff = Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory")
Check ($diff.Count -eq 0) "inventories are equal with the engine in"
$serverLog = Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") -Raw
Check ($serverLog -match "\[Cars\] Change .* inventory \+0 -1") "the group removal travelled inside the part change"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

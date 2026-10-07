# areas: presence, cars
# sync-players-and-scenes 5.2-5.4: A sits in a car both players see; B sees A seated with the body hidden, also after
# B rejoins. A starts the engine and B plays it on that car; A stops it and the sound ends. With the engine running
# again, B deletes the car and A is put out of the seat without an error.
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

function Expect([string]$Name, [string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 15) {
    try {
        Wait-HarnessDump -Instance $Name -TimeoutSec $TimeoutSec -What $What -Condition $Condition | Out-Null
        Check $true "$Name`: $What"
    } catch {
        Check $false "$Name`: $What ($($_.Exception.Message.Split("`n")[0]))"
    }
}

function EnginesOf($Dump, [int]$PlayerId) { @($Dump.remoteEngines | Where-Object { $_.playerId -eq $PlayerId }) }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Send-HarnessCommand -Instance $a -Verb set-name -Arguments "Ann" | Out-Null
Send-HarnessCommand -Instance $b -Verb set-name -Arguments "Bob" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }
$idA = [int](Get-HarnessStatus -Instance $a).playerId

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

Send-HarnessCommand -Instance $a -Verb sit -Arguments "0 left" | Out-Null
Expect $a "seated in car 0 (left)" { param($d) $d.local.seat -eq 0 -and $d.local.seatLeft }
Expect $b "sees Ann seated in car 0, body hidden" {
    param($d) $r = $d.roster."$idA"; $r -and $r.seat -eq 0 -and $r.seatLeft -and -not $r.avatarActive
}
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "seated"

Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 60 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Ready $b | Out-Null
Expect $b "after the rejoin still sees Ann seated in car 0" {
    param($d) $r = $d.roster."$idA"; $r -and $r.seat -eq 0 -and -not $r.avatarActive
}

Send-HarnessCommand -Instance $a -Verb engine -Arguments "on" | Out-Null
Expect $a "engine of car 0 running" { param($d) $d.local.engine.running -and $d.local.engine.carLoaderId -eq 0 }
Expect $b "hears Ann's engine on car 0" {
    param($d) $r = $d.roster."$idA"; $e = @(EnginesOf $d $idA)
    $r -and $r.engineRunning -and $r.engineCarLoaderId -eq 0 -and $e.Count -eq 1 -and $e[0].carLoaderId -eq 0 -and $e[0].audioPlaying
}

Send-HarnessCommand -Instance $a -Verb engine -Arguments "off" | Out-Null
Expect $a "engine stopped" { param($d) -not $d.local.engine.running }
Expect $b "Ann's engine sound ended" {
    param($d) $r = $d.roster."$idA"; $r -and -not $r.engineRunning -and @(EnginesOf $d $idA).Count -eq 0
}

Send-HarnessCommand -Instance $a -Verb engine -Arguments "on" | Out-Null
Expect $b "hears Ann's engine again" { param($d) @(EnginesOf $d $idA).Count -eq 1 }
Send-HarnessCommand -Instance $b -Verb car-delete -Arguments "0" | Out-Null
Expect $a "out of the seat and engine off after B deleted the car" { param($d) $d.local.seat -eq -1 -and -not $d.local.engine.running } 20
Expect $b "sees Ann standing again, no engine" {
    param($d) $r = $d.roster."$idA"; $r -and $r.seat -eq -1 -and -not $r.engineRunning -and $r.avatarActive -and @(EnginesOf $d $idA).Count -eq 0
} 20
$loaded = Send-HarnessCommand -Instance $a -Verb car-loaded -Arguments "0"
Check (-not $loaded.loaded) "A's car 0 is deleted ($($loaded | ConvertTo-Json -Compress))"
$status = Get-HarnessStatus -Instance $a
Check ($status.connectionValid -and $status.scene -eq "garage") "A is still connected in the garage"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

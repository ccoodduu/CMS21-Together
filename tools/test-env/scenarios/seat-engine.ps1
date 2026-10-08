# areas: presence, cars
# sync-players-and-scenes 5.2-5.4: A sits in a car both players see; B sees A seated with the body hidden, also after
# B rejoins. A starts the engine and B plays it on that car; A stops it and the sound ends. Both sit in the same seat
# with their packets held, in both server orders: the first keeps it, the second is out of the seat at once with
# "<name> is sitting there." (state-merges-and-contention D17). With the engine running again, B deletes the car and A
# is put out of the seat without an error.
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

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

$idB = [int](Get-HarnessStatus -Instance $b).playerId
function Toasts([string]$Name) { @((Send-HarnessCommand -Instance $Name -Verb dump).session.toasts) }

# Seat race (state-merges-and-contention D17): both sit in the same seat with their packets held, the server takes the
# first presence it gets; the second player is put out of the seat at once with a message naming the first.
function SeatRace([string]$First, [string]$Second, [int]$FirstId, [int]$SecondId, [string]$FirstName) {
    $toastsBefore = @(Toasts $Second).Count
    $mark = Get-ServerLogMark
    foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments "out" | Out-Null }
    Send-HarnessCommand -Instance $First -Verb sit -Arguments "0 left" | Out-Null
    Send-HarnessCommand -Instance $Second -Verb sit -Arguments "0 left" | Out-Null
    foreach ($name in $First, $Second) {
        try { Wait-HarnessDump -Instance $name -TimeoutSec 15 -What "seated locally" -Condition { param($d) $d.local.seat -eq 0 } | Out-Null } catch { Check $false "$name sat down locally while held" }
    }
    Send-HarnessCommand -Instance $First -Verb net-hold -Arguments "off" | Out-Null
    Start-Sleep -Seconds 2
    Send-HarnessCommand -Instance $Second -Verb net-hold -Arguments "off" | Out-Null
    $refused = try { Wait-ServerLog -Pattern "Seat left of car 0 refused for client $SecondId" -After $mark -TimeoutSec 10 } catch { $null }
    Check ([bool]$refused) "the server refused the second seat claim ($refused)"
    Expect $Second "out of the seat right after the refusal" { param($d) $d.local.seat -eq -1 } 10
    $newToasts = @(Toasts $Second | Select-Object -Skip $toastsBefore)
    Check (@($newToasts | Where-Object { $_ -eq "$FirstName is sitting there." }).Count -ge 1) "$Second was told '$FirstName is sitting there.' ($($newToasts -join ' | '))"
    Expect $First "still seated in car 0" { param($d) $d.local.seat -eq 0 -and $d.local.seatLeft } 5
    Expect $First "sees the other player standing" { param($d) $r = $d.roster."$SecondId"; $r -and $r.seat -eq -1 }
    Expect $Second "sees the first player in the seat" { param($d) $r = $d.roster."$FirstId"; $r -and $r.seat -eq 0 -and $r.seatLeft }
    Send-HarnessCommand -Instance $First -Verb stand | Out-Null
    Expect $First "stood up again" { param($d) $d.local.seat -eq -1 }
    Expect $Second "sees nobody in the seat" { param($d) $r = $d.roster."$FirstId"; $r -and $r.seat -eq -1 }
}

Send-HarnessCommand -Instance $a -Verb stand | Out-Null
Expect $a "stood up for the seat race" { param($d) $d.local.seat -eq -1 }
Expect $b "sees Ann standing" { param($d) $r = $d.roster."$idA"; $r -and $r.seat -eq -1 }
SeatRace $a $b $idA $idB "Ann"
SeatRace $b $a $idB $idA "Bob"

Send-HarnessCommand -Instance $a -Verb sit -Arguments "0 left" | Out-Null
Expect $a "seated in car 0 again" { param($d) $d.local.seat -eq 0 -and $d.local.seatLeft }
Send-HarnessCommand -Instance $a -Verb engine -Arguments "on" | Out-Null
Expect $b "hears Ann's engine again" { param($d) @(EnginesOf $d $idA).Count -eq 1 }
Send-HarnessCommand -Instance $b -Verb car-delete -Arguments "0" | Out-Null
Expect $a "out of the seat and engine off after B deleted the car" { param($d) $d.local.seat -eq -1 -and -not $d.local.engine.running } 20
Expect $b "sees Ann standing again, no engine" {
    param($d) $r = $d.roster."$idA"; $r -and $r.seat -eq -1 -and -not $r.engineRunning -and $r.avatarActive -and @(EnginesOf $d $idA).Count -eq 0
} 20
$deadline = (Get-Date).AddSeconds(10)
do {
    $loaded = Send-HarnessCommand -Instance $a -Verb car-loaded -Arguments "0"
    if (-not $loaded.loaded) { break }
    Start-Sleep -Milliseconds 300
} while ((Get-Date) -lt $deadline)
Check (-not $loaded.loaded) "A's car 0 is deleted ($($loaded | ConvertTo-Json -Compress))"
$status = Get-HarnessStatus -Instance $a
Check ($status.connectionValid -and $status.scene -eq "garage") "A is still connected in the garage"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: placement
# Soak 2026-10-08 (confirmed desync "lift:0.state 1 vs 0" after every move of a car onto lift 1): the game raises a
# lift to Middle by itself when the car put on it misses a wheel (ChangeCarPos on the mover, PlaceAtPosition on the
# others), while the server kept the lift on the floor. A takes a tire off and moves the car onto lift 1, moves it off
# and back, B moves it once more, and B reconnects; each time every client and the server agree on the lift.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Lift0([string]$Name) { @(Cmd $Name lifters)[0].state }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Check-Agree([string]$What, [string]$Expected) {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $states = @($Ctx.Instances | ForEach-Object { Lift0 $_ })
        if (@($states | Where-Object { $_ -ne $Expected }).Count -eq 0) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Check (@($states | Where-Object { $_ -ne $Expected }).Count -eq 0) "$What`: every client has lift 0 $Expected ($($states -join ', '))"
    $mark = Get-ServerLogMark
    Send-ServerCommand "desync check"
    Start-Sleep -Seconds 3
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] car-placement" })
    Check ($lines.Count -ge 2 -and -not ($lines -match "mismatch|fields differ")) "$What`: the server's car-placement digest matches both players ($($lines -join ' | '))"
}

function Move-Car([string]$Name, [string]$Place) {
    Cmd $Name car-move "$loader $Place" | Out-Null
    Start-Sleep -Seconds 6
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader car_boltatlanta 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
$car = @((Cmd $a dump).cars | Where-Object { $_.index -eq $loader })[0]
$tire = @($car.subParts | Where-Object { $_.id -like "tire_*" -and -not $_.unmounted })[0]
if (-not $tire) { throw "no mounted tire on loader $loader" }
Cmd $a part-fast-unmount "$loader $($tire.key)" | Out-Null
Start-Sleep -Seconds 4
Check-Agree "a car without a tire stands at the entrance" "OnFloor"

Move-Car $a CarLifter1
Check-Agree "A moved the car without a tire onto lift 1" "Middle"

Move-Car $a Entrance2
Check-Agree "A moved it off the lift" "OnFloor"

Move-Car $b CarLifter1
Check-Agree "B moved it back onto lift 1" "Middle"

Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Ready $b | Out-Null
Check-Agree "after B reconnected (server snapshot)" "Middle"

# Soak 2026-10-10 (lift:1.state 0 vs 1 on every client, persistent): CarLifter2 is not built in the test profiles, so
# no game raises it, but the mover reported Middle and the server raised it.
$lift2 = @(Cmd $a lifters | Where-Object { $_.nearestPlace -eq "CarLifter2" })[0]
Write-Host "CarLifter2: $($lift2 | ConvertTo-Json -Compress)"
Move-Car $a CarLifter2
Check-Agree "A moved the car without a tire onto lift 2 (active $($lift2.active))" "OnFloor"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: placement
# Soak 2026-10-10 (client C, lift 0 OnFloor while every other client had it Middle, then 158 "Exception in Harmony
# patch of method void CarLifter::Action" every 15 s):
# 1. A job car arrived on lift 1 and a remote move took it off within a frame. The game's lift coroutine then threw
#    on the missing car and never cleared isMoving, so that lift ignored every later step. B does the same two places
#    in one frame; its lift must stop moving, and a lift step A makes afterwards must reach B.
# 2. A remote lift step was still running when B left for the junkyard. The step's coroutine kept calling Action on
#    the destroyed lift and LifterSync.IsApplying stayed true. A raises lift 1 to Up and B leaves while B's lift moves
#    (CarLifter2 is not built in the test profiles); after B's return there is no lift exception in B's log, no lift
#    step still applying, and A and B have the same lifts.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")

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

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}

function Lifter([string]$Name, [int]$Index) { @(Cmd $Name lifters | Where-Object { $_.index -eq $Index })[0] }

function Wait-LifterState([string]$Name, [int]$Index, [string]$State, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $l = Lifter $Name $Index
        if ($l.state -eq $State -and -not $l.isMoving) { return $l }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $l
}

function Check-LiftsAgree([string]$What, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $la = @(Cmd $a lifters | ForEach-Object { "$($_.index):$($_.state):$($_.connectedLoader)" }) -join ","
        $lb = @(Cmd $b lifters | ForEach-Object { "$($_.index):$($_.state):$($_.connectedLoader)" }) -join ","
        if ($la -eq $lb) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Check ($la -eq $lb) "$What`: A and B have the same lifts (A $la, B $lb)"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b

Cmd $a car-spawn "0 $car 0 Entrance1" | Out-Null
Wait-Ready $a 0 | Out-Null
Wait-Ready $b 0 | Out-Null
Start-Sleep -Seconds 2

$lift1 = @(Cmd $a lifters | Where-Object { $_.nearestPlace -eq "CarLifter1" })[0].index
Write-Host "CarLifter1 is lift $lift1"

# Part 1: a car put on lift 1 and taken off within one frame.
$placed = Cmd $b car-place "0 CarLifter1 Entrance1"
Write-Host "B car-place: $($placed | ConvertTo-Json -Compress)"
$moving = (Lifter $b $lift1).isMoving
Write-Host "B lift $lift1 right after: isMoving $moving"
$deadline = (Get-Date).AddSeconds(40)
do {
    Start-Sleep -Seconds 1
    $l = Lifter $b $lift1
} while ($l.isMoving -and (Get-Date) -lt $deadline)
Check (-not $l.isMoving) "B's lift $lift1 stops moving after its car left it mid-move ($($l | ConvertTo-Json -Compress))"

Cmd $a car-move "0 CarLifter1" | Out-Null
Start-Sleep -Seconds 6
Cmd $a lift "$lift1 up" | Out-Null
$la = Wait-LifterState $a $lift1 "Middle"
Check ($la.state -eq "Middle") "A raised lift $lift1 to Middle ($($la.state))"
$lb = Wait-LifterState $b $lift1 "Middle"
Check ($lb.state -eq "Middle") "B's lift $lift1 follows A's step to Middle ($($lb | ConvertTo-Json -Compress))"
Check (-not $lb.applying) "B applies no step on lift $lift1 any more"

# Part 2: B leaves for the junkyard while A's lift step runs on B.
$marks = Get-ClientLogMarks @($b)
Cmd $a lift "$lift1 up" | Out-Null
$deadline = (Get-Date).AddSeconds(5)
do {
    Start-Sleep -Milliseconds 100
    $l = Lifter $b $lift1
} while (-not $l.isMoving -and (Get-Date) -lt $deadline)
Check ($l.isMoving -and $l.applying) "B is applying A's step on lift $lift1 when it leaves ($($l | ConvertTo-Json -Compress))"
Cmd $b travel "Junkyard" | Out-Null
$s = Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "B in the junkyard" -Condition { param($s) $s.scene -ne "garage" -and $s.playable -and $s.connectionValid }
Write-Host "B in $($s.scene); waiting 35 s"
Start-Sleep -Seconds 35
Cmd $b travel "Garage" | Out-Null
Start-Sleep -Seconds 3
Wait-InGarage $b
Start-Sleep -Seconds 5

$path = Get-ClientLogPath $b
$lines = @(Read-FileFrom $path $marks[$b].Length)
$exceptions = @($lines | Where-Object { $_ -match "Exception in Harmony patch of method void CarLifter::Action" })
Check ($exceptions.Count -eq 0) "no lift exception in B's log while B was away ($($exceptions.Count))"
$dropped = @($lines | Where-Object { $_ -match "\[Placement\] Lift $lift1`: step to \d+ dropped" })
Check ($dropped.Count -ge 1) "B dropped the running step when it left ($($dropped -join ' | '))"
$applying = @(Cmd $b lifters | Where-Object { $_.applying } | ForEach-Object { $_.index })
Check ($applying.Count -eq 0) "B applies no lift step after its return ($($applying -join ', '))"
Check-LiftsAgree "after B's return"
$lb = Lifter $b $lift1
Check ($lb.state -eq "Up") "B's lift $lift1 is Up after its return ($($lb.state))"

$stuck = @(Read-FileFrom $path 0 | Where-Object { $_ -match "\[Placement\] Lift \d+: still moving after" })
Write-Host "B stuck-lift lines: $($stuck -join ' | ')"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: locks, placement, testdrive, jobs
# part-locks 8.3: the car-level lock (D7). A move and a swap run under a car lock (two linked records for a swap). While
# B holds a part lock, A's lift and move are refused on A's own game, a lift request sent without a lock is refused by
# the server, park and car delete are refused by the server with Busy and a message on A, and ending the job whose car
# B works on is refused on A's own game; nothing moves or disappears. A's lift with B's incoming packets delayed by
# 150 ms: B cannot start part work while its own lift still moves. A car removed while its owner holds a lock leaves no
# lock behind and the next car on that loader can be locked (row 19 review P8).
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1")

$a, $b = $Ctx.Instances
$loader = 0
$other = 1
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$Loader = $loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}

function Try-Wait([string]$Name, [string]$Arguments, [int]$TimeoutSec = 15, [switch]$UntilFinished) {
    $t = Cmd $Name lock-try $Arguments
    if ($t.result -ne "pending") { return $t }
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Cmd $Name lock-try "result $($t.tryId)"
        $settled = $r.result -ne "pending" -and (-not $UntilFinished -or $r.result -ne "granted" -or -not $r.started -or $r.finished -or $r.state -match "^not ")
        if ($settled) { return $r }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $r
}

function Wait-Mirror([string]$Name, [int]$Count) {
    Wait-LockMirror $Name { param($l) @($l.mirror).Count -eq $Count } "$Count lock(s)" | Out-Null
}

function Wait-NoLocks([string]$What, [int]$TimeoutSec = 10) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $locks = Get-ServerLocks
        if ($locks.Count -eq 0) { break }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    Check ($locks.Count -eq 0 -and (Get-LockCounter $locks "overlapViolations") -eq 0) "$What`: no lock is left and no overlap was seen ($($locks.Line))"
}

function Place([string]$Name, [int]$Loader) { @(Cmd $Name placement | Where-Object { $_.loader -eq $Loader })[0] }
function Lifter([string]$Name) { @(Cmd $Name lifters | Where-Object { $_.connectedLoader -eq $loader })[0] }
function Busy([string]$Name) { $c = (Get-LockMirror $Name).counters; if ($c.'denied.CarBusy') { [int]$c.'denied.CarBusy' } else { 0 } }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Cmd $name guard-set "Off" | Out-Null
    Cmd $name orders-autogen "off" | Out-Null
}
$idA = (Get-HarnessStatus $a).playerId
$idB = (Get-HarnessStatus $b).playerId

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Cmd $a car-spawn "$other $car 0 Entrance2" | Out-Null
Wait-Ready $a $other | Out-Null
Wait-Ready $b $other | Out-Null
Start-Sleep -Seconds 2
$placeOther = (Place $a $other).placeNo
Write-Host "placement: $((Cmd $a placement) | ConvertTo-Json -Compress)"

# A move under a car lock.
$r = Try-Wait $a "$loader move CarLifter1 finish" -UntilFinished
Check ($r.result -eq "granted" -and $r.started -and $r.finished) "A's move to lift 1 runs under a car lock ($($r.result) $($r.state))"
Wait-NoLocks "after the move"
Start-Sleep -Seconds 2
Check ((Place $a $loader).placeNo -eq 3 -and (Place $b $loader).placeNo -eq 3) "the car stands on lift 1 for A and B"
$lifter = Lifter $a
Check ($null -ne $lifter) "a lift holds the car"

# B works on a part: A's lift and move are refused on A's game; the server refuses a lift sent without a lock.
$part = @(Cmd $b vfx-parts "$loader")[0].key
$answer = Request-Lock $b "$loader unmount $part"
Check ($answer.result -eq "granted") "B holds $part"
Wait-Mirror $a 1
$r = Try-Wait $a "$loader lift $($lifter.index) up"
Check ($r.result -eq "refusedLocally" -and $r.holder -eq $idB) "A's lift is refused while B works ($($r.result) $($r.holder) $($r.conflictKey))"
$r = Try-Wait $a "$loader move Entrance3"
Check ($r.result -eq "refusedLocally" -and $r.holder -eq $idB) "A's move is refused while B works ($($r.result) $($r.holder))"
$busy = Busy $a
Cmd $a lock-try "$loader lift $($lifter.index) up nogate" | Out-Null
Start-Sleep -Seconds 2
Check ((Busy $a) -eq $busy + 1) "the server refuses A's lift request without a lock (CarBusy $busy -> $(Busy $a))"
foreach ($name in $Ctx.Instances) { Check ((Lifter $name).state -eq "OnFloor") "$name`: the lift stayed down" }

# Park, delete and job end are refused with Busy.
$busy = Busy $a
Cmd $a park "$loader" | Out-Null
Start-Sleep -Seconds 4
Check ((Busy $a) -ge $busy + 1) "A's park is refused while B works (CarBusy $busy -> $(Busy $a))"
Check ((Get-LockMirror $a).lastMessage -match "is working on this car") "A gets the message ($((Get-LockMirror $a).lastMessage))"
foreach ($name in $Ctx.Instances) { Check ((Cmd $name car-ready "$loader").loaded) "$name still has the car after the refused park" }
$busy = Busy $a
Cmd $a car-delete "$loader" | Out-Null
Start-Sleep -Seconds 4
Check ((Busy $a) -eq $busy + 1) "A's delete is refused while B works (CarBusy $busy -> $(Busy $a))"
Wait-Ready $a | Out-Null
Check ((Cmd $b car-ready "$loader").loaded) "B still has the car after the refused delete"

Cmd $b lock-release "$loader" | Out-Null
Wait-NoLocks "after B's release"

# M3: A's lift with B's incoming packets delayed; B's own lift still moves after A's lock is gone.
Cmd $b net-delay "150" | Out-Null
$r = Try-Wait $a "$loader lift $($lifter.index) up"
Check ($r.result -eq "granted" -and $r.started) "A's lift is granted ($($r.result))"
$results = @()
$deadline = (Get-Date).AddSeconds(20)
do {
    $lb = Lifter $b
    $mirror = @((Get-LockMirror $b).mirror)
    if ($lb.isMoving -or $mirror.Count -gt 0) {
        $t = Try-Wait $b "$loader unmount $part"
        $after = Lifter $b
        $results += [pscustomobject]@{ moving = $lb.isMoving; locks = $mirror.Count; result = $t.result; movingAfter = $after.isMoving }
        if ($t.result -eq "granted") { Cmd $b lock-probe "mode PartSelect $loader" | Out-Null; break }
    } elseif ($results.Count -gt 0) { break }
    Start-Sleep -Milliseconds 150
} while ((Get-Date) -lt $deadline)
Write-Host "  B's tries during A's lift: $($results | ConvertTo-Json -Compress)"
Check ($results.Count -ge 1 -and -not ($results | Where-Object { $_.result -eq "granted" -and $_.movingAfter })) "B's part work is refused while the lift moves on B or A's lock is held (a grant only once B's lift has stopped)"
Check (@($results | Where-Object { $_.result -eq "moving" }).Count -ge 1) "B's own moving lift refuses part work (M3)"
Cmd $b net-delay "0" | Out-Null
Wait-NoLocks "after the lift"
$answer = Request-Lock $b "$loader unmount $part"
Check ($answer.result -eq "granted") "B can work on the car once the lift has stopped ($($answer.result))"
Cmd $b lock-release "$loader" | Out-Null
Wait-NoLocks "after B's lock"
$r = Try-Wait $a "$loader lift $($lifter.index) down finish" -UntilFinished
Wait-NoLocks "after the lift went down"

# Swap: B's lock on the other car refuses A's swap on the server (linked records are granted together or not at all).
$part1 = @(Cmd $b vfx-parts "$other")[0].key
$answer = Request-Lock $b "$other unmount $part1"
Check ($answer.result -eq "granted") "B holds $part1 on the other car"
Wait-Mirror $a 1
$placeName = @("Entrance1", "Entrance2", "Entrance3", "CarLifter1", "CarLifter2")[$placeOther]
$r = Try-Wait $a "$loader move $placeName"
Check ($r.result -eq "denied" -and $r.holder -eq $idB) "A's swap with the car B works on is refused by the server ($($r.result) $($r.holder) $($r.conflictKey))"
Cmd $b lock-release "$other" | Out-Null
Wait-NoLocks "after B's release on the other car"
$r = Try-Wait $a "$loader move $placeName finish" -UntilFinished
Check ($r.result -eq "granted" -and $r.finished) "A's swap runs under two linked locks ($($r.result) $($r.state))"
Wait-NoLocks "after the swap"
Start-Sleep -Seconds 2
Check ((Place $b $loader).placeNo -eq $placeOther -and (Place $b $other).placeNo -eq 3) "B sees the two cars swapped"
$r = Try-Wait $a "$loader move CarLifter1 finish" -UntilFinished
Wait-NoLocks "after the swap back"
Start-Sleep -Seconds 2
$lifter = Lifter $a

# Job end: a job car that B works on is not cleared.
$gen = if ((Get-HarnessStatus $a).isOrderGenerator) { $a } else { $b }
$base = @((Cmd $a dump).jobs.orders | Where-Object { -not $_.IsMission }).Count
Cmd $gen orders-generate | Out-Null
$deadline = (Get-Date).AddSeconds(30)
do { Start-Sleep -Milliseconds 700; $orders = @((Cmd $a dump).jobs.orders | Where-Object { -not $_.IsMission }) } while ($orders.Count -le $base -and (Get-Date) -lt $deadline)
$take = $orders[-1].id
Cmd $a orders-accept "$take" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 700; $job = @((Cmd $a dump).jobs.active | Where-Object { $_.id -eq $take })[0] } while (-not $job -and (Get-Date) -lt $deadline)
Check ($null -ne $job) "A took job $take"
if ($job) {
    $jobLoader = $job.carLoaderID
    Wait-Ready $a $jobLoader | Out-Null
    Wait-Ready $b $jobLoader | Out-Null
    Start-Sleep -Seconds 2
    $jobPart = @(Cmd $b vfx-parts "$jobLoader")[0].key
    $answer = Request-Lock $b "$jobLoader unmount $jobPart"
    Check ($answer.result -eq "granted") "B holds $jobPart on the job car"
    Wait-Mirror $a 1
    Cmd $a job-finish "$take" | Out-Null
    Start-Sleep -Seconds 3
    Check ((Get-LockMirror $a).lastMessage -match "is working on this car") "A's job end is refused on A's game while B works on the job car ($((Get-LockMirror $a).lastMessage))"
    Check (@((Cmd $a dump).jobs.active | Where-Object { $_.id -eq $take }).Count -eq 1) "the job is still active"
    foreach ($name in $Ctx.Instances) { Check ((Cmd $name car-ready "$jobLoader").loaded) "$name still has the job car" }
    Cmd $b lock-release "$jobLoader" | Out-Null
    Wait-NoLocks "after B's release on the job car"
    Cmd $a job-end-direct "$take" | Out-Null
    $deadline = (Get-Date).AddSeconds(30)
    do { Start-Sleep -Milliseconds 700; $left = @((Cmd $b dump).jobs.active | Where-Object { $_.id -eq $take }).Count } while ($left -gt 0 -and (Get-Date) -lt $deadline)
    Check ($left -eq 0) "the job ends once B has stopped"
}

# P8: B removes the car while B holds a lock on it; no lock is left and the next car on that loader can be locked.
$answer = Request-Lock $b "$other unmount $part1"
Check ($answer.result -eq "granted") "B holds $part1 before removing the car"
Wait-Mirror $a 1
Cmd $b car-delete "$other" | Out-Null
Start-Sleep -Seconds 3
Wait-NoLocks "after the car with B's lock was removed" -TimeoutSec 5
Wait-Mirror $a 0
Wait-Mirror $b 0
Check (-not (Cmd $a car-ready "$other").loaded) "A no longer has the removed car"
Cmd $a car-spawn "$other $car 0 Entrance2" | Out-Null
Wait-Ready $a $other | Out-Null
Wait-Ready $b $other | Out-Null
Start-Sleep -Seconds 2
$answer = Request-Lock $a "$other unmount $part1"
Check ($answer.result -eq "granted") "A can lock a part of the new car on that loader ($($answer.result) $($answer.conflictKey))"
$answer = Request-Lock $b "$other unmount $part1"
Check ($answer.result -eq "denied" -and $answer.holder -eq $idA) "and B is refused for it, naming A ($($answer.result) $($answer.holder))"
Cmd $a lock-release "$other" | Out-Null
Wait-NoLocks "at the end"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

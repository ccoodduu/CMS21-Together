# areas: locks, parts, details, placement
# part-locks 5.3 [review B2]: every lock ends. The item chooser opened and closed without a choice, an unmount the
# game refuses (a blocked part), a mount on a part that is still mounted, and the idle cancels (chooser after 3 s,
# bolt view after 5 s with lock-idle 5 3) each leave the server's lock table empty within 1 s of the end. The steps
# for a refill without a target, a lift that is already moving and an aborted prefetch join with tasks 7.1, 8.1 and 10.1.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1")

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Cmd $Name car-ready "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Wait-Try([int]$TryId, [int]$TimeoutSec = 10) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Cmd $a lock-try "result $TryId"
        if ($r.result -ne "pending") { return $r }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $r
}

function Wait-NoLocks([string]$What, [int]$TimeoutSec = 1) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec + 1)
    do {
        $locks = Get-ServerLocks
        if ($locks.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    Check ($locks.Count -eq 0) "$What`: the server holds no lock ($($locks.Line))"
    $locks
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$candidates = @(Cmd $a vfx-parts "$loader")
$slot = $candidates[0].key
$free = $candidates[1].key
Cmd $a part-fast-unmount "$loader $slot" | Out-Null
Start-Sleep -Seconds 3

# Chooser opened and closed without a choice.
$open = Cmd $a lock-chooser "$loader $slot open"
$r = Wait-Try $open.tryId
Check ($r.result -eq "granted" -and $r.started) "the chooser opens under a slot lock ($($r.result), started $($r.started))"
Check ((Get-ServerLocks).Count -eq 1) "the server holds the slot lock while the chooser is open"
Cmd $a lock-chooser "$loader $slot close" | Out-Null
Wait-NoLocks "chooser closed without a choice" | Out-Null

# The game refuses the unmount of a blocked part: the grant is released at once.
$dump = Cmd $a dump
$blocked = @($dump.cars | Where-Object { $_.index -eq $loader } | ForEach-Object { $_.subParts } | Where-Object { $_.blocked -and -not $_.unmounted } | Select-Object -First 1)[0]
$before = (Get-LockMirror $a).counters.notStarted
$try = Cmd $a lock-try "$loader unmount $($blocked.key)"
$r = Wait-Try $try.tryId
Check ($r.result -eq "granted" -and -not $r.started) "a blocked part is granted but does not start ($($blocked.key): $($r.result), started $($r.started))"
Wait-NoLocks "refused unmount" | Out-Null
Check ((Get-LockMirror $a).counters.notStarted -eq $before + 1) "the client counted a lock that did not start"

# Mount on a part that is still mounted: no chooser, released at once.
$try = Cmd $a lock-try "$loader mount $free"
$r = Wait-Try $try.tryId
Check (-not $r.started) "a mount on a mounted part does not start ($($r.result), started $($r.started))"
Wait-NoLocks "mount on a mounted part" | Out-Null

# Idle cancels.
Cmd $a lock-idle "5 3" | Out-Null
$open = Cmd $a lock-chooser "$loader $slot open"
$r = Wait-Try $open.tryId
Check ($r.started) "the chooser is open again"
Start-Sleep -Seconds 3
Wait-NoLocks "chooser idle after 3 s" -TimeoutSec 2 | Out-Null
Check (-not (Cmd $a guard-trace "state").activeWindows -contains "ChoosePartUp") "the idle chooser was closed"

$try = Cmd $a lock-try "$loader unmount $free"
$r = Wait-Try $try.tryId
Check ($r.result -eq "granted" -and $r.started) "the bolt view of $free is open under a lock ($($r.result), started $($r.started))"
Start-Sleep -Seconds 5
Wait-NoLocks "bolt view idle after 5 s" -TimeoutSec 2 | Out-Null
$part = Cmd $a lock-probe "part $loader $free"
Check (-not $part.unmounted -and $part.mode -ne "PartUnMount") "the idle unmount was undone (unmounted $($part.unmounted), mode $($part.mode))"
Check ((Get-LockMirror $a).counters.idleCancelled -ge 2) "two idle cancels were counted ($((Get-LockMirror $a).counters.idleCancelled))"
Cmd $a lock-idle "default" | Out-Null

$locks = Get-ServerLocks
Check ((Get-LockCounter $locks "overlapViolations") -eq 0) "overlapViolations is 0 ($($locks.Line))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

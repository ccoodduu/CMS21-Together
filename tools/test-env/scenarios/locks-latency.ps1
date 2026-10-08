# areas: locks, parts, connect
# part-locks 10.1/10.2: latency and lost answers (D10). With A's incoming packets delayed by 150 ms, a click (no hold)
# starts after about one delayed answer. With 80 ms, a hold through the game's own raycast prefetches its lock at hold
# start, so the completed hold waits 0 ms in at least 8 of 10 tries. A request stalled for 4 s times out with a
# message and its late grant is released. With lock_expiry_seconds = 10 and renews off, a lock ends within 10 +- 2 s.
# A holder's disconnect frees the part for B at once.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")
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

function Try-Wait([string]$Name, [string]$Arguments, [int]$TimeoutSec = 10) {
    $t = Cmd $Name lock-try $Arguments
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Cmd $Name lock-try "result $($t.tryId)"
        if ($r.result -ne "pending") { return $r }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    $r
}

function Wait-NoLocks([string]$What, [int]$TimeoutSec = 5) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $locks = Get-ServerLocks
        if ($locks.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    Check ($locks.Count -eq 0) "$What`: the server holds no lock ($($locks.Line))"
}

function Counter([string]$Name, [string]$Counter) { $c = (Get-LockMirror $Name).counters; if ($c.$Counter) { [int]$c.$Counter } else { 0 } }

Set-ServerConfigValues $Ctx.ServerDir @{ lock_expiry_seconds = 10 }
Restart-TestServer

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$idA = (Get-HarnessStatus $a).playerId
$idB = (Get-HarnessStatus $b).playerId
Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$started = Get-Date
$part = @(Cmd $a vfx-parts "$loader")[0].key
Write-Host "part: $part"

# A click without a hold waits one delayed answer.
Cmd $a net-delay "150" | Out-Null
$r = Try-Wait $a "$loader unmount $part release"
Check ($r.result -eq "granted" -and $r.waitedMs -ge 140 -and $r.waitedMs -lt 800) "with 150 ms delay a click starts after one answer (waited $($r.waitedMs) ms)"
Cmd $a lock-probe "mode PartSelect $loader" | Out-Null
Wait-NoLocks "after the click"

# Holds prefetch at hold start.
Cmd $a net-delay "80" | Out-Null
Cmd $a lock-reports "clear" | Out-Null
for ($i = 1; $i -le 10; $i++) {
    Cmd $a lock-click "$loader $part hold 1500" | Out-Null
    $deadline = (Get-Date).AddSeconds(12)
    do { Start-Sleep -Milliseconds 300; $status = Cmd $a lock-click "status" } while (-not $status.done -and (Get-Date) -lt $deadline)
    Start-Sleep -Milliseconds 300
    Cmd $a lock-probe "mode PartSelect $loader" | Out-Null
    Wait-NoLocks "after hold $i" -TimeoutSec 4
}
$reports = @(Cmd $a lock-reports | Where-Object { $_.kind -eq "PartUnmount" })
$instant = @($reports | Where-Object { $_.result -eq "granted" -and $_.waitedMs -eq 0 })
Write-Host "  holds: $(($reports | ForEach-Object { "$($_.result)/$($_.waitedMs)/$($_.prefetched)" }) -join ' ')"
Check ($reports.Count -ge 10 -and $instant.Count -ge 8) "with 80 ms delay, $($instant.Count) of $($reports.Count) completed holds waited 0 ms (prefetch)"
Cmd $a net-delay "0" | Out-Null

# A stalled request times out and its late grant is released.
$timeouts = Counter $a "timeouts"
$late = Counter $a "lateGrantsReleased"
Cmd $a net-hold "out" | Out-Null
$t = Cmd $a lock-try "$loader unmount $part"
Start-Sleep -Seconds 4
Cmd $a net-hold "off" | Out-Null
$r = Cmd $a lock-try "result $($t.tryId)"
Check ($r.result -eq "timeout") "a request without an answer for 3 s times out ($($r.result))"
Check ((Get-LockMirror $a).lastMessage -match "did not answer") "the player gets the no-answer message ($((Get-LockMirror $a).lastMessage))"
$deadline = (Get-Date).AddSeconds(5)
do { Start-Sleep -Milliseconds 300 } while ((Counter $a "lateGrantsReleased") -eq $late -and (Get-Date) -lt $deadline)
Check ((Counter $a "timeouts") -eq $timeouts + 1 -and (Counter $a "lateGrantsReleased") -eq $late + 1) "the late grant is released (timeouts +$((Counter $a "timeouts") - $timeouts), lateGrantsReleased +$((Counter $a "lateGrantsReleased") - $late))"
Wait-NoLocks "after the late grant"

# Expiry without renews.
Cmd $a lock-renew "off" | Out-Null
$answer = Request-Lock $a "$loader unmount $part"
Check ($answer.result -eq "granted") "A holds $part without renewing"
$granted = Get-Date
$deadline = $granted.AddSeconds(16)
do { Start-Sleep -Milliseconds 500; $locks = Get-ServerLocks } while ($locks.Count -gt 0 -and (Get-Date) -lt $deadline)
$seconds = ((Get-Date) - $granted).TotalSeconds
Check ($locks.Count -eq 0 -and $seconds -ge 8 -and $seconds -le 13) "the lock expired after $([math]::Round($seconds, 1)) s (10 +- 2 s, plus the poll)"
Cmd $a lock-renew "on" | Out-Null

# The holder's disconnect frees the part.
$answer = Request-Lock $a "$loader unmount $part"
Check ($answer.result -eq "granted") "A holds $part again"
Wait-LockMirror $b { param($l) @($l.mirror).Count -eq 1 } "B sees A's lock" | Out-Null
Cmd $a disconnect | Out-Null
Wait-LockMirror $b { param($l) @($l.mirror).Count -eq 0 } "A's lock gone on B" -TimeoutSec 5 | Out-Null
$answer = Request-Lock $b "$loader unmount $part"
Check ($answer.result -eq "granted") "B gets $part right after A's disconnect ($($answer.result))"
Cmd $b lock-release "$loader" | Out-Null

$locks = Get-ServerLocks
Check ((Get-LockCounter $locks "overlapViolations") -eq 0) "overlapViolations is 0 ($($locks.Line))"
$minutes = ((Get-Date) - $started).TotalMinutes
Check ($minutes -lt 2) "the lock steps ran in $([math]::Round($minutes, 1)) min (after the car spawned)"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: locks, parts, placement
# part-locks tasks 2.3, 3.1 and 4.1: the server lock table fed by the bare lock-take verb, before any game hook asks for
# locks. A bearing cap held by A blocks the crankshaft for B (shared/exclusive) but not a sibling cap; car-level work
# (lift lock, park, delete) is refused while the other player holds a lock; a lock that is not renewed expires after
# lock_expiry_seconds (10 here); a request stalled for 4 s times out and its late grant is released; the owner's
# release and disconnect free everything. Both mirrors equal the server's table, and overlapViolations stays 0.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")
Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1")

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$cap = "s:13.65"
$sibling = "s:13.66"
$crankshaft = "s:13.5"
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
        $r = Cmd $Name car-ready "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function MirrorIds([string]$Name) { @((Get-LockMirror $Name).mirror | ForEach-Object { $_.lockId } | Sort-Object) -join "," }

function Same-AsServer([string]$What) {
    $server = Get-ServerLocks
    $ids = @($server.Records | ForEach-Object { $_.LockId } | Sort-Object) -join ","
    Wait-LockMirror $a { param($l) (@($l.mirror | ForEach-Object { $_.lockId } | Sort-Object) -join ",") -eq $ids } "A equal to the server ($ids)" | Out-Null
    Wait-LockMirror $b { param($l) (@($l.mirror | ForEach-Object { $_.lockId } | Sort-Object) -join ",") -eq $ids } "B equal to the server ($ids)" | Out-Null
    Check $true "$What`: both mirrors equal the server's table [$ids]"
    $server
}

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

$dumpA = Cmd $a dump
Check ($null -ne $dumpA.locks) "the dump has a locks section"

# Grant, broadcast, shared/exclusive.
$answer = Request-Lock $a "$loader unmount $cap"
Check ($answer.result -eq "granted") "A's lock on the bearing cap $cap is granted ($($answer.result) $($answer.refusal))"
$server = Same-AsServer "after A's grant"
$capLock = @($server.Records | Where-Object { $_.X -contains $cap })[0]
Check ($capLock -and $capLock.S -contains $crankshaft -and $capLock.S -contains "car") "the cap's lock holds the crankshaft and the car shared (S $($capLock.S -join ','))"

$answer = Request-Lock $b "$loader unmount $crankshaft"
Check ($answer.result -eq "denied" -and $answer.holder -eq $idA -and $answer.conflictKey -eq $cap) "B's crankshaft is denied, naming A and the cap $cap ($($answer.result) $($answer.holder) $($answer.conflictKey))"
$answer = Request-Lock $b "$loader unmount $sibling"
Check ($answer.result -eq "granted") "B's sibling cap $sibling is granted ($($answer.result) $($answer.conflictKey))"
$answer = Request-Lock $a "$loader lift car"
Check ($answer.result -eq "denied" -and $answer.holder -eq $idB) "A's lift lock is denied while B works on the car ($($answer.result) $($answer.conflictKey))"
Same-AsServer "after the denials" | Out-Null

# Car-level refusals while B holds a lock.
$mark = Get-ServerLogMark
Cmd $a park "$loader" | Out-Null
$parkRefused = try { Wait-ServerLog -Pattern "park of loader $loader by client $idA refused" -After $mark -TimeoutSec 15 } catch { $null }
Check ([bool]$parkRefused) "parking the car is refused while B holds a lock"
Start-Sleep -Seconds 4
Check ((Cmd $b car-ready "$loader").loaded) "the car is still there on B after the refused park"
Wait-Ready $a | Out-Null
$mark = Get-ServerLogMark
Cmd $a car-request "$loader delete" | Out-Null
$deleteRefused = try { Wait-ServerLog -Pattern "delete of loader $loader by client $idA refused" -After $mark -TimeoutSec 15 } catch { $null }
Check ([bool]$deleteRefused) "deleting the car is refused while B holds a lock"
Start-Sleep -Seconds 4
Check ((Cmd $b car-ready "$loader").loaded) "the car is still there on B after the refused delete"
Wait-Ready $a | Out-Null
$counters = (Get-LockMirror $a).counters
Check ($counters.'denied.CarBusy' -ge 2) "A was told twice that B works on the car (denied.CarBusy $($counters.'denied.CarBusy'))"

# Expiry: B stops renewing; its lock ends within 10 s + one tick, A's stays.
Cmd $b lock-renew "off" | Out-Null
$mark = Get-ServerLogMark
$expired = try { Wait-ServerLog -Pattern "Lock \d+ of client $idB on loader $loader released: not renewed" -After $mark -TimeoutSec 20 } catch { $null }
Check ([bool]$expired) "B's lock expires without renews ($expired)"
Cmd $b lock-renew "on" | Out-Null
Start-Sleep -Seconds 4
$server = Same-AsServer "after the expiry"
Check (@($server.Records | Where-Object { $_.Owner -eq $idA }).Count -eq 1) "A's renewed lock is still held after the expiry window"

# Timeout and late grant: A's request is stalled 4 s on the way out.
Cmd $a lock-counters "reset" | Out-Null
Cmd $a net-hold "out" | Out-Null
$r = Cmd $a lock-take "$loader unmount $sibling"
Start-Sleep -Seconds 4
$timedOut = Cmd $a lock-take "result $($r.requestId)"
Check ($timedOut.result -eq "timeout") "the stalled request times out after 3 s ($($timedOut.result))"
Cmd $a net-hold "off" | Out-Null
Start-Sleep -Seconds 3
$counters = (Get-LockMirror $a).counters
Check ($counters.lateGrantsReleased -eq 1 -and $counters.timeouts -eq 1) "the late grant is released (timeouts $($counters.timeouts), lateGrantsReleased $($counters.lateGrantsReleased))"
$server = Same-AsServer "after the late grant"
Check (@($server.Records | Where-Object { $_.X -contains $sibling }).Count -eq 0) "no lock on $sibling is left after the late grant"

# CarDetailsSync.FlushNow (task 4.4): while details are being applied the flush waits, then sends within 1 s.
$logA = Join-Path $env:USERPROFILE "CMS21-TestInstalls\$a\MelonLoader\Latest.log"
$logMark = @(Get-Content -LiteralPath $logA).Count
$flush = Cmd $a cardetails-flush "$loader applying 500"
Check ($flush.result -eq "Deferred") "FlushNow during an applying window is deferred ($($flush.result))"
Start-Sleep -Seconds 2
$deferred = @(Get-Content -LiteralPath $logA | Select-Object -Skip $logMark | Where-Object { $_ -match "deferred flush of Fluids" })
Check ($deferred.Count -eq 1 -and $deferred[0] -notmatch "although still busy") "the deferred flush ran within 1 s ($($deferred -join ' | '))"
$flush = Cmd $a cardetails-flush "$loader"
Check ($flush.result -in @("Sent", "Unchanged")) "FlushNow outside an applying window runs at once ($($flush.result))"

# Release and disconnect.
Cmd $a lock-release "$loader" | Out-Null
Start-Sleep -Seconds 2
$answer = Request-Lock $b "$loader unmount $crankshaft"
Check ($answer.result -eq "granted") "after A's release B gets the crankshaft ($($answer.result))"
Cmd $b disconnect | Out-Null
Start-Sleep -Seconds 4
$server = Get-ServerLocks
Check ($server.Count -eq 0) "B's disconnect releases B's locks (server count $($server.Count))"
Check ((Get-LockCounter $server "overlapViolations") -eq 0) "overlapViolations is 0 ($($server.Line))"
Check ((Get-LockCounter $server "expired") -ge 1 -and (Get-LockCounter $server "granted") -ge 4) "the server counted grants and the expiry ($($server.Line))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: locks, parts
# part-locks 6.2: what a lock also covers (D3). On car_boltatlanta the bearing caps s:13.65-67 are fixed to the
# crankshaft s:13.5 (spike 1.3). A cap held by A refuses B's crankshaft on B's own game, naming A and the cap; a
# sibling cap is free for B; the crankshaft held by A refuses B's cap; a lock on an engine part refuses the crane
# (key "engine"); with lock_scope = part only the same part conflicts.
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

function Try-Wait([string]$Name, [string]$Arguments) {
    $t = Cmd $Name lock-try $Arguments
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $r = Cmd $Name lock-try "result $($t.tryId)"
        if ($r.result -ne "pending") { return $r }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $r
}

function Wait-Mirror([string]$Name, [int]$Count) {
    Wait-LockMirror $Name { param($l) @($l.mirror).Count -eq $Count } "$Count lock(s)" | Out-Null
}

function Start-Session {
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
}

Start-Session
$idA = (Get-HarnessStatus $a).playerId
$idB = (Get-HarnessStatus $b).playerId

$answer = Request-Lock $a "$loader unmount $cap"
Check ($answer.result -eq "granted") "A holds the bearing cap $cap"
Wait-Mirror $b 1
$mark = Get-ServerLogMark
$r = Try-Wait $b "$loader unmount $crankshaft"
Check ($r.result -eq "refusedLocally" -and $r.holder -eq $idA -and $r.conflictKey -eq $cap) "B's crankshaft is refused on B's game, naming A and the cap ($($r.result) $($r.holder) $($r.conflictKey))"
Start-Sleep -Milliseconds 500
$requests = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Locks\] (Request \d+ by client $idB|Lock \d+ granted to client $idB)" })
Check ($requests.Count -eq 0) "no request reached the server for the local refusal"
$answer = Request-Lock $b "$loader unmount $sibling"
Check ($answer.result -eq "granted") "B gets the sibling cap $sibling while A holds $cap ($($answer.result) $($answer.conflictKey))"
Cmd $a lock-release "$loader" | Out-Null
Cmd $b lock-release "$loader" | Out-Null
Wait-Mirror $a 0

$answer = Request-Lock $a "$loader unmount $crankshaft"
Check ($answer.result -eq "granted") "A holds the crankshaft"
Wait-Mirror $b 1
$r = Try-Wait $b "$loader unmount $cap"
Check ($r.result -eq "refusedLocally" -and $r.holder -eq $idA -and $r.conflictKey -eq $crankshaft) "B's cap is refused while A holds the crankshaft ($($r.result) $($r.conflictKey))"
$r = Try-Wait $b "$loader crane-out"
Check ($r.result -eq "refusedLocally" -and $r.holder -eq $idA) "B's crane is refused while A works on an engine part ($($r.result) $($r.conflictKey))"
$answer = Request-Lock $b "$loader crane engine"
Check ($answer.result -eq "denied") "the server also refuses the engine key ($($answer.result) $($answer.conflictKey))"
Cmd $a lock-release "$loader" | Out-Null
Wait-Mirror $b 0

$locks = Get-ServerLocks
Check ((Get-LockCounter $locks "overlapViolations") -eq 0) "overlapViolations is 0 ($($locks.Line))"

# lock_scope = part: only the same part and the car.
Stop-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 30 -What "menu after the server stop" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
    Cmd $name mp-ui "ok" | Out-Null
}
Set-ServerConfigValues $Ctx.ServerDir @{ lock_scope = "part" }
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
if (-not (Cmd $a car-ready "$loader").loaded) { Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null }
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2
$answer = Request-Lock $a "$loader unmount $cap"
Check ($answer.result -eq "granted") "with lock_scope = part A holds the cap"
Wait-Mirror $b 1
$r = Try-Wait $b "$loader unmount $crankshaft"
Check ($r.result -ne "refusedLocally" -and $r.result -ne "denied") "with lock_scope = part B's crankshaft is not refused ($($r.result) $($r.conflictKey))"
$locks = Get-ServerLocks
Check ((Get-LockCounter $locks "overlapViolations") -eq 0) "overlapViolations is 0 with lock_scope = part"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

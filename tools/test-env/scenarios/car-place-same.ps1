# areas: placement, locks
# Race-and-drift audit L4 (soak contention kind place-same) and the D16 rule that a refused move ends with the server's
# placement on the refused game. Desync autofix is off, so the digest's resend cannot repair anything here.
# 1. A move the server refuses after its lock was granted (B swaps its car with A's car on a raised lift): B's game
#    has already swapped the cars and lowered the lift; it must end with the server's places and lift state.
# 2. A and B move different cars to the same free place at once (outgoing packets held, B released right after A's
#    move lock is granted): the target place is part of the move lock, so B's lock is refused ("Ann is moving a car
#    there.") before B's game moves anything, the server never refuses a move, and both games keep A's move.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")
Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1")

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

function Saw([string]$Pattern, [int]$After, [int]$TimeoutSec = 10) { try { [bool](Wait-ServerLog -Pattern $Pattern -After $After -TimeoutSec $TimeoutSec) } catch { $false } }

function Placement([string]$Name) { (Cmd $Name dump).placement }

function Wait-SamePlacement([string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 700
        $pa = Placement $a
        $ja = $pa | ConvertTo-Json -Depth 6 -Compress
        $jb = Placement $b | ConvertTo-Json -Depth 6 -Compress
        if ($ja -eq $jb -and (& $Condition $pa)) { break }
    } while ((Get-Date) -lt $deadline)
    Check ($ja -eq $jb) "$What`: A and B agree ($ja)"
    if ($ja -ne $jb) { Write-Host "  B: $jb" }
    Check ([bool](& $Condition $pa)) "$What`: expected state reached"
}

function At($p, [int]$Loader, [string]$Place) { @($p.cars | Where-Object { $_.loader -eq $Loader -and $_.inPlace -eq $Place }).Count -eq 1 }
function LiftState($p, [string]$State) { @($p.lifters | Where-Object { $_.car -eq 0 -and $_.state -eq $State }).Count -eq 1 }

function Wait-NoLocks([string]$What) {
    foreach ($name in $Ctx.Instances) { Wait-LockMirror $name { param($l) @($l.mirror).Count -eq 0 } "$What`: no lock left" -TimeoutSec 15 | Out-Null }
}

function Test-ServerPlacement([string]$What) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "desync check"
    Start-Sleep -Seconds 3
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] car-placement for client \d+: (match|mismatch|not ready)" })
    Check ($lines.Count -ge 2 -and @($lines | Where-Object { $_ -notmatch ": match" }).Count -eq 0) "$What`: both games match the server's placement ($($lines -join ' | '))"
}

Set-ServerConfigValues $Ctx.ServerDir @{ desync_check_interval_seconds = 600; desync_autofix = $false }
Restart-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Cmd $a set-name "Ann" | Out-Null
Cmd $b set-name "Bob" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$idA = (Get-HarnessStatus $a).playerId
$idB = (Get-HarnessStatus $b).playerId

Cmd $a car-spawn "0 $car 0 Entrance1" | Out-Null
Wait-Ready $a 0 | Out-Null; Wait-Ready $b 0 | Out-Null
Cmd $b car-spawn "1 $car 0 Entrance2" | Out-Null
Wait-Ready $b 1 | Out-Null; Wait-Ready $a 1 | Out-Null

Write-Host "--- 1. a move refused after its lock was granted (swap with a car on a raised lift)"
Cmd $a car-move "0 CarLifter1" | Out-Null
Wait-SamePlacement "A's car on lift 1" { param($p) (At $p 0 "CarLifter1") -and (At $p 1 "Entrance2") }
Wait-NoLocks "A's move"
Cmd $a lift "0 up" | Out-Null
Wait-SamePlacement "lift 1 raised" { param($p) (At $p 0 "CarLifter1") -and (LiftState $p "Middle") }
Wait-NoLocks "A's lift"
$mark = Get-ServerLogMark
Cmd $b car-move "1 CarLifter1" | Out-Null
Check (Saw "Move of loader 1 1->3 from client $idB refused" $mark) "the server refuses B's swap with the car on the raised lift"
Wait-SamePlacement "B's refused swap" { param($p) (At $p 0 "CarLifter1") -and (At $p 1 "Entrance2") -and (LiftState $p "Middle") } 15
Wait-NoLocks "B's refused move"
Test-ServerPlacement "after the refused swap"

Cmd $a lift "0 down" | Out-Null
Wait-SamePlacement "lift 1 down" { param($p) (At $p 0 "CarLifter1") -and (LiftState $p "OnFloor") }
Wait-NoLocks "A's lift down"
Cmd $a car-move "0 Entrance1" | Out-Null
Wait-SamePlacement "A's car back at entrance 1" { param($p) (At $p 0 "Entrance1") -and (At $p 1 "Entrance2") }
Wait-NoLocks "A's move back"

Write-Host "--- 2. two cars moved to one free place at once"
$mark = Get-ServerLogMark
foreach ($name in $Ctx.Instances) { Cmd $name net-hold "out" | Out-Null }
Cmd $a car-move "0 CarLifter1" | Out-Null
Cmd $b car-move "1 CarLifter1" | Out-Null
Start-Sleep -Milliseconds 500
Cmd $a net-hold "off" | Out-Null
$granted = Saw "\[Locks\] Lock \d+ granted to client $idA`: loader 0 Move" $mark
Cmd $b net-hold "off" | Out-Null
Check $granted "A's move lock is granted first"
Wait-SamePlacement "two moves to one free place" { param($p) (At $p 0 "CarLifter1") -and (At $p 1 "Entrance2") } 15
Wait-NoLocks "the two moves"
$lines = @(Get-ServerLogLines | Select-Object -Skip $mark)
Check (@($lines | Where-Object { $_ -match "Request \d+ by client $idB on loader 1 \(Move\) denied: Held p:3 held by $idA" }).Count -eq 1) "B's move lock is refused for the target place"
Check (@($lines | Where-Object { $_ -match "\[Placement\] Move of loader \d+ .* refused" }).Count -eq 0) "the server refuses no move"
$message = (Get-LockMirror $b).lastMessage
Check ($message -eq "Ann is moving a car there.") "B gets the message ($message)"
Test-ServerPlacement "after the two moves"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

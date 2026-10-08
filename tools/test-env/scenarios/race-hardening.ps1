# areas: cars, parts
# ROADMAP row 20 race-hardening (docs/design/race-hardening.md). Audit I6: B makes an item, puts it in the warehouse
# and leaves; B joins again with the same player id and makes a new item, whose UID must continue after the warehouse
# item's (it used to restart after the highest UID of B's range in the inventory only, and so handed out the
# warehouse item's UID again). Audit C1: B spawns a car on an empty loader while stalled, A spawns another car on the
# same loader first; B's late spawn request must be refused with the server's car kept (it used to clear A's car
# silently), and B must end with A's car and a reason.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-InMenu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 60 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

function Wait-Car([string]$Name, [int]$Loader, [string]$Car, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded -and $r.car -eq $Car) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $r
}

function Wait-Where([string]$Name, [string]$Uid, [string]$Where) {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $r = Cmd $Name item-where $Uid
        if ($r.where -eq $Where) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $r
}

function Wait-Toast([string]$Name, [string]$Text) {
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $toasts = @((Dump $Name).session.toasts)
        if ($toasts -contains $Text) { return $true }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Write-Host "  toasts on ${Name}: $($toasts -join ' | ')"
    return $false
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$idB = [int](Get-HarnessStatus -Instance $b).playerId
$rangeB = [long]$idB * 1000000000000

# I6: a warehouse item of B's range survives B leaving; B's next item must not get its UID again.
$u1 = "$((Cmd $b give-item "tarczaHamulcowa_1 0.5").UID)"
Check ([long]$u1 -gt $rangeB -and [long]$u1 -lt $rangeB + 1000000000000) "B's item $u1 is in B's UID range (player $idB)"
Check ((Wait-Where $a $u1 "inventory").where -eq "inventory") "A has B's item $u1"
Cmd $b warehouse-move "$u1 to" | Out-Null
Check ((Wait-Where $a $u1 "warehouse").where -eq "warehouse") "B's item $u1 is in the warehouse on A"
Cmd $b to-menu | Out-Null
Wait-InMenu $b
Start-Sleep -Seconds 2
Connect-HarnessInstance $b; Wait-InGarage $b
$idAgain = [int](Get-HarnessStatus -Instance $b).playerId
Check ($idAgain -eq $idB) "B got player id $idB again ($idAgain)"
Check ((Wait-Where $b $u1 "warehouse").where -eq "warehouse") "B sees its item $u1 in the warehouse after joining again"
$u2 = "$((Cmd $b give-item "tarczaHamulcowa_1 0.6").UID)"
Check ([long]$u2 -gt [long]$u1) "B's new item $u2 continues after the warehouse item $u1"
Start-Sleep -Seconds 1
Cmd $a warehouse-move "$u1 from" | Out-Null
Start-Sleep -Seconds 2
foreach ($name in $Ctx.Instances) {
    $items = @((Dump $name).inventory.items)
    $first = @($items | Where-Object { "$($_.UID)" -eq $u1 })
    $second = @($items | Where-Object { "$($_.UID)" -eq $u2 })
    Check ($first.Count -eq 1 -and $second.Count -eq 1 -and [math]::Abs($first[0].condition - 0.5) -lt 0.001 -and [math]::Abs($second[0].condition - 0.6) -lt 0.001) "$name holds both items once each with their own condition ($u1 x$($first.Count), $u2 x$($second.Count))"
}

# C1: B's spawn request reaches the server after A's car took the same loader.
$loader = 1
foreach ($name in $Ctx.Instances) {
    $r = Cmd $name car-ready "$loader"
    Check (-not $r.car) "loader $loader is empty on $name before the spawn race ($($r.car))"
}
Cmd $b net-hold "out" | Out-Null
Cmd $b car-spawn "$loader car_sixoncebulion 0" | Out-Null
$deadline = (Get-Date).AddSeconds(60)
do { Start-Sleep -Milliseconds 500; $loaded = (Cmd $b car-loaded "$loader").loaded } while (-not $loaded -and (Get-Date) -lt $deadline)
Check ([bool]$loaded) "B loaded its own car on loader $loader while stalled"
Start-Sleep -Seconds 1
$mark = Get-ServerLogMark
Cmd $a car-spawn "$loader car_boltatlanta 0" | Out-Null
$readyA = Wait-Car $a $loader "car_boltatlanta"
Check ($readyA.state -eq "Ready") "A's car_boltatlanta is Ready on loader $loader (SpawnSeq $($readyA.spawnSeq))"
Cmd $b net-hold "off" | Out-Null
$refused = try { Wait-ServerLog -Pattern "Spawn of car_sixoncebulion on loader $loader from client $idB refused" -After $mark -TimeoutSec 15 } catch { $null }
Check ([bool]$refused) "the server refused B's spawn on the occupied loader ($refused)"
Start-Sleep -Seconds 3
$mark = Get-ServerLogMark
Send-ServerCommand "cars"
$line = try { Wait-ServerLog -Pattern "loader ${loader}: \S+ SpawnSeq \d+, revision" -After $mark -TimeoutSec 10 } catch { $null }
Check ($line -match "loader ${loader}: car_boltatlanta SpawnSeq $($readyA.spawnSeq),") "the server keeps A's car on loader $loader ($line)"
$finalA = Wait-Car $a $loader "car_boltatlanta" 20
$finalB = Wait-Car $b $loader "car_boltatlanta" 30
Check ($finalA.car -eq "car_boltatlanta" -and $finalA.spawnSeq -eq $readyA.spawnSeq) "A still has its car on loader $loader ($($finalA.car), SpawnSeq $($finalA.spawnSeq))"
Check ($finalB.car -eq "car_boltatlanta" -and $finalB.state -eq "Ready" -and $finalB.spawnSeq -eq $readyA.spawnSeq) "B ends with A's car on loader $loader ($($finalB.car), $($finalB.state), SpawnSeq $($finalB.spawnSeq))"
Check ($finalA.stateHash -eq $finalB.stateHash) "A's and B's car on loader $loader are in the same state ($($finalA.stateHash) / $($finalB.stateHash))"
Check (Wait-Toast $b "Another car is already in that place.") "B was told why its car is not there"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

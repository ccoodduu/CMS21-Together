# areas: driving, testdrive
# track-collisions 3.1 (ROADMAP row 27c): Ann waits on the test track's car spot and Bob arrives on the same spot, so
# each sees the other's car appear on her or his own: the collider stays off (overlap), no frame takes over 1 s and Ann
# drives away normally (the row 17 freeze). Ann parks ahead (held still); Bob drives at her car at a fixed low speed,
# coasts the last metres and stops behind it, his collider counts the contact (old code: he drives through). With his
# setting off Bob restarts and drives through her car. Bob rides along in Ann's car: the copy carrying him has no
# collider (reason passenger).
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function TryCmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { $null } }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "collide_$Label.json") -Encoding utf8 }
function Note([string]$Message) { $script:Ctx.Result.notes += $Message; Write-Host "note: $Message" -ForegroundColor Yellow }
function Id([string]$Name) { [int](Get-HarnessStatus -Instance $Name).playerId }
function Sub($p, $q) { [pscustomobject]@{ x = $p.x - $q.x; y = $p.y - $q.y; z = $p.z - $q.z } }
function Dot($p, $q) { $p.x * $q.x + $p.y * $q.y + $p.z * $q.z }
function Distance($p, $q) { if (-not $p -or -not $q) { return -1 }; [math]::Sqrt((Dot (Sub $p $q) (Sub $p $q))) }
function Rnd([double]$Value) { [math]::Round($Value, 2) }

function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}
function Wait-Driving([string]$Name, [int]$TimeoutSec = 180) {
    try {
        Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null
        $deadline = (Get-Date).AddSeconds(60)
        do { Start-Sleep -Milliseconds 700; $p = TryCmd $Name drive-probe } while (-not ($p -and $p.mode -eq "CarDrive" -and $p.initialized) -and (Get-Date) -lt $deadline)
        return [bool]($p -and $p.mode -eq "CarDrive")
    } catch { return $false }
}
function RemoteCar([string]$Observer, [int]$DriverId) { @((Cmd $Observer dump).remoteCars.cars) | Where-Object { $_.playerId -eq $DriverId } | Select-Object -First 1 }
function Wait-RemoteCar([string]$Observer, [int]$DriverId, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $car = RemoteCar $Observer $DriverId
        if ($car -and ($car.visible -or $car.mode -eq "failed")) { return $car }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    return $car
}
function Collider([string]$Observer, [int]$DriverId) { $r = TryCmd $Observer remote-collider "$DriverId"; if ($r) { @($r.cars) | Select-Object -First 1 } }
function Wait-Collider([string]$Observer, [int]$DriverId, [scriptblock]$Condition, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $c = Collider $Observer $DriverId
        if ($c -and (& $Condition $c)) { return $c }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $c
}
function Wait-InputDone([string]$Name, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do { Start-Sleep -Milliseconds 400; $s = Cmd $Name drive-input-state } while ($s.running -and (Get-Date) -lt $deadline)
    return $s
}
# drive-stop leaves the brake pressed, and the game shifts into reverse when the brake is held at a standstill; the
# short zero input releases it.
function Stop-Car([string]$Name) { Cmd $Name drive-stop | Out-Null; Wait-InputDone $Name | Out-Null; Cmd $Name drive-input "0 0 0.1" | Out-Null; Wait-InputDone $Name | Out-Null; Start-Sleep -Milliseconds 500 }
function Position([string]$Name) { (Cmd $Name drive-probe).capturePosition }
function MaxFrame([string]$Name) { (Cmd $Name perf).maxMs }
function Wait-Ride([string]$Name, [scriptblock]$Condition, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $state = TryCmd $Name ride-state
        if ($state -and (& $Condition $state)) { return $state }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $state
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Cmd $a set-name "Ann" | Out-Null
Cmd $b set-name "Bob" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$idA = Id $a; $idB = Id $b
foreach ($name in $a, $b) { Cmd $name guard-allow "Mode:CarDrive" | Out-Null }

Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0; Wait-Ready $b 0
Cmd $b car-spawn "1 car_boltatlanta 0" | Out-Null
Wait-Ready $a 1; Wait-Ready $b 1
Start-Sleep -Seconds 2

# 1. Bob's car appears on Ann's: no collider while they overlap, no long frame, Ann drives away.
Cmd $a track-go "0 TestTrack" | Out-Null
Check (Wait-Driving $a) "Ann drives on the test track"
Start-Sleep -Seconds 4
$spawnA = Position $a
Cmd $b track-go "1 TestTrack" | Out-Null
Check (Wait-Driving $b) "Bob drives on the test track, on the same car spot"
$trailB = @([pscustomobject]@{ at = "Bob arrived"; position = (Position $b); spot = $spawnA })
$copyB = Wait-RemoteCar $a $idB
$copyA = Wait-RemoteCar $b $idA
Check ($copyB.visible -and $copyA.visible) "each sees the other's car (Ann sees Bob: $($copyB.visible), Bob sees Ann: $($copyA.visible))"
$onA = Wait-Collider $a $idB { param($c) $c.built -and $c.reason -ne "snap" } 20
$onB = Wait-Collider $b $idA { param($c) $c.built -and $c.reason -ne "snap" } 20
$trailB += [pscustomobject]@{ at = "copies built"; position = (Position $b); collider = $onB }
Save "spawn_A" $onA; Save "spawn_B" $onB
Check ($onA.built -and -not $onA.enabled -and $onA.reason -eq "overlap") "Ann's collider of Bob's car stays off while it overlaps her car (enabled $($onA.enabled), reason $($onA.reason))"
Check ($onB.built -and -not $onB.enabled -and $onB.reason -eq "overlap") "Bob's collider of Ann's car stays off while it overlaps his car (enabled $($onB.enabled), reason $($onB.reason))"
$frameA = MaxFrame $a; $frameB = MaxFrame $b
Check ($frameA -lt 1000 -and $frameB -lt 1000 -and $copyB.longestFrame -lt 1 -and $copyA.longestFrame -lt 1) "no frame over 1 s while the cars overlap (Ann $frameA ms, Bob $frameB ms)"

Cmd $a drive-input "0.6 0 3" | Out-Null
$inputA = Wait-InputDone $a
$trailB += [pscustomobject]@{ at = "Ann driving away"; position = (Position $b); collider = (Collider $b $idA) }
Stop-Car $a
TryCmd $a collide-hold "on" | Out-Null
$trailB += [pscustomobject]@{ at = "Ann stopped"; position = (Position $b); collider = (Collider $b $idA) }
$parkA = Position $a
$away = Distance $spawnA $parkA
Check ($away -gt 5) "Ann drives away from the spot with Bob's car on it ($(Rnd $away) m, input $($inputA.mode))"
$clear = Wait-Collider $a $idB { param($c) $c.reason -eq "on" } 10
Check ($clear.enabled -and $clear.reason -eq "on") "Ann's collider of Bob's car is on once the cars are apart (reason $($clear.reason))"
Start-Sleep -Seconds 1

# 2. Bob drives into Ann's parked car and stops behind it.
$trailB += [pscustomobject]@{ at = "before Bob drives"; position = (Position $b); collider = (Collider $b $idA) }
Save "trail_B" $trailB
$startB = Position $b
$toAnn = Sub (Position $a) $startB
$gap = [math]::Sqrt((Dot $toAnn $toAnn))
$dir = [pscustomobject]@{ x = $toAnn.x / $gap; y = $toAnn.y / $gap; z = $toAnn.z / $gap }
$ready = Wait-Collider $b $idA { param($c) $c.reason -eq "on" } 10
Check ($ready.enabled) "Bob's collider of Ann's car is on before he drives (reason $($ready.reason))"
$annBefore = Position $a
Cmd $b drive-input ([string]::Format([cultureinfo]::InvariantCulture, "0.4 0 {0:F1}", 1.5 + [math]::Max(0, $gap - 5.5) / 6 + 0.2)) | Out-Null
$inputB = Wait-InputDone $b
Start-Sleep -Seconds 3
$endB = Position $b
$progress = Dot (Sub $endB $startB) $dir
$hit = Collider $b $idA
Save "hit_B" ([pscustomobject]@{ start = $startB; end = $endB; gap = $gap; progress = $progress; collider = $hit; input = $inputB })
Check ($progress -lt $gap - 2) "Bob stops behind Ann's car ($(Rnd $progress) m of the $(Rnd $gap) m to her car; input $($inputB.mode))"
Check ($hit.contacts -ge 1) "Bob's collider counts the contact ($($hit.contacts))"
$annMoved = Distance $annBefore (Position $a)
Note "Ann's car moved $(Rnd $annMoved) m on her own client while Bob drove into it"
$contacts = [int]$hit.contacts
Stop-Car $b

# 3. Bob turns collisions off, restarts and drives through Ann's car.
$set = TryCmd $b collide-set "setting off"
$off = Wait-Collider $b $idA { param($c) $c.reason -eq "setting" } 5
Check ($set -and -not $off.enabled -and $off.reason -eq "setting") "Bob's setting turns his collider off (reason $($off.reason))"
Cmd $b race-restart | Out-Null
Start-Sleep -Seconds 4
$startB = Position $b
$toAnn = Sub (Position $a) $startB
$gap = [math]::Sqrt((Dot $toAnn $toAnn))
$annBefore = Position $a
$dir = [pscustomobject]@{ x = $toAnn.x / $gap; y = $toAnn.y / $gap; z = $toAnn.z / $gap }
Cmd $b drive-input ([string]::Format([cultureinfo]::InvariantCulture, "0.4 0 {0:F1}", 1.5 + [math]::Max(0, $gap - 5.5) / 6 + 0.2)) | Out-Null
Wait-InputDone $b | Out-Null
Start-Sleep -Seconds 3
Note "Ann's car moved $(Rnd (Distance $annBefore (Position $a))) m on her own client while Bob drove through it with his setting off"
$progress = Dot (Sub (Position $b) $startB) $dir
$through = Collider $b $idA
Check ($progress -gt $gap + 3) "with the setting off Bob drives through Ann's car ($(Rnd $progress) m past the start, her car at $(Rnd $gap) m)"
Check ([int]$through.contacts -eq $contacts) "no new contact with the setting off ($($through.contacts))"
Stop-Car $b
TryCmd $b collide-set "setting on" | Out-Null
TryCmd $a collide-hold "off" | Out-Null

Cmd $a track-return | Out-Null
Cmd $b track-return | Out-Null
Wait-InGarage $a 180; Wait-InGarage $b 180
foreach ($name in $a, $b) {
    $deadline = (Get-Date).AddSeconds(20)
    do { Start-Sleep -Milliseconds 500; $away = @((Cmd $name dump).away) } while ($away.Count -ne 0 -and (Get-Date) -lt $deadline)
}
Start-Sleep -Seconds 2

# 4. Bob rides along in Ann's car: the copy that carries him has no collider.
Cmd $a sit "0 left" | Out-Null
Cmd $b sit "0 right" | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 15 -What "Bob in the right seat" -Condition { param($d) $d.local.seat -eq 0 -and -not $d.local.seatLeft } | Out-Null
Wait-HarnessDump -Instance $a -TimeoutSec 15 -What "Ann sees Bob seated" -Condition { param($d) $r = $d.roster."$idB"; $r -and $r.seat -eq 0 -and -not $r.seatLeft } | Out-Null
Cmd $a track-go "0 TestTrack" | Out-Null
Check (Wait-Driving $a) "Ann drives to the test track with Bob seated"
$seated = Wait-Ride $b { param($s) $s.phase -eq "Seated" } 120
Check ($seated.phase -eq "Seated" -and $seated.driverId -eq $idA) "Bob rides along in Ann's car (phase $($seated.phase))"
$ride = Wait-Collider $b $idA { param($c) $c.built } 20
Save "ride_B" $ride
Check ($ride.built -and -not $ride.enabled -and $ride.reason -eq "passenger") "the copy carrying Bob has no collider (enabled $($ride.enabled), reason $($ride.reason))"
Cmd $a drive-input "0.6 0 3" | Out-Null
Wait-InputDone $a | Out-Null
$ride = Collider $b $idA
Check (-not $ride.enabled -and $ride.reason -eq "passenger") "it stays off while Ann drives (reason $($ride.reason))"
Cmd $a track-return | Out-Null
Wait-InGarage $a 180
try { Wait-InGarage $b 180; $back = $true } catch { $back = $false }
Check $back "Bob returned to the garage with Ann"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: driving, testdrive, presence, persistence, guard
# shared-race-tracks 4.1 (ROADMAP row 27a): Ann takes a garage car to the race track; the car is claimed for her
# ("Ann has this car on the race track."), Bob joins her there with his own car and each sees the other drive. Laps
# go through the game's own RaceTrackManager.LastTime: Ann sets the group record (everyone is told), Bob a personal
# best; the records come back after rejoining and after a server restart, and a 5 s lap is ignored. The returns bring
# the mileage back and free the claims. Bob rides along to the speed track (scene SpeedTrack everywhere, not the
# game's TestTrack; Ann's drive stream names her car) and to the race track, where his laps are never sent. The drag
# strip stays refused with the DLC label. -OpenGuard opens the race and speed track in the guard (old code).
param($Ctx, [switch]$OpenGuard)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "race_$Label.json") -Encoding utf8 }
function Distance($p, $q) { if (-not $p -or -not $q) { return 0 }; [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }
function Toasts([string]$Name) { @((Cmd $Name dump).session.toasts) }
function Id([string]$Name) { [int](Get-HarnessStatus -Instance $Name).playerId }
function Mileage([string]$Name, [int]$Loader) { [int]((Cmd $Name cardetails-show "$Loader").Info | ConvertFrom-Json).Mileage }
function ServerLines($Mark) { @(Get-ServerLogLines | Select-Object -Skip $Mark) }

function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Wait-Menu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 60 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
}
function Wait-Scene([string]$Name, [string]$Scene, [int]$TimeoutSec = 180) {
    try {
        Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "on a track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null
        Wait-HarnessDump -Instance $Name -TimeoutSec 30 -What "local scene $Scene" -Condition { param($d) $d.local.scene -eq $Scene } | Out-Null
        $true
    } catch { $false }
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
function Wait-Away([string]$Name, [int]$Count) {
    $deadline = (Get-Date).AddSeconds(20)
    do { Start-Sleep -Milliseconds 500; $away = @((Cmd $Name dump).away) } while ($away.Count -ne $Count -and (Get-Date) -lt $deadline)
    return $away
}
function RemoteCar([string]$Observer, [int]$DriverId) {
    @((Cmd $Observer dump).remoteCars.cars) | Where-Object { $_.playerId -eq $DriverId } | Select-Object -First 1
}
function Wait-RemoteCar([string]$Observer, [int]$DriverId, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $car = RemoteCar $Observer $DriverId
        if ($car -and ($car.visible -or $car.mode -eq "failed")) { return $car }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    return $car
}
function Wait-InputDone([string]$Name, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do { Start-Sleep -Milliseconds 500; $s = Cmd $Name drive-input-state } while ($s.running -and (Get-Date) -lt $deadline)
}
function Wait-Ride([string]$Name, [scriptblock]$Condition, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $state = try { Cmd $Name ride-state } catch { $null }
        if ($state -and (& $Condition $state)) { return $state }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $state
}
function Wait-Log([string]$Pattern, $Mark, [int]$TimeoutSec = 20) {
    try { Wait-ServerLog -Pattern $Pattern -After $Mark -TimeoutSec $TimeoutSec } catch { $null }
}
function Wait-Toast([string]$Name, [int]$Before, [string]$Text, [int]$TimeoutSec = 15) {
    try {
        Wait-HarnessDump -Instance $Name -TimeoutSec $TimeoutSec -What "toast '$Text'" -Condition { param($d) @($d.session.toasts | Select-Object -Skip $Before | Where-Object { $_ -eq $Text }).Count -ge 1 } | Out-Null
        $true
    } catch { $false }
}
function Rejoin([string]$Name) {
    Cmd $Name to-menu | Out-Null
    Wait-Menu $Name
    Connect-HarnessInstance $Name
    Wait-InGarage $Name
}
function Records([string]$Label) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "records"
    Wait-Log "Track records:" $mark | Out-Null
    Start-Sleep -Seconds 1
    $lines = ServerLines $mark
    $lines | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "records_$Label.txt") -Encoding utf8
    return ($lines -join "`n")
}
function Start-Ride([string]$Track, [string]$Label) {
    Cmd $a sit "0 left" | Out-Null
    Cmd $b sit "0 right" | Out-Null
    Wait-HarnessDump -Instance $b -TimeoutSec 15 -What "Bob in the right seat" -Condition { param($d) $d.local.seat -eq 0 -and -not $d.local.seatLeft } | Out-Null
    Wait-HarnessDump -Instance $a -TimeoutSec 15 -What "Ann sees Bob seated" -Condition { param($d) $r = $d.roster."$idB"; $r -and $r.seat -eq 0 -and -not $r.seatLeft } | Out-Null
    $script:rideMark = Get-ServerLogMark
    $script:toastsB = @(Toasts $b).Count
    Cmd $a track-go "0 $Track" | Out-Null
    $ride = Wait-Log "\[Ride\] Client $idB sits in car 0 \(right\) and rides along with client $idA" $rideMark
    Check ([bool]$ride) "${Label}: the server puts Bob on Ann's drive to the $Track"
    Check (Wait-Scene $a $Track) "${Label}: Ann is on the $Track"
    Check (Wait-Scene $b $Track) "${Label}: Bob travelled to the $Track with her"
    $seated = Wait-Ride $b { param($s) $s.phase -eq "Seated" -and $s.camera.placed } 120
    Save "${Label}_seated_B" $seated
    Check ($seated.phase -eq "Seated" -and $seated.driverId -eq $idA) "${Label}: Bob is seated in Ann's car (phase $($seated.phase), driver $($seated.driverId))"
    Check ($seated.camera.toHead -ge 0 -and $seated.camera.toHead -le 0.05) "${Label}: Bob's camera sits at the passenger head ($($seated.camera.toHead) m)"
    $stateA = Wait-Ride $a { param($s) $av = @($s.avatars) | Where-Object { $_.playerId -eq $idB } | Select-Object -First 1; $av -and $av.active -and $av.toSeat -ge 0 -and $av.toSeat -le 0.05 } 30
    $avB = @($stateA.avatars) | Where-Object { $_.playerId -eq $idB } | Select-Object -First 1
    Check ($avB -and $avB.active -and $avB.toSeat -le 0.05) "${Label}: Ann sees Bob's avatar in her passenger seat ($($avB.toSeat) m)"
}
function End-Ride([string]$Label) {
    Cmd $a track-return | Out-Null
    Wait-InGarage $a 180
    try { Wait-InGarage $b 180; $back = $true } catch { $back = $false }
    Check $back "${Label}: Bob returned to the garage with Ann"
    $done = Wait-Ride $b { param($s) $s.phase -eq "None" } 20
    Check ($done.phase -eq "None") "${Label}: Bob is no longer a passenger (phase $($done.phase))"
    Check (@(Wait-Away $a 0).Count -eq 0 -and @(Wait-Away $b 0).Count -eq 0) "${Label}: the claim is released on both"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Cmd $a set-name "Ann" | Out-Null
Cmd $b set-name "Bob" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$idA = Id $a; $idB = Id $b
$guardOpen = @("Mode:CarDrive") + $(if ($OpenGuard) { @("Scene:RaceTrack", "Scene:SpeedTrack") } else { @() })
function Open-Guard { foreach ($name in $a, $b) { foreach ($key in $guardOpen) { Cmd $name guard-allow $key | Out-Null } } }
Open-Guard

Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0; Wait-Ready $b 0
Cmd $b car-spawn "1 car_boltatlanta 0" | Out-Null
Wait-Ready $a 1; Wait-Ready $b 1
Start-Sleep -Seconds 3
$repair = Cmd $b part-unmount "0"
Start-Sleep -Seconds 4
$mileage = Mileage $a 0
Check ($mileage -eq (Mileage $b 0)) "same mileage before the drive ($mileage)"

# 1. Ann on the race track: the car is claimed, Bob cannot work on it.
$mark = Get-ServerLogMark
Cmd $a track-go "0 RaceTrack" | Out-Null
$away = @(Wait-Away $b 1)
Save "away_B" $away
Check ($away.Count -eq 1 -and $away[0].owner -eq $idA -and $away[0].kind -eq "RaceTrack") "B's away shows A's claim of kind RaceTrack ($($away | ConvertTo-Json -Compress))"
Check (Wait-Scene $a "RaceTrack") "Ann is on the race track (scene RaceTrack)"
Wait-HarnessDump -Instance $b -TimeoutSec 15 -What "Ann on the race track in Bob's roster" -Condition { param($d) $d.roster."$idA".scene -eq "RaceTrack" } | Out-Null
Check ((Cmd $b dump).roster."$idA".scene -eq "RaceTrack") "Bob's roster has Ann on RaceTrack"
$toasts = @(Toasts $b).Count
$try = Cmd $b away-try "0 unmount $($repair.key)"
Check $try.blocked "Bob's unmount on the away car is blocked ($($try.what))"
Check (Wait-Toast $b $toasts "Ann has this car on the race track.") "Bob is told 'Ann has this car on the race track.'"
$try = Cmd $b away-try "0 move Entrance1"
Check $try.blocked "Bob's move of the away car is blocked ($($try.what))"

# 2. Bob joins with his own car; each sees the other drive.
Cmd $b track-go "1 RaceTrack" | Out-Null
Check (Wait-Scene $b "RaceTrack") "Bob is on the race track with car 1"
$grants = @(ServerLines $mark | Where-Object { $_ -match "\[Away\] RaceTrack on loader (0 granted to client $idA|1 granted to client $idB)" })
Check ($grants.Count -eq 2) "both race track claims granted ($($grants.Count))"
$carA = Wait-RemoteCar $b $idA
$carB = Wait-RemoteCar $a $idB
Save "observer_B" $carA
Save "observer_A" $carB
Check ($carA -and $carA.visible -and $carA.mode -match "^ghost") "Bob shows Ann's car (mode $($carA.mode))"
Check ($carB -and $carB.visible -and $carB.mode -match "^ghost") "Ann shows Bob's car (mode $($carB.mode))"
$before = (RemoteCar $b $idA).position
Cmd $a drive-input "0.6 0 4" | Out-Null
Wait-InputDone $a
Start-Sleep -Seconds 1
$after = (RemoteCar $b $idA).position
Check ((Distance $before $after) -gt 5) "Bob sees Ann's car move ($([math]::Round((Distance $before $after), 1)) m)"
Cmd $a drive-stop | Out-Null
$before = (RemoteCar $a $idB).position
Cmd $b drive-input "0.6 0 3" | Out-Null
Wait-InputDone $b
Start-Sleep -Seconds 1
$after = (RemoteCar $a $idB).position
Check ((Distance $before $after) -gt 2) "Ann sees Bob's car move ($([math]::Round((Distance $before $after), 1)) m)"
Cmd $b drive-stop | Out-Null
$drives = @(ServerLines $mark | Where-Object { $_ -match "\[Drive\] Player ($idA|$idB) drives .* in RaceTrack" })
Check ($drives.Count -ge 2) "the server relays both race track drives ($($drives.Count) starts)"

# 4a. Laps through the game's LastTime.
$mark = Get-ServerLogMark
$toastsA = @(Toasts $a).Count; $toastsB = @(Toasts $b).Count
$lap = Cmd $a track-lap "95000"
Save "lap_A" $lap
Check ($lap.elapsedMs -eq 95000 -and $lap.sent -eq 1) "Ann's 1:35.000 lap goes through LastTime and is sent (elapsed $($lap.elapsedMs) ms, sent $($lap.sent))"
Check ($lap.bestRaceTime -eq 95000) "the game writes Ann's lap as her best race time ($($lap.bestRaceTime))"
Check ([bool](Wait-Log "\[Tracks\] New group record on the race track: Ann \(client $idA\), 1:35\.000" $mark)) "the server keeps Ann's lap as the group record"
Check (Wait-Toast $b $toastsB "New group record on the race track: Ann, 1:35.000") "Bob is told about Ann's group record"
Check (Wait-Toast $a $toastsA "New group record on the race track: Ann, 1:35.000") "Ann is told about her group record"
$lapB = Cmd $b track-lap "99000"
Check ($lapB.sent -eq 1) "Bob's 1:39.000 lap is sent"
Check ([bool](Wait-Log "\[Tracks\] Personal best on the race track for Bob \(client $idB\): 1:39\.000" $mark)) "the server keeps Bob's personal best"
Check (Wait-Toast $b $toastsB "New personal best on the race track: 1:39.000") "Bob is told about his personal best"
$lapA2 = Cmd $a track-lap "97000"
Check ([bool](Wait-Log "\[Tracks\] RaceTrack 1:37\.000 by client $idA is not a personal best" $mark)) "Ann's slower 1:37.000 lap does not replace her best"
Start-Sleep -Seconds 2
Check ((Cmd $a dump).tracks.profileBestRaceTime -eq 95000) "Ann's profile keeps 95000 ms after the slower lap"
Check (@(Toasts $a | Select-Object -Skip $toastsA | Where-Object { $_ -match "Bob" }).Count -eq 0) "Ann is not told about Bob's personal best"

# 3. Both return: the mileage comes back, the claims are released.
Cmd $a testdrive-drive "2500" | Out-Null
Cmd $a track-return | Out-Null
Wait-InGarage $a 180
Cmd $b track-return | Out-Null
Wait-InGarage $b 180
Check (@(Wait-Away $a 0).Count -eq 0 -and @(Wait-Away $b 0).Count -eq 0) "both race track claims are released after the return"
Start-Sleep -Seconds 4
$mA = Mileage $a 0; $mB = Mileage $b 0
Check ($mA -ge $mileage + 2 -and $mA -eq $mB) "the drive's mileage reached car 0 on both ($mileage -> Ann $mA, Bob $mB km)"
$differ = Compare-HarnessDumps -Left (Cmd $a dump) -Right (Cmd $b dump) -Sections cars, away
Check ($differ.Count -eq 0) "Ann and Bob have the same cars and away sections (differ: $($differ -join ', '))"

# 4b. Records across sessions and a server restart; a 5 s lap is ignored.
$records = Records "after_laps"
Check ($records -match "race track group record: Ann, 1:35\.000" -and $records -match "'Ann': 1:35\.000" -and $records -match "'Bob': 1:39\.000") "server records: group Ann 1:35.000, Ann 1:35.000, Bob 1:39.000"
Rejoin $b; $idB = Id $b
Open-Guard
$tracksB = (Cmd $b dump).tracks
Check ($tracksB.groupBestLap.name -eq "Ann" -and $tracksB.groupBestLap.value -eq 95000) "Bob joins again and has the group record ($($tracksB.groupBestLap | ConvertTo-Json -Compress))"
Check ($tracksB.profileBestRaceTime -eq 99000) "Bob's session profile has his best race time ($($tracksB.profileBestRaceTime))"
$mark = Get-ServerLogMark
Rejoin $a; $idA = Id $a
Open-Guard
Check ([bool](Wait-Log "\[Players\] Client\[$idA\] gets its track records: best lap 95000 ms" $mark 5)) "the server sends Ann her records on joining"
$tracksA = (Cmd $a dump).tracks
Check ($tracksA.profileBestRaceTime -eq 95000 -and $tracksA.bestLapMs -eq 95000) "Ann joins again and her session profile has BestRaceTime 95000 ($($tracksA | ConvertTo-Json -Compress))"

$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
Stop-TestServer
foreach ($name in $a, $b) { Wait-Menu $name; Cmd $name mp-ui "ok" | Out-Null }
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$idA = Id $a; $idB = Id $b
Open-Guard
$records = Records "after_restart"
Check ($records -match "race track group record: Ann, 1:35\.000" -and $records -match "'Ann': 1:35\.000" -and $records -match "'Bob': 1:39\.000") "server records unchanged after the restart"
$tracksA = (Cmd $a dump).tracks; $tracksB = (Cmd $b dump).tracks
Save "tracks_after_restart" @{ A = $tracksA; B = $tracksB }
Check ($tracksB.groupBestLap.name -eq "Ann" -and $tracksB.groupBestLap.value -eq 95000) "Bob has the group record after the restart"
Check ($tracksA.profileBestRaceTime -eq 95000) "Ann's BestRaceTime is 95000 after the restart ($($tracksA.profileBestRaceTime))"
$mark = Get-ServerLogMark
Cmd $a track-record-send "RaceTrack 5000" | Out-Null
Check ([bool](Wait-Log "\[Tracks\] RaceTrack value 5000 from client $idA ignored \(out of bounds\)" $mark)) "a 5 s lap is ignored and logged"
Start-Sleep -Seconds 2
Check ((Cmd $a dump).tracks.profileBestRaceTime -eq 95000) "Ann's BestRaceTime stays 95000 after the ignored lap"
Check ((Records "after_ignored") -match "race track group record: Ann, 1:35\.000") "the group record is unchanged after the ignored lap"

# 5. Bob rides along to the speed track and to the race track.
Wait-Ready $a 0; Wait-Ready $b 0
Start-Sleep -Seconds 2
Start-Ride "SpeedTrack" "speed"
foreach ($pair in @(@($a, $idB), @($b, $idA))) {
    $d = Cmd $pair[0] dump
    Check ($d.local.scene -eq "SpeedTrack" -and $d.roster."$($pair[1])".scene -eq "SpeedTrack") "$($pair[0]) has both players on SpeedTrack, not TestTrack (own $($d.local.scene), other $($d.roster."$($pair[1])".scene))"
}
$stream = @(ServerLines $rideMark | Where-Object { $_ -match "\[Drive\] Player $idA drives \S+ \(car (-?\d+), drive \d+\) in (\w+)" } | Select-Object -Last 1)
$streamCar = if ($stream -and $stream[0] -match "\(car (-?\d+), drive \d+\) in (\w+)") { "$($Matches[1]) in $($Matches[2])" } else { "none" }
Check ($streamCar -eq "0 in SpeedTrack") "Ann's drive stream names her car 0 on the SpeedTrack ($streamCar)"
$mark = Get-ServerLogMark
$toastsB = @(Toasts $b).Count
Cmd $a track-topspeed "231" | Out-Null
End-Ride "speed"
Check ([bool](Wait-Log "\[Tracks\] New group record on the speed track: Ann \(client $idA\), 231 km/h" $mark)) "Ann's top speed of 231 km/h is the group record"
Check (-not (ServerLines $mark | Where-Object { $_ -match "\[Tracks\] SpeedTrack .*client $idB" })) "Bob's return from the speed track sends no top speed"
Check ((Cmd $b dump).tracks.groupTopSpeed.value -eq 231) "Bob has Ann's speed track record"

Start-Sleep -Seconds 2
Start-Ride "RaceTrack" "race"
$mark = Get-ServerLogMark
$lapB = Cmd $b track-lap "90000"
Check ($lapB.passenger -and $lapB.sent -eq 0) "the passenger's lap is not sent (passenger $($lapB.passenger), sent $($lapB.sent))"
$lapA = Cmd $a track-lap "97000"
Check ($lapA.sent -eq 1) "the driver's lap is sent"
Check ([bool](Wait-Log "\[Tracks\] RaceTrack 1:37\.000 by client $idA" $mark)) "the server got Ann's lap"
Start-Sleep -Seconds 2
Check (-not (ServerLines $mark | Where-Object { $_ -match "\[Tracks\] .*client $idB" })) "no lap from Bob reached the server"
End-Ride "race"
Check ((Records "after_rides") -match "'Bob': 1:39\.000") "Bob's best is still his own 1:39.000"

# 6. The drag strip stays refused.
$drag = Cmd $a guard-try "Scene:DragStrip"
Check ($drag.result -eq "blocked" -and $drag.message -match "Drag strip \(Drag Racing DLC\)") "the drag strip is refused with the DLC label ($($drag.result): $($drag.message))"
Start-Sleep -Seconds 3
Check ((Cmd $a dump).local.scene -eq "Garage") "Ann stays in the garage"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

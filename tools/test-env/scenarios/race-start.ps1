# areas: driving, persistence
# track-races 4.1 (ROADMAP row 27b): Ann and Bob drive on the race track; Ann starts a one-lap race, both get the same
# race and the game's own lights turn green within 150 ms on both (wall clock, one PC). Laps through the game's
# LastTime give the result Ann 1:30 first, Bob 1:35 second on both and on the server. In a two-lap race Bob leaves the
# track, comes back and watches, and his own start is refused while the race runs; Ann finishes, Bob is DNF. In a third
# race Bob restarts from the pause menu and is DNF. Bob joins again and the server restarts: the results stay.
# track-races D7: Ann (the starter) starts on grid box 1, Bob on box 2 beside her (6.15 m to the right), each within
# -GridToleranceM, and each sees the other's copy there. track-collisions D3: during the countdown neither collider is
# on; with their own boxes both are on after the green.
param($Ctx, [int]$GreenToleranceMs = 150, [double]$GridToleranceM = 0.2)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "race_$Label.json") -Encoding utf8 }
function Note([string]$Message) { $script:Ctx.Result.notes += $Message; Write-Host "note: $Message" -ForegroundColor Yellow }
function Id([string]$Name) { [int](Get-HarnessStatus -Instance $Name).playerId }
function ServerLines($Mark) { @(Get-ServerLogLines | Select-Object -Skip $Mark) }
function Wait-Log([string]$Pattern, $Mark, [int]$TimeoutSec = 20) {
    try { Wait-ServerLog -Pattern $Pattern -After $Mark -TimeoutSec $TimeoutSec } catch { $null }
}
function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Wait-Menu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 60 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
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
function Wait-Scene([string]$Name, [string]$Scene, [int]$TimeoutSec = 180) {
    try {
        Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "on a track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null
        Wait-HarnessDump -Instance $Name -TimeoutSec 30 -What "local scene $Scene" -Condition { param($d) $d.local.scene -eq $Scene } | Out-Null
        $true
    } catch { $false }
}
function Wait-Race([string]$Name, [scriptblock]$Condition, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $state = try { Cmd $Name race-state } catch { $null }
        if ($state -and (& $Condition $state)) { return $state }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $state
}
function Wait-Result([string]$Name, [int]$RaceId) {
    $state = Wait-Race $Name { param($s) @($s.results | Where-Object { $_.RaceId -eq $RaceId }).Count -eq 1 } 30
    @($state.results | Where-Object { $_.RaceId -eq $RaceId }) | Select-Object -First 1
}
function To-Track([string]$Name, [int]$Loader) {
    $mark = Get-ServerLogMark
    $id = Id $Name
    Cmd $Name track-go "$Loader RaceTrack" | Out-Null
    $on = Wait-Scene $Name "RaceTrack"
    $drive = Wait-Log "\[Drive\] Player $id drives .* in RaceTrack" $mark 60
    return ($on -and [bool]$drive)
}
function Grid-Place([string]$Name) {
    $s = Cmd $Name grid-start
    [pscustomobject]@{ right = [double]$s.car.relative.right; forward = [double]$s.car.relative.forward; text = "right $($s.car.relative.right), forward $($s.car.relative.forward)" }
}
function Wait-Collider([string]$Name, [int]$Other, [int]$TimeoutSec = 6) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $c = @((Cmd $Name remote-collider "$Other").cars) | Select-Object -First 1
        if ($c.enabled) { return $c }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $c
}
function Names($Result) { (@($Result.order) | ForEach-Object { if ($_.Dnf) { "$($_.name) DNF $($_.reason)" } else { "$($_.name) $($_.TotalMs)" } }) -join ", " }
function Races([string]$Label) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "races"
    Wait-Log "Races:" $mark | Out-Null
    Start-Sleep -Seconds 1
    $lines = ServerLines $mark
    $lines | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "races_$Label.txt") -Encoding utf8
    return ($lines -join "`n")
}
function Rejoin([string]$Name) {
    Cmd $Name to-menu | Out-Null
    Wait-Menu $Name
    Connect-HarnessInstance $Name
    Wait-InGarage $Name
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
Check (To-Track $a 0) "Ann drives on the race track"
Check (To-Track $b 1) "Bob drives on the race track"
Check ((Cmd $a race-state).panelShown) "the session panel offers the race on the race track"
$deadline = (Get-Date).AddSeconds(60)
do { Start-Sleep -Milliseconds 700; $built = @((Cmd $a remote-collider "$idB").cars)[0].built -and @((Cmd $b remote-collider "$idA").cars)[0].built } while (-not $built -and (Get-Date) -lt $deadline)
Check $built "both have a collider on the other's car before the race"

# 1. A one-lap race: same race on both, green within the tolerance.
$mark = Get-ServerLogMark
$start = Cmd $a race-start "1"
Check $start.sent "Ann's start request is sent ($($start.message))"
$started = Wait-Log "\[Race\] Race (\d+) on the race track started by client $idA" $mark
Check ([bool]$started) "the server starts the race"
$stateA = Wait-Race $a { param($s) $s.race -and $s.race.phase -eq "Countdown" }
$stateB = Wait-Race $b { param($s) $s.race -and $s.race.phase -eq "Countdown" }
$race1 = [int]$stateA.race.RaceId
Save "countdown_A" $stateA; Save "countdown_B" $stateB
$colliderA = @((Cmd $a remote-collider "$idB").cars) | Select-Object -First 1
Check (-not $colliderA.enabled -and $colliderA.reason -eq "race-start") "during the countdown Ann's collider of Bob's car is off (reason $($colliderA.reason))"
Check ($race1 -gt 0 -and $stateB.race.RaceId -eq $race1) "both have race $race1 (Ann $($stateA.race.RaceId), Bob $($stateB.race.RaceId))"
foreach ($s in $stateA, $stateB) {
    Check ((@($s.race.Participants) -contains $idA) -and (@($s.race.Participants) -contains $idB)) "the participants are Ann and Bob ($(@($s.race.Participants) -join ', '))"
    Check ((@($s.race.Grid) -join ',') -eq "$idA,$idB") "the grid is Ann (the starter), then Bob ($(@($s.race.Grid) -join ', '))"
}
$greenA = Wait-Race $a { param($s) $s.race -and $s.race.GreenWallMs -gt 0 } 25
$greenB = Wait-Race $b { param($s) $s.race -and $s.race.GreenWallMs -gt 0 } 25
Save "green_A" $greenA; Save "green_B" $greenB
$gA = [long]$greenA.race.GreenWallMs; $gB = [long]$greenB.race.GreenWallMs
$spread = [math]::Abs($gA - $gB)
Note "green: Ann $gA (plan $($greenA.race.StartWallMs), released $($greenA.race.ReleasedWallMs)), Bob $gB (plan $($greenB.race.StartWallMs), released $($greenB.race.ReleasedWallMs)), spread $spread ms"
Check ($gA -gt 0 -and $gB -gt 0) "the game's lights turn green on both (Ann $gA, Bob $gB)"
Check ($spread -le $GreenToleranceMs) "the green lights are $spread ms apart (at most $GreenToleranceMs ms)"
Check ($greenA.race.phase -eq "Racing" -and $greenB.race.phase -eq "Racing") "both are racing"
$placeA = Grid-Place $a
$placeB = Grid-Place $b
Save "grid_A" $placeA; Save "grid_B" $placeB
Check ([math]::Abs($placeA.right) -le $GridToleranceM -and [math]::Abs($placeA.forward) -le $GridToleranceM) "Ann stands on grid box 1, the game's own spot ($($placeA.text))"
Check ([math]::Abs($placeB.right - 6.15) -le $GridToleranceM -and [math]::Abs($placeB.forward) -le $GridToleranceM) "Bob stands on grid box 2, 6.15 m to Ann's right ($($placeB.text))"
Check (-not (Cmd $a race-state).spawnMoved -and -not (Cmd $b race-state).spawnMoved) "the car spawn is back on the game's own spot on both after the restart"
$colliderA = Wait-Collider $a $idB
$colliderB = Wait-Collider $b $idA
Save "collider_after_green_A" $colliderA; Save "collider_after_green_B" $colliderB
Check ([math]::Abs($colliderA.toLocal - 6.15) -le 0.5 -and [math]::Abs($colliderB.toLocal - 6.15) -le 0.5) "each sees the other's copy on the next box (Ann to Bob's copy $($colliderA.toLocal) m, Bob to Ann's copy $($colliderB.toLocal) m)"
Check ($colliderA.enabled -and $colliderB.enabled) "after the green both colliders are on: each racer has a box of their own (Ann: $($colliderA.reason), Bob: $($colliderB.reason))"

$lapA = Cmd $a track-lap "90000"
$lapB = Cmd $b track-lap "95000"
Check ($lapA.sent -eq 1 -and $lapB.sent -eq 1) "both laps go through LastTime (Ann $($lapA.sent), Bob $($lapB.sent))"
$resultA = Wait-Result $a $race1
$resultB = Wait-Result $b $race1
Save "result1_A" $resultA; Save "result1_B" $resultB
Check ((Names $resultA) -eq "Ann 90000, Bob 95000") "Ann sees Ann first (1:30) and Bob second (1:35) ($(Names $resultA))"
Check ((Names $resultB) -eq "Ann 90000, Bob 95000") "Bob sees the same result ($(Names $resultB))"
Check ((Races "after_race1") -match "race $race1 \(1 laps\): 1\. Ann 1:30\.000 \(best lap 1:30\.000\), 2\. Bob 1:35\.000") "the server's races shows the stored result"
Check ((Cmd $a race-state).race -eq $null) "Ann's race is over"

# 2. Two laps: Bob leaves mid-race, comes back to watch, his start is refused; Ann finishes, Bob is DNF.
$mark = Get-ServerLogMark
Cmd $a race-start "2" | Out-Null
$greenA = Wait-Race $a { param($s) $s.race -and $s.race.GreenWallMs -gt 0 } 25
Wait-Race $b { param($s) $s.race -and $s.race.GreenWallMs -gt 0 } 25 | Out-Null
$race2 = [int]$greenA.race.RaceId
Check ($race2 -gt $race1) "race $race2 runs"
Cmd $a track-lap "60000" | Out-Null
Cmd $b track-return | Out-Null
Wait-InGarage $b 180
Check ([bool](Wait-Log "\[Race\] Race $race2`: Bob \(client $idB\) did not finish \(LeftTrack" $mark)) "the server marks Bob DNF when he leaves the track"
Check ((Cmd $a race-state).race.phase -eq "Racing") "Ann's race runs on"
$mark = Get-ServerLogMark
Check (To-Track $b 1) "Bob drives on the race track again"
$watch = Wait-Race $b { param($s) $s.race -and $s.race.phase -eq "Watching" } 20
Save "watch_B" $watch
Check ($watch.race.RaceId -eq $race2 -and $watch.race.phase -eq "Watching") "Bob arrives during race $race2 and watches it (phase $($watch.race.phase))"
Check ([bool](Wait-Log "\[Race\] Client $idB arrived on the race track during race $race2 and watches it" $mark)) "the server sends Bob the running race"
$refuse = Cmd $b race-start "1"
Check $refuse.sent "Bob's start request is sent while he watches"
Check ([bool](Wait-Log "\[Race\] Start of a 1-lap race on the race track by client $idB refused: RaceRunning" $mark)) "the server refuses Bob's start: a race is running"
$refused = Wait-Race $b { param($s) $s.message -eq "A race is already running on the race track." } 10
Check ($refused.message -eq "A race is already running on the race track.") "Bob is told 'A race is already running on the race track.' ($($refused.message))"
Check ((Cmd $a race-state).race.RaceId -eq $race2) "Ann's race is unchanged"
Cmd $a track-lap "65000" | Out-Null
$result2 = Wait-Result $b $race2
Save "result2_B" $result2
Check ((Names $result2) -eq "Ann 125000, Bob DNF LeftTrack") "result: Ann 2:05 over two laps, Bob DNF (left the track) ($(Names $result2))"

# 3. A pause-menu restart during a race is a DNF.
$mark = Get-ServerLogMark
Cmd $a race-start "1" | Out-Null
Wait-Race $a { param($s) $s.race -and $s.race.GreenWallMs -gt 0 } 25 | Out-Null
$greenB = Wait-Race $b { param($s) $s.race -and $s.race.GreenWallMs -gt 0 } 25
$race3 = [int]$greenB.race.RaceId
Check ($greenB.race.phase -eq "Racing" -and @($greenB.race.Participants) -contains $idB) "Bob races in race $race3 after coming back"
$restart = Cmd $b race-restart
Check ($restart.quitSent -eq 1) "Bob's pause-menu restart sends a quit ($($restart.quitSent))"
Check ([bool](Wait-Log "\[Race\] Race $race3`: Bob \(client $idB\) did not finish \(Quit" $mark)) "the server marks Bob DNF after the restart"
Cmd $a track-lap "88000" | Out-Null
$result3 = Wait-Result $a $race3
Check ((Names $result3) -eq "Ann 88000, Bob DNF Quit") "result: Ann 1:28, Bob DNF (restarted) ($(Names $result3))"

# 4. The results stay after joining again and after a server restart.
Cmd $a track-return | Out-Null
Cmd $b track-return | Out-Null
Wait-InGarage $a 180; Wait-InGarage $b 180
Rejoin $b
$ids = @((Cmd $b race-state).results | ForEach-Object { [int]$_.RaceId })
Check (($ids -contains $race1) -and ($ids -contains $race2) -and ($ids -contains $race3)) "Bob joins again and has the results of races $race1, $race2, $race3 ($($ids -join ', '))"

$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
Stop-TestServer
foreach ($name in $a, $b) { Wait-Menu $name; Cmd $name mp-ui "ok" | Out-Null }
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$races = Races "after_restart"
Check ($races -match "stored results: 3" -and $races -match "race $race1 \(1 laps\): 1\. Ann 1:30\.000" -and $races -match "race $race2 \(2 laps\): 1\. Ann 2:05\.000.*DNF Bob \(LeftTrack") "the server keeps the three results after the restart"
$after = Cmd $a race-state
Save "results_after_restart_A" $after
Check (@($after.results).Count -eq 3 -and (Names (@($after.results)[0])) -eq "Ann 90000, Bob 95000") "Ann has the three results after the restart ($(@($after.results).Count))"
Check (-not (Cmd $a race-state).panelShown) "the race entry is not shown in the garage"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

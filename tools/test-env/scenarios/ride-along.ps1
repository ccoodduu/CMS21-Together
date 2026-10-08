# areas: driving, testdrive, presence
# ride-along (ROADMAP row 21): Ann sits in the driver seat and Bob in the passenger seat of a car; Ann starts a test
# drive and Bob travels with her. On the track Bob's own track car is frozen and hidden, his camera sits at the
# passenger head of the observer copy of Ann's car and stays there while she drives, his input moves nothing, and Ann
# sees Bob's avatar in her passenger seat. Ann drives back: Bob returns too, not seated, no extra mileage. A second
# ride: Bob returns on his own and Ann is told. A third: Ann disconnects mid-drive and Bob returns. Headless games:
# transforms are checked, not pixels.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "ride_$Label.json") -Encoding utf8 }
function Distance($p, $q) { if (-not $p -or -not $q) { return 999 }; [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }
function Angle($p, $q) {
    if (-not $p -or -not $q) { return 999 }
    $dot = ($p.x * $q.x + $p.y * $q.y + $p.z * $q.z) / ([math]::Max(1e-6, [math]::Sqrt($p.x * $p.x + $p.y * $p.y + $p.z * $p.z) * [math]::Sqrt($q.x * $q.x + $q.y * $q.y + $q.z * $q.z)))
    [math]::Round([math]::Acos([math]::Max(-1, [math]::Min(1, $dot))) * 180 / [math]::PI, 1)
}

function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Wait-Track([string]$Name) {
    try { Wait-HarnessStatus -Instance $Name -TimeoutSec 180 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null; $true } catch { $false }
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
function Wait-Ride([string]$Name, [scriptblock]$Condition, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $state = try { Cmd $Name ride-state } catch { $null }
        if ($state -and (& $Condition $state)) { return $state }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $state
}
function Expect([string]$Name, [string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 15) {
    try { Wait-HarnessDump -Instance $Name -TimeoutSec $TimeoutSec -What $What -Condition $Condition | Out-Null; Check $true "$Name`: $What" }
    catch { Check $false "$Name`: $What ($($_.Exception.Message.Split("`n")[0]))" }
}
function Wait-InputDone([string]$Name, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do { Start-Sleep -Milliseconds 500; $s = Cmd $Name drive-input-state } while ($s.running -and (Get-Date) -lt $deadline)
    return $s
}
function Toasts([string]$Name) { @((Cmd $Name dump).session.toasts) }
function Avatar($State, [int]$PlayerId) { @($State.avatars) | Where-Object { $_.playerId -eq $PlayerId } | Select-Object -First 1 }

function Start-Ride([string]$Label) {
    Cmd $a sit "0 left" | Out-Null
    Cmd $b sit "0 right" | Out-Null
    Expect $a "seated in car 0 (left) for $Label" { param($d) $d.local.seat -eq 0 -and $d.local.seatLeft }
    Expect $b "seated in car 0 (right) for $Label" { param($d) $d.local.seat -eq 0 -and -not $d.local.seatLeft }
    Expect $a "sees Bob in the right seat" { param($d) $r = $d.roster."$idB"; $r -and $r.seat -eq 0 -and -not $r.seatLeft }
    $script:mark = Get-ServerLogMark
    $script:toastsB = @(Toasts $b).Count
    $script:toastsA = @(Toasts $a).Count
    $script:captureB = (Cmd $b ride-state).capture
    Cmd $a testdrive-go "0" | Out-Null
    $ride = try { Wait-ServerLog -Pattern "\[Ride\] Client $idB sits in car 0 \(right\) and rides along with client $idA" -After $mark -TimeoutSec 20 } catch { $null }
    Check ([bool]$ride) "${Label}: the server puts Bob on Ann's test drive ($ride)"
    Check (Wait-Track $a) "${Label}: Ann reached the test track"
    Check (Wait-Track $b) "${Label}: Bob travelled to the test track with her"
    $seated = Wait-Ride $b { param($s) $s.phase -eq "Seated" -and $s.camera.placed } 120
    Save "${Label}_seated_B" $seated
    Check ($seated.phase -eq "Seated" -and $seated.driverId -eq $idA) "${Label}: Bob is seated in Ann's car (phase $($seated.phase), driver $($seated.driverId))"
    return $seated
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Cmd $a set-name "Ann" | Out-Null
Cmd $b set-name "Bob" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$idA = [int](Get-HarnessStatus -Instance $a).playerId
$idB = [int](Get-HarnessStatus -Instance $b).playerId
foreach ($name in $a, $b) { Cmd $name guard-allow "Mode:CarDrive" | Out-Null }

Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0; Wait-Ready $b 0
Start-Sleep -Seconds 3
$mileageBefore = [int]((Cmd $a cardetails-show "0").Info | ConvertFrom-Json).Mileage

# Round 1: a ride, a drive, Ann drives back.
$seated = Start-Ride "ride1"
$toast = @(Toasts $b | Select-Object -Skip $toastsB)
Check (@($toast | Where-Object { $_ -eq "Riding along with Ann." }).Count -ge 1) "Bob was told he rides along ($($toast -join ' | '))"
Check ($seated.own.kinematic -and $seated.own.inputsEnabled -eq 0 -and $seated.hiddenRenderers -gt 0) "Bob's own track car is frozen and hidden (kinematic $($seated.own.kinematic), inputs on $($seated.own.inputsEnabled), $($seated.hiddenRenderers) renderers hidden)"
$newDrives = $seated.capture.drives - $captureB.drives
$newStates = $seated.capture.sent - $captureB.sent
Check (-not $seated.capture.active -and $newDrives -eq 0 -and $newStates -eq 0) "Bob streams no drive of his own (active $($seated.capture.active), new drives $newDrives, new states $newStates)"
Check ($seated.camera.customPos -and $seated.camera.toHead -ge 0 -and $seated.camera.toHead -le 0.05) "Bob's camera sits at the passenger head of Ann's car ($($seated.camera.toHead) m, game camera off $($seated.camera.customPos), head measured $($seated.headMeasured))"
$stateA = Wait-Ride $a { param($s) $av = Avatar $s $idB; $av -and $av.active -and $av.toSeat -ge 0 -and $av.toSeat -le 0.05 } 30
Save "ride1_seated_A" $stateA
$avB = Avatar $stateA $idB
Check ($avB -and $avB.active -and $avB.toSeat -le 0.05) "Ann sees Bob's avatar in her passenger seat (active $($avB.active), $($avB.toSeat) m from the seat)"
Check (@(Toasts $a | Select-Object -Skip $toastsA | Where-Object { $_ -eq "Bob rides along." }).Count -ge 1) "Ann was told Bob rides along"
$avA = Avatar (Cmd $b ride-state) $idA
Check ($avA -and $avA.active -and $avA.toSeat -le 0.05) "Bob sees Ann's avatar in the driver seat of her car ($($avA.toSeat) m)"

$samples = @()
$follow = @()
$worstHead = 0.0
$worstFollow = 0.0
Cmd $a drive-input "0.6 0 5" | Out-Null
for ($i = 0; $i -lt 6; $i++) {
    Start-Sleep -Milliseconds 600
    $s = Cmd $b ride-state
    $car = @((Cmd $b dump).remoteCars.cars) | Where-Object { $_.playerId -eq $idA } | Select-Object -First 1
    $truth = Cmd $a drive-history ([string]::Format([cultureinfo]::InvariantCulture, "{0}", $car.renderTime))
    $samples += $s
    $follow += [pscustomobject]@{ renderTime = $car.renderTime; copy = $car.position; driver = $truth.position }
    $worstHead = [math]::Max($worstHead, $(if ($s.camera.toHead -lt 0) { 999 } else { $s.camera.toHead }))
    $worstFollow = [math]::Max($worstFollow, (Distance $car.position $truth.position))
}
Save "ride1_follow" $follow
Wait-InputDone $a | Out-Null
Cmd $a drive-input "0.4 0.5 3" | Out-Null
for ($i = 0; $i -lt 4; $i++) {
    Start-Sleep -Milliseconds 600
    $s = Cmd $b ride-state
    $samples += $s
    $worstHead = [math]::Max($worstHead, $(if ($s.camera.toHead -lt 0) { 999 } else { $s.camera.toHead }))
}
Wait-InputDone $a | Out-Null
Save "ride1_samples_B" $samples
$travel = Distance $samples[0].camera.position $samples[-1].camera.position
Check ($travel -gt 5) "Bob's camera travelled with Ann's car ($([math]::Round($travel, 1)) m)"
Check ($worstHead -le 0.05) "Bob's camera stayed at the passenger head while Ann drove (worst $([math]::Round($worstHead, 3)) m)"
Check ($worstFollow -le 1.5) "the car Bob rides follows Ann's path within 1.5 m (worst $([math]::Round($worstFollow, 2)) m)"
Check ($samples[-1].camera.maxDrift -le 0.05) "nothing else moved Bob's camera between frames (max drift $($samples[-1].camera.maxDrift) m over $($samples[-1].camera.frames) frames)"

Cmd $a drive-stop | Out-Null
Wait-InputDone $a | Out-Null
Start-Sleep -Seconds 2
$restA = Cmd $a ride-state
$restB = Cmd $b ride-state
Save "ride1_rest_A" $restA
Save "ride1_rest_B" $restB
$frontAngle = Angle $restA.own.passengerSeat.front $restB.copy.passengerSeat.front
$seatLocal = Distance $restA.own.passengerSeat.local $restB.copy.passengerSeat.local
$seatWorld = Distance $restA.own.passengerSeat.world $restB.copy.passengerSeat.world
Check ($frontAngle -le 5) "Ann's car faces the same way on both games ($frontAngle degrees)"
Check ($seatWorld -le 0.1) "Bob's seat is where Ann's passenger seat is ($([math]::Round($seatWorld, 3)) m apart; from the wheel centre $([math]::Round($seatLocal, 3)) m, the copy's wheels have no suspension travel)"

$ownBefore = (Cmd $b ride-state).own.position
$markInput = Get-ServerLogMark
Cmd $b drive-input "0.6 0 3" | Out-Null
Wait-InputDone $b | Out-Null
Start-Sleep -Seconds 1
$afterB = Cmd $b ride-state
$restA2 = Cmd $a ride-state
$moved = Distance $ownBefore $afterB.own.position
$starts = @(Get-ServerLogLines | Select-Object -Skip $markInput | Where-Object { $_ -match "\[Drive\] Player $idB drives" })
Check ($moved -le 0.05 -and $afterB.capture.drives -eq $captureB.drives -and $starts.Count -eq 0) "Bob's input drives nothing (own car moved $([math]::Round($moved, 3)) m, new drives $($afterB.capture.drives - $captureB.drives), server drive starts $($starts.Count))"
Check ((Distance $restA.own.passengerSeat.world $restA2.own.passengerSeat.world) -le 0.1) "Ann's car did not move from Bob's input"
Check ($afterB.camera.toHead -le 0.05) "Bob is still in his seat ($($afterB.camera.toHead) m)"

$markReturn = Get-ServerLogMark
$toastsB = @(Toasts $b).Count
Cmd $a testdrive-drive "3000" | Out-Null
Cmd $a testdrive-finish "all" | Out-Null
$ended = try { Wait-ServerLog -Pattern "\[Ride\] Client $idB's ride with client $idA in car 0 ended: the driver went to" -After $markReturn -TimeoutSec 30 } catch { $null }
Check ([bool]$ended) "the ride ends when Ann drives back ($ended)"
Wait-InGarage $a 180
try { Wait-InGarage $b 180; $backB = $true } catch { $backB = $false }
Check $backB "Bob returned to the garage with Ann"
$done = Wait-Ride $b { param($s) $s.phase -eq "None" } 20
Check ($done.phase -eq "None" -and @($done.rides).Count -eq 0) "Bob is no longer a passenger (phase $($done.phase), rides $(@($done.rides).Count))"
Check (@(Toasts $b | Select-Object -Skip $toastsB | Where-Object { $_ -eq "Ann drove back to the garage." }).Count -ge 1) "Bob was told Ann drove back"
Expect $b "not seated after the return" { param($d) $d.local.seat -eq -1 }
Expect $a "sees Bob standing" { param($d) $r = $d.roster."$idB"; $r -and $r.seat -eq -1 }
$deadline = (Get-Date).AddSeconds(40)
do { Start-Sleep -Seconds 1; $awayA = @((Cmd $a dump).away); $awayB = @((Cmd $b dump).away) } while (($awayA.Count -ne 0 -or $awayB.Count -ne 0) -and (Get-Date) -lt $deadline)
Check ($awayA.Count -eq 0 -and $awayB.Count -eq 0) "the test drive claim is released after the return"
Start-Sleep -Seconds 4
$mileageA = [int]((Cmd $a cardetails-show "0").Info | ConvertFrom-Json).Mileage
$mileageB = [int]((Cmd $b cardetails-show "0").Info | ConvertFrom-Json).Mileage
Check ($mileageA -ge $mileageBefore + 3 -and $mileageA -eq $mileageB) "only Ann's drive counts (car 0: $mileageBefore -> Ann $mileageA, Bob $mileageB km)"
$differ = Compare-HarnessDumps -Left (Cmd $a dump) -Right (Cmd $b dump) -Sections cars, away
Check ($differ.Count -eq 0) "Ann and Bob have the same cars and away sections (differ: $($differ -join ', '))"

# Round 2: Bob leaves early by the track's own return; Ann keeps driving and is told.
Start-Sleep -Seconds 2
Start-Ride "ride2" | Out-Null
$markEarly = Get-ServerLogMark
$toastsA = @(Toasts $a).Count
Cmd $b testdrive-finish | Out-Null
$ended = try { Wait-ServerLog -Pattern "\[Ride\] Client $idB's ride with client $idA in car 0 ended: the passenger went to" -After $markEarly -TimeoutSec 30 } catch { $null }
Check ([bool]$ended) "the ride ends when Bob returns on his own ($ended)"
try { Wait-InGarage $b 180; $backB = $true } catch { $backB = $false }
Check $backB "Bob is back in the garage on his own"
$done = Wait-Ride $b { param($s) $s.phase -eq "None" } 20
Check ($done.phase -eq "None") "Bob is no longer a passenger (phase $($done.phase))"
Check (@(Toasts $a | Select-Object -Skip $toastsA | Where-Object { $_ -eq "Bob left the ride." }).Count -ge 1) "Ann was told Bob left the ride"
$stillA = Cmd $a ride-state
Check ((Get-HarnessStatus -Instance $a).scene -match "(?i)track" -and $stillA.capture.active -and @($stillA.rides).Count -eq 0) "Ann is still driving on the track, without a passenger (drive active $($stillA.capture.active), rides $(@($stillA.rides).Count))"
Cmd $a testdrive-finish "all" | Out-Null
Wait-InGarage $a 180
$deadline = (Get-Date).AddSeconds(40)
do { Start-Sleep -Seconds 1; $awayA = @((Cmd $a dump).away); $awayB = @((Cmd $b dump).away) } while (($awayA.Count -ne 0 -or $awayB.Count -ne 0) -and (Get-Date) -lt $deadline)
Start-Sleep -Seconds 4
$mileageA = [int]((Cmd $a cardetails-show "0").Info | ConvertFrom-Json).Mileage
$mileageB = [int]((Cmd $b cardetails-show "0").Info | ConvertFrom-Json).Mileage
Check ($awayA.Count -eq 0 -and $mileageA -eq $mileageB) "after the second drive the claim is released and both see $mileageA km (Bob $mileageB km)"

# Round 3: the same ride, Ann disconnects mid-drive.
Start-Sleep -Seconds 2
Start-Ride "ride3" | Out-Null
Cmd $a drive-input "0.6 0 3" | Out-Null
Start-Sleep -Seconds 2
$markLeave = Get-ServerLogMark
$toastsB = @(Toasts $b).Count
Cmd $a disconnect | Out-Null
$ended = try { Wait-ServerLog -Pattern "\[Ride\] Client $idB's ride with client $idA in car 0 ended: the driver left the game" -After $markLeave -TimeoutSec 30 } catch { $null }
Check ([bool]$ended) "the ride ends when Ann disconnects ($ended)"
try { Wait-InGarage $b 180; $backB = $true } catch { $backB = $false }
Check $backB "Bob returned to the garage after Ann left"
$done = Wait-Ride $b { param($s) $s.phase -eq "None" } 20
Save "ride3_back_B" $done
Check ($done.phase -eq "None") "Bob is no longer a passenger (phase $($done.phase))"
Check (@(Toasts $b | Select-Object -Skip $toastsB | Where-Object { $_ -eq "Ann left the game." }).Count -ge 1) "Bob was told Ann left"
Expect $b "not seated, no claim left after Ann left" { param($d) $d.local.seat -eq -1 -and @($d.away).Count -eq 0 } 30

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

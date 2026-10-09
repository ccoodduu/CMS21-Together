# areas: presence, placement
# seated-avatars (ROADMAP row 29): Ann sits in a car on lift 1 with the engine running and Bob sees her avatar in that
# seat (no jitter), also while the lift goes up; she changes to the right seat; she stands up and Bob sees her where
# she stands; Bob rejoins while she sits and sees her seated once the car is Ready; both sit in one car and each sees
# the other in the right seat; Bob moves the car while Ann sits, and Ann's avatar never floats away from the car or
# from Ann. Offsets come from the harness's per-frame sampler (seat-pose), not from single dumps.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "seat_$Label.json") -Encoding utf8 }
function Distance($p, $q) { if (-not $p -or -not $q) { return 999 }; [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}
function Pose([string]$Name, [int]$PlayerId) { (Cmd $Name seat-pose).players."$PlayerId" }
function Expect-Pose([string]$Name, [int]$PlayerId, [string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 15) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $p = try { Pose $Name $PlayerId } catch { $null }
        if ($p -and (& $Condition $p)) { Check $true "$Name`: $What ($($p.seatPose | ConvertTo-Json -Compress))"; return $p }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    Check $false "$Name`: $What (last: $($p | ConvertTo-Json -Compress -Depth 5))"
    return $p
}
function Expect-Local([string]$Name, [string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 15) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $l = try { (Cmd $Name seat-pose).local } catch { $null }
        if ($l -and (& $Condition $l)) { Check $true "$Name`: $What"; return $l }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    Check $false "$Name`: $What (last: $($l | ConvertTo-Json -Compress))"
    return $l
}
function Wait-Lift([string]$State) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $lift = @(Cmd $a lifters)[0]
        if ($lift.state -eq $State -and -not $lift.isMoving) { return $lift }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    Check $false "lift 0 reached $State (is $($lift.state), moving $($lift.isMoving))"
    return $lift
}
function Seated($p, [string]$Side) { $p.active -and $p.seatPose.seated -and $p.seatPose.side -eq $Side -and $p.seatPose.toSeat -ge 0 -and $p.seatPose.toSeat -le 0.05 }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Cmd $a set-name "Ann" | Out-Null
Cmd $b set-name "Bob" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$idA = [int](Get-HarnessStatus -Instance $a).playerId
$idB = [int](Get-HarnessStatus -Instance $b).playerId

Cmd $a car-spawn "$loader car_boltatlanta 0" | Out-Null
Wait-Ready $a; Wait-Ready $b
Cmd $a car-move "$loader CarLifter1" | Out-Null
Start-Sleep -Seconds 6
Wait-Ready $a; Wait-Ready $b

# 1. Ann sits on the left with the engine running: Bob sees her in that seat, without jitter.
Cmd $a sit "$loader left" | Out-Null
Expect-Local $a "seated in car $loader (left)" { param($l) $l.seat -eq $loader -and $l.seatLeft } | Out-Null
Cmd $a engine "on" | Out-Null
$p = Expect-Pose $b $idA "sees Ann seated on the left within 5 cm" { param($p) Seated $p "left" }
Cmd $b seat-pose-reset | Out-Null
Start-Sleep -Seconds 3
$p = Pose $b $idA
Save "1_engine" $p
Check ($p.seatPose.seated -and $p.seatPose.frames -gt 0 -and $p.seatPose.maxSinceReset -ge 0 -and $p.seatPose.maxSinceReset -le 0.05) "B: Ann stays in the seat with the engine running for 3 s (max $($p.seatPose.maxSinceReset) m over $($p.seatPose.frames)+ frames)"
$dumpB = Cmd $b dump
Check ($dumpB.roster."$idA".avatarActive -and $dumpB.players."$idA".seatPose.seated) "B's dump shows Ann's avatar active and seated"
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "seated"

# 2. The lift goes up with Ann in the car.
Cmd $b seat-pose-reset | Out-Null
Cmd $a lift "0 up" | Out-Null
Wait-Lift "Middle" | Out-Null
Cmd $a lift "0 up" | Out-Null
Wait-Lift "Up" | Out-Null
Start-Sleep -Seconds 1
$p = Pose $b $idA
Save "2_lift" $p
Check ($p.seatPose.seated -and $p.seatPose.maxSinceReset -ge 0 -and $p.seatPose.maxSinceReset -le 0.10) "B: Ann stays in the seat while the lift rises (max $($p.seatPose.maxSinceReset) m; handle moved $($p.seatPose.handleMovedBeforeLate) m before and $($p.seatPose.handleMovedAfterLate) m after OnLateUpdate)"
Check ($p.seatPose.handleMovedBeforeLate + $p.seatPose.handleMovedAfterLate -gt 0.5) "B: the seat handle travelled with the lift ($($p.seatPose.handleMovedBeforeLate + $p.seatPose.handleMovedAfterLate) m)"
Expect-Pose $b $idA "at the top Ann is within 5 cm of the seat" { param($p) Seated $p "left" } | Out-Null

# 3. Ann changes to the right seat (the car is still up).
Cmd $a engine "off" | Out-Null
Cmd $a stand | Out-Null
Expect-Local $a "stood up" { param($l) $l.seat -eq -1 -and -not $l.seatedMode } | Out-Null
Cmd $a sit "$loader right" | Out-Null
Expect-Local $a "seated in car $loader (right) on the raised lift" { param($l) $l.seat -eq $loader -and -not $l.seatLeft } | Out-Null
Expect-Pose $b $idA "sees Ann seated on the right" { param($p) Seated $p "right" } | Out-Null

# 4. Ann stands up: Bob sees her standing where she stands.
Cmd $a stand | Out-Null
$local = Expect-Local $a "stood up, seated mode ended" { param($l) $l.seat -eq -1 -and -not $l.seatedMode }
Start-Sleep -Seconds 1
$local = (Cmd $a seat-pose).local
$p = Expect-Pose $b $idA "sees Ann standing within 1.5 m of where she stands" { param($p) $p.active -and -not $p.seatPose.seated -and (Distance $p.position $script:local.position) -le 1.5 } 10
Save "4_stand" @{ local = $local; pose = $p }

Cmd $a lift "0 down" | Out-Null
Wait-Lift "Middle" | Out-Null
Cmd $a lift "0 down" | Out-Null
Wait-Lift "OnFloor" | Out-Null

# 5. Bob rejoins while Ann sits on the left.
Cmd $a sit "$loader left" | Out-Null
Expect-Local $a "seated on the left again" { param($l) $l.seat -eq $loader -and $l.seatLeft } | Out-Null
Expect-Pose $b $idA "sees Ann seated before the rejoin" { param($p) Seated $p "left" } | Out-Null
Cmd $b to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 60 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Ready $b
Expect-Pose $b $idA "after the rejoin sees Ann seated on the left within 10 s of the car being Ready" { param($p) Seated $p "left" } 10 | Out-Null

# 6. Bob sits on the right of the same car.
Cmd $b sit "$loader right" | Out-Null
Expect-Local $b "Bob seated on the right" { param($l) $l.seat -eq $loader -and -not $l.seatLeft } | Out-Null
Expect-Pose $a $idB "Ann sees Bob seated on the right" { param($p) Seated $p "right" } | Out-Null
Expect-Pose $b $idA "Bob sees Ann seated on the left" { param($p) Seated $p "left" } | Out-Null
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "both-seated"

# 7. Bob stands and moves the car while Ann sits: Ann leaves the seat, and her avatar on Bob is hidden or near the car
# or near Ann the whole time.
Cmd $b stand | Out-Null
Expect-Local $b "Bob stood up" { param($l) $l.seat -eq -1 -and -not $l.seatedMode } | Out-Null
Cmd $b car-move "$loader Entrance2" | Out-Null
$samples = @()
$worst = 0
$deadline = (Get-Date).AddSeconds(12)
do {
    $p = try { Pose $b $idA } catch { $null }
    $l = try { (Cmd $a seat-pose).local } catch { $null }
    $h = try { Cmd $b seat-handles "$loader" } catch { $null }
    if ($p -and $p.active) {
        $gap = [math]::Min((Distance $p.position $h.center), (Distance $p.position $l.position))
        if ($gap -gt $worst) { $worst = $gap }
        $samples += [pscustomobject]@{ active = $true; seated = $p.seatPose.seated; gap = [math]::Round($gap, 3); avatar = $p.position; car = $h.center; ann = $l.position }
    } else {
        $samples += [pscustomobject]@{ active = $false }
    }
    Start-Sleep -Milliseconds 200
} while ((Get-Date) -lt $deadline)
Save "7_move" $samples
Check ($worst -le 2.0) "B: Ann's avatar never floated more than 2 m from the car and from Ann during the move (worst $([math]::Round($worst, 2)) m over $($samples.Count) samples, $(@($samples | Where-Object { -not $_.active }).Count) hidden)"
Expect-Local $a "Ann left the seat when the car moved" { param($l) $l.seat -eq -1 } 10 | Out-Null
$local = (Cmd $a seat-pose).local
Expect-Pose $b $idA "sees Ann standing where she stands after the move" { param($p) $p.active -and -not $p.seatPose.seated -and (Distance $p.position $script:local.position) -le 1.5 } 10 | Out-Null

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

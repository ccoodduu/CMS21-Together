# areas: driving
# run-all: skip
# track-collisions spike 1.1: Ann on the test track probes the layers, puts a box on the collider layer under a wheel,
# spawns test copies on her own car ten times, drives into a parked test copy at 30 (ten times), 80 and 150 km/h (each
# from the car spot, restored by the pause-menu restart), compares the rebound with a static and a kinematic test box,
# puts a copy on her car with the overlap guard forced off, and jumps a copy onto her car. Bob joins on the test track: his
# drive at 30 and 80 km/h gives the copy's lag and position error, and Ann drives into his parked car. On the race
# track a forced-on copy is moved into the active checkpoint, and a pause-menu restart puts Ann on a copy at the start.
# Output: spike_*.json in the run folder.
param($Ctx, [int]$Spawns = 10, [int]$SlowHits = 10, [string[]]$Phases = @("probe", "spawns", "hits", "bounce", "forced", "snap", "lag", "realhit", "checkpoint", "restart"))

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "spike_$Label.json") -Encoding utf8 }
function Note([string]$Message) { $script:Ctx.Result.notes += $Message; Write-Host "note: $Message" -ForegroundColor Yellow }
function Id([string]$Name) { [int](Get-HarnessStatus -Instance $Name).playerId }
function Distance($p, $q) { if (-not $p -or -not $q) { return -1 }; [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }
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
function Wait-Scene([string]$Name, [string]$Scene, [int]$TimeoutSec = 180) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "on a track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null
    Wait-HarnessDump -Instance $Name -TimeoutSec 30 -What "local scene $Scene" -Condition { param($d) $d.local.scene -eq $Scene } | Out-Null
    $deadline = (Get-Date).AddSeconds(60)
    do { Start-Sleep -Milliseconds 700; $p = try { Cmd $Name drive-probe } catch { $null } } while (-not ($p -and $p.mode -eq "CarDrive" -and $p.initialized) -and (Get-Date) -lt $deadline)
    Start-Sleep -Seconds 4
}
function Collider([string]$Name, [int]$Id) { @((Cmd $Name remote-collider "$Id").cars) | Select-Object -First 1 }
function Wait-Collider([string]$Name, [int]$Id, [scriptblock]$Condition, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $c = try { Collider $Name $Id } catch { $null }
        if ($c -and (& $Condition $c)) { return $c }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $c
}
function Wait-Cruise([string]$Name, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do { Start-Sleep -Milliseconds 300; $s = Cmd $Name collide-cruise-state } while ($s.running -and (Get-Date) -lt $deadline)
    return $s
}
function Wait-InputDone([string]$Name, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do { Start-Sleep -Milliseconds 400; $s = Cmd $Name drive-input-state } while ($s.running -and (Get-Date) -lt $deadline)
    return $s
}
function Stop-Car([string]$Name) { Cmd $Name drive-stop | Out-Null; Wait-InputDone $Name | Out-Null; Cmd $Name drive-input "0 0 0.1" | Out-Null; Wait-InputDone $Name | Out-Null }
function Ghost([string]$Name, [string]$At) {
    $g = Cmd $Name collide-ghost $At
    $c = Wait-Collider $Name $g.playerId { param($c) $c.built } 40
    return [int]$g.playerId
}
function MaxFrame([string]$Name) { (Cmd $Name perf).maxMs }
function Spot([string]$Name) {
    Stop-Car $Name
    Cmd $Name race-restart | Out-Null
    Start-Sleep -Seconds 3
    $p = (Cmd $Name collide-clock).position
    if (-not $script:spots.ContainsKey($Name)) { $script:spots[$Name] = $p }
    return [pscustomobject]@{ position = $p; fromFirst = [math]::Round((Distance $p $script:spots[$Name]), 2) }
}
$spots = @{}
function Phase([string]$Label, [scriptblock]$Body) {
    if ($Phases -notcontains $Label) { return }
    try { & $Body } catch { Note "$Label failed: $($_.Exception.Message)"; Save "error_$Label" "$($_.Exception.Message) $($_.ScriptStackTrace)" }
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

Cmd $a track-go "0 TestTrack" | Out-Null
Wait-Scene $a "TestTrack"
Cmd $a collide-set "setting on" | Out-Null

Phase "probe" {
    Save "probe_testtrack" (Cmd $a collide-probe)
    Save "wheels_before" (Cmd $a collide-wheels)
    Save "box_wheel" (Cmd $a collide-box "wheel 0.06")
    Start-Sleep -Seconds 1
    Save "wheels_on_box" (Cmd $a collide-wheels)
    Cmd $a collide-box "clear" | Out-Null
}

Phase "spawns" {
    $rows = @()
    for ($i = 1; $i -le $Spawns; $i++) {
        $spot = Spot $a
        $g = Ghost $a "0 0"
        Start-Sleep -Seconds 1
        $on = Collider $a $g
        $frame = MaxFrame $a
        $before = (Cmd $a collide-clock).position
        Cmd $a drive-input "0.6 0 2.5" | Out-Null
        $inp = Wait-InputDone $a
        Start-Sleep -Milliseconds 500
        $after = Cmd $a collide-clock
        $clear = Wait-Collider $a $g { param($c) $c.reason -eq "on" } 5
        $rows += [pscustomobject]@{ i = $i; spotFromFirst = $spot.fromFirst; reasonOnSpawn = $on.reason; overlap = $on.overlap; size = $on.size; maxFrameMs = $frame; inputMode = $inp.mode; moved = [math]::Round((Distance $before $after.position), 2); reasonAfter = $clear.reason; contacts = $clear.contacts; maxFrameAfterMs = (MaxFrame $a) }
        Stop-Car $a
        Cmd $a collide-ghost-clear | Out-Null
        Start-Sleep -Milliseconds 500
    }
    Save "spawns" $rows
}

function Hit([string]$Name, [double]$Kmh, [double]$Ahead) {
    $spot = Spot $Name
    $g = Ghost $Name ([string]::Format([cultureinfo]::InvariantCulture, "{0} 0", $Ahead))
    $ready = Wait-Collider $Name $g { param($c) $c.reason -eq "on" } 10
    Cmd $Name collide-cruise ([string]::Format([cultureinfo]::InvariantCulture, "{0} 6", $Kmh)) | Out-Null
    $cruise = Wait-Cruise $Name
    $after = Collider $Name $g
    $row = [pscustomobject]@{ kmh = $Kmh; ahead = $Ahead; spotFromFirst = $spot.fromFirst; reasonBefore = $ready.reason; cruise = $cruise; contacts = $after.contacts; toLocal = $after.toLocal; size = $after.size; maxFrameMs = (MaxFrame $Name) }
    Cmd $Name collide-ghost-clear | Out-Null
    Start-Sleep -Milliseconds 500
    return $row
}

Phase "hits" {
    $rows = @()
    for ($i = 1; $i -le $SlowHits; $i++) { $rows += Hit $a 30 12 }
    $rows += Hit $a 80 15
    $rows += Hit $a 150 20
    Cmd $a collide-set "setting off" | Out-Null
    $rows += Hit $a 30 12
    Cmd $a collide-set "setting on" | Out-Null
    Save "hits" $rows
}

function Obstacle([string]$Name, [double]$Kmh, [string]$Kind, [double]$Seconds = 6) {
    $spot = Spot $Name
    if ($Kind -ne "none") { Cmd $Name collide-box "ahead 12 $Kind" | Out-Null }
    Start-Sleep -Milliseconds 500
    Cmd $Name collide-cruise ([string]::Format([cultureinfo]::InvariantCulture, "{0} {1}", $Kmh, $Seconds)) | Out-Null
    $cruise = Wait-Cruise $Name 30
    Cmd $Name collide-box "clear" | Out-Null
    return [pscustomobject]@{ kind = $Kind; kmh = $Kmh; cruise = $cruise; maxFrameMs = (MaxFrame $Name) }
}

Phase "bounce" {
    $rows = @()
    $rows += Obstacle $a 30 "none" 15
    foreach ($kind in "static", "kinematic") { $rows += Obstacle $a 30 $kind; $rows += Obstacle $a 30 $kind }
    Save "bounce_boxes" $rows
    $hits = @()
    $hits += Hit $a 30 12
    $hits += Hit $a 80 15
    $hits += Hit $a 150 20
    $hits += Hit $a 150 20
    Save "bounce_hits" $hits
}

Phase "forced" {
    Stop-Car $a
    Cmd $a collide-set "forced on" | Out-Null
    $before = Cmd $a collide-clock
    $g = Ghost $a "0 0"
    $samples = @()
    for ($i = 0; $i -lt 8; $i++) { Start-Sleep -Milliseconds 250; $samples += [pscustomobject]@{ clock = (Cmd $a collide-clock); collider = (Collider $a $g) } }
    Save "forced_overlap" ([pscustomobject]@{ before = $before; samples = $samples; maxFrameMs = (MaxFrame $a); wheels = (Cmd $a collide-wheels) })
    Cmd $a collide-set "forced auto" | Out-Null
    Cmd $a collide-ghost-clear | Out-Null
}

Phase "snap" {
    Stop-Car $a
    $g = Ghost $a "15 0"
    $on = Wait-Collider $a $g { param($c) $c.reason -eq "on" } 10
    Cmd $a collide-ghost-move "$g 0 0" | Out-Null
    $samples = @()
    for ($i = 0; $i -lt 10; $i++) { Start-Sleep -Milliseconds 200; $samples += Collider $a $g }
    Save "snap" ([pscustomobject]@{ before = $on; samples = $samples; maxFrameMs = (MaxFrame $a); clock = (Cmd $a collide-clock) })
    Cmd $a drive-input "0.6 0 2.5" | Out-Null
    Wait-InputDone $a | Out-Null
    Save "snap_after_drive" ([pscustomobject]@{ collider = (Collider $a $g); clock = (Cmd $a collide-clock) })
    Cmd $a collide-ghost-clear | Out-Null
}

Cmd $b track-go "1 TestTrack" | Out-Null
Wait-Scene $b "TestTrack"

Phase "lag" {
    Stop-Car $a
    $copy = Wait-Collider $a $idB { param($c) $c.built } 60
    Save "bob_copy" $copy
    $rows = @()
    foreach ($kmh in 30, 80, 30, 80) {
        Spot $b | Out-Null
        Cmd $b collide-cruise ([string]::Format([cultureinfo]::InvariantCulture, "{0} 3", $kmh)) | Out-Null
        Start-Sleep -Milliseconds 1200
        for ($i = 0; $i -lt 5; $i++) {
            $seen = Collider $a $idB
            $truth = Cmd $b collide-clock
            $truthNow = $truth.time + ($seen.wallMs - $truth.wallMs) / 1000.0
            $rows += [pscustomobject]@{ kmh = $kmh; speed = $truth.speed; lagS = [math]::Round($truthNow - $seen.renderTime, 3); errorM = [math]::Round((Distance $seen.copyPosition $truth.position) - $truth.speed * ($truth.wallMs - $seen.wallMs) / 1000.0, 2); callGapMs = $truth.wallMs - $seen.wallMs }
            Start-Sleep -Milliseconds 200
        }
        Wait-Cruise $b | Out-Null
        Stop-Car $b
    }
    Save "lag" $rows
}

function Lift($Frames, [double]$BaseY) {
    $ys = @($Frames | ForEach-Object { $_.p.y })
    $vs = @($Frames | ForEach-Object { [math]::Sqrt($_.v.x * $_.v.x + $_.v.y * $_.v.y + $_.v.z * $_.v.z) })
    [pscustomobject]@{ maxLift = [math]::Round((($ys | Measure-Object -Maximum).Maximum - $BaseY), 2); maxSpeed = [math]::Round(($vs | Measure-Object -Maximum).Maximum, 2); minUp = [math]::Round((@($Frames | ForEach-Object { $_.up }) | Measure-Object -Minimum).Minimum, 2); frames = $Frames.Count }
}

Phase "mutual" {
    $rows = @()
    foreach ($variant in @(@{ cap = 0; hold = "on" }, @{ cap = 0; hold = "off" }, @{ cap = 1.5; hold = "on" }, @{ cap = 1.5; hold = "off" })) {
        foreach ($name in $a, $b) { Cmd $name collide-set ([string]::Format([cultureinfo]::InvariantCulture, "cap {0}", $variant.cap)) | Out-Null; Cmd $name collide-hold "off" | Out-Null }
        Spot $a | Out-Null
        Cmd $a drive-input "0.6 0 3" | Out-Null
        Wait-InputDone $a | Out-Null
        Stop-Car $a
        Cmd $a collide-hold $variant.hold | Out-Null
        Spot $b | Out-Null
        Wait-Collider $b $idA { param($c) $c.reason -eq "on" } 10 | Out-Null
        Wait-Collider $a $idB { param($c) $c.reason -eq "on" } 10 | Out-Null
        $baseA = (Cmd $a collide-clock).position; $baseB = (Cmd $b collide-clock).position
        foreach ($name in $a, $b) { Cmd $name collide-trace "on" | Out-Null }
        Cmd $b collide-cruise "8 10" | Out-Null
        $cruise = Wait-Cruise $b 20
        Start-Sleep -Seconds 2
        $traceA = (Cmd $a collide-trace "report").frames; $traceB = (Cmd $b collide-trace "report").frames
        foreach ($name in $a, $b) { Cmd $name collide-trace "off" | Out-Null }
        $label = "cap$($variant.cap)_hold$($variant.hold)"
        Save "mutual_trace_A_$label" $traceA; Save "mutual_trace_B_$label" $traceB
        $rows += [pscustomobject]@{ variant = $label; cruise = $cruise; ann = (Lift $traceA $baseA.y); bob = (Lift $traceB $baseB.y); annMoved = [math]::Round((Distance $baseA (Cmd $a collide-clock).position), 2); bobAt = (Cmd $b collide-clock).position; contactsB = (Collider $b $idA).contacts; contactsA = (Collider $a $idB).contacts }
        Cmd $a collide-hold "off" | Out-Null
    }
    foreach ($name in $a, $b) { Cmd $name collide-set "cap 0" | Out-Null }
    Save "mutual" $rows
}

Phase "realhit" {
    Cmd $b collide-set "setting on" | Out-Null
    Spot $b | Out-Null
    Cmd $b drive-input "0.6 0 2" | Out-Null
    Wait-InputDone $b | Out-Null
    Stop-Car $b
    Start-Sleep -Seconds 2
    $bobBefore = Cmd $b collide-clock
    Save "real_place" (Spot $a)
    $ready = Wait-Collider $a $idB { param($c) $c.reason -eq "on" } 10
    Cmd $a collide-cruise "30 6" | Out-Null
    $cruise = Wait-Cruise $a
    Start-Sleep -Seconds 1
    $bobAfter = Cmd $b collide-clock
    Save "real_hit" ([pscustomobject]@{ ready = $ready; cruise = $cruise; collider = (Collider $a $idB); bobMoved = [math]::Round((Distance $bobBefore.position $bobAfter.position), 3); bobSeesAnn = (Collider $b $idA); maxFrameA = (MaxFrame $a); maxFrameB = (MaxFrame $b) })
}

Cmd $a track-return | Out-Null
Wait-InGarage $a 180
if ($Phases -contains "checkpoint" -or $Phases -contains "restart") {
Cmd $a track-go "0 RaceTrack" | Out-Null
Wait-Scene $a "RaceTrack"
}

Phase "checkpoint" {
    Save "probe_racetrack" (Cmd $a collide-probe)
    $g = Ghost $a "8 0"
    Cmd $a collide-set "forced on" | Out-Null
    Start-Sleep -Seconds 1
    $before = Cmd $a collide-race
    $moved = Cmd $a collide-ghost-checkpoint "$g"
    Start-Sleep -Seconds 2
    $after = Cmd $a collide-race
    $c = Collider $a $g
    Save "checkpoint" ([pscustomobject]@{ before = $before; moved = $moved; after = $after; collider = $c })
    Cmd $a collide-set "forced auto" | Out-Null
    Cmd $a collide-ghost-clear | Out-Null
}

Phase "restart" {
    Stop-Car $a
    $g = Ghost $a "0 0"
    Cmd $a drive-input "0.6 0 3" | Out-Null
    Wait-InputDone $a | Out-Null
    $on = Wait-Collider $a $g { param($c) $c.reason -eq "on" } 10
    Stop-Car $a
    $before = Cmd $a collide-clock
    Cmd $a race-restart | Out-Null
    $samples = @()
    for ($i = 0; $i -lt 12; $i++) { Start-Sleep -Milliseconds 250; $samples += [pscustomobject]@{ clock = (Cmd $a collide-clock); collider = (Collider $a $g) } }
    Save "restart" ([pscustomobject]@{ on = $on; before = $before; samples = $samples; maxFrameMs = (MaxFrame $a) })
    $start = (Cmd $a collide-clock).position
    Cmd $a drive-input "0.6 0 3" | Out-Null
    $inp = Wait-InputDone $a
    $end = Cmd $a collide-clock
    Save "restart_drive" ([pscustomobject]@{ mode = $inp.mode; moved = [math]::Round((Distance $start $end.position), 2); collider = (Collider $a $g) })
}

try { Cmd $a track-return | Out-Null } catch { }
Cmd $b track-return | Out-Null
Wait-InGarage $a 180; Wait-InGarage $b 180
$Ctx.Result.passed = $true

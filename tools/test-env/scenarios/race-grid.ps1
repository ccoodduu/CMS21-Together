# areas: driving
# needs: graphics
# run-all: skip
# race-grid spike: Ann drives to the race track; the game's start spot and checkpoints are saved, and a camera of the
# harness renders the start area from above and from driver height into PNGs (grid_*.png in the run folder). -Find also
# lists scene objects whose name, mesh or material suggests a start grid or that lie within -Near metres of the start
# spot; -TrySpots moves the start spot onto grid boxes and checks where the game's restart puts the car. Run with
# CMS21_TEST_BACKGROUND=1: the game window is kept minimized and gives the foreground back about 10 s after launch.
param($Ctx, [switch]$Find, [string]$TrySpots = "", [switch]$SpotsOnly, [double]$Near = 40, [string]$ShotDir = "", [string]$ExtraShots = "")

$a = $Ctx.Instances[0]
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "grid_$Label.json") -Encoding utf8 }
function Note([string]$Message) { $script:Ctx.Result.notes += $Message; Write-Host "note: $Message" -ForegroundColor Yellow }
function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}
function Shot([string]$Label, [string]$Spec) {
    $file = Join-Path $Ctx.RunDir "grid_$Label.png"
    Cmd $a grid-shot "$file $Spec" | Out-Null
    $deadline = (Get-Date).AddSeconds(30)
    do { Start-Sleep -Milliseconds 500; $s = Cmd $a grid-shot-state } while (-not $s.lastShot -and -not $s.lastShotError -and (Get-Date) -lt $deadline)
    Check ((Test-Path -LiteralPath $file) -and -not $s.lastShotError) "shot $Label $($s.lastShotError)"
    if ($ShotDir -and (Test-Path -LiteralPath $file)) { Copy-Item -LiteralPath $file -Destination $ShotDir -Force }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
if ($ShotDir) { New-Item -ItemType Directory -Force -Path $ShotDir | Out-Null }
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Cmd $a guard-allow "Mode:CarDrive" | Out-Null
Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0
Cmd $a track-go "0 RaceTrack" | Out-Null
Wait-HarnessStatus -Instance $a -TimeoutSec 180 -What "race track" -Condition { param($s) $s.scene -match "(?i)race" -and $s.playable } | Out-Null
Start-Sleep -Seconds 10

$start = Cmd $a grid-start
Save "start" $start
Check ($null -ne $start.startPosition) "the race track has a start spot"
Note "start spot $($start.startPosition.path) at $($start.startPosition.position | ConvertTo-Json -Compress), euler $($start.startPosition.euler | ConvertTo-Json -Compress); car spawn $($start.carSpawnPosition.relative | ConvertTo-Json -Compress); $(@($start.checkpoints).Count) checkpoints"

# Each scene scan blocks the game for about 10 s (53,000 objects), long enough for the server to drop the connection
# and send the game to the menu, so the scans run only with -Find and come after the shots.
function Find-Objects {
    $byName = Cmd $a grid-find "max=600"
    Save "find_names" $byName
    Note "name/mesh/material matches: $($byName.matched) of $($byName.scanned) objects"
    $nearby = Cmd $a grid-find "near=$Near max=600"
    Save "find_near" $nearby
    Note "objects within $Near m of the start spot: $($nearby.matched)"
    $m = try { Cmd $a grid-mesh "!RaceMap/Lines -12 30 -90 30" } catch { @{ error = $_.Exception.Message.Split("`n")[0] } }
    Save "mesh_lines" $m
    Note "mesh !RaceMap/Lines near the start: readable $($m.readable), $(@($m.triangles).Count) triangles $($m.error)"
}

# label: width height frame right up forward pitch yaw [fov|ortho=halfHeight]
$shots = [ordered]@{
    "top-start-15m"     = "1600 1600 start 0 60 0 90 0 ortho=15"
    "top-start-30m"     = "1600 1600 start 0 80 0 90 0 ortho=30"
    "top-ahead-30m"     = "1600 1600 start 0 80 25 90 0 ortho=30"
    "top-behind-30m"    = "1600 1600 start 0 80 -25 90 0 ortho=30"
    "top-wide-80m"      = "1600 1600 start 0 150 0 90 0 ortho=80"
    "driver-forward"    = "1600 900 start 0 1.2 3 5 0 70"
    "driver-back"       = "1600 900 start 0 1.2 -3 5 180 70"
    "chase-behind"      = "1600 900 start 0 5 -14 18 0 70"
    "chase-ahead-back"  = "1600 900 start 0 6 20 18 180 70"
    "oblique-left"      = "1600 900 start -18 20 -10 40 35 60"
    "oblique-right"     = "1600 900 start 18 20 -10 40 -35 60"
}
foreach ($pair in ($ExtraShots -split ';' | Where-Object { $_ -match '=' })) {
    $label, $spec = $pair -split '=', 2
    $shots[$label.Trim()] = $spec.Trim()
}
if ($SpotsOnly) { $shots.Clear() }
foreach ($label in $shots.Keys) { Shot $label $shots[$label] }

Cmd $a race-spike-restart | Out-Null
Start-Sleep -Seconds 8
$after = Cmd $a grid-start
Save "start_after_restart" $after
Note "after the game's restart: start source $($after.startSource), car at $($after.car.relative | ConvertTo-Json -Compress) from the start spot"

if ($Find) { Find-Objects }

# -TrySpots 1,3,19: moves the game's CarSpawnPosition onto grid box N (0 = the game's own spot; two boxes per row, 6.15 m
# apart, rows 10 m apart), runs the game's restart and records where the car ends up.
foreach ($slot in @($TrySpots -split ',' | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ })) {
    $right = ($slot % 2) * 6.15
    $forward = -10 * [math]::Floor($slot / 2)
    $moved = Cmd $a grid-spawn-move "$right $forward"
    Cmd $a race-spike-restart | Out-Null
    Start-Sleep -Seconds 8
    Cmd $a grid-spawn-move "reset" | Out-Null
    $placed = Cmd $a grid-start
    Save "spot_$slot" @{ moved = $moved; start = $placed }
    $car = $placed.car.relative
    Note "spot ${slot}: spawn moved to right $right forward $forward (ground $($moved.ground), original $($moved.originalGround)); car after the restart at $($car | ConvertTo-Json -Compress)"
    Check ([math]::Abs($car.right - $right) -lt 0.5 -and [math]::Abs($car.forward - $forward) -lt 0.5) "spot ${slot}: the restart puts the car on the box"
    Shot "spot-$slot" "1200 1200 start $right 60 $forward 90 0 ortho=6"
}

$bg = try { Cmd $a background-state } catch { $null }
if ($bg) { Note "background window: minimized $($bg.minimized)x, foreground handed back $($bg.handedBack)x, game is foreground now: $($bg.foregroundIsOwn)" }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

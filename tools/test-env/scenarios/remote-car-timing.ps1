# areas: driving, testdrive
# faster-remote-cars (row 31): A drives on the test track and B arrives. B shows A's car within 1.5 s of B's track
# scene being ready, A (already driving) shows B's car within 1 s of B's drive start reaching A, and no frame of either
# game takes over 0.2 s while the copy loads (the build's own longest frame and the harness frame log up to 1 s after
# the car shows); a frame over the 0.13 s target is a WARN note: the first copy's one-frame LoadCar measures 85-130 ms
# here with lane 1 busy, before and after this change. Both own cars still drive. Then the same with B driving first
# and A arriving. Both clients run at a 60 fps cap, so frame times show the work instead of the headless 15 fps cap.
# Old code: the 3 s settle shows the car 3.2-3.4 s after arrival (20261010-120734_L2).
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Inv($Value) { [string]::Format([cultureinfo]::InvariantCulture, "{0}", $Value) }

function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Track([string]$Name) {
    try { Wait-HarnessStatus -Instance $Name -TimeoutSec 150 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null; $true } catch { $false }
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

function Wait-Shown([string]$Observer, [int]$DriverId, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $car = Cmd $Observer remote-build "state $DriverId"
        if ($car.exists -and ($car.visible -or $car.mode -eq "failed")) { return $car }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $car
}

function Wait-InputDone([string]$Name, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do { Start-Sleep -Milliseconds 500; $s = Cmd $Name drive-input-state } while ($s.running -and (Get-Date) -lt $deadline)
}

function Distance($p, $q) { [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }

function Frames-Over([string]$Observer, $Car, [double]$LimitMs) {
    $from = $Car.shownAt - $Car.shownAfter + $Car.waitSeconds - 0.05
    $log = Cmd $Observer frame-log "$(Inv $from) 30"
    $until = $Car.shownAt + 1.0
    $over = @($log.frames | Where-Object { $_[0] -le $until -and $_[1] -gt $LimitMs })
    $max = (@($log.frames | Where-Object { $_[0] -le $until }) | ForEach-Object { $_[1] } | Measure-Object -Maximum).Maximum
    [pscustomobject]@{ Over = $over.Count; Max = $max }
}

function Round-Trip([string]$Label, [string]$First, [int]$FirstLoader, [int]$FirstId, [string]$Second, [int]$SecondLoader, [int]$SecondId) {
    Cmd $First testdrive-go "$FirstLoader" | Out-Null
    Check (Wait-Track $First) "${Label}: $First reached the test track"
    Start-Sleep -Seconds 3
    Cmd $First drive-input "0.3 0.15 400" | Out-Null
    Start-Sleep -Seconds 2

    Cmd $Second testdrive-go "$SecondLoader" | Out-Null
    Check (Wait-Track $Second) "${Label}: $Second reached the test track"
    $arrived = Wait-Shown $Second $FirstId
    $present = Wait-Shown $First $SecondId
    Start-Sleep -Milliseconds 1500

    $sinceReady = if ($arrived.exists -and $arrived.readyAt -gt 0 -and $arrived.shownAt -gt 0) { [math]::Round($arrived.shownAt - $arrived.readyAt, 3) } else { 999 }
    Check ($arrived.visible -and $arrived.mode -match "^ghost") "${Label}: $Second shows ${First}'s car (mode $($arrived.mode))"
    Check ($sinceReady -le 1.5) "${Label}: $Second shows ${First}'s car $sinceReady s after its track scene was ready (limit 1.5 s; waited $([math]::Round($arrived.waitSeconds, 2)) s, build $([math]::Round($arrived.buildSeconds, 2)) s)"
    Check ($present.visible -and $present.shownAfter -le 1.0) "${Label}: $First (already driving) shows ${Second}'s car $([math]::Round($present.shownAfter, 3)) s after the drive start arrived (limit 1 s; waited $([math]::Round($present.waitSeconds, 2)) s)"
    foreach ($pair in @(@($Second, $arrived), @($First, $present))) {
        $observer, $car = $pair
        $frames = Frames-Over $observer $car 200
        Check ($car.longestFrame -le 0.2 -and $frames.Over -eq 0) "${Label}: no frame over 0.2 s on $observer while the copy loaded (build longest $([math]::Round($car.longestFrame, 3)) s, frame log max $($frames.Max) ms up to 1 s after shown)"
        if ($car.longestFrame -gt 0.13 -or $frames.Max -gt 130) { $Ctx.Result.notes += "WARN ${Label} $observer`: a frame over the 0.13 s target while the copy loaded (the first copy's one-frame LoadCar, unchanged by this change; design D2 would split it)" }
        $Ctx.Result.notes += "${Label} $observer`: wait $([math]::Round($car.waitSeconds, 3)) s, build $([math]::Round($car.buildSeconds, 3)) s, shown $([math]::Round($car.shownAfter, 3)) s after the start, longest frame $([math]::Round($car.longestFrame, 3)) s, frame log max $($frames.Max) ms"
    }

    Cmd $First drive-stop | Out-Null
    Wait-InputDone $First
    $before = (Cmd $Second drive-probe).capturePosition
    Cmd $Second drive-input "0.6 0 3" | Out-Null
    Wait-InputDone $Second
    $after = (Cmd $Second drive-probe).capturePosition
    Check ((Distance $before $after) -gt 2) "${Label}: $Second's own car drives with ${First}'s car shown ($([math]::Round((Distance $before $after), 1)) m)"
    Cmd $Second drive-stop | Out-Null

    foreach ($n in $First, $Second) { Cmd $n testdrive-finish "all" | Out-Null }
    foreach ($n in $First, $Second) { Wait-InGarage $n 180 }
    Start-Sleep -Seconds 3
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$idA = (Get-HarnessStatus -Instance $a).playerId
$idB = (Get-HarnessStatus -Instance $b).playerId
$fpsBefore = @{}
foreach ($name in $a, $b) {
    Cmd $name guard-allow "Mode:CarDrive" | Out-Null
    $fpsBefore[$name] = (Cmd $name perf).targetFrameRate
    Cmd $name fps-cap "60" | Out-Null
}

Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0; Wait-Ready $b 0
Cmd $b car-spawn "1 car_boltatlanta 0" | Out-Null
Wait-Ready $a 1; Wait-Ready $b 1
Start-Sleep -Seconds 3

Round-Trip "A first" $a 0 $idA $b 1 $idB
Round-Trip "B first" $b 1 $idB $a 0 $idA

foreach ($name in $a, $b) { if ($fpsBefore[$name] -gt 0) { Cmd $name fps-cap "$($fpsBefore[$name])" | Out-Null } }
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

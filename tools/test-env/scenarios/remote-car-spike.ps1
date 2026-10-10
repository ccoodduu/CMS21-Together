# areas: driving
# run-all: skip
# Probe for faster-remote-cars, no checks. Round "a-first": A drives on the test track, B arrives (B builds A's car on
# arrival, A builds B's car while already there). Round "b-first": the same with the roles swapped. Then B builds
# -Ghosts extra copies alone (full car data and base model, alternating). Each observer's build trace (remote-build),
# frame log (real clock), the remoteCars dump, the copies' counts and the optional method-time (-MethodTargets, named
# methods only: all of CarLoader hangs the game) go to spike_<round>_<instance>.json. -Wait game|none|settle:<s>
# replaces the 3 s settle on both clients; -Stage $true turns on the remote-stage prototype; -FpsCap 60 by default.
param($Ctx, [string]$Wait = "game", [int]$Ghosts = 6, [int]$FpsCap = 60, [bool]$Stage = $false, [string]$MethodTargets = "", [int]$MethodMinMs = 8)

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments -TimeoutSec 60 }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 -Compress | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "spike_$Label.json") -Encoding utf8 }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Track([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 150 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null
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

function RemoteCar([string]$Observer, [int]$DriverId) {
    @((Cmd $Observer dump).remoteCars.cars) | Where-Object { $_.playerId -eq $DriverId } | Select-Object -First 1
}

function Wait-Shown([string]$Observer, [int]$DriverId, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $car = Cmd $Observer remote-build "state $DriverId"; if (-not $car.exists) { $car = $null }
        if ($car -and ($car.visible -or $car.mode -eq "failed")) { return $car }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $car
}

function Mark { $m = @{}; foreach ($n in $a, $b) { $m[$n] = (Cmd $n remote-build clock) }; $m }

function Collect([string]$Round, $Marks) {
    foreach ($n in $a, $b) {
        $since = [string]::Format([cultureinfo]::InvariantCulture, "{0}", $Marks[$n].realtime)
        $data = [ordered]@{
            round = $Round; instance = $n; wait = $Wait; mark = $Marks[$n]
            trace = (Cmd $n remote-build "trace report")
            frames = (Cmd $n frame-log "$since 2")
            methods = (Cmd $n method-time "report")
            remoteCars = (Cmd $n dump).remoteCars
            stage = (Cmd $n remote-stage "report")
            counts = @(@((Cmd $n dump).remoteCars.cars) | Where-Object { $_.ready } | ForEach-Object { try { [ordered]@{ playerId = $_.playerId; counts = (Cmd $n remote-build "count $($_.playerId)"); park = (Cmd $n remote-build "park $($_.playerId)") } } catch { $null } })
        }
        Save "${Round}_$n" $data
        $frames = @($data.frames.frames)
        $max = ($frames | ForEach-Object { $_[1] } | Measure-Object -Maximum).Maximum
        $steps = @($data.trace.events | Where-Object { $_.what -eq "step" })
        $longStep = ($steps | ForEach-Object { $_.ms } | Measure-Object -Maximum).Maximum
        $cars = @($data.remoteCars.cars | ForEach-Object { "player $($_.playerId) shown $($_.shownAfter) s build $($_.buildSeconds) s longest $($_.longestFrame) s" }) -join "; "
        $Ctx.Result.notes += "${Round} $n`: $cars; $($steps.Count) steps, longest step $longStep ms; max frame since mark $max ms over $($data.frames.framesInWindow) frames (oldest kept $($data.frames.oldestKept), mark $($Marks[$n].realtime))"
        Cmd $n remote-build "trace on" | Out-Null
        if ($MethodTargets) { Cmd $n method-time "on $MethodTargets min=$MethodMinMs" | Out-Null }
    }
}

function Round([string]$Label, [string]$First, [int]$FirstLoader, [int]$FirstId, [string]$Second, [int]$SecondLoader, [int]$SecondId) {
    Cmd $First testdrive-go "$FirstLoader" | Out-Null
    Wait-Track $First
    Start-Sleep -Seconds 3
    Cmd $First drive-input "0.3 0.15 400" | Out-Null
    Start-Sleep -Seconds 2
    foreach ($n in $a, $b) { Cmd $n remote-build "trace on" | Out-Null; if ($MethodTargets) { Cmd $n method-time "on $MethodTargets min=$MethodMinMs" | Out-Null } }
    $marks = Mark
    $go = Get-Date
    Cmd $Second testdrive-go "$SecondLoader" | Out-Null
    Wait-Track $Second
    $arrived = Get-Date
    $seen = Wait-Shown $Second $FirstId
    $seenAt = Get-Date
    $other = Wait-Shown $First $SecondId
    $otherAt = Get-Date
    $Ctx.Result.notes += "${Label}: $Second arrived $([math]::Round(($arrived - $go).TotalSeconds, 2)) s after travel; saw ${First}'s car $([math]::Round(($seenAt - $arrived).TotalSeconds, 2)) s after the harness saw the track (poll 0.25 s); $First saw ${Second}'s car $([math]::Round(($otherAt - $arrived).TotalSeconds, 2)) s after that"
    Start-Sleep -Seconds 2
    Collect $Label $marks
}

function Return-All {
    foreach ($n in $a, $b) { try { Cmd $n drive-stop | Out-Null } catch { } }
    Start-Sleep -Seconds 1
    foreach ($n in $a, $b) { Cmd $n testdrive-finish "all" | Out-Null }
    foreach ($n in $a, $b) { Wait-InGarage $n }
    Start-Sleep -Seconds 3
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$idA = (Get-HarnessStatus -Instance $a).playerId
$idB = (Get-HarnessStatus -Instance $b).playerId
foreach ($name in $a, $b) { Cmd $name guard-allow "Mode:CarDrive" | Out-Null }
$waitArgs = if ($Wait -match "^settle:(.+)$") { "wait settle $($Matches[1])" } else { "wait $Wait" }
foreach ($name in $a, $b) {
    $Ctx.Result.notes += "$name $((Cmd $name remote-build $waitArgs) | ConvertTo-Json -Compress)"
    if ($Stage) { $Ctx.Result.notes += "$name remote-stage $((Cmd $name remote-stage "on").enabled)" }
    if ($FpsCap -ne 0) { $Ctx.Result.notes += "$name fps-cap $((Cmd $name fps-cap "$FpsCap") | ConvertTo-Json -Compress)" }
}

Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0; Wait-Ready $b 0
Cmd $b car-spawn "1 car_boltatlanta 0" | Out-Null
Wait-Ready $a 1; Wait-Ready $b 1
Start-Sleep -Seconds 3

Round "a-first" $a 0 $idA $b 1 $idB
Return-All
Round "b-first" $b 1 $idB $a 0 $idA

if ($Ghosts -gt 0) {
    Start-Sleep -Seconds 2
    $marks = Mark
    foreach ($n in $a, $b) { Cmd $n remote-build "trace on" | Out-Null; if ($MethodTargets) { Cmd $n method-time "on $MethodTargets min=$MethodMinMs" | Out-Null } }
    $results = @()
    for ($i = 0; $i -lt $Ghosts; $i++) {
        $route = if ($i % 2 -eq 0) { "clone" } else { "base" }
        $g = Cmd $b remote-build "ghost $idA $route"
        $car = Wait-Shown $b $g.playerId 60
        $results += [ordered]@{ route = $route; playerId = $g.playerId; mode = $car.mode; buildSeconds = $car.buildSeconds; longestFrame = $car.longestFrame; shownAfter = $car.shownAfter; counts = (Cmd $b remote-build "count $($g.playerId)") }
        Start-Sleep -Seconds 2
    }
    $Ctx.Result.notes += @($results | ForEach-Object { "ghost $($_.route): $($_.mode) build $($_.buildSeconds) s longest $($_.longestFrame) s, $($_.counts.renderers) renderers, $($_.counts.transforms) transforms, $($_.counts.colliders) colliders" })
    $removed = @($results | ForEach-Object { Cmd $b remote-build "remove $($_.playerId)"; Start-Sleep -Seconds 1 })
    Save "ghosts_summary" ([ordered]@{ ghosts = $results; removed = $removed })
    Start-Sleep -Seconds 1
    Collect "ghosts" $marks
}
foreach ($n in $a, $b) { Cmd $n method-time "off" | Out-Null; Cmd $n remote-build "trace off" | Out-Null }
$Ctx.Result.passed = $true

# areas: visuals, parts, placement
# Playtest 2026-10-07: B worked on a brake caliper while A moved the lift; A then saw the caliper floating where it
# had been and the mounted caliper stayed invisible. Here B unscrews and mounts a part of a car on lift 1 while A
# raises and lowers the lift, also while A's ghost is held at its midpoint. Afterwards A has no ghost or hidden
# renderer left, the part's renderers draw, and the part sits at the same place relative to the car as on B.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Visuals([string]$Name) { (Cmd $Name dump).visuals }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Wait-Same([string]$What, [int]$TimeoutSec = 30) {
    try {
        Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("stats", "inventory", "cars") -TimeoutSec $TimeoutSec | Out-Null
        Check $true "$What`: A and B have equal stats, inventory and cars"
    } catch {
        Check $false "$What`: $($_.Exception.Message)"
    }
}

function Wait-Lift([string]$State) {
    foreach ($name in $Ctx.Instances) {
        $deadline = (Get-Date).AddSeconds(30)
        do {
            $lift = @(Cmd $name lifters)[0]
            if ($lift.state -eq $State) { break }
            Start-Sleep -Milliseconds 300
        } while ((Get-Date) -lt $deadline)
        Check ($lift.state -eq $State) "$name sees lift 0 $State (is $($lift.state))"
    }
}

function Wait-Quiet([string]$What) {
    try {
        Wait-HarnessDump -Instance $a -TimeoutSec 10 -What $What -Condition {
            param($d) @($d.visuals.ghostsActive).Count -eq 0 -and @($d.visuals.boltsActive).Count -eq 0 -and $d.visuals.renderersHidden -eq 0
        } | Out-Null
        Check $true "A: $What"
    } catch {
        Check $false "A: $What (visuals: $((Visuals $a) | ConvertTo-Json -Compress -Depth 6))"
    }
}

function Check-Part([string]$What, [bool]$Unmounted) {
    $pa = Cmd $a vfx-probe "$loader $key"
    $pb = Cmd $b vfx-probe "$loader $key"
    Check ($pa.unmounted -eq $Unmounted -and $pb.unmounted -eq $Unmounted) "$What`: the part is $(if ($Unmounted) { 'off' } else { 'on' }) on A and B"
    $hidden = @($pa.renderers | Where-Object { $_.forceOff })
    Check ($hidden.Count -eq 0) "$What`: none of A's part renderers is forced off ($(($hidden | ForEach-Object { $_.path }) -join ', '))"
    if (-not $Unmounted) {
        $drawn = @($pa.renderers | Where-Object { $_.enabled -and $_.active -and -not $_.forceOff })
        Check ($drawn.Count -gt 0) "$What`: A draws the mounted part ($($drawn.Count) of $(@($pa.renderers).Count) renderers)"
    }
    $delta = [math]::Sqrt(((0..2) | ForEach-Object { [math]::Pow([double]$pa.offset[$_] - [double]$pb.offset[$_], 2) } | Measure-Object -Sum).Sum)
    Check ($delta -lt 0.02) "$What`: the part sits at the same place on the car on A and B (A $($pa.offset -join ','), B $($pb.offset -join ','))"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null; Cmd $name vfx-trace "on" | Out-Null }

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Cmd $a car-move "$loader CarLifter1" | Out-Null
Start-Sleep -Seconds 5
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Wait-Same "car on lift 1"
Cmd $a vfx-stand "$loader 1.5" | Out-Null
Cmd $b vfx-stand "$loader 1.5" | Out-Null

$candidates = @(Cmd $b vfx-parts "$loader")
$part = @($candidates | Where-Object { $_.id -match "zacisk|caliper" } | Select-Object -First 1)[0]
if (-not $part) { $part = $candidates[0] }
$key = $part.key
Write-Host "part: $key ($($part.id), $($part.bolts) bolts)"

# B unscrews; A raises the lift while B's bolts are half out.
Cmd $b vfx-unscrew "$loader $key pause 0.5" | Out-Null
Start-Sleep -Seconds 8
Cmd $a lift "0 up" | Out-Null
Wait-Lift "Middle"
Cmd $b vfx-unscrew "$loader $key resume" | Out-Null
Start-Sleep -Seconds 8
Wait-Quiet "after the unmount on a moved lift"
Wait-Same "unmount on a moved lift"
Check-Part "unmount on a moved lift" $true

# B mounts; A lowers the lift right away, while A's On ghost flies.
Cmd $b part-fast-mount "$loader $key" | Out-Null
Cmd $a lift "0 down" | Out-Null
Wait-Lift "OnFloor"
Wait-Quiet "after the mount while the lift moved"
Wait-Same "mount while the lift moved"
Check-Part "mount while the lift moved" $false

# A's ghost is held at its midpoint while the lift moves, then released.
Cmd $a vfx-hold "on" | Out-Null
Cmd $b part-fast-unmount "$loader $key" | Out-Null
Start-Sleep -Seconds 2
Cmd $b part-fast-mount "$loader $key" | Out-Null
Start-Sleep -Seconds 2
Cmd $a lift "0 up" | Out-Null
Wait-Lift "Middle"
Cmd $a vfx-hold "off" | Out-Null
Wait-Quiet "after a held ghost and a lift move"
Wait-Same "held ghost and lift move"
Check-Part "held ghost and lift move" $false

foreach ($name in $Ctx.Instances) {
    $v = Visuals $name
    Check ($v.leaks -eq 0) "$name has no visual state leak ($($v.leaks))"
    (Cmd $name vfx-trace "report") | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "vfx-trace_$name.json") -Encoding utf8
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

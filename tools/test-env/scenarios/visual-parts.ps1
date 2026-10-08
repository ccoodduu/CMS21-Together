# areas: visuals, parts
# remote-visual-feedback groups 4 and 5: A unscrews and mounts parts of a car both see and opens and closes the hood;
# B plays ghosts (Off, On, Swing) and bolt ghosts paced by A's progress, while B's car state equals A's the whole time
# (also while B holds a ghost at its midpoint). A resync and B's visuals switched off play nothing. No visual leaks
# state on either client.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Count($Table, [string]$Name) { if ($null -eq $Table -or $null -eq $Table.$Name) { 0 } else { [int]$Table.$Name } }
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

function Wait-Visuals([string]$Name, [string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 10) {
    try {
        $dump = Wait-HarnessDump -Instance $Name -TimeoutSec $TimeoutSec -What $What -Condition { param($d) & $Condition $d.visuals }
        Check $true "$Name`: $What"
        return $dump.visuals
    } catch {
        Check $false "$Name`: $What (visuals: $((Visuals $Name) | ConvertTo-Json -Compress -Depth 6))"
        return $null
    }
}

function Wait-Unscrew([string]$State, [int]$TimeoutSec = 90) {
    try {
        Wait-HarnessDump -Instance $a -TimeoutSec $TimeoutSec -What "A's vfx-unscrew $State" -Condition { param($d) $d.visuals.unscrew.state -eq $State } | Out-Null
        Check $true "A's vfx-unscrew reaches '$State'"
    } catch {
        Check $false "A's vfx-unscrew reaches '$State' (now: $((Visuals $a).unscrew | ConvertTo-Json -Compress))"
    }
}

function Wait-Quiet([string]$What) {
    Wait-Visuals $b "$What`: no ghost or bolt left and every renderer restored" { param($v) @($v.ghostsActive).Count -eq 0 -and @($v.boltsActive).Count -eq 0 -and $v.renderersHidden -eq 0 } 5 | Out-Null
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null; Cmd $name vfx-trace "on" | Out-Null }
$idA = (Get-HarnessStatus $a).playerId

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Wait-Same "spawn"
Cmd $a vfx-stand "$loader 1.5" | Out-Null
Cmd $b vfx-stand "$loader 3" | Out-Null

$candidates = @(Cmd $a vfx-parts "$loader")
Check ($candidates.Count -gt 0) "the car has unscrewable parts with bolts ($($candidates.Count))"
$part = @($candidates | Where-Object { $_.id -match "wheel|rim|tire|caliper|disc" } | Select-Object -First 1)[0]
if (-not $part) { $part = $candidates[0] }
$key = $part.key
$others = @($candidates | Where-Object { $_.key -ne $part.key })
Write-Host "part: $key ($($part.id), $($part.bolts) bolts)"

# 4.3 Off: A unscrews the part the way a player does; B shows bolts, then the Off ghost, then nothing.
$before = Visuals $b
Cmd $a vfx-unscrew "$loader $key" | Out-Null
Wait-Visuals $b "bolt ghosts for A's claim on $key" { param($v) @($v.boltsActive | Where-Object { $_.key -eq $key -and $_.owner -eq $idA }).Count -eq 1 } 20 | Out-Null
Wait-Unscrew "finished"
Wait-Visuals $b "an Off ghost started" { param($v) (Count $v.ghostsStarted "Off") -eq (Count $before.ghostsStarted "Off") + 1 } | Out-Null
Wait-Quiet "after the Off ghost"
Wait-Same "unmount"

# 4.3 On: A mounts the part back; B flies a ghost in and restores the real renderers.
$before = Visuals $b
Cmd $a part-fast-mount "$loader $key" | Out-Null
Wait-Visuals $b "an On ghost started" { param($v) (Count $v.ghostsStarted "On") -eq (Count $before.ghostsStarted "On") + 1 } | Out-Null
Wait-Quiet "after the On ghost"
Wait-Same "mount"

# 4.3 hold: the state is applied while B's ghost is frozen at its midpoint.
Cmd $b vfx-hold "on" | Out-Null
$before = Visuals $b
Cmd $a part-fast-unmount "$loader $key" | Out-Null
$held = Wait-Visuals $b "an Off ghost is held" { param($v) @($v.ghostsActive | Where-Object { $_.key -eq $key -and $_.kind -eq "Off" }).Count -eq 1 }
if ($held) { $Ctx.Result.notes += "Off ghost fade: $(@($held.ghostsActive)[0].fade)" }
Wait-Same "while the ghost is held" 15
Check (@((Visuals $b).ghostsActive | Where-Object { $_.key -eq $key }).Count -eq 1) "the ghost is still held after the state matched"
Cmd $b vfx-hold "off" | Out-Null
Wait-Quiet "after the hold"
Cmd $a part-fast-mount "$loader $key" | Out-Null
Wait-Quiet "after the mount"
Wait-Same "mount after hold"

# 4.3 doors, hood, trunk: two swings for open and close.
$before = Visuals $b
Cmd $a vfx-switch "$loader hood" | Out-Null
Start-Sleep -Seconds 2
Cmd $a vfx-switch "$loader hood" | Out-Null
Wait-Visuals $b "two Swing ghosts for the hood" { param($v) (Count $v.ghostsStarted "Swing") -eq (Count $before.ghostsStarted "Swing") + 2 } 15 | Out-Null
Wait-Quiet "after the swings"
Wait-Same "hood"

# 4.3 resync: snapshots never animate.
$before = Visuals $b
Cmd $b resync | Out-Null
Start-Sleep -Seconds 2
Wait-Ready $b | Out-Null
Wait-Same "resync"
$after = Visuals $b
# The resync resets the session state, counters included, so no kind may count more than before.
$grown = @($after.ghostsStarted.PSObject.Properties | Where-Object { $_.Value -gt (Count $before.ghostsStarted $_.Name) } | ForEach-Object { $_.Name })
Check ($grown.Count -eq 0) "B's resync started no ghost (before $($before.ghostsStarted | ConvertTo-Json -Compress), after $($after.ghostsStarted | ConvertTo-Json -Compress))"

# 4.3 switch off: the change applies without a visual.
Cmd $b vfx-enable "off" | Out-Null
$before = Visuals $b
Cmd $a part-fast-unmount "$loader $key" | Out-Null
Wait-Visuals $b "the Off ghost is skipped as disabled" { param($v) (Count $v.ghostsSkipped "disabled") -gt (Count $before.ghostsSkipped "disabled") } | Out-Null
Wait-Same "visuals off"
Check (@((Visuals $b).ghostsActive).Count -eq 0) "B shows no ghost with visuals off"
Cmd $b vfx-enable "on" | Out-Null
Cmd $a part-fast-mount "$loader $key" | Out-Null
Wait-Quiet "after visuals back on"
Wait-Same "mount after visuals off"

# 5.2 bolts, on parts not touched yet (a fast mount leaves the bolts unscrewed on the actor): A stops halfway; B shows
# about half the bolts out and the real bolts hidden.
$key = $others[0].key
Write-Host "bolt part: $key ($($others[0].id))"
Cmd $a vfx-unscrew "$loader $key pause 0.5" | Out-Null
Wait-Unscrew "paused"
$bolts = Wait-Visuals $b "about half of the bolts out" { param($v)
    $entry = @($v.boltsActive | Where-Object { $_.key -eq $key })[0]
    $entry -and $entry.owner -eq $idA -and [math]::Abs($entry.done - [math]::Floor($entry.total / 2)) -le 1 -and $v.renderersHidden -gt 0
} 15
if ($bolts) { Write-Host "bolts: $(@($bolts.boltsActive)[0] | ConvertTo-Json -Compress)" }
$before = Visuals $b
Cmd $a vfx-unscrew "$loader $key resume" | Out-Null
Wait-Unscrew "finished"
Wait-Visuals $b "the Off ghost after the bolts" { param($v) (Count $v.ghostsStarted "Off") -eq (Count $before.ghostsStarted "Off") + 1 } | Out-Null
Wait-Quiet "after resume"
Wait-Same "unmount after resume"

# 5.2 undo: the bolts run back and the part stays mounted for both.
$key = $others[1].key
Write-Host "undo part: $key ($($others[1].id))"
Cmd $a vfx-unscrew "$loader $key pause 0.5" | Out-Null
Wait-Unscrew "paused"
Wait-Visuals $b "bolt ghosts before the undo" { param($v) @($v.boltsActive | Where-Object { $_.key -eq $key }).Count -eq 1 } 15 | Out-Null
$before = Visuals $b
Cmd $a vfx-unscrew "$loader $key undo" | Out-Null
Wait-Visuals $b "the bolts ran back" { param($v) @($v.boltsActive).Count -eq 0 -and $v.renderersHidden -eq 0 } 5 | Out-Null
Check ((Count (Visuals $b).ghostsStarted "Off") -eq (Count $before.ghostsStarted "Off")) "the undo played no Off ghost"
Wait-Same "undo"
$partA = @((Cmd $a dump).cars | Where-Object { $_.index -eq $loader })[0].subParts | Where-Object { $_.key -eq $key }
Check (-not $partA.unmounted) "the part stays mounted after the undo"

foreach ($name in $Ctx.Instances) {
    $v = Visuals $name
    Check ($v.leaks -eq 0) "$name has no visual state leak ($($v.leaks))"
    $log = Get-Content -LiteralPath (Join-Path $env:USERPROFILE "CMS21-TestInstalls\$name\MelonLoader\Latest.log") -Raw
    Check (-not ($log -match "\[Visuals\] state leak")) "$name's log has no '[Visuals] state leak'"
    (Cmd $name vfx-trace "report") | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "vfx-trace_$name.json") -Encoding utf8
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

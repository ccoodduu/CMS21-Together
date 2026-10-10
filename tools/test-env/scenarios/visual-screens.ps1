# areas: visuals
# needs: graphics
# run-all: skip
# remote-visual-feedback 7.2: screenshots for a look by eye, with B's visuals held at their midpoint: an Off ghost
# half-way, bolts half out, A's avatar holding the OBD scanner, and A reaching under a lifted car. Also records the
# ghost's fade mode (dissolve or shrink) and the hood swing. Runs with the windows minimized when
# CMS21_TEST_BACKGROUND=1: the shots come from B's own camera aimed at the target (vfx-shot), not from the window.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Note([string]$Message) { $script:Ctx.Result.notes += $Message; Write-Host "note: $Message" -ForegroundColor Yellow }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Cmd $Name car-ready "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

# B renders its own camera aimed at the target (vfx-shot), so the shot does not depend on where B's player looks and
# works with the window minimized (CMS21_TEST_BACKGROUND=1).
function Shot([string]$Label, [string]$Target, [string]$Offset) {
    $file = Join-Path $Ctx.RunDir "shot_${Label}_$b.png"
    try {
        $r = Cmd $b vfx-shot "$file $loader $Target $Offset"
        Write-Host "shot ${Label}: target $($r.target -join ','), camera $($r.position -join ','), loader object $($r.loader -join ','), car root $($r.root -join ',')"
        $deadline = (Get-Date).AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 300
            $state = Cmd $b grid-shot-state
        } while (-not $state.lastShot -and -not $state.lastShotError -and (Get-Date) -lt $deadline)
        if ($state.lastShotError) { Write-Host "vfx-shot ${Label}: $($state.lastShotError)" }
    } catch { Write-Host "vfx-shot ${Label}: $($_.Exception.Message.Split("`n")[0])" }
    $bytes = if (Test-Path -LiteralPath $file) { (Get-Item -LiteralPath $file).Length } else { 0 }
    # A black or empty frame compresses to a few kB; a rendered garage is far larger.
    Check ($bytes -gt 50000) "screenshot $Label rendered ($bytes bytes)"
}

function Wait-LiftStill([string]$What) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $lifts = @($Ctx.Instances | ForEach-Object { @(Cmd $_ lifters)[0] })
    } while (@($lifts | Where-Object { $_.isMoving -or $_.state -ne $lifts[0].state }).Count -gt 0 -and (Get-Date) -lt $deadline)
    Note "lift 0 $What`: $(($lifts | ForEach-Object { $_.state }) -join ' / ')"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Cmd $a set-name "Ann" | Out-Null
Cmd $b set-name "Bob" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
# With a place, as a player's car always has one: without it the spawner keeps the car hidden under the floor
# (y -99.7, no place) while the others show it at Entrance1.
Cmd $a car-spawn "$loader $car 0 auto" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Cmd $a vfx-stand "$loader 1.5" | Out-Null
Cmd $b vfx-stand "$loader 3" | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 20 -What "Ann's avatar" -Condition {
    param($d) @($d.roster.PSObject.Properties | Where-Object { $_.Value.name -eq "Ann" -and $_.Value.avatarActive }).Count -eq 1
} | Out-Null
$candidates = @(Cmd $a vfx-parts "$loader")
$part = @($candidates | Where-Object { $_.id -match "wheel|rim|tire|caliper|disc" } | Select-Object -First 1)[0]
if (-not $part) { $part = $candidates[0] }
$key = $part.key
Note "part: $key ($($part.id), $($part.bolts) bolts)"
$aId = "$((Get-HarnessStatus $a).playerId)"

# The hood is opened first (without a held ghost), so a part in the engine bay can be seen from above.
Cmd $a vfx-switch "$loader hood" | Out-Null
Start-Sleep -Seconds 3

Cmd $b vfx-hold "on" | Out-Null
Cmd $a vfx-unscrew "$loader $key pause 0.5" | Out-Null
Wait-HarnessDump -Instance $a -TimeoutSec 90 -What "A paused" -Condition { param($d) $d.visuals.unscrew.state -eq "paused" } | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 10 -What "B shows the bolts" -Condition { param($d) @($d.visuals.boltsActive | Where-Object { $_.key -eq $key }).Count -ge 1 } | Out-Null
Start-Sleep -Seconds 1
Shot "bolts-half" "part:$key" "0.5 1.1 0.3"
Cmd $a vfx-unscrew "$loader $key resume" | Out-Null
try {
    $v = (Wait-HarnessDump -Instance $b -TimeoutSec 60 -What "Off ghost held" -Condition { param($d) @($d.visuals.ghostsActive | Where-Object { $_.kind -eq "Off" }).Count -ge 1 }).visuals
    Note "Off ghost fade: $(@($v.ghostsActive | Where-Object { $_.kind -eq 'Off' })[0].fade)"
    Shot "off-ghost" "part:$key" "0.9 1.5 0.6"
} catch { Check $false "B holds an Off ghost ($($_.Exception.Message))" }
Cmd $b vfx-hold "off" | Out-Null
Cmd $a part-fast-mount "$loader $key" | Out-Null
Start-Sleep -Seconds 2

# The hood closes with B's swing ghost held half-way.
Cmd $b vfx-hold "on" | Out-Null
Cmd $a vfx-switch "$loader hood" | Out-Null
Start-Sleep -Seconds 1
Shot "hood-swing" "body:hood" "1.8 1.2 2.2"
Cmd $b vfx-hold "off" | Out-Null
Start-Sleep -Seconds 2

Cmd $a vfx-tool "OBD $loader" | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 10 -What "B shows A's OBD scanner" -Condition { param($d) $d.visuals.players.$aId.propActive } | Out-Null
Start-Sleep -Seconds 1
Shot "obd-in-hand" "player:Ann" "-0.7 1.4 1.8 aim=1.1"
Cmd $a vfx-tool "none" | Out-Null

Cmd $a car-move "$loader CarLifter1" | Out-Null
Start-Sleep -Seconds 5
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Cmd $a lift "0 up" | Out-Null
Wait-LiftStill "after one up"
Cmd $a lift "0 up" | Out-Null
Wait-LiftStill "after two ups"
Cmd $a vfx-stand "$loader -1" | Out-Null
Cmd $b vfx-stand "$loader 3" | Out-Null
Cmd $a vfx-unscrew "$loader $key pause 0.5" | Out-Null
Wait-HarnessDump -Instance $a -TimeoutSec 90 -What "A paused under the car" -Condition { param($d) $d.visuals.unscrew.state -eq "paused" } | Out-Null
try {
    Wait-HarnessDump -Instance $b -TimeoutSec 10 -What "A's arms up" -Condition { param($d) $d.visuals.players.$aId.pose -eq "ArmsUp" } | Out-Null
    Check $true "A works with the arms up under the lifted car"
} catch { Check $false "A's pose under the lifted car is ArmsUp (is $((Cmd $b dump).visuals.players.$aId.pose))" }
Shot "arms-up" "player:Ann" "3.2 0.5 1.5 aim=1.5"
Cmd $a vfx-unscrew "$loader $key undo" | Out-Null

Note "screenshots for the user's look (rendered on $b, the receiver): shot_bolts-half, shot_off-ghost, shot_hood-swing, shot_obd-in-hand, shot_arms-up"
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

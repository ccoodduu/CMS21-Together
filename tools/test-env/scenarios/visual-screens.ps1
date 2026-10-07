# areas: visuals
# needs: graphics
# run-all: skip
# remote-visual-feedback 7.2: screenshots for a look by eye, with B's visuals held at their midpoint: an Off ghost
# half-way, bolts half out, A's avatar holding the OBD scanner, and A reaching under a lifted car. Also records the
# ghost's fade mode (dissolve or shrink) and the hood swing.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
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

function Shot([string]$Label) {
    Start-Sleep -Milliseconds 500
    Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label $Label
    Start-Sleep -Milliseconds 500
    $file = Join-Path $Ctx.RunDir "shot_${Label}_$b.png"
    Check (Test-Path -LiteralPath $file) "screenshot $Label"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Cmd $a vfx-stand "$loader 1.5" | Out-Null
Cmd $b vfx-stand "$loader 3" | Out-Null
Cmd $b stand-before "$((Cmd $a dump).local.name) 3" | Out-Null
$key = @(Cmd $a vfx-parts "$loader")[0].key

Cmd $b vfx-hold "on" | Out-Null
Cmd $a vfx-unscrew "$loader $key pause 0.5" | Out-Null
Wait-HarnessDump -Instance $a -TimeoutSec 90 -What "A paused" -Condition { param($d) $d.visuals.unscrew.state -eq "paused" } | Out-Null
Shot "bolts-half"
Cmd $a vfx-unscrew "$loader $key resume" | Out-Null
try {
    $v = (Wait-HarnessDump -Instance $b -TimeoutSec 60 -What "Off ghost held" -Condition { param($d) @($d.visuals.ghostsActive | Where-Object { $_.kind -eq "Off" }).Count -ge 1 }).visuals
    Note "Off ghost fade: $(@($v.ghostsActive | Where-Object { $_.kind -eq 'Off' })[0].fade)"
    Shot "off-ghost"
} catch { Check $false "B holds an Off ghost ($($_.Exception.Message))" }
Cmd $b vfx-hold "off" | Out-Null
Cmd $a part-fast-mount "$loader $key" | Out-Null
Start-Sleep -Seconds 2

Cmd $b vfx-hold "on" | Out-Null
Cmd $a vfx-switch "$loader hood" | Out-Null
Start-Sleep -Seconds 1
Shot "hood-swing"
Cmd $b vfx-hold "off" | Out-Null
Start-Sleep -Seconds 2
Cmd $a vfx-switch "$loader hood" | Out-Null
Start-Sleep -Seconds 2

Cmd $a vfx-tool "OBD $loader" | Out-Null
Start-Sleep -Seconds 2
Shot "obd-in-hand"
Cmd $a vfx-tool "none" | Out-Null

Cmd $a car-move "$loader CarLifter1" | Out-Null
Start-Sleep -Seconds 5
Cmd $a lift "0 up" | Out-Null
Start-Sleep -Seconds 4
Cmd $a lift "0 up" | Out-Null
Start-Sleep -Seconds 4
Cmd $a vfx-stand "$loader -1" | Out-Null
$key = @(Cmd $a vfx-parts "$loader")[0].key
Cmd $a vfx-unscrew "$loader $key pause 0.5" | Out-Null
Wait-HarnessDump -Instance $a -TimeoutSec 90 -What "A paused under the car" -Condition { param($d) $d.visuals.unscrew.state -eq "paused" } | Out-Null
$pose = (Cmd $b dump).visuals.players."$((Get-HarnessStatus $a).playerId)".pose
Note "pose under the lifted car: $pose"
Shot "arms-up"
Cmd $a vfx-unscrew "$loader $key undo" | Out-Null

Note "screenshots for the user's look: shot_bolts-half, shot_off-ghost, shot_hood-swing, shot_obd-in-hand, shot_arms-up (instance $b)"
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

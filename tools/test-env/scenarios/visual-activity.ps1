# areas: visuals, presence, tools
# remote-visual-feedback group 6: A's activity (OBD scanner, unscrewing, welder, travel) reaches B's roster; B's avatar
# of A faces the car, holds the scanner, works with a wrench motion and goes back to idle. Stats and cars stay equal.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

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

function Wait-Same([string]$What, [int]$TimeoutSec = 30) {
    try {
        Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("stats", "cars") -TimeoutSec $TimeoutSec | Out-Null
        Check $true "$What`: A and B have equal stats and cars"
    } catch {
        Check $false "$What`: $($_.Exception.Message)"
    }
}

function Move-Car([string]$Place) {
    Cmd $a car-move "$loader $Place" | Out-Null
    foreach ($name in $a, $b) {
        try {
            Wait-HarnessDump -Instance $name -TimeoutSec 60 -What "car at $Place" -Condition { param($x) @($x.placement.cars | Where-Object { $_.loader -eq $loader -and $_.inPlace -eq $Place }).Count -eq 1 } | Out-Null
        } catch {
            Check $false "$name has the car at $Place"
        }
    }
    Start-Sleep -Seconds 2
}

function PlayerA($Dump) { $Dump.visuals.players."$script:idA" }

function Wait-A([string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 10) {
    try {
        $dump = Wait-HarnessDump -Instance $b -TimeoutSec $TimeoutSec -What $What -Condition { param($d) $p = PlayerA $d; $p -and (& $Condition $p) }
        Check $true "B sees A: $What"
        return PlayerA $dump
    } catch {
        Check $false "B sees A: $What (now: $((PlayerA (Cmd $b dump)) | ConvertTo-Json -Compress -Depth 5))"
        return $null
    }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$script:idA = (Get-HarnessStatus $a).playerId
Cmd $a vfx-trace "on" | Out-Null

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Wait-Same "spawn"
Cmd $a vfx-stand "$loader 1.5" | Out-Null
Cmd $b vfx-stand "$loader 4" | Out-Null
Start-Sleep -Seconds 1

# OBD scanner in hand, facing the car.
Cmd $a vfx-tool "OBD $loader" | Out-Null
$seen = Wait-A "examining with the OBD scanner in hand, facing the car" { param($p) $p.activity.kind -eq "Examine" -and $p.propActive -and $p.propTool -eq "OBD" -and $p.facing -lt 20 }
if ($seen) { Write-Host "A on B: $($seen | ConvertTo-Json -Compress -Depth 4)" }
Cmd $a vfx-tool "none" | Out-Null
Wait-A "no activity and no prop after the tool is put away" { param($p) $p.activity.kind -eq "None" -and -not $p.propActive -and $p.pose -eq "Idle" } | Out-Null

# Unscrewing: wrench pose while the bolts move, idle after.
$key = @(Cmd $a vfx-parts "$loader")[0].key
Cmd $a vfx-unscrew "$loader $key" | Out-Null
Wait-A "unscrewing $key with the wrench pose" { param($p) $p.activity.kind -eq "Unmount" -and $p.activity.key -eq $key -and $p.pose -eq "Wrench" } 30 | Out-Null
Wait-HarnessDump -Instance $a -TimeoutSec 90 -What "A finished unscrewing" -Condition { param($d) $d.visuals.unscrew.state -in @("finished", "part not committed", "timeout") } | Out-Null
Wait-A "idle after the part is off" { param($p) $p.activity.kind -eq "None" -and $p.pose -eq "Idle" } 10 | Out-Null
$sent = (Cmd $a dump).visuals.activitySent
Write-Host "A sent $sent activity packets so far"
Wait-Same "after unscrewing"
Cmd $a part-fast-mount "$loader $key" | Out-Null
Wait-Same "mounted back"

# Welder (row 5b): car tool activity while it works; B still plays the weld once.
Move-Car "CarLifter1"
Cmd $a vfx-stand "$loader 1.5" | Out-Null
Cmd $b vfx-stand "$loader 4" | Out-Null
$weld = Cmd $a tool-use "Welder $loader"
Wait-A "working with the welder" { param($p) $p.activity.kind -eq "CarTool" -and $p.activity.modTool -eq "Welder" } 15 | Out-Null
try {
    Wait-HarnessDump -Instance $b -TimeoutSec 20 -What "B saw the weld" -Condition { param($x) $x.toolActionsSeen.Weld -eq 1 } | Out-Null
    Check $true "B saw A's weld once"
} catch { Check $false "B saw A's weld once ($((Cmd $b dump).toolActionsSeen | ConvertTo-Json -Compress))" }
Start-Sleep -Seconds ([math]::Ceiling($weld.effectTime) + 3)
Wait-Same "after the weld"

# Travel clears the activity on B.
Cmd $a vfx-tool "OBD $loader" | Out-Null
Wait-A "examining before the travel" { param($p) $p.activity.kind -eq "Examine" } | Out-Null
Cmd $a vfx-tool "none" | Out-Null
Cmd $a travel "Junkyard" | Out-Null
Wait-A "no activity after A left the garage" { param($p) $p.activity.kind -eq "None" } 60 | Out-Null
Cmd $a travel "Garage" | Out-Null
Wait-InGarage $a
Wait-Same "after the trip"

$v = (Cmd $a dump).visuals
Check ($v.activityDropped -eq 0) "A dropped no activity above the cap ($($v.activityDropped))"
foreach ($name in $Ctx.Instances) { Check ((Cmd $name dump).visuals.leaks -eq 0) "$name has no visual state leak" }
(Cmd $a vfx-trace "report") | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "vfx-trace_$a.json") -Encoding utf8

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

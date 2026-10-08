# areas: visuals, presence, persistence
# remote-visual-feedback 7.1: a player who joins or rejoins sees the current activity of the others at once (pose, tool
# in hand) and no animation of anything that happened before; the next live change animates once.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Count($Table, [string]$Name) { if ($null -eq $Table -or $null -eq $Table.$Name) { 0 } else { [int]$Table.$Name } }

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

function Wait-A([string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 10) {
    try {
        $dump = Wait-HarnessDump -Instance $b -TimeoutSec $TimeoutSec -What $What -Condition { param($d) $p = $d.visuals.players."$script:idA"; $p -and (& $Condition $p $d.visuals) }
        Check $true "B: $What"
        return $dump.visuals
    } catch {
        Check $false "B: $What (now: $((Cmd $b dump).visuals | ConvertTo-Json -Compress -Depth 6))"
        return $null
    }
}

function Join-B {
    Connect-HarnessInstance $b
    Wait-InGarage $b
    Wait-Ready $b | Out-Null
    Cmd $b vfx-stand "$loader 4" | Out-Null
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Cmd $a guard-set "Off" | Out-Null
$script:idA = (Get-HarnessStatus $a).playerId
Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Cmd $a vfx-stand "$loader 1.5" | Out-Null
$key = @(Cmd $a vfx-parts "$loader")[0].key

# A holds the scanner before B joins: B shows it at once, and no ghost or bolt plays.
Cmd $a vfx-tool "OBD $loader" | Out-Null
Start-Sleep -Seconds 1
Join-B
Cmd $b guard-set "Off" | Out-Null
Wait-A "A's scanner and pose right after joining" { param($p, $v) $p.activity.kind -eq "Examine" -and $p.propActive -and $p.pose -ne "Idle" } | Out-Null
$v = (Cmd $b dump).visuals
Check (@($v.ghostsStarted.PSObject.Properties).Count -eq 0 -and @($v.boltsActive).Count -eq 0) "B started no ghost and no bolts on joining"
Wait-Same "join"

# A pauses halfway through unscrewing; B rejoins and sees A working, without bolts (the claim came in the snapshot).
Cmd $a vfx-tool "none" | Out-Null
Cmd $a vfx-unscrew "$loader $key pause 0.5" | Out-Null
Wait-HarnessDump -Instance $a -TimeoutSec 90 -What "A paused" -Condition { param($d) $d.visuals.unscrew.state -eq "paused" } | Out-Null
Cmd $b disconnect | Out-Null
Start-Sleep -Seconds 3
Join-B
# D6: the wrench motion runs only while progress moves; a paused actor shows the reach pose.
Wait-A "A unscrewing $key from the roster" { param($p, $v) $p.activity.kind -eq "Unmount" -and $p.activity.key -eq $key -and $p.pose -in @("Wrench", "Reach") } | Out-Null
$v = (Cmd $b dump).visuals
Check (@($v.boltsActive).Count -eq 0) "B shows no bolts for a claim from the snapshot"
Check ((Count $v.ghostsStarted "Off") -eq 0) "B played no Off ghost on rejoining"

# The live finish animates once.
Cmd $a vfx-unscrew "$loader $key resume" | Out-Null
Wait-A "one Off ghost for the live finish" { param($p, $v) (Count $v.ghostsStarted "Off") -eq 1 } 90 | Out-Null
Wait-Same "finish"
foreach ($name in $Ctx.Instances) { Check ((Cmd $name dump).visuals.leaks -eq 0) "$name has no visual state leak" }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

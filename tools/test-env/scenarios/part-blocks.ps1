# areas: parts, persistence
# Soak 2026-10-08 (9 of 24 checkpoints): the "blocked" flag of parts behind a mounted part (brake caliper, cap and
# stabiliser link behind a wheel) differed between clients. The game keeps it as a counter (PartScript.blockedNo) that
# every mount adds to and every unmount takes from, so a client that applied a change through another path than the
# actor drifted. A takes a part that blocks others off and puts it back, B does the same, and B rejoins with the part
# off; after each step both clients' counters must be equal, and back at the spawn's once the part is on again.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

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

function Format-Counts($Counts) { (@($Counts.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' ') }

function Check-Blocks([string]$What, [string]$Baseline) {
    Start-Sleep -Seconds 3
    $seen = @{}
    foreach ($name in $Ctx.Instances) {
        $r = Cmd $name part-blocks "$loader"
        $seen[$name] = Format-Counts $r.actual
        Write-Host "  $name counters [$($seen[$name])]; mounted blockers [$(Format-Counts $r.expected)]"
    }
    Check ($seen[$a] -eq $seen[$b]) "$What`: A and B have the same blocked counters"
    if ($Baseline) { Check ($seen[$a] -eq $Baseline -and $seen[$b] -eq $Baseline) "$What`: both are back at the counters after the spawn" }
    return $seen[$a]
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader car_boltatlanta 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
$baseline = Check-Blocks "after the spawn"

$probe = Cmd $a part-blocks "$loader"
$key = @($probe.candidates)[0]
if (-not $key) { throw "no free part on loader $loader blocks another part" }
Write-Host "part $key blocks $(@($probe.blocks.$key) -join ', ')"

Cmd $a part-fast-unmount "$loader $key" | Out-Null
Check-Blocks "A took $key off" | Out-Null
Cmd $a part-fast-mount "$loader $key" | Out-Null
Check-Blocks "A put $key back" $baseline | Out-Null
Cmd $b part-fast-unmount "$loader $key" | Out-Null
Check-Blocks "B took $key off" | Out-Null
Cmd $b part-fast-mount "$loader $key" | Out-Null
Check-Blocks "B put $key back" $baseline | Out-Null

Cmd $a part-fast-unmount "$loader $key" | Out-Null
Start-Sleep -Seconds 3
Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Ready $b | Out-Null
Check-Blocks "B rejoined with $key off" | Out-Null
Cmd $b part-fast-mount "$loader $key" | Out-Null
Check-Blocks "B put $key back after the rejoin" $baseline | Out-Null

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

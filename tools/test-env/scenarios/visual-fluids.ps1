# areas: visuals, tools
# remote-fluid-visuals: while A drains a car's oil with the oil bin (the game's UseOilbin through the lock gate), B
# replays the drain on its own copy of the drain object: the stream particles play, the OilDrain loop is started on
# the copy and the drain plug's renderers are hidden. After A's drain B's copy, sound and hidden plug are gone and the
# oil is equal. With RemoteVisuals off B plays nothing. When A refills brake fluid (cap opened, can out, pour held), B
# shows the can at the reservoir, its stream only while A pours, and nothing after A puts the can away. When A leaves
# mid-drain B's replay is cancelled at once.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Note([string]$Message) { $script:Ctx.Result.notes += $Message; Write-Host "note: $Message" -ForegroundColor Yellow }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "$Label.json") -Encoding utf8 }

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

function Oil([string]$Name) { (@((Cmd $Name dump).carDetails) | Where-Object { $_.loader -eq $loader }).entries.'f:EngineOil.0' }

function Wait-Oil([string]$What, [double]$Level, [int]$TimeoutSec = 15) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $oa = Oil $a; $ob = Oil $b
        $ok = $oa -and $ob -and [math]::Abs($oa.Level - $Level) -lt 0.01 -and [math]::Abs($ob.Level - $Level) -lt 0.01
        if (-not $ok) { Start-Sleep -Milliseconds 500 }
    } while (-not $ok -and (Get-Date) -lt $deadline)
    Check $ok "$What`: the oil is $Level on A and B (A $($oa.Level)/$($oa.Condition), B $($ob.Level)/$($ob.Condition))"
}

function Fill-Oil([string]$What) {
    Cmd $a cardetails-fluid "$loader EngineOil 0 1 0.3" | Out-Null
    Wait-Oil $what 1
}

function Report([string]$Name) { Cmd $Name vfx-fluids "report $loader" }
function Copies($r) { @($r.copies) }
function Playing($r) { @(Copies $r | Where-Object { @($_.particles | Where-Object { $_.playing }).Count -gt 0 }) }
function DrainsOfA($r) { @($r.effects | Where-Object { $_.kind -eq "Drain" -and $_.playerId -eq $script:idA -and $_.loader -eq $loader }) }
function Loops($r, [string]$Call) { @($r.sounds | Where-Object { $_.call -eq $Call -and "$($_.go)".StartsWith("TogetherFluid") }) }

function Wait-Report([string]$Name, [string]$What, [scriptblock]$Condition, [int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Report $Name
        if (& $Condition $r) { return $r }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Write-Host "$Name $What not reached: $($r | ConvertTo-Json -Compress -Depth 6)"
    return $r
}

function Wait-DrainEnd([int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 500
        $oa = Oil $a
    } while ($oa.Level -gt 0.001 -and (Get-Date) -lt $deadline)
    Check ($oa.Level -le 0.001) "A's drain ended (A's oil $($oa.Level))"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$script:idA = (Get-HarnessStatus $a).playerId

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Wait-Same "spawn"
Cmd $a vfx-stand "$loader 1.5" | Out-Null
Cmd $b vfx-stand "$loader 4" | Out-Null
foreach ($name in $Ctx.Instances) { Cmd $name vfx-fluids "on" | Out-Null }
Save "probe_$a" (Cmd $a vfx-fluids "probe")

# 1. A drains: B plays the stream, the loop sound and hides the plug while A drains.
Fill-Oil "before the drain"
$before = Report $b
Check ($before.plug -and $before.plug.active -and $before.plug.renderers -gt 0) "B's car has the drain plug ($($before.plug | ConvertTo-Json -Compress))"
$use = Cmd $a tool-use "OilBin $loader"
$started = Get-Date
Write-Host "A drains (oil $($use.oil))"
$during = Wait-Report $b "drain replay" { param($r) (DrainsOfA $r).Count -eq 1 -and (Playing $r).Count -eq 1 -and $r.plug.hidden -gt 0 } 4
$latency = ((Get-Date) - $started).TotalSeconds
Save "during_$b" $during
Save "during_$a" (Report $a)
Check ((DrainsOfA $during).Count -eq 1) "B has a Drain effect for A on loader $loader while A drains ($(@($during.effects) | ConvertTo-Json -Compress))"
Check ((Playing $during).Count -eq 1) "B has one TogetherFluid copy with its particles playing ($(@($during.copies) | ConvertTo-Json -Compress -Depth 5))"
Check ($during.plug.hidden -eq $during.plug.renderers -and $during.plug.active) "B's drain plug is hidden by its renderers and stays active ($($during.plug | ConvertTo-Json -Compress))"
Check ((Loops $during "PlayLoopSFX" | Where-Object { $_.sound -eq "OilDrain" }).Count -eq 1) "B started the OilDrain loop on its copy ($(@($during.sounds) | ConvertTo-Json -Compress))"
Check ((Loops (Report $a) "PlayLoopSFX").Count -eq 0) "A plays no replay of its own drain"
Write-Host "B's replay started $([math]::Round($latency, 2)) s after A's drain"

$after = Wait-Report $b "replay gone" { param($r) (Copies $r).Count -eq 0 -and @($r.effects).Count -eq 0 -and $r.plug.hidden -eq 0 } 15
Wait-DrainEnd
Save "after_$b" $after
Check (@($after.effects).Count -eq 0 -and (Copies $after).Count -eq 0) "B's replay and copy are gone after A's drain"
Check ($after.plug.hidden -eq 0 -and $after.renderersHidden -eq 0) "no renderer stays hidden on B ($($after.plug | ConvertTo-Json -Compress), $($after.renderersHidden))"
Check ((Loops $after "StopLoopSFX" | Where-Object { $_.playEnd }).Count -ge 1) "B ended the loop with the drain's tail"
Check ($after.leaks -eq 0) "no state leak on B ($($after.leaks))"
Wait-Oil "after the drain" 0
Wait-Same "after the drain"

# 2. RemoteVisuals off: B plays nothing.
Cmd $b vfx-enable "off" | Out-Null
Fill-Oil "before the hidden drain"
$skippedBefore = [int]$(if ($null -ne (Report $b).skipped.disabled) { (Report $b).skipped.disabled } else { 0 })
Cmd $a tool-use "OilBin $loader" | Out-Null
Start-Sleep -Seconds 2
$off = Report $b
Check (@($off.effects).Count -eq 0 -and (Copies $off).Count -eq 0 -and $off.plug.hidden -eq 0) "with RemoteVisuals off B shows no drain ($($off | ConvertTo-Json -Compress -Depth 5))"
Check ([int]$off.skipped.disabled -gt $skippedBefore) "B counted the drain as skipped (disabled: $($off.skipped.disabled))"
Wait-DrainEnd
Cmd $b vfx-enable "on" | Out-Null
Wait-Oil "after the hidden drain" 0

# 3. A refills brake fluid: B shows the can at the reservoir, streams only while A holds the pour.
function PoursOfA($r) { @($r.effects | Where-Object { $_.kind -eq "Pour" -and $_.playerId -eq $script:idA -and $_.loader -eq $loader }) }
function Streaming($r) { @(Copies $r | Where-Object { @($_.particles | Where-Object { $_.name -eq "Emit" -and $_.emitting }).Count -gt 0 }) }
function Brake([string]$Name) { (@((Cmd $Name dump).carDetails) | Where-Object { $_.loader -eq $loader }).entries.'f:Brake.0' }

Cmd $a cardetails-fluid "$loader Brake 0 0.2" | Out-Null
Start-Sleep -Seconds 2
$pourStart = Cmd $a vfx-pour "$loader BrakeRefill start"
Write-Host "A opens $($pourStart.cap) ($($pourStart.capKey)) and takes the brake fluid can"
$deadline = (Get-Date).AddSeconds(15)
do { Start-Sleep -Milliseconds 500; $pa = Cmd $a vfx-pour "$loader BrakeRefill status" } while (-not $pa.canUse -and (Get-Date) -lt $deadline)
Save "pour-ready_$a" $pa
Check $pa.canUse "A's can is out and ready to pour ($($pa | ConvertTo-Json -Compress))"
$can = Wait-Report $b "pour can" { param($r) (PoursOfA $r).Count -eq 1 -and (Copies $r).Count -eq 1 } 4
Save "pour-can_$b" $can
Check ((PoursOfA $can).Count -eq 1 -and (Copies $can).Count -eq 1) "B shows A's can at the reservoir ($(@($can.effects) | ConvertTo-Json -Compress))"
Check ((Streaming $can).Count -eq 0) "B's can does not stream before A pours"
if ($pa.logicPosition -and @(Copies $can).Count -eq 1) {
    $p = @(Copies $can)[0].position
    $gap = [math]::Sqrt([math]::Pow($p[0] - $pa.logicPosition[0], 2) + [math]::Pow($p[1] - $pa.logicPosition[1], 2) + [math]::Pow($p[2] - $pa.logicPosition[2], 2))
    Check ($gap -lt 0.05) "B's can stands where A's can is ($([math]::Round($gap, 3)) m apart)"
}
Cmd $a vfx-pour "$loader BrakeRefill hold 3" | Out-Null
$pouring = Wait-Report $b "stream" { param($r) (Streaming $r).Count -eq 1 } 3
Save "pouring_$b" $pouring
Check ((Streaming $pouring).Count -eq 1) "B's can streams while A pours ($(@($pouring.copies) | ConvertTo-Json -Compress -Depth 5))"
Start-Sleep -Seconds 3
$stopped = Wait-Report $b "stream stopped" { param($r) (Streaming $r).Count -eq 0 } 4
Check ((Streaming $stopped).Count -eq 0 -and (PoursOfA $stopped).Count -eq 1) "B's stream stops when A lets go, the can stays"
Cmd $a vfx-pour "$loader BrakeRefill end" | Out-Null
$away = Wait-Report $b "can away" { param($r) (Copies $r).Count -eq 0 -and @($r.effects).Count -eq 0 } 6
Check ((Copies $away).Count -eq 0 -and @($away.effects).Count -eq 0) "B's can is gone after A puts it away"
Check ($away.leaks -eq 0 -and $away.renderersHidden -eq 0) "no state leak or hidden renderer on B ($($away.leaks), $($away.renderersHidden))"
$deadline = (Get-Date).AddSeconds(10)
do { Start-Sleep -Milliseconds 500; $ba = Brake $a; $bb = Brake $b } while (($null -eq $ba -or $null -eq $bb -or [math]::Abs($ba.Level - $bb.Level) -gt 0.01) -and (Get-Date) -lt $deadline)
Check ($ba -and $bb -and [math]::Abs($ba.Level - $bb.Level) -le 0.01 -and $ba.Level -gt 0.25) "A poured and B has A's brake fluid level (A $($ba.Level), B $($bb.Level))"
Wait-Same "after the pour"

# 4. A leaves mid-drain: B's replay is cancelled and its sound stopped.
Fill-Oil "before the cut drain"
Cmd $a tool-use "OilBin $loader" | Out-Null
$cut = Wait-Report $b "drain replay" { param($r) (DrainsOfA $r).Count -eq 1 } 4
Check ((DrainsOfA $cut).Count -eq 1) "B replays the drain before A leaves"
$stopsBefore = (Loops $cut "StopLoopSFX").Count
Cmd $a to-menu | Out-Null
$gone = Wait-Report $b "replay cancelled" { param($r) (Copies $r).Count -eq 0 -and @($r.effects).Count -eq 0 } 8
Save "left_$b" $gone
Check (@($gone.effects).Count -eq 0 -and (Copies $gone).Count -eq 0) "B's replay is gone after A left"
Check ($gone.plug.hidden -eq 0 -and $gone.renderersHidden -eq 0) "B's drain plug is shown again after A left"
Check ((Loops $gone "StopLoopSFX").Count -gt $stopsBefore) "B stopped the loop when A left"
Check ($gone.leaks -eq 0) "no state leak on B ($($gone.leaks))"
$status = Get-HarnessStatus -Instance $b
Check ($status.playable -and $status.scene -eq "garage") "B stays playable in the garage"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

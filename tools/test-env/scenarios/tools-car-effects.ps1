# areas: tools, details, cars
# sync-workshop-car-tools: A uses the car tools on a car both players see (paint shop, car wash, stationary and portable
# interior detailing, welder, oil bin, engine crane out/in, a refused engine swap, the map dyno). After every step A and
# B have equal stats, inventory, cars and car details; B saw each tool's action once, stays playable and keeps its lift
# buttons. At the end B rejoins and still has every result (they travel through rows 1/4, not ToolAction).
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$sections = @("stats", "inventory", "cars")
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Note([string]$Message) { $script:Ctx.Result.notes += $Message; Write-Host "note: $Message" -ForegroundColor Yellow }
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
        $dump = Wait-HarnessDumpsEqual -Left $a -Right $b -Sections $sections -TimeoutSec $TimeoutSec
        Check $true "$What`: A and B have equal $($sections -join ', ')"
    } catch {
        Check $false "$What`: $($_.Exception.Message)"
        $dump = Cmd $a dump
    }
    Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label $What | Out-Null
    Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label $What | Out-Null
    return $dump
}

function Wait-SameDetails([string]$What, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Seconds 1
        $da = Cmd $a cardetails-show "$loader"
        $db = Cmd $b cardetails-show "$loader"
        $differ = @($da.PSObject.Properties.Name | Where-Object { $da.$_ -ne $db.$_ })
    } while ($differ.Count -gt 0 -and (Get-Date) -lt $deadline)
    Check ($differ.Count -eq 0) "$What`: A and B have the same car details (differ: $($differ -join ', '))"
    foreach ($section in $differ) { Write-Host "  $section A: $($da.$section)"; Write-Host "  $section B: $($db.$section)" }
    return $da
}

function Wait-Seen([string]$Kind, [int]$Count) {
    try {
        Wait-HarnessDump -Instance $b -TimeoutSec 15 -What "B saw $Count $Kind" -Condition { param($x) $x.toolActionsSeen.$Kind -eq $Count } | Out-Null
        Check $true "B saw A's $Kind action $Count time(s)"
    } catch {
        Check $false "B saw A's $Kind action $Count time(s) (seen: $((Cmd $b dump).toolActionsSeen | ConvertTo-Json -Compress))"
    }
}

function Check-Playable([string]$What) {
    $status = Get-HarnessStatus -Instance $b
    Check ($status.playable -and $status.scene -eq "garage") "$What`: B stays playable in the garage"
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

function Body($Dump, [string]$Name) { @(@($Dump.cars | Where-Object { $_.index -eq $loader })[0].bodyParts | Where-Object { $_.name -eq $Name })[0] }
function EngineGroups($Dump, [string]$Engine) { @($Dump.inventory.groups | Where-Object { $_.ID -eq $Engine }) }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Wait-Same "spawn" | Out-Null
Wait-SameDetails "spawn" | Out-Null
Cmd $a cardetails-randomize "$loader" | Out-Null
Wait-SameDetails "dirty car" | Out-Null

# Paint shop (car): row 4 sends the paint from SubmitColor, B only plays the effect.
Cmd $a tool-paint-car "$loader 0.8,0.1,0.1" | Out-Null
Wait-SameDetails "paint shop" | Out-Null
Wait-Seen "PaintCar" 1
Wait-Same "paint shop" | Out-Null
Check-Playable "paint shop"

# Car wash and the stationary interior kit work on the car at the car wash.
Move-Car "CarWash"
$wash = Cmd $a tool-use "CarWash $loader"
Start-Sleep -Seconds ([math]::Ceiling($wash.effectTime) + 4)
Wait-SameDetails "car wash" | Out-Null
Wait-Seen "Wash" 1
Wait-Same "car wash" | Out-Null
Check-Playable "car wash"

$interior = Cmd $a tool-use "InteriorDetailingStationary $loader"
Start-Sleep -Seconds ([math]::Ceiling($interior.effectTime) + 4)
Wait-SameDetails "stationary interior detailing" | Out-Null
Wait-Seen "InteriorDetailing" 1
Wait-Same "stationary interior detailing" | Out-Null
Check-Playable "stationary interior detailing"

# Welder and portable kit on lift 1: B's lift buttons stay enabled (no StartAnim on B).
Move-Car "CarLifter1"
$weld = Cmd $a tool-use "Welder $loader"
Start-Sleep -Seconds 1
$during = Cmd $b dump
Check ($during.lifterButtonsEnabled."$loader" -eq $true) "B's lift buttons are enabled while A welds ($($during.lifterButtonsEnabled | ConvertTo-Json -Compress))"
Start-Sleep -Seconds ([math]::Ceiling($weld.effectTime) + 4)
$d = Wait-Same "welder"
$bodyPart = Body $d "body"
Check ($bodyPart.condition -eq 1 -and $bodyPart.dent -eq 1) "the body is welded on both (condition $($bodyPart.condition), dent $($bodyPart.dent))"
Wait-Seen "Weld" 1
Check-Playable "welder"
$after = Cmd $b dump
Check ($after.lifterButtonsEnabled."$loader" -eq $true) "B's lift buttons are enabled after the welding"

$interior = Cmd $a tool-use "InteriorDetailing $loader"
Start-Sleep -Seconds ([math]::Ceiling($interior.effectTime) + 4)
Wait-SameDetails "portable interior detailing" | Out-Null
Wait-Seen "InteriorDetailing" 2
Wait-Same "portable interior detailing" | Out-Null
Check-Playable "portable interior detailing"

# Oil bin: the drain takes oil level * 5 s; the result travels through row 4's Fluids.
$oil = Cmd $a tool-use "OilBin $loader"
Write-Host "oil level before the drain: $($oil.oil)"
Start-Sleep -Seconds ([math]::Ceiling($oil.oil * 5) + 6)
$details = Wait-SameDetails "oil bin"
if ($oil.oil -gt 0) { Wait-Seen "DrainOil" 1 } else { Note "the car had no engine oil; the drain did nothing" }
Wait-Same "oil bin" | Out-Null

# Engine crane: out and back in (row 1's transaction), then a different engine is refused while connected.
$out = Cmd $a tool-engine-out "$loader"
Start-Sleep -Seconds 4
$engine = $out.engine
if (@(EngineGroups (Cmd $a dump) $engine).Count -eq 0) {
    Note "tool-engine-out did nothing (blockers: $(@($out.blockers) -join ', ')); using crane-out"
    Cmd $a crane-out "$loader" | Out-Null
    Start-Sleep -Seconds 3
}
$d = Wait-Same "engine out"
Check (@(EngineGroups $d $engine).Count -eq 1) "one $engine group in the shared inventory"
Wait-Seen "EngineOut" 1
Check-Playable "engine out"

$group = @(EngineGroups $d $engine)[0].UID
Cmd $a tool-engine-in "$loader $group" | Out-Null
Start-Sleep -Seconds 4
$d = Wait-Same "engine in"
Check (@(EngineGroups $d $engine).Count -eq 0) "the $engine group is gone on both"
Wait-Seen "EngineIn" 1

$swap = Cmd $a tool-engine-in "$loader swap"
if (-not $swap.swapOption) {
    Note "$car has no engine swap option; swap step skipped"
} else {
    Check $swap.refused "a swap to $($swap.engine) is refused while connected"
    Wait-Same "refused swap" | Out-Null
}

# Dyno (map path): the result is stored by row 13's Dyno details section.
$dyno = Cmd $a tool-dyno "$loader"
Write-Host "dyno: $($dyno | ConvertTo-Json -Compress)"
$details = Wait-SameDetails "dyno"
Check ($null -ne $details.Dyno -and $details.Dyno -ne "null") "the dyno result is in the car details ($($details.Dyno))"

# Late join: the results come from rows 1/4 snapshots.
Cmd $b to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 60 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Ready $b | Out-Null
Wait-Same "late join" 60 | Out-Null
Wait-SameDetails "late join" | Out-Null

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

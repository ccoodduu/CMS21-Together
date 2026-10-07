# economy-audit 7.1: every fee is applied once by the server, with its amount, and A and B end with the same money.
# Steps whose window is still guarded or whose verb belongs to a row that is not merged are skipped with a note.
# Also: Expert doubles work XP once, two fees paid in a race are both applied, an unclaimed money change is dropped,
# a wrong amount is refused and corrected, and a fee larger than the money clamps at 0.
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
$skipped = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Skip([string]$Message) { $script:skipped += "skipped: $Message"; Write-Host "SKIP: $Message" -ForegroundColor Yellow }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$Loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $true }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Wait-StatsEqual([string]$What, [scriptblock]$Condition = { param($s) $true }) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $sa = (Dump $a).stats; $sb = (Dump $b).stats
        $same = ($sa | ConvertTo-Json -Compress) -eq ($sb | ConvertTo-Json -Compress)
    } while (-not ($same -and (& $Condition $sa)) -and (Get-Date) -lt $deadline)
    Check $same "$What`: A and B have the same stats (A $($sa | ConvertTo-Json -Compress), B $($sb | ConvertTo-Json -Compress))"
    return $sa
}

function Check-Unattributed([string]$What) {
    foreach ($name in $Ctx.Instances) {
        $count = (Dump $name).economy.unattributed
        Check ($count -eq 0) "$What`: $name has no unattributed economy call ($count)"
    }
}

function Server-Lines([int]$Mark, [string]$Pattern) {
    @(Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $Mark | Where-Object { $_ -match $Pattern })
}

$script:denied = @()
function Guarded([string]$Key) { $script:denied -contains $Key }

# Runs one fee step and checks that the server applied it once ($Expected = money delta, $null = ranged).
function Fee-Step {
    param([string]$Who, [string]$Verb, [string]$Arguments, [string]$Reason, $Expected, [string]$GuardKey = "", [string]$What, [switch]$MayChargeNothing)
    if ($GuardKey -and (Guarded $GuardKey)) { Skip "$What ($GuardKey is guarded on this build)"; return }
    $before = (Dump $a).stats.money
    $mark = Get-ServerLogMark
    try { Send-HarnessCommand -Instance $Who -Verb $Verb -Arguments $Arguments | Out-Null }
    catch {
        if ($_.Exception.Message -match "unknown command|no .* tool|no loaded car|no mounted part|has no windows") { Skip "$What ($($_.Exception.Message))" }
        else { Check $false "$What`: $($_.Exception.Message)" }
        return
    }
    $line = try { Wait-ServerLog -Pattern "\[Economy\] client \d+ $Reason\(-?\d+\) money" -After $mark -TimeoutSec 20 } catch { $null }
    if (-not $line -and $MayChargeNothing) { Skip "$What (the game charged nothing)"; return }
    Check ([bool]$line) "$What`: the server applied $Reason ($line)"
    if (-not $line) { return }
    $delta = if ($line -match "money ([+-]?\d+) ->") { [int]$Matches[1] } else { 0 }
    if ($null -ne $Expected) { Check ($delta -eq $Expected) "$What`: the server's money changed by $Expected ($delta)" }
    else { Check ($delta -lt 0) "$What`: a ranged fee was charged ($delta)" }
    $stats = Wait-StatsEqual $What { param($s) $s.money -eq $before + $delta }
    Check ($stats.money -eq $before + $delta) "$What`: money $before -> $($before + $delta) on A and B ($($stats.money))"
    Start-Sleep -Seconds 1
    $applied = @(Server-Lines $mark "\[Economy\] client \d+ $Reason\(-?\d+\) money").Count
    Check ($applied -eq 1) "$What`: applied once ($applied)"
    Check-Unattributed $What
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "enforce" | Out-Null }
$script:denied = @(Send-HarnessCommand -Instance $a -Verb guard-rules | Where-Object { $_ -like "deny *" } | ForEach-Object { ($_ -split '\s+')[1] })
$startMark = Get-ServerLogMark

Send-ServerCommand "money set 200000"
Wait-StatsEqual "money set 200000" { param($s) $s.money -eq 200000 } | Out-Null
Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
Send-HarnessCommand -Instance $b -Verb car-spawn -Arguments "1 $car 0" | Out-Null
Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "2 $car 0" | Out-Null
Send-HarnessCommand -Instance $b -Verb car-spawn -Arguments "3 $car 0" | Out-Null
Send-HarnessCommand -Instance $b -Verb car-spawn -Arguments "4 $car 0" | Out-Null
foreach ($name in $Ctx.Instances) {
    foreach ($loader in 0, 1, 2, 3, 4) { Check (Wait-Ready $name $loader) "$name has loader $loader ready" }
}
Start-Sleep -Seconds 3
Check-Unattributed "after connecting and spawning"

# Travel fees (server rule travel_fees, default on).
Fee-Step $a "econ-map-travel" "Junkyard" "TravelFee" -500 -GuardKey "Scene:Junkyard" -What "A travels to the junkyard"
$inJunkyard = try { Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "A in the junkyard" -Condition { param($s) $s.scene -match "(?i)junkyard" -and $s.playable }; $true } catch { $false }
if ($inJunkyard) {
    Send-HarnessCommand -Instance $a -Verb travel -Arguments "Garage" | Out-Null
    Wait-InGarage $a
    foreach ($loader in 0, 1) { Check (Wait-Ready $a $loader) "A has loader $loader ready after the trip" }
    Start-Sleep -Seconds 3
}
Fee-Step $b "econ-map-travel" "Auction" "TravelFee" -200 -GuardKey "Scene:Auction" -What "B travels to the auction"
$inAuction = try { Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "B in the auction" -Condition { param($s) $s.scene -ne "garage" -and $s.playable }; $true } catch { $false }
if ($inAuction) {
    Send-HarnessCommand -Instance $b -Verb travel -Arguments "Garage" | Out-Null
    Wait-InGarage $b
    foreach ($loader in 0, 1) { Check (Wait-Ready $b $loader) "B has loader $loader ready after the trip" }
    Start-Sleep -Seconds 3
}

# Fees with a fixed amount.
Fee-Step $a "econ-fee" "spill 0" "FluidSpill" $null -What "A spills fluid"
Fee-Step $b "econ-fee" "refill 1" "FluidRefill" $null -GuardKey "Mode:DrainTool" -What "B refills fluid" -MayChargeNothing
Fee-Step $a "econ-fee" "wash-paint 0" "WashBeforePaint" -100 -GuardKey "Window:Paintshop" -What "A washes before painting"
Fee-Step $a "tool-paint-car" "0 0.8,0.1,0.1" "PaintCar" -1000 -GuardKey "Window:Paintshop" -What "A paints the car"
Fee-Step $b "econ-fee" "wash-tint 1" "WashBeforeTint" -100 -GuardKey "Window:Tinting" -What "B washes before tinting"
Fee-Step $b "econ-fee" "tint 1 4" "Tint" -200 -GuardKey "Window:Tinting" -What "B tints 4 windows"
Fee-Step $a "tool-use" "Welder 0 paid" "Welder" $null -GuardKey "Pie:equipment_use" -What "A welds"
Fee-Step $a "tool-use" "InteriorDetailing 0 paid" "InteriorDetailing" $null -GuardKey "Pie:equipment_use" -What "A details the interior"
Fee-Step $b "tool-repair" "" "PartRepair" $null -GuardKey "Window:RepairPart" -What "B repairs a part"

# A wrong amount is refused and the requester's prediction is corrected.
$before = (Dump $a).stats.money
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-send -Arguments "FluidSpill -900" | Out-Null
$refused = try { Wait-ServerLog -Pattern "\[Economy\] client \d+ FluidSpill\(0\) refused Invalid" -After $mark -TimeoutSec 15 } catch { $null }
Check ([bool]$refused) "a spill fine of 900 is refused Invalid ($refused)"
$stats = Wait-StatsEqual "after the refused fee"
Check ($stats.money -eq $before) "the refused fee changed no money ($before -> $($stats.money))"

# Expert doubles work XP once.
Send-ServerCommand "gamemode Expert"
Start-Sleep -Seconds 3
$s0 = Wait-StatsEqual "Expert set"
try {
    Send-HarnessCommand -Instance $a -Verb econ-unmount -Arguments "0" | Out-Null
    $s1 = Wait-StatsEqual "work XP on Expert" { param($s) $s.exp -ne $s0.exp -or $s.level -ne $s0.level }
    if ($s1.level -eq $s0.level) { Check ($s1.exp - $s0.exp -eq 2) "an unmount on Expert gives 2 XP on both ($($s0.exp) -> $($s1.exp))" }
    else { Write-Host "level changed during the Expert step ($($s0.level) -> $($s1.level)); XP delta not checked" }
} catch { Check $false "Expert unmount: $($_.Exception.Message)" }
Send-ServerCommand "gamemode Normal"
Start-Sleep -Seconds 3
Check-Unattributed "after the Expert step"

# Race: both pay a fee while incoming packets are held.
$before = (Dump $a).stats.money
$mark = Get-ServerLogMark
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments "on" | Out-Null }
$race = @()
try { Send-HarnessCommand -Instance $a -Verb econ-fee -Arguments "spill 2" | Out-Null; $race += "A" } catch { Write-Host "A race spill: $($_.Exception.Message)" }
try { Send-HarnessCommand -Instance $b -Verb econ-fee -Arguments "spill 3" | Out-Null; $race += "B" } catch { Write-Host "B race spill: $($_.Exception.Message)" }
Start-Sleep -Seconds 6
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments "off" | Out-Null }
if ($race.Count -eq 0) { Skip "race step (no part with fluid left)" }
else {
    Start-Sleep -Seconds 2
    $lines = @(Server-Lines $mark "\[Economy\] client \d+ FluidSpill\(0\) money")
    Check ($lines.Count -eq $race.Count) "each racing fee was applied once ($($lines.Count) of $($race.Count))"
    $cost = 0; foreach ($l in $lines) { if ($l -match "money ([+-]?\d+) ->") { $cost -= [int]$Matches[1] } }
    $stats = Wait-StatsEqual "after the race" { param($s) $s.money -eq $before - $cost }
    Check ($stats.money -eq $before - $cost) "the race cost $cost on both ($before -> $($stats.money))"
}

# Default deny: money changed by no synced feature stays unchanged.
$before = (Dump $a).stats.money
Send-HarnessCommand -Instance $a -Verb econ-unattributed -Arguments "500" | Out-Null
Start-Sleep -Seconds 2
$dumpA = Dump $a; $dumpB = Dump $b
Check ($dumpA.stats.money -eq $before -and $dumpB.stats.money -eq $before) "an unclaimed +500 changes no money (A $($dumpA.stats.money), B $($dumpB.stats.money), was $before)"
Check ($dumpA.economy.unattributed -eq 1) "A counted one unattributed call ($($dumpA.economy.unattributed))"
Send-HarnessCommand -Instance $a -Verb econ-unattributed -Arguments "reset" | Out-Null

# Short money: a fee after the fact clamps at 0.
Send-ServerCommand "money set 30"
Wait-StatsEqual "money set 30" { param($s) $s.money -eq 30 } | Out-Null
try {
    Send-HarnessCommand -Instance $b -Verb econ-fee -Arguments "spill 4" | Out-Null
    $stats = Wait-StatsEqual "fee with too little money" { param($s) $s.money -eq 0 }
    Check ($stats.money -eq 0) "a fine larger than the 30 money leaves 0 on both ($($stats.money))"
} catch { Skip "clamp step ($($_.Exception.Message))" }
Check-Unattributed "at the end"

$invalid = @(Server-Lines $startMark "\[Economy\] .* refused Invalid")
Check ($invalid.Count -eq 1) "the only Invalid refusal is the deliberate one ($($invalid.Count): $($invalid -join ' | '))"
Send-ServerCommand "economy 40"

$Ctx.Result.notes += $failures
$Ctx.Result.notes += $skipped
$Ctx.Result.passed = ($failures.Count -eq 0)

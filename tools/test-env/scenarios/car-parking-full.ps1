# areas: placement, economy
# sync-car-placement-and-lifts full parking: A fills 9 of the 10 slots, then A and B park one car each at the same
# time (net-hold). One gets the last slot, the other is refused and its car comes back into the garage for both.
# A priced arrival into the full parking is refused and costs nothing.
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$Loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}

function Placement([string]$Name) { (Send-HarnessCommand -Instance $Name -Verb dump).placement }

function Wait-Slots([string]$Name, [int]$Count) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $n = @((Placement $Name).parking.slots).Count
    } while ($n -ne $Count -and (Get-Date) -lt $deadline)
    if ($n -ne $Count) { throw "$Name has $n parked cars, expected $Count" }
}

function Hold([string]$Mode) { foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments $Mode | Out-Null } }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

for ($i = 1; $i -le 9; $i++) {
    Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
    Wait-Ready $a 0 | Out-Null
    Wait-Ready $b 0 | Out-Null
    Send-HarnessCommand -Instance $a -Verb park -Arguments "0" | Out-Null
    Wait-Slots $a $i
}
Wait-Slots $b 9
Write-Host "9 of 10 slots used"

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
Send-HarnessCommand -Instance $b -Verb car-spawn -Arguments "1 $car 0" | Out-Null
foreach ($name in $Ctx.Instances) { Wait-Ready $name 0 | Out-Null; Wait-Ready $name 1 | Out-Null }
Send-HarnessCommand -Instance $a -Verb car-move -Arguments "0 Entrance1" | Out-Null
Start-Sleep -Seconds 4
Send-HarnessCommand -Instance $b -Verb car-move -Arguments "1 Entrance2" | Out-Null
Start-Sleep -Seconds 4

$mark = Get-ServerLogMark
Hold "on"
Send-HarnessCommand -Instance $b -Verb park -Arguments "1" | Out-Null
Send-HarnessCommand -Instance $a -Verb park -Arguments "0" | Out-Null
Start-Sleep -Seconds 3
Hold "off"
$refused = Wait-ServerLog -Pattern "Park of loader \d from client \d+ refused: ParkingFull" -After $mark -TimeoutSec 15
Check ([bool]$refused) "one of the two parks was refused ($refused)"
$deadline = (Get-Date).AddSeconds(60)
do {
    Start-Sleep -Seconds 1
    $pa = Placement $a | ConvertTo-Json -Depth 6 -Compress
    $pb = Placement $b | ConvertTo-Json -Depth 6 -Compress
    $placement = Placement $a
} while (-not ($pa -eq $pb -and @($placement.parking.slots).Count -eq 10 -and @($placement.cars).Count -eq 1) -and (Get-Date) -lt $deadline)
Check ($pa -eq $pb) "A and B agree after the race (A: $pa, B: $pb)"
Check (@($placement.parking.slots).Count -eq 10) "the parking is full ($(@($placement.parking.slots).Count) slots)"
Check (@($placement.cars).Count -eq 1) "the refused car is back in the garage ($(@($placement.cars).Count) cars)"

$moneyBefore = (Send-HarnessCommand -Instance $a -Verb dump).stats.money
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb park-incoming -Arguments "0 1000" | Out-Null
Check ([bool](Wait-ServerLog -Pattern "Arrival of .* refused: ParkingFull" -After $mark -TimeoutSec 10)) "an arrival into the full parking is refused"
Start-Sleep -Seconds 2
$moneyA = (Send-HarnessCommand -Instance $a -Verb dump).stats.money
$moneyB = (Send-HarnessCommand -Instance $b -Verb dump).stats.money
Check ($moneyA -eq $moneyBefore -and $moneyB -eq $moneyBefore) "money unchanged ($moneyBefore -> A $moneyA, B $moneyB)"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

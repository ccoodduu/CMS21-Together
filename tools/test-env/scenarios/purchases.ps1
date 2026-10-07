# sync-players-and-scenes 6.5 (row 6 part 2), guard enforcing: A buys 3 parts in the junkyard (both get them once and
# the same money); A buys a junkyard car, which lands in the shared parking of both with the money down exactly once;
# a second car through the location window's Garage button still goes to the parking; with too little money the
# purchase is refused NoMoney and nothing changes.
param($Ctx)

$a, $b = $Ctx.Instances
$part = "tuleja_1"
$price = 5000
$failures = @()
$skipped = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Skip([string]$Message) { $script:skipped += "skipped: $Message"; Write-Host "SKIP: $Message" -ForegroundColor Yellow }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-InJunkyard([string]$Name) {
    $s = Wait-HarnessStatus -Instance $Name -TimeoutSec 180 -What "$Name in the junkyard" -Condition {
        param($s) $s.scene -match "(?i)junkyard" -and $s.playable -and $s.connectionValid
    }
    Write-Host "$Name is in $($s.scene)"
}

function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }

function Money([string]$Name) { (Dump $Name).stats.money }

function Slots([string]$Name) { @((Send-HarnessCommand -Instance $Name -Verb parking).slots) }

function Slots-Json([string]$Name) { Slots $Name | ConvertTo-Json -Depth 4 -Compress }

function Count-Part($Dump) { @($Dump.inventory.items | Where-Object { $_.ID -eq $part }).Count }

function Wait-Money([string]$What, [int]$Expected) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $ma = Money $a; $mb = Money $b
    } while (-not ($ma -eq $Expected -and $mb -eq $Expected) -and (Get-Date) -lt $deadline)
    Check ($ma -eq $Expected -and $mb -eq $Expected) "$What`: money is $Expected on A and B (A $ma, B $mb)"
}

function Wait-SlotCount([string]$What, [int]$Expected) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $na = (Slots $a).Count; $nb = (Slots $b).Count
    } while (-not ($na -eq $Expected -and $nb -eq $Expected) -and (Get-Date) -lt $deadline)
    Check ($na -eq $Expected -and $nb -eq $Expected) "$What`: $Expected parked cars on A and B (A $na, B $nb)"
    Check ((Slots-Json $a) -eq (Slots-Json $b)) "$What`: A and B have the same parking (A $(Slots-Json $a), B $(Slots-Json $b))"
}

function Server-Lines([int]$Mark, [string]$Pattern) {
    @(Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $Mark | Where-Object { $_ -match $Pattern })
}

function Wait-Cars([string]$Name, [int]$Count) {
    $deadline = (Get-Date).AddSeconds(90)
    do {
        $cars = @(Send-HarnessCommand -Instance $Name -Verb outdoor-cars)
        if ($cars.Count -ge $Count) { return $cars }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    return $cars
}

function Check-Unattributed([string]$What) {
    foreach ($name in $Ctx.Instances) {
        $n = (Dump $name).economy.unattributed
        Check ($n -eq 0) "$What`: $name has no unattributed money change ($n)"
    }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "enforce" | Out-Null }
Send-ServerCommand "money set 200000"
Wait-Money "money set" 200000

# Parts bought in the junkyard.
$before = Dump $b
Send-HarnessCommand -Instance $a -Verb travel -Arguments "Junkyard" | Out-Null
Wait-InJunkyard $a
$buy = Send-HarnessCommand -Instance $a -Verb junk-buy -Arguments "$part 3"
Write-Host "junk-buy: $($buy | ConvertTo-Json -Compress)"
Start-Sleep -Seconds 3
Wait-InGarage $a
$deadline = (Get-Date).AddSeconds(25)
do {
    Start-Sleep -Milliseconds 700
    $dumpA = Dump $a; $dumpB = Dump $b
    $differ = @(Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory", "stats"))
} while (-not ($differ.Count -eq 0 -and (Count-Part $dumpA) -eq (Count-Part $before) + 3) -and (Get-Date) -lt $deadline)
Check ($differ.Count -eq 0) "after the junkyard parts A and B have the same inventory and money (differ: $($differ -join ', '))"
Check ((Count-Part $dumpA) -eq (Count-Part $before) + 3) "the 3 bought parts are in the inventory once ($(Count-Part $before) -> $(Count-Part $dumpA))"
$partUids = @($dumpA.inventory.items | Where-Object { $_.ID -eq $part } | ForEach-Object { $_.UID })
Check (($partUids | Select-Object -Unique).Count -eq $partUids.Count) "no part UID appears twice"
$paid = $before.stats.money - $dumpA.stats.money
Check ($paid -gt 0) "the parts cost money ($paid)"
if ($paid -ne $buy.price) { Write-Host "note: the server charged $paid, the window showed $($buy.price)" -ForegroundColor Yellow }
Check-Unattributed "after the junkyard parts"

# A junkyard car to the shared parking.
Send-HarnessCommand -Instance $a -Verb travel -Arguments "Junkyard" | Out-Null
Wait-InJunkyard $a
$cars = Wait-Cars $a 2
Write-Host "junkyard cars: $($cars | ForEach-Object { $_.carToLoad })"
if ($cars.Count -lt 1) {
    Check $false "the junkyard has cars to buy"
} else {
    $moneyBefore = Money $a
    $slotsBefore = (Slots $a).Count
    $suppressedBefore = [int](Dump $a).economy.suppressed.CarPurchase
    $mark = Get-ServerLogMark
    $r = Send-HarnessCommand -Instance $a -Verb buy-car-here -Arguments "0 $price"
    Write-Host "buy-car-here: $($r | ConvertTo-Json -Compress)"
    Check ($r.captureOpen) "the purchase was captured"
    Check ($r.moneyAfter -eq $r.moneyBefore) "the local money did not change at BuyCar ($($r.moneyBefore) -> $($r.moneyAfter))"
    $arrived = try { Wait-ServerLog -Pattern "\[Parking\] .* arrived in slot \d+ from client \d+ for $price\." -After $mark -TimeoutSec 20 } catch { $null }
    Check ([bool]$arrived) "the server took the car into the parking ($arrived)"
    Wait-Money "after the car" ($moneyBefore - $price)
    Wait-SlotCount "after the car" ($slotsBefore + 1)
    Check (@(Slots $b | Where-Object { $_.carToLoad -eq $r.carToLoad }).Count -ge 1) "B's parking has $($r.carToLoad)"
    Start-Sleep -Seconds 3
    Check ((Money $a) -eq $moneyBefore - $price -and (Money $b) -eq $moneyBefore - $price) "the money went down exactly once"
    Check (@(Server-Lines $mark "arrived in slot").Count -eq 1) "the server stored the car once"
    Check ([int](Dump $a).economy.suppressed.CarPurchase -eq $suppressedBefore + 1) "A suppressed the vanilla debit once"
    Check-Unattributed "after the car"

    # The Garage button still sends the car to the parking.
    $cars = Wait-Cars $a 1
    if ($cars.Count -lt 1) { Skip "garage button (no car left in the junkyard)" }
    else {
        $moneyBefore = Money $a
        $slotsBefore = (Slots $a).Count
        $carsB = @((Dump $b).cars).Count
        $mark = Get-ServerLogMark
        $r = Send-HarnessCommand -Instance $a -Verb buy-car-here -Arguments "0 $price garage"
        Write-Host "buy-car-here (garage button): $($r | ConvertTo-Json -Compress)"
        $arrived = try { Wait-ServerLog -Pattern "arrived in slot \d+ from client \d+ for $price\." -After $mark -TimeoutSec 20 } catch { $null }
        Check ([bool]$arrived) "the Garage button also parks the car ($arrived)"
        Wait-Money "after the garage-button car" ($moneyBefore - $price)
        Wait-SlotCount "after the garage-button car" ($slotsBefore + 1)
        Check (@((Dump $b).cars).Count -eq $carsB) "no car appeared in B's garage"
    }

    # Too little money: refused, nothing changes.
    $cars = Wait-Cars $a 1
    if ($cars.Count -lt 1) { Skip "NoMoney (no car left in the junkyard)" }
    else {
        Send-ServerCommand "money set $($price - 1)"
        Wait-Money "money set below the price" ($price - 1)
        $slotsBefore = (Slots $a).Count
        $mark = Get-ServerLogMark
        $r = Send-HarnessCommand -Instance $a -Verb buy-car-here -Arguments "0 $price"
        Write-Host "buy-car-here (no money): $($r | ConvertTo-Json -Compress)"
        $refused = try { Wait-ServerLog -Pattern "Arrival of .* from client \d+ refused: NoMoney" -After $mark -TimeoutSec 20 } catch { $null }
        Check ([bool]$refused) "the purchase is refused NoMoney ($refused)"
        Start-Sleep -Seconds 3
        Wait-Money "after the refusal" ($price - 1)
        Wait-SlotCount "after the refusal" $slotsBefore
        Check-Unattributed "after the refusal"
    }
}

Send-HarnessCommand -Instance $a -Verb travel -Arguments "Garage" | Out-Null
Start-Sleep -Seconds 3
Wait-InGarage $a
Start-Sleep -Seconds 3
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "end"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "end"
$diff = Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory", "stats", "cars")
Check ($diff.Count -eq 0) "back in the garage A and B agree on inventory, money and cars (differ: $($diff -join ', '))"
Check ((Slots-Json $a) -eq (Slots-Json $b)) "back in the garage A and B have the same parking"

$Ctx.Result.notes += $failures
$Ctx.Result.notes += $skipped
$Ctx.Result.passed = ($failures.Count -eq 0)

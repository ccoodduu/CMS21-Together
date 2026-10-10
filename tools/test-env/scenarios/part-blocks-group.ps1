# areas: parts
# Soak 20261010-020148_L2 (rule 1 from checkpoint 4): after C took the radiator fan of a car_mayenm3 off and put it
# back, the radiator behind it was blocked on D only. The fan's blades come off with it as separate items
# (unmountWithSeparate), so each part goes back from its own item; the game's ShowMounted stopped early for the fan
# (it wants a group item), so the actor kept the radiator free and the fan item. A and then B take the fan off and
# mount the fan and its blades from their items (DoMount + ShowMounted, then A once more with FastMount). After each
# step both clients' blocked counters (PartScript.blockedNo) are equal and, with the fan on again, back at the spawn's;
# the mounted items are gone from both inventories, and the clients' own blocked-counter check reports no drift.
# Last, the soak's item-less mount is repeated and its counters printed (no player action).
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_mayenm3"
$fan = "s:29.1"
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

function Counters([string]$Name) { (@((Cmd $Name part-blocks "$loader").actual.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' ') }

function Mounted([string]$Name, [string[]]$Keys) {
    $parts = @(@((Cmd $Name dump).cars | Where-Object { $_.index -eq $loader })[0].subParts)
    @($Keys | Where-Object { $k = $_; @($parts | Where-Object { $_.key -eq $k -and -not $_.unmounted }).Count -eq 1 }).Count -eq $Keys.Count
}

function Check-Counters([string]$What, [string]$Expected = "") {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Seconds 1
        $ca = Counters $a
        $cb = Counters $b
        if ($ca -eq $cb -and (-not $Expected -or $ca -eq $Expected)) { break }
    } while ((Get-Date) -lt $deadline)
    Write-Host "  A [$ca]"
    if ($cb -ne $ca) { Write-Host "  B [$cb]" }
    Check ($ca -eq $cb) "$What`: A and B have the same blocked counters"
    if ($Expected) { Check ($ca -eq $Expected) "$What`: the counters are back at the spawn's" }
}

function Items([string]$Name) { @((Cmd $Name dump).inventory.items) }

function Part-Ids([string[]]$Keys) {
    $parts = @(@((Cmd $a dump).cars | Where-Object { $_.index -eq $loader })[0].subParts)
    $ids = @{}
    foreach ($k in $Keys) { $ids[$k] = @($parts | Where-Object { $_.key -eq $k })[0].id }
    $ids
}

# The player's way: each part comes back with its own item (the fan's blades are separate items), through the game's
# FastMount with the item selected.
function Round-Trip([string]$Actor, [string]$What, [string]$Baseline, [string]$Verb) {
    $before = @(Items $Actor | ForEach-Object { "$($_.UID)" })
    $off = Cmd $Actor part-fast-unmount "$loader $fan"
    $members = @($off.members)
    Check ($members.Count -gt 0) "$What`: the fan comes off with its blades ($($members -join ', '))"
    Check-Counters "$What`: fan off"
    $ids = Part-Ids (@($fan) + $members)
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 500
        $new = @(Items $Actor | Where-Object { $before -notcontains "$($_.UID)" })
    } while ($new.Count -lt $ids.Count -and (Get-Date) -lt $deadline)
    Check ($new.Count -eq $ids.Count) "$What`: the fan and its blades are in the inventory ($(($new | ForEach-Object { "$($_.ID) $($_.UID)" }) -join ', '))"
    $uids = @($new | ForEach-Object { "$($_.UID)" })
    foreach ($key in @($fan) + $members) {
        $item = @($new | Where-Object { $_.ID -eq $ids[$key] })[0]
        if (-not $item) { Check $false "$What`: an item for $key ($($ids[$key]))"; continue }
        $new = @($new | Where-Object { $_.UID -ne $item.UID })
        Cmd $Actor $Verb "$loader $key $($item.UID)" | Out-Null
        Start-Sleep -Seconds 2
        if ($key -eq $fan) { Check-Counters "$What`: fan on, blades still off" }
    }
    $deadline = (Get-Date).AddSeconds(20)
    while (-not ((Mounted $a (@($fan) + $members)) -and (Mounted $b (@($fan) + $members))) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    Check ((Mounted $a (@($fan) + $members)) -and (Mounted $b (@($fan) + $members))) "$What`: the fan and its blades are on again on A and B"
    Check-Counters "$What`: fan and blades on again" $Baseline
    foreach ($name in $Ctx.Instances) {
        $left = @(Items $name | Where-Object { $uids -contains "$($_.UID)" } | ForEach-Object { "$($_.ID) $($_.UID)" })
        Check ($left.Count -eq 0) "$What`: the mounted items left $name's inventory ($($left -join ', '))"
    }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null; Wait-Ready $b | Out-Null
Start-Sleep -Seconds 3
$parts = @(@((Cmd $a dump).cars | Where-Object { $_.index -eq $loader })[0].subParts)
Write-Host "  fan and radiator parts: $(($parts | Where-Object { $_.id -match 'wentylator|chlodnica' } | ForEach-Object { "$($_.key) $($_.id) u=$($_.unmounted) b=$($_.blocked)" }) -join '; ')"
$fan = @($parts | Where-Object { $_.id -match '^wentylatorChlodnicy_\d+$' -and -not $_.unmounted })[0].key
Check ([bool]$fan) "the car has a radiator fan ($fan)"
if (-not $fan) { $Ctx.Result.notes += $failures; $Ctx.Result.passed = $false; return }
$blocked = @((Cmd $a part-blocks "$loader").blocks.PSObject.Properties | Where-Object { $_.Name -eq $fan } | ForEach-Object { $_.Value })
Write-Host "  $fan blocks [$($blocked -join ', ')]"
Check ($blocked.Count -gt 0) "$fan blocks other parts"
$baseline = Counters $a
Check-Counters "after the spawn"

Round-Trip $a "A (part-domount)" $baseline part-domount
Round-Trip $b "B (part-domount)" $baseline part-domount
Round-Trip $a "A (part-fast-mount)" $baseline part-fast-mount

Start-Sleep -Seconds 22
foreach ($name in $Ctx.Instances) {
    $drifts = (Cmd $name part-blocks "$loader").drifts
    Check ($drifts -eq 0) "$name's blocked-counter check reports no drift ($drifts)"
}

Write-Host "--- the soak's item-less mount (no player action)"
Cmd $a part-fast-unmount "$loader $fan" | Out-Null
Start-Sleep -Seconds 3
Cmd $a part-fast-mount "$loader $fan" | Out-Null
Start-Sleep -Seconds 4
Write-Host "  A [$(Counters $a)]"
Write-Host "  B [$(Counters $b)]"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: tools, parts, economy
# Race and drift audit gap 9, I2 and I5 (state-merges-and-contention D9): one item, two uses at once, in both server
# orders. A wheel group put on the tire changer by A while B mounts it on the car; a brake disc put on the lathe by A
# while B sells it or scraps it; a part mounted by A while B sells its item; one item moved to the warehouse by both.
# The item must end up in exactly one place on every side; a refused machine put tells the player "<name> used this
# part." and does not give the item back when another player used it.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1") -Force

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }

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

function Wait-Same([string]$What) {
    try {
        Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("inventory", "tools", "stats") -TimeoutSec 20 | Out-Null
        Check $true "$What`: A and B have equal inventories, machines and money"
    } catch { Check $false "$What`: $($_.Exception.Message)" }
}

function Where-Item([string]$What, $Uid) {
    $wa = Cmd $a item-where "$Uid"; $wb = Cmd $b item-where "$Uid"
    Check ($wa.where -eq $wb.where) "$What`: A and B agree where $Uid is ($($wa.where) / $($wb.where))"
    Check ($wa.count -le 1) "$What`: $Uid is in at most one place ($($wa.where))"
    $wa.where
}

function Toasts([string]$Name) { @((Dump $Name).session.toasts) }

# Both members' packets are held; the first is released, then the second 2 s later, so the server takes them in that order.
function Race([string]$First, [scriptblock]$FirstAction, [string]$Second, [scriptblock]$SecondAction) {
    foreach ($name in $a, $b) { Cmd $name net-hold "out" | Out-Null }
    & $FirstAction
    & $SecondAction
    Start-Sleep -Seconds 2
    Cmd $First net-hold "off" | Out-Null
    Start-Sleep -Seconds 2
    Cmd $Second net-hold "off" | Out-Null
    Start-Sleep -Seconds 4
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$nameB = (Dump $a).roster."$((Get-HarnessStatus -Instance $b).playerId)".name

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2
$rims = @(Cmd $b wheel-parts "$loader" | Where-Object { $_.id -like "rim*" -and -not $_.unmounted } | ForEach-Object { $_.key })

# A wheel group: A puts it on the tire changer, B mounts it on the car.
foreach ($order in @("A", "B")) {
    $rim = $rims[$(if ($order -eq "A") { 0 } else { 1 })]
    Cmd $b part-fast-unmount "$loader $rim" | Out-Null
    Start-Sleep -Seconds 2
    $group = (Cmd $a give-group "wheel").UID
    Wait-Same "wheel $order first: setup"
    $toasts = (Toasts $a).Count
    $put = { Cmd $a tool-put "TireChanger $group" | Out-Null }
    $mount = { Cmd $b wheel-mount "$loader $rim $group" | Out-Null }
    if ($order -eq "A") { Race $a $put $b $mount } else { Race $b $mount $a $put }
    Wait-Same "wheel, $order first"
    $where = Where-Item "wheel, $order first" $group
    $mounted = -not (@(Cmd $a wheel-parts "$loader" | Where-Object { $_.key -eq $rim })[0].unmounted)
    $mountedB = -not (@(Cmd $b wheel-parts "$loader" | Where-Object { $_.key -eq $rim })[0].unmounted)
    Check ($mounted -eq $mountedB) "wheel, $order first: A and B agree whether the wheel is on the car ($mounted / $mountedB)"
    Check (($where -eq "machine:TireChanger") -xor $mounted) "wheel, $order first: the group is on the changer or on the car, not both ($where, mounted $mounted)"
    if ($order -eq "B") { Check (@(Toasts $a | Select-Object -Skip $toasts | Where-Object { $_ -eq "$nameB used this part." }).Count -ge 1) "wheel, B first: A was told '$nameB used this part.'" }
    if ($where -eq "machine:TireChanger") { Cmd $a tool-take "TireChanger" | Out-Null; Start-Sleep -Seconds 2 }
    $d = Dump $a
    Check (@($d.inventory.groups | Where-Object { $_.UID -eq $group }).Count -le 1) "wheel, $order first: the group exists at most once after the take"
    Wait-Same "wheel, $order first, after the take"
}

# A brake disc on the lathe against a sale and against scrap, in both orders.
foreach ($case in @(@("sale", "A"), @("sale", "B"), @("scrap", "B"))) {
    $kind, $order = $case
    $disc = (Cmd $a give-item "tarczaHamulcowa_1 0.4").UID
    Wait-Same "disc $kind, $order first: setup"
    $money = (Dump $a).stats
    $toasts = (Toasts $a).Count
    $put = { Cmd $a tool-put "BrakeLathe $disc" | Out-Null }
    $use = if ($kind -eq "sale") { { Cmd $b sell-item "$disc" | Out-Null } } else { { Cmd $b econ-scrap "$disc 0" | Out-Null } }
    if ($order -eq "A") { Race $a $put $b $use } else { Race $b $use $a $put }
    Wait-Same "disc $kind, $order first"
    $where = Where-Item "disc $kind, $order first" $disc
    $after = (Dump $a).stats
    $paid = $after.money -ne $money.money -or $after.scrap -ne $money.scrap
    Check (($where -eq "machine:BrakeLathe") -xor $paid) "disc $kind, $order first: on the lathe or $(if ($kind -eq 'sale') { 'sold' } else { 'scrapped' }), never both ($where; money $($money.money) -> $($after.money), scrap $($money.scrap) -> $($after.scrap))"
    if ($order -eq "B") { Check (@(Toasts $a | Select-Object -Skip $toasts | Where-Object { $_ -eq "$nameB used this part." }).Count -ge 1) "disc $kind, B first: A was told '$nameB used this part.'" }
    if ($where -eq "machine:BrakeLathe") { Cmd $a tool-take "BrakeLathe" | Out-Null; Start-Sleep -Seconds 2; Wait-Same "disc $kind, after the take" }
}

# Mount against sale [I5]: A mounts a part with an item while B sells that item.
$free = @(Cmd $a vfx-parts "$loader all" | ForEach-Object { $_.key })
foreach ($order in @("A", "B")) {
    $key = $free[$(if ($order -eq "A") { 0 } else { 1 })]
    $before = @((Dump $a).inventory.items | ForEach-Object { "$($_.UID)" })
    Cmd $a part-fast-unmount "$loader $key" | Out-Null
    Start-Sleep -Seconds 3
    $item = @((Dump $a).inventory.items | Where-Object { $before -notcontains "$($_.UID)" })[0]
    Wait-Same "mount against sale, $order first: setup"
    $money = (Dump $a).stats.money
    $mount = { Cmd $a part-fast-mount "$loader $key $($item.UID)" | Out-Null }
    $sell = { Cmd $b sell-item "$($item.UID)" | Out-Null }
    if ($order -eq "A") { Race $a $mount $b $sell } else { Race $b $sell $a $mount }
    Wait-Same "mount against sale, $order first"
    $mounted = -not (@(@((Dump $a).cars)[0].subParts | Where-Object { $_.key -eq $key })[0].unmounted)
    $mountedB = -not (@(@((Dump $b).cars)[0].subParts | Where-Object { $_.key -eq $key })[0].unmounted)
    Check ($mounted -eq $mountedB) "mount against sale, $order first: A and B agree whether $key is on ($mounted / $mountedB)"
    $sold = (Dump $a).stats.money -ne $money
    $where = Where-Item "mount against sale, $order first" $item.UID
    Check ($mounted -xor $sold) "mount against sale, $order first: mounted or sold, never both (mounted $mounted, sold $sold, item $where)"
    Check ($where -eq "none") "mount against sale, $order first: the item is in no inventory ($where)"
}

# Both move one item to the warehouse [I2].
$box = (Cmd $a give-item "tarczaHamulcowa_1 0.6").UID
Wait-Same "warehouse: setup"
Race $a { Cmd $a warehouse-move "$box to" | Out-Null } $b { Cmd $b warehouse-move "$box to" | Out-Null }
Wait-Same "warehouse move by both"
Check ((Where-Item "warehouse move by both" $box) -eq "warehouse") "warehouse move by both: the item is in the warehouse once"

# Row 18's item lock (task 8.3): B holds a mount lock with a wheel group, A's put of that group is refused and the
# server puts it back into the inventory.
$rim = $rims[2]
$before = @((Dump $b).inventory.groups | ForEach-Object { "$($_.UID)" })
Cmd $b part-fast-unmount "$loader $rim" | Out-Null
Start-Sleep -Seconds 3
$group = @((Dump $b).inventory.groups | Where-Object { $before -notcontains "$($_.UID)" })[0].UID
Wait-Same "item lock: setup"
$answer = Request-Lock $b "$loader mount $rim items $group"
Check ($answer.result -eq "granted") "item lock: B holds $rim with group $group ($($answer.result))"
Start-Sleep -Seconds 1
$toasts = (Toasts $a).Count
Cmd $a tool-put "TireChanger $group" | Out-Null
Start-Sleep -Seconds 4
Wait-Same "item lock: A's put refused"
Check ((Where-Item "item lock" $group) -eq "inventory") "item lock: the group is back in the inventory, not on the changer"
Check (@(Toasts $a | Select-Object -Skip $toasts | Where-Object { $_ -eq "$nameB is mounting this part." }).Count -ge 1) "item lock: A was told '$nameB is mounting this part.'"
Cmd $b lock-release "$loader" | Out-Null

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

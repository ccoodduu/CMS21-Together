# areas: economy, jobs, placement, parts
# state-merges-and-contention D16 (no silent drops): one step per refused, ignored or overridden action that now gets
# an answer. Each step makes one client lose (a race with net-hold, or a raw packet the server must refuse) and checks
# that client against the server right after the answer, without a resync: a forced desync check for the keys that
# have a digest (inventory, world, car-placement), the server's own output and the other client for skills and jobs.
# The server's own desync rounds are slowed down (600 s, autofix off) so that only the answer can repair the loser.
# Steps: an update of an item another player mounted; an update and a repair of an item the server no longer has; an
# add of a stored item with other values; a repair of an item another player mounted; a skill both unlock; a car both
# park; a car both delete; an order from a client that is not the generator; an accept of an order that just expired;
# a job both end; a part transaction dropped with its car (I7).
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")

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

function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }
function Hold([string]$Name, [string]$Mode) { Cmd $Name net-hold $Mode | Out-Null }
function HoldBoth([string]$Mode) { foreach ($name in $Ctx.Instances) { Hold $name $Mode } }
function ServerLog([string]$Pattern, [int]$After, [int]$TimeoutSec = 10) { try { Wait-ServerLog -Pattern $Pattern -After $After -TimeoutSec $TimeoutSec } catch { $null } }
function ServerLines([int]$After, [string]$Pattern) { @(Get-ServerLogLines | Select-Object -Skip $After | Where-Object { $_ -match $Pattern }) }
function Item([string]$Name, [string]$Uid) { @((Dump $Name).inventory.items | Where-Object { "$($_.UID)" -eq $Uid }) }

# A forced desync round; "not ready" is asked again once.
function Assert-ServerMatch([string]$Key, [string]$What) {
    foreach ($attempt in 1, 2) {
        $mark = Get-ServerLogMark
        Send-ServerCommand "desync check"
        $answers = @{}
        foreach ($name in $Ctx.Instances) {
            $line = ServerLog "\[Desync\] $([regex]::Escape($Key)) for client $($ids[$name]): (match|mismatch|not ready)" $mark 10
            $answers[$name] = if ($line -match ": (match|mismatch|not ready)") { $Matches[1] } else { "no answer" }
        }
        if (@($answers.Values | Where-Object { $_ -eq "not ready" }).Count -eq 0) { break }
        Start-Sleep -Seconds 1
    }
    foreach ($name in $Ctx.Instances) { Check ($answers[$name] -eq "match") "$What`: $name's $Key equals the server's ($($answers[$name]))" }
}

function Wait-Same([string]$Section, [string]$What, [scriptblock]$Condition = { $true }, [int]$TimeoutSec = 10) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $da = Dump $a
        $db = Dump $b
        $ja = $da.$Section | ConvertTo-Json -Depth 6 -Compress
        $jb = $db.$Section | ConvertTo-Json -Depth 6 -Compress
        if ($ja -eq $jb -and (& $Condition $da)) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Check ($ja -eq $jb) "$What`: A's and B's $Section agree ($ja)"
    if ($ja -ne $jb) { Write-Host "  B: $jb" }
    Check ([bool](& $Condition $da)) "$What`: expected $Section"
    return $da
}

# The loader is gone on both and both parkings hold the same cars (other loaders' local place names are not compared).
function Wait-Placement([string]$What, [int]$Loader, [int]$Slots) {
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $pa = (Dump $a).placement
        $pb = (Dump $b).placement
        $gone = @(@($pa.cars) + @($pb.cars) | Where-Object { $_.loader -eq $Loader }).Count -eq 0
        $ja = $pa.parking | ConvertTo-Json -Depth 5 -Compress
        $jb = $pb.parking | ConvertTo-Json -Depth 5 -Compress
        if ($gone -and $ja -eq $jb -and @($pa.parking.slots).Count -eq $Slots) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Check $gone "$What`: loader $Loader is empty on A and B"
    Check ($ja -eq $jb -and @($pa.parking.slots).Count -eq $Slots) "$What`: A's and B's parking agree with $Slots car(s) ($ja / $jb)"
}

function Wait-Toast([string]$Name, [string]$Text, [int]$Before) {
    try {
        Wait-HarnessDump -Instance $Name -TimeoutSec 10 -What "toast '$Text'" -Condition { param($d) @($d.session.toasts | Select-Object -Skip $Before | Where-Object { $_ -eq $Text }).Count -ge 1 } | Out-Null
        Check $true "$Name was told '$Text'"
    } catch { Check $false "$Name was told '$Text' ($(@((Dump $Name).session.toasts) -join ' | '))" }
}

function New-Uid($Before, $After, [string]$Id) {
    $old = @($Before.inventory.items | ForEach-Object { "$($_.UID)" })
    @($After.inventory.items | Where-Object { $_.ID -eq $Id -and $old -notcontains "$($_.UID)" } | ForEach-Object { "$($_.UID)" }) | Select-Object -First 1
}

function Give([string]$Id, [string]$Condition) {
    $uid = "$((Cmd $a give-item "$Id $Condition").UID)"
    foreach ($name in $Ctx.Instances) {
        Wait-HarnessDump -Instance $name -TimeoutSec 10 -What "item $uid" -Condition { param($d) @($d.inventory.items | Where-Object { "$($_.UID)" -eq $uid }).Count -eq 1 } | Out-Null
    }
    return $uid
}

# Unmounts a part on B and returns its key and the new item's UID (on both clients).
function Unmount-OnB([int]$Loader) {
    $before = Dump $b
    $part = Cmd $b part-fast-unmount "$Loader"
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 500
        $uid = New-Uid $before (Dump $b) $part.id
    } while (-not $uid -and (Get-Date) -lt $deadline)
    if (-not $uid) { throw "B's unmount of $($part.key) added no $($part.id) to the inventory" }
    Wait-HarnessDump -Instance $a -TimeoutSec 15 -What "A has item $uid" -Condition { param($d) @($d.inventory.items | Where-Object { "$($_.UID)" -eq $uid }).Count -eq 1 } | Out-Null
    Start-Sleep -Seconds 1
    return @{ Key = $part.key; Id = $part.id; Uid = $uid }
}

Set-ServerConfigValues $Ctx.ServerDir @{ desync_check_interval_seconds = 600; desync_autofix = $false }
Restart-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Cmd $a set-name "Ann" | Out-Null
Cmd $b set-name "Bob" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Cmd $name guard-set "Off" | Out-Null
    Cmd $name orders-autogen "off" | Out-Null
}
$ids = @{}
foreach ($name in $Ctx.Instances) { $ids[$name] = [int](Get-HarnessStatus -Instance $name).playerId }
Send-ServerCommand "money set 500000"
Start-Sleep -Seconds 1

Cmd $a car-spawn "0 $car 0" | Out-Null
Wait-Ready $a 0 | Out-Null; Wait-Ready $b 0 | Out-Null
Start-Sleep -Seconds 2

# I3/M8: A updates an item that B mounted meanwhile (A does not see the mount yet).
$part = Unmount-OnB 0
$mark = Get-ServerLogMark
Hold $a "on"
Cmd $b part-fast-mount "0 $($part.Key) $($part.Uid)" | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 10 -What "B mounted $($part.Uid)" -Condition { param($d) @($d.inventory.items | Where-Object { "$($_.UID)" -eq $part.Uid }).Count -eq 0 } | Out-Null
Start-Sleep -Seconds 1
Cmd $a inv-send "update $($part.Uid) 0.25" | Out-Null
Check ([bool](ServerLog "Update of item $($part.Uid) from client $($ids[$a]) ignored.*answering with its Remove" $mark)) "the server answered A's update of the mounted item"
Hold $a "off"
Start-Sleep -Seconds 1
Check (@(Item $a $part.Uid).Count -eq 0) "A has no copy of the item B mounted"
Assert-ServerMatch "inventory" "update of a mounted item"

# I3: an update of an item the server no longer has, with no other packet that would remove it on A.
$uid = Give "tarczaHamulcowa_1" "0.4"
Cmd $a inv-send "remove $uid" | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 10 -What "B lost $uid" -Condition { param($d) @($d.inventory.items | Where-Object { "$($_.UID)" -eq $uid }).Count -eq 0 } | Out-Null
Check (@(Item $a $uid).Count -eq 1) "A still has the item the server removed (the setup of the next step)"
$mark = Get-ServerLogMark
Cmd $a inv-send "update $uid 0.6" | Out-Null
Check ([bool](ServerLog "Update of item $uid from client $($ids[$a]) ignored" $mark)) "the server ignored A's update"
Start-Sleep -Seconds 1
Check (@(Item $a $uid).Count -eq 0) "A dropped the item after the server's answer"
Assert-ServerMatch "inventory" "update of a removed item"

# I3: an add of an item the server has, with another condition: A ends on the server's copy.
$uid = Give "tarczaHamulcowa_1" "0.4"
Cmd $a inv-send "add $uid 0.3" | Out-Null
Start-Sleep -Seconds 1.5
$mine = @(Item $a $uid)
Check ($mine.Count -eq 1 -and [math]::Abs($mine[0].condition - 0.4) -lt 0.001) "A's copy has the server's condition 0.4 again ($($mine | ConvertTo-Json -Compress))"
Assert-ServerMatch "inventory" "add of a stored item"

# M8: A repairs an item B mounted meanwhile.
$part = Unmount-OnB 0
$mark = Get-ServerLogMark
Hold $a "on"
Cmd $b part-fast-mount "0 $($part.Key) $($part.Uid)" | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 10 -What "B mounted $($part.Uid)" -Condition { param($d) @($d.inventory.items | Where-Object { "$($_.UID)" -eq $part.Uid }).Count -eq 0 } | Out-Null
Start-Sleep -Seconds 1
Cmd $a tool-repair "$($part.Uid) success paid" | Out-Null
Check ([bool](ServerLog "client $($ids[$a]) PartRepair\(\d+\) refused Invalid|Update of item $($part.Uid) from client $($ids[$a]) ignored.*answering with its Remove" $mark)) "the server refused or answered A's repair of the mounted item"
Hold $a "off"
Start-Sleep -Seconds 1
Check (@(Item $a $part.Uid).Count -eq 0) "A has no copy of the item B mounted after the refused repair"
Assert-ServerMatch "inventory" "repair of a mounted item"
Assert-ServerMatch "world" "repair of a mounted item"

# M8: a repair of an item the server no longer has, with no other packet that would remove it on A.
$uid = Give "tarczaHamulcowa_1" "0.3"
Cmd $a inv-send "remove $uid" | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 10 -What "B lost $uid" -Condition { param($d) @($d.inventory.items | Where-Object { "$($_.UID)" -eq $uid }).Count -eq 0 } | Out-Null
$mark = Get-ServerLogMark
Cmd $a tool-repair "$uid success paid" | Out-Null
Check ([bool](ServerLog "client $($ids[$a]) PartRepair\(\d+\) refused Invalid" $mark)) "the server refused A's repair"
Start-Sleep -Seconds 1
Check (@(Item $a $uid).Count -eq 0) "A dropped the item after the refused repair"
Assert-ServerMatch "inventory" "repair of a removed item"

# E4: both unlock the same skill.
$mark = Get-ServerLogMark
HoldBoth "on"
Cmd $a econ-skill-unlock "fast_movement 0" | Out-Null
Cmd $b econ-skill-unlock "fast_movement 0" | Out-Null
Start-Sleep -Seconds 1
HoldBoth "off"
Check (@(ServerLines $mark "Points fast_movement level 0 unlocked by client").Count -eq 1) "the skill was unlocked once"
Check ([bool](ServerLog "Points fast_movement level 0 from client \d+ refused: already unlocked; answering" $mark)) "the second unlock was answered"
Wait-Same "skills" "both unlocked the same skill" { param($d) @($d.skills.unlocked) -contains "fast_movement:0" } | Out-Null
Wait-Same "stats" "both unlocked the same skill" | Out-Null
Assert-ServerMatch "world" "both unlocked the same skill"

# C4: both park the same car.
Cmd $a car-spawn "1 $car 0" | Out-Null
Wait-Ready $a 1 | Out-Null; Wait-Ready $b 1 | Out-Null
Start-Sleep -Seconds 2
$mark = Get-ServerLogMark
HoldBoth "on"
Cmd $a park "1" | Out-Null
Cmd $b park "1" | Out-Null
Start-Sleep -Seconds 3
HoldBoth "off"
Check (@(ServerLines $mark "Loader 1 \(.*\) parked in slot").Count -eq 1) "the car was parked once"
Check ([bool](ServerLog "Park of loader 1 from client \d+ refused: Invalid" $mark)) "the second park was refused"
Wait-Placement "both parked the same car" 1 1
Assert-ServerMatch "car-placement" "both parked the same car"

# C4: both delete the same car.
Cmd $a car-spawn "2 $car 0" | Out-Null
Wait-Ready $a 2 | Out-Null; Wait-Ready $b 2 | Out-Null
Start-Sleep -Seconds 2
$mark = Get-ServerLogMark
HoldBoth "on"
Cmd $a car-delete "2" | Out-Null
Cmd $b car-delete "2" | Out-Null
Start-Sleep -Seconds 2
HoldBoth "off"
Check ([bool](ServerLog "Delete of empty loader 2 from client \d+: nothing to delete, not relayed" $mark)) "the second delete was not relayed"
Wait-Placement "both deleted the same car" 2 1
Assert-ServerMatch "car-placement" "both deleted the same car"

# J2: an order from a client that is not the generator.
$gen = if ((Get-HarnessStatus $a).isOrderGenerator) { $a } else { $b }
$other = if ($gen -eq $a) { $b } else { $a }
$mark = Get-ServerLogMark
Cmd $other orders-generate | Out-Null
Check ([bool](ServerLog "Order from client $($ids[$other]) dropped.*answering with the jobs state" $mark)) "the server answered the order from the client that is not the generator"
Start-Sleep -Seconds 1
Wait-Same "jobs" "an order from the client that is not the generator" | Out-Null

# J4: A accepts an order that expired on the server meanwhile.
$before = @((Dump $a).jobs.orders | ForEach-Object { $_.id })
Cmd $gen orders-generate | Out-Null
$jobs = Wait-Same "jobs" "a new order" { param($d) @($d.jobs.orders).Count -eq $before.Count + 1 }
$order = @($jobs.jobs.orders | Where-Object { $before -notcontains $_.id })[0].id
$toasts = @((Dump $a).session.toasts).Count
$mark = Get-ServerLogMark
Hold $a "on"
Send-ServerCommand "jobs expire $order"
Check ([bool](ServerLog "Order $order expired" $mark)) "the server expired order $order"
Cmd $a orders-accept "$order" | Out-Null
Check ([bool](ServerLog "Accept of order $order by client $($ids[$a]) refused: Unknown" $mark)) "the server refused A's accept"
Hold $a "off"
Wait-Toast $a "This order is no longer available." $toasts
Wait-Same "jobs" "an accept of an expired order" { param($d) @($d.jobs.orders | Where-Object { $_.id -eq $order }).Count -eq 0 } | Out-Null

# J5: both end the same job.
Cmd $gen orders-generate | Out-Null
$jobs = Wait-Same "jobs" "an order to take" { param($d) @($d.jobs.orders | Where-Object { -not $_.IsMission }).Count -ge 1 }
$take = @($jobs.jobs.orders | Where-Object { -not $_.IsMission })[-1].id
Cmd $a orders-accept "$take" | Out-Null
$jobs = Wait-Same "jobs" "the job is active for both" { param($d) @($d.jobs.active | Where-Object { $_.id -eq $take }).Count -eq 1 } 90
$loader = @($jobs.jobs.active | Where-Object { $_.id -eq $take })[0].carLoaderID
Wait-Ready $a $loader | Out-Null; Wait-Ready $b $loader | Out-Null
Start-Sleep -Seconds 3
$mark = Get-ServerLogMark
HoldBoth "on"
Cmd $a job-end-direct "$take" | Out-Null
Cmd $b job-end-direct "$take" | Out-Null
Start-Sleep -Seconds 4
HoldBoth "off"
Check (@(ServerLines $mark "Job $take ended by client").Count -eq 1) "the job was paid once"
Check ([bool](ServerLog "End of job $take from client \d+ ignored \(not active\); answering with the world and jobs state" $mark)) "the second end was answered"
Wait-Same "jobs" "both ended the same job" { param($d) @($d.jobs.active | Where-Object { $_.id -eq $take }).Count -eq 0 } 20 | Out-Null
Wait-Same "stats" "both ended the same job" | Out-Null
Assert-ServerMatch "world" "both ended the same job"
Assert-ServerMatch "car-placement" "both ended the same job"

# I7: B's part transaction (its unmount and the item it gave) is dropped with the car A deletes; B rolls it back.
Cmd $a car-spawn "3 $car 0" | Out-Null
Wait-Ready $a 3 | Out-Null; Wait-Ready $b 3 | Out-Null
Start-Sleep -Seconds 3
Hold $b "out"
Cmd $b part-fast-unmount "3" | Out-Null
Start-Sleep -Seconds 2
Cmd $a car-delete "3" | Out-Null
Start-Sleep -Seconds 3
Hold $b "off"
Start-Sleep -Seconds 3
$tx = @((Dump $b).parts.transactions | Where-Object { $_.loader -eq 3 })
Check ($tx.Count -eq 0) "B keeps no part transaction for the deleted car ($($tx | ConvertTo-Json -Compress))"
Assert-ServerMatch "inventory" "a part transaction dropped with its car"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

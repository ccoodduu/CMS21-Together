# areas: locks, parts
# part-locks 5.2: two players start the same part at the same moment, once in parallel jobs and once with both
# clients' incoming packets held (neither mirror knows the other's lock). Exactly one gets the lock and finishes the
# unmount (a real commit, released on commit); the other is denied and its game never starts the action, so no part
# change is rejected and both inventories match. Then both pick the same inventory item for two empty slots of the
# same kind: one mount runs, the other is refused for the item, and the item ends up exactly once.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1")

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
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
        $r = Cmd $Name car-ready "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Wait-Try([string]$Name, [int]$TryId, [scriptblock]$Done, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Cmd $Name lock-try "result $TryId"
        if (& $Done $r) { return $r }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    $r
}

function Settled($r) { $r.result -ne "pending" -and ($r.result -ne "granted" -or -not $r.started -or $r.finished -or $r.state -match "not committed|released|^item (denied|refusedLocally|timeout|no answer|context-lost|dropped)|^no item") }

function Wait-SameCar([string]$What) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $ra = Cmd $a car-ready "$loader"
        $rb = Cmd $b car-ready "$loader"
    } while ($ra.stateHash -ne $rb.stateHash -and (Get-Date) -lt $deadline)
    Check ($ra.stateHash -eq $rb.stateHash) "$What`: A and B have the same part state ($($ra.stateHash) / $($rb.stateHash))"
    $ra
}

function Race([string]$Label, [string]$ArgsA, [string]$ArgsB, [switch]$Held) {
    $mark = Get-ServerLogMark
    if ($Held) { foreach ($name in $Ctx.Instances) { Cmd $name net-hold "on" | Out-Null } }
    $job = Start-Job -ScriptBlock { param($module, $name, $arguments) Import-Module $module; Send-HarnessCommand -Instance $name -Verb lock-try -Arguments $arguments } `
        -ArgumentList (Join-Path $PSScriptRoot "..\HarnessClient.psm1"), $a, $ArgsA
    $tryB = Cmd $b lock-try $ArgsB
    $job | Wait-Job -Timeout 30 | Out-Null
    $tryA = $job | Receive-Job
    $job | Remove-Job -Force
    if ($Held) {
        Start-Sleep -Milliseconds 500
        foreach ($name in $Ctx.Instances) { Cmd $name net-hold "off" | Out-Null }
    }
    $resA = Wait-Try $a $tryA.tryId { param($r) Settled $r }
    $resB = Wait-Try $b $tryB.tryId { param($r) Settled $r }
    Write-Host "  $Label A: $($resA | ConvertTo-Json -Compress)"
    Write-Host "  $Label B: $($resB | ConvertTo-Json -Compress)"
    $winners = @(@($resA, $resB) | Where-Object { $_.result -eq "granted" -and $_.started -and ($_.finished -or $_.state -notmatch "^(no )?item") })
    $losers = @(@($resA, $resB) | Where-Object { $_.result -in @("denied", "refusedLocally") -or $_.state -match "^item (denied|refusedLocally)" -or $_.state -match "^no item" })
    Check ($winners.Count -eq 1 -and $losers.Count -eq 1) "$Label`: one player gets the lock, the other is refused ($($resA.result)/$($resB.result))"
    if ($winners.Count -eq 1) { Check ($winners[0].finished) "$Label`: the winner's work commits ($($winners[0].state))" }
    $idWinner = if ($resA.result -eq "granted") { $idA } else { $idB }
    if ($losers.Count -eq 1 -and $losers[0].result -ne "granted") { Check ($losers[0].holder -eq $idWinner -and -not $losers[0].ran) "$Label`: the loser's refusal names the winner and nothing ran (holder $($losers[0].holder), ran $($losers[0].ran))" }
    Start-Sleep -Seconds 2
    $rejected = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Cars\] Change .* rejected" })
    Check ($rejected.Count -eq 0) "$Label`: no part change was rejected ($($rejected -join ' | '))"
    $locks = Get-ServerLocks
    Check ($locks.Count -eq 0 -and (Get-LockCounter $locks "overlapViolations") -eq 0) "$Label`: no lock is left and no overlap was seen ($($locks.Line))"
    Check ((Get-LockCounter $locks "releasedByCommit") -ge 1) "$Label`: the server released the winner's lock on its commit"
    [pscustomobject]@{ A = $resA; B = $resB }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$idA = (Get-HarnessStatus $a).playerId
$idB = (Get-HarnessStatus $b).playerId

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$candidates = @(Cmd $a vfx-parts "$loader")
$part = @($candidates | Where-Object { $_.id -match "wentylator|pokrywa_glowicy" } | Select-Object -First 1)[0]
if (-not $part) { $part = $candidates[0] }
Write-Host "race part: $($part.key) $($part.id)"

$race1 = Race "same part" "$loader unmount $($part.key) finish" "$loader unmount $($part.key) finish"
Wait-SameCar "after the first race" | Out-Null
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "race1"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "race1"
Check ((Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory")).Count -eq 0) "inventories are equal after the first race"

Cmd $a part-fast-mount "$loader $($part.key)" | Out-Null
Start-Sleep -Seconds 3
Wait-SameCar "after mounting it back" | Out-Null
$race2 = Race "same part, incoming held" "$loader unmount $($part.key) finish" "$loader unmount $($part.key) finish" -Held
Wait-SameCar "after the held race" | Out-Null
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "race2"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "race2"
Check ((Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory")).Count -eq 0) "inventories are equal after the held race"

# Same item into two slots of the same kind.
$twins = @($candidates | Group-Object id | Where-Object { $_.Count -ge 2 } | Select-Object -First 1)[0]
if (-not $twins) { throw "no two parts of the same kind on $car" }
$slot1, $slot2 = $twins.Group[0].key, $twins.Group[1].key
Write-Host "slots: $slot1 $slot2 ($($twins.Name))"
Cmd $a part-fast-unmount "$loader $slot1" | Out-Null
Start-Sleep -Seconds 2
Cmd $a part-fast-unmount "$loader $slot2" | Out-Null
Start-Sleep -Seconds 3
Wait-SameCar "both slots empty" | Out-Null
$items = @((Cmd $a dump).inventory.items | Where-Object { $_.ID -eq $twins.Name } | ForEach-Object { $_.UID })
Check ($items.Count -ge 1) "the unmounted parts are in the inventory ($($items -join ','))"
$uid = $items[0]
$before = @((Cmd $a dump).inventory.items | Where-Object { $_.ID -eq $twins.Name }).Count
$race3 = Race "same item" "$loader mount $slot1 $uid finish" "$loader mount $slot2 $uid finish"
$state = Wait-SameCar "after the item race"
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "race3"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "race3"
Check ((Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory")).Count -eq 0) "inventories are equal after the item race"
$after = @($dumpA.inventory.items | Where-Object { $_.ID -eq $twins.Name }).Count
$stillThere = @($dumpA.inventory.items | Where-Object { $_.UID -eq $uid }).Count
Check ($after -eq $before - 1 -and $stillThere -eq 0) "the item was used exactly once ($before -> $after items of $($twins.Name))"

# A caliper with its piston, built as a group in the item chooser, into two calipers' slots (B1, finding 4). The items
# are locked when they are picked. Both players' incoming packets are held, so both pick before either sees the other;
# one builds and mounts the group, the other's pick is refused (by the server, even once the item has left its
# inventory) or finds the item already gone.
function SubParts([string]$Name) { @((Cmd $Name dump).cars | Where-Object { $_.index -eq $loader } | ForEach-Object { $_.subParts }) }
$calipers = @(SubParts $a | Where-Object { $_.id -eq "zaciskHamulcowy_1" -and -not $_.unmounted } | Select-Object -First 2)
if ($calipers.Count -lt 2) { throw "fewer than two calipers on $car" }
$cal1, $cal2 = $calipers[0].key, $calipers[1].key
Write-Host "calipers: $cal1 $cal2"
Cmd $a part-fast-unmount "$loader $cal1" | Out-Null
Start-Sleep -Seconds 2
Cmd $a part-fast-unmount "$loader $cal2" | Out-Null
Start-Sleep -Seconds 3
Wait-SameCar "both calipers off" | Out-Null
$inventory = (Cmd $a dump).inventory.items
$caliperUid = @($inventory | Where-Object { $_.ID -eq "zaciskHamulcowy_1" } | ForEach-Object { $_.UID })[0]
$pistonUid = @($inventory | Where-Object { $_.ID -eq "zaciskHamulcowy_tloczek_1" } | ForEach-Object { $_.UID })[0]
Check ($caliperUid -and $pistonUid) "a caliper and a piston are in the inventory ($caliperUid, $pistonUid)"
$countBefore = @($inventory | Where-Object { $_.ID -like "zaciskHamulcowy*" }).Count
$race4 = Race "caliper group" "$loader mount $cal1 group $caliperUid $pistonUid finish" "$loader mount $cal2 group $caliperUid $pistonUid finish" -Held
$groupLoser = @(@($race4.A, $race4.B) | Where-Object { $_.state -match "^item (denied|refusedLocally)" })[0]
$groupWinnerId = if ($race4.A.finished) { $idA } else { $idB }
if ($groupLoser) { Check ($groupLoser.holder -eq $groupWinnerId) "caliper group: the refused pick names the winner (holder $($groupLoser.holder))" }
Wait-SameCar "after the caliper group race" | Out-Null
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "race4"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "race4"
Check ((Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory")).Count -eq 0) "inventories are equal after the caliper group race"
$left = @($dumpA.inventory.items | Where-Object { $_.ID -like "zaciskHamulcowy*" })
Check ($left.Count -eq $countBefore - 2 -and -not ($left | Where-Object { $_.UID -in @($caliperUid, $pistonUid) })) "the caliper and the piston were used exactly once ($countBefore -> $($left.Count))"
$mountedParts = SubParts $a | Where-Object { $_.key -in @($cal1, $cal2) }
Check (@($mountedParts | Where-Object { -not $_.unmounted }).Count -eq 1) "exactly one caliper slot is mounted ($(($mountedParts | ForEach-Object { "$($_.key)=$($_.unmounted)" }) -join ', '))"

# Playtest-review P10: the same body panel taken off by both players at once.
$panel = @((Cmd $a dump).cars | Where-Object { $_.index -eq $loader } | ForEach-Object { $_.bodyParts } | Where-Object { -not $_.unmounted -and $_.name -match "door|hood|bonnet|maska|drzwi" } | Select-Object -First 1)[0]
if (-not $panel) { throw "no mounted door or hood on $car" }
$panelIndex = [int]($panel.key -replace '^b:', '')
Write-Host "panel: $($panel.key) $($panel.name)"
$panelItems = @((Cmd $a dump).inventory.items).Count
$race5 = Race "same body panel" "$loader body $panelIndex finish" "$loader body $panelIndex finish"
Wait-SameCar "after the body panel race" | Out-Null
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "race5"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "race5"
Check ((Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory")).Count -eq 0) "inventories are equal after the body panel race"
Check (@($dumpA.inventory.items).Count -eq $panelItems + 1) "the panel reached the inventory once ($panelItems -> $(@($dumpA.inventory.items).Count))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

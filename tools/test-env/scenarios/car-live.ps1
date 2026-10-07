# areas: cars, parts, smoke
# sync-car-parts live changes: both clients have the same car; A unmounts a mechanical part through the game's own
# path (FastUnmount -> Hide: inventory item, XP), B sees the same part state and inventory within a few seconds;
# A mounts it back (FastMount) and both match again.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$TimeoutSec = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready within $TimeoutSec s"
}

function Get-BlockedKeys($Dump) {
    $car = @($Dump.cars | Where-Object { $_.index -eq $loader })[0]
    @($car.subParts | Where-Object { $_.blocked } | ForEach-Object { $_.key } | Sort-Object)
}

function Wait-Same([string]$What, [int]$TimeoutSec = 15) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 500
        $ra = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "$loader"
        $rb = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "$loader"
        if ($ra.stateHash -eq $rb.stateHash) { return @($ra, $rb) }
    } while ((Get-Date) -lt $deadline)
    Send-HarnessCommand -Instance $a -Verb part-state -Arguments "$loader $(Join-Path $Ctx.RunDir "parts_${What}_A.txt")" | Out-Null
    Send-HarnessCommand -Instance $b -Verb part-state -Arguments "$loader $(Join-Path $Ctx.RunDir "parts_${What}_B.txt")" | Out-Null
    return @($ra, $rb)
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "$loader $car 0" | Out-Null
$readyA = Wait-Ready $a
Wait-Ready $b | Out-Null
$same = Wait-Same "spawn"
Check ($same[0].stateHash -eq $same[1].stateHash) "B has A's spawned car in the same state ($($same[0].stateHash) / $($same[1].stateHash))"
$mark = Get-ServerLogMark
Send-ServerCommand "cars"
$carsLine = Wait-ServerLog -Pattern "loader ${loader}: $car SpawnSeq \d+, revision \d+, baseline True" -After $mark -TimeoutSec 10
Check ([bool]$carsLine) "the cars command lists the spawned car ($carsLine)"

$claimKey = "s:2.0"
Send-HarnessCommand -Instance $a -Verb part-claim -Arguments "$loader $claimKey" | Out-Null
Start-Sleep -Seconds 1
$try = Send-HarnessCommand -Instance $b -Verb part-action-unmount -Arguments "$loader $claimKey"
Check ([bool]$try.blocked) "B is blocked from a part A holds (owner $($try.owner))"
Send-HarnessCommand -Instance $a -Verb part-claim -Arguments "$loader $claimKey release" | Out-Null
Start-Sleep -Seconds 1
$try = Send-HarnessCommand -Instance $b -Verb part-action-unmount -Arguments "$loader $claimKey"
Check (-not $try.blocked) "the part is free again after A releases it"

$dumpBeforeUnmount = Send-HarnessCommand -Instance $b -Verb dump
$unmount = Send-HarnessCommand -Instance $a -Verb part-fast-unmount -Arguments "$loader"
Write-Host "A unmounts $($unmount.key) ($($unmount.id))"
$same = Wait-Same "unmount"
Check ($same[0].unmounted -gt $readyA.unmounted) "A's part is unmounted ($($readyA.unmounted) -> $($same[0].unmounted))"
Check ($same[0].stateHash -eq $same[1].stateHash) "B sees A's unmount ($($same[0].stateHash) / $($same[1].stateHash))"
Start-Sleep -Seconds 2
$dumpA = Send-HarnessCommand -Instance $a -Verb dump
$dumpB = Send-HarnessCommand -Instance $b -Verb dump
$diff = Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory", "stats")
Check ($diff.Count -eq 0) "inventory and stats equal after the unmount (differ: $($diff -join ', '))"
$blockedA = Get-BlockedKeys $dumpA
$blockedB = Get-BlockedKeys $dumpB
$blockedBefore = Get-BlockedKeys $dumpBeforeUnmount
Write-Host "blocked parts: $(@($blockedBefore).Count) before the unmount, $(@($blockedA).Count) after"
Check (($blockedA -join ",") -eq ($blockedB -join ",")) "A and B block the same parts after the unmount (A $(@($blockedA).Count), B $(@($blockedB).Count))"
$changeLine = Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-String "\[Cars\] Change .* inventory \+1" | Select-Object -First 1
Check ([bool]$changeLine) "the unmount's inventory item travelled inside the part change ($($changeLine.Line))"

Send-HarnessCommand -Instance $a -Verb part-fast-mount -Arguments "$loader $($unmount.key)" | Out-Null
$same = Wait-Same "mount"
Check ($same[0].unmounted -eq $readyA.unmounted) "A's part is mounted again ($($same[0].unmounted))"
Check ($same[0].stateHash -eq $same[1].stateHash) "B sees A's mount ($($same[0].stateHash) / $($same[1].stateHash))"

# Soak 2026-10-07 (cars:1 desync): A unmounts a part with unmountWith members (the members come off with it), B
# mounts only that part, as a player does with a single new part. The members must stay unmounted on A too; the
# remote apply used to mount them along with it.
$beforeGroup = $same[0].unmounted
$group = Send-HarnessCommand -Instance $a -Verb part-fast-unmount -Arguments "$loader group"
$members = @($group.members)
Write-Host "A unmounts $($group.key) ($($group.id)) with members $($members -join ', ')"
$same = Wait-Same "group-unmount"
Check ($same[0].unmounted -eq $beforeGroup + 1 + $members.Count) "A's part and its $($members.Count) members are unmounted ($beforeGroup -> $($same[0].unmounted))"
Check ($same[0].stateHash -eq $same[1].stateHash) "B sees the group unmount ($($same[0].stateHash) / $($same[1].stateHash))"
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb part-fast-mount -Arguments "$loader $($group.key)" | Out-Null
# Read A as soon as the mount arrives: the server's desync repair would otherwise resend the members a few seconds later.
$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Milliseconds 250
    $ra = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "$loader"
} while ($ra.unmounted -eq $beforeGroup + 1 + $members.Count -and (Get-Date) -lt $deadline)
Check ($ra.unmounted -eq $beforeGroup + $members.Count) "A applies B's mount of the part alone ($($beforeGroup + 1 + $members.Count) -> $($ra.unmounted), expected $($beforeGroup + $members.Count))"
$same = Wait-Same "group-leader-mount"
Check ($same[1].unmounted -eq $beforeGroup + $members.Count) "B mounted only the part, its members stay unmounted on B ($($same[1].unmounted))"
Check ($same[0].unmounted -eq $same[1].unmounted -and $same[0].stateHash -eq $same[1].stateHash) "A keeps the members unmounted after B's mount (A $($same[0].unmounted) $($same[0].stateHash) / B $($same[1].unmounted) $($same[1].stateHash))"
foreach ($member in $members) { Send-HarnessCommand -Instance $b -Verb part-fast-mount -Arguments "$loader $member" | Out-Null }
$same = Wait-Same "group-members-mount"
Check ($same[0].unmounted -eq $beforeGroup -and $same[0].stateHash -eq $same[1].stateHash) "B mounts the members and both match ($($same[0].unmounted), $($same[0].stateHash) / $($same[1].stateHash))"
$desync = Get-ServerLogLines | Select-Object -Skip $mark | Select-String "\[Desync\] cars:$loader .* differ"
Check (-not $desync) "the server found no part desync during the group steps ($($desync.Line))"

# D8 queue: A unmounts right after its own baseline, while B is still loading the car; B applies the queued change
# once its snapshot is in. car-hold-snapshot keeps B loading for 8 s; a second load of the same car is too fast otherwise.
Send-HarnessCommand -Instance $a -Verb car-delete -Arguments "$loader" | Out-Null
$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Milliseconds 500
    $gone = @($a, $b | ForEach-Object { (Send-HarnessCommand -Instance $_ -Verb car-ready -Arguments "$loader").loaded }) -notcontains $true
} while (-not $gone -and (Get-Date) -lt $deadline)
Check $gone "the car is gone on both after A deletes it"
$mark = Get-ServerLogMark
Send-ServerCommand "cars"
Check ([bool](Wait-ServerLog -Pattern "no cars" -After $mark -TimeoutSec 10)) "the cars command shows no cars after the delete"
Send-HarnessCommand -Instance $b -Verb car-hold-snapshot -Arguments "8" | Out-Null
Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "$loader $car 0" | Out-Null
$readyA = Wait-Ready $a
$early = Send-HarnessCommand -Instance $a -Verb part-fast-unmount -Arguments "$loader"
$stateB = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "$loader"
Write-Host "A unmounts $($early.key) while B is $($stateB.state) (loaded $($stateB.loaded))"
Check ($stateB.state -ne "Ready") "B is not Ready yet when A unmounts ($($stateB.state)), so the change goes through the queue"
Wait-Ready $b | Out-Null
Send-HarnessCommand -Instance $b -Verb car-hold-snapshot -Arguments "0" | Out-Null
$same = Wait-Same "unmount-while-loading" 20
Check ($same[0].unmounted -eq $readyA.unmounted + 1) "A's early unmount happened ($($readyA.unmounted) -> $($same[0].unmounted))"
Check ($same[0].stateHash -eq $same[1].stateHash) "B ends with A's early unmount after loading ($($same[0].stateHash) / $($same[1].stateHash))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

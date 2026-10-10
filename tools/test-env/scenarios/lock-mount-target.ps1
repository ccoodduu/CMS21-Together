# areas: locks, parts
# Playtest 3 (2026-10-09): A picks an item for an empty slot X; while the item's lock answer is on its way, the mouse
# moves onto another, mounted part Y (lock-try "repoint"). The mount replayed after the grant must go into X: X gets
# the item, Y keeps its identity and condition on A, B and the server. Before the fix the replay mounted into Y (pads
# into the brake disc slot). The same for a body part: the hood's item, with the mouse moved onto the trunk.
param($Ctx)

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

function Wait-Try([string]$Name, [int]$TryId, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Cmd $Name lock-try "result $TryId"
        $settled = $r.result -ne "pending" -and ($r.result -ne "granted" -or -not $r.started -or $r.finished -or $r.state -match "not committed|^item (denied|refusedLocally|timeout|no answer|context-lost|dropped)|^no item")
        if ($settled) { return $r }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    $r
}

function Wait-Settled([string]$What) {
    Start-Sleep -Seconds 3
    try { Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("cars", "inventory") -TimeoutSec 20 | Out-Null }
    catch { Check $false "$What`: A and B agree on cars and inventory ($($_.Exception.Message))" }
}

function Car($Dump) { @($Dump.cars | Where-Object { $_.index -eq $loader })[0] }
function Sub($Dump, [string]$Key) { @((Car $Dump).subParts | Where-Object { $_.key -eq $Key })[0] }
function Body($Dump, [string]$Key) { @((Car $Dump).bodyParts | Where-Object { $_.key -eq $Key })[0] }
function Uids($Dump) { @($Dump.inventory.items | ForEach-Object { [long]$_.UID }) }

function Check-ServerDigests([string]$What) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "desync check"
    try { Wait-ServerLog -Pattern "\[Desync\] cars:$loader for client \d+: (match|mismatch)" -After $mark -TimeoutSec 10 | Out-Null } catch { }
    Start-Sleep -Seconds 2
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] cars:$loader for client" })
    $lines | ForEach-Object { Write-Host "  server: $_" }
    Check ($lines.Count -ge 2 -and -not ($lines -match "mismatch")) "$What`: the server's car digest matches A and B"
}

function Replay-Lines {
    @((Cmd $a lock-trace "report 400").lines | Where-Object { $_ -match "SelectPartToMount" })
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

# Mechanical part.
$candidates = @(Cmd $a vfx-parts "$loader")
$x = @($candidates | Where-Object { $_.id -match "pokrywa_glowicy|wentylator" } | Select-Object -First 1)[0]
if (-not $x) { $x = $candidates[0] }
$group = ($x.key -split '[:.]')[1]
$y = @($candidates | Where-Object { $_.id -ne $x.id -and ($_.key -split '[:.]')[1] -ne $group } | Select-Object -First 1)[0]
Write-Host "slot X $($x.key) ($($x.id)), mounted part Y $($y.key) ($($y.id))"

$before = Cmd $a dump
Cmd $a part-condition "$loader $($x.key) 0.35" | Out-Null
Cmd $a part-fast-unmount "$loader $($x.key)" | Out-Null
Wait-Settled "X unmounted"
$dump = Cmd $a dump
$item = @($dump.inventory.items | Where-Object { $_.ID -eq $x.id -and (Uids $before) -notcontains [long]$_.UID })[0]
Check ([bool]$item -and (Sub $dump $x.key).unmounted) "X came off as item $($item.UID) ($($item.ID), condition $($item.Condition))"
$yBefore = Sub $dump $y.key
Write-Host "  Y before: $($yBefore | ConvertTo-Json -Compress)"

Cmd $a lock-trace "on" | Out-Null
Cmd $a net-delay "500" | Out-Null
$t = Cmd $a lock-try "$loader mount $($x.key) $($item.UID) repoint $($y.key) finish"
$r = Wait-Try $a $t.tryId
Cmd $a net-delay "0" | Out-Null
Write-Host "  lock-try: $($r | ConvertTo-Json -Compress)"
Check ($r.repointed) "the mouse-over moved to Y while the item's lock answer was on its way"
Replay-Lines | ForEach-Object { Write-Host "  trace: $_" }
Cmd $a lock-trace "off" | Out-Null
Wait-Settled "after the mount"

$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "mechanical"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "mechanical"
foreach ($pair in @(@($a, $dumpA), @($b, $dumpB))) {
    $yNow = Sub $pair[1] $y.key
    $xNow = Sub $pair[1] $x.key
    Check ($yNow.id -eq $yBefore.id -and -not $yNow.unmounted -and $yNow.condition -eq $yBefore.condition) "$($pair[0]): Y keeps its identity and condition ($($yNow.id) $($yNow.condition), before $($yBefore.id) $($yBefore.condition))"
    Check (-not $xNow.unmounted -and $xNow.id -eq $x.id -and [math]::Abs($xNow.condition - 0.35) -lt 0.01) "$($pair[0]): X holds the picked item ($($xNow.id), unmounted $($xNow.unmounted), condition $($xNow.condition))"
    Check ((Uids $pair[1]) -notcontains [long]$item.UID) "$($pair[0]): the item left the inventory"
}
Check-ServerDigests "mechanical part"

# Body part.
$hood = "b:0"; $trunk = "b:3"
$dump = Cmd $a dump
Write-Host "body X $hood ($((Body $dump $hood).name)), mounted body part Y $trunk ($((Body $dump $trunk).name))"
Cmd $a part-condition "$loader $hood 0.4" | Out-Null
Wait-Settled "hood condition"
$before = Cmd $a dump
$t = Cmd $a lock-try "$loader body 0 finish"
$r = Wait-Try $a $t.tryId
Wait-Settled "hood off"
$dump = Cmd $a dump
$bodyItem = @($dump.inventory.items | Where-Object { (Uids $before) -notcontains [long]$_.UID })[0]
Check ((Body $dump $hood).unmounted -and [bool]$bodyItem) "the hood came off as item $($bodyItem.UID) ($($bodyItem.ID), condition $($bodyItem.Condition); lock-try $($r.state))"
$trunkBefore = Body $dump $trunk
Write-Host "  Y before: $($trunkBefore | ConvertTo-Json -Compress)"

if ($bodyItem) {
    Cmd $a lock-trace "on" | Out-Null
    Cmd $a net-delay "500" | Out-Null
    $t = Cmd $a lock-try "$loader body-mount 0 $($bodyItem.UID) repoint 3 finish"
    $r = Wait-Try $a $t.tryId
    Cmd $a net-delay "0" | Out-Null
    Write-Host "  lock-try: $($r | ConvertTo-Json -Compress)"
    Check ($r.repointed) "the mouse-over moved to the trunk while the body item's lock answer was on its way"
    Replay-Lines | ForEach-Object { Write-Host "  trace: $_" }
    Cmd $a lock-trace "off" | Out-Null
    Wait-Settled "after the body mount"

    $dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "body"
    $dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "body"
    foreach ($pair in @(@($a, $dumpA), @($b, $dumpB))) {
        $trunkNow = Body $pair[1] $trunk
        $hoodNow = Body $pair[1] $hood
        Check (-not $trunkNow.unmounted -and $trunkNow.condition -eq $trunkBefore.condition -and $trunkNow.tunedId -eq $trunkBefore.tunedId) "$($pair[0]): the trunk is unchanged ($($trunkNow | ConvertTo-Json -Compress))"
        Check (-not $hoodNow.unmounted -and [math]::Abs($hoodNow.condition - 0.4) -lt 0.01) "$($pair[0]): the hood is back with its item ($($hoodNow | ConvertTo-Json -Compress))"
        Check ((Uids $pair[1]) -notcontains [long]$bodyItem.UID) "$($pair[0]): the hood item left the inventory"
    }
    Check-ServerDigests "body part"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

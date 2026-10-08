# areas: parts, cars, visuals
# Race and drift audit gap 3 (state-merges-and-contention D1-D3): an attribute change (an examine, a condition edit)
# carries the field groups its sender changed, and the server checks the sender's view of the part before merging.
# (1) A examines while B's unmount of an examined part has not reached A: the stale record is dropped, the part
# stays off everywhere, B's item exists once. (2) The reverse: B unmounts while A's examine relay is on its way to B:
# B's own unmount is not undone and B shows no ghost. (3) A examines a part B is unscrewing: B's bolts and pause are
# untouched, B's commit still goes through. (4) B's condition edit that has not been sent yet survives A's examine
# relay. (5) A's condition edit of a part B replaced meanwhile (another quality) never lands on the new part.
# (6) Row 18's locks (D3): A's examine of a part B holds a lock on is accepted and the lock stays; A's stale edit of a
# part B unmounted under a lock that is still held (its other key has not flipped) is dropped, never refused as locked.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1") -Force

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
$usedKeys = @()
$tools = [System.Collections.ArrayList]@("TestDrive", "PathTest", "Compression", "Multimeter", "OBD", "CompoundMeter", "TireTreadDepthTester")
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

function Part([string]$Name, [string]$Key) { @(@((Cmd $Name dump).cars) | Where-Object { $_.index -eq $loader })[0].subParts | Where-Object { $_.key -eq $Key } }

function Items([string]$Name, [string]$Id) { @((Cmd $Name dump).inventory.items | Where-Object { $_.ID -eq $Id }).Count }

# One examine tool per step (an examined part is never sent again) and a key it reaches from the step's pool.
function Pick-Examined([string]$What, [string[]]$Pool) {
    foreach ($tool in @($script:tools)) {
        $keys = @($script:toolKeys[$tool] | Where-Object { ($Pool.Count -eq 0 -or $Pool -contains $_) -and $script:usedKeys -notcontains $_ })
        if ($keys.Count -eq 0) { continue }
        $script:tools.Remove($tool)
        $script:usedKeys += $keys[0]
        Write-Host "$What`: $tool examines $($keys[0])"
        return @{ Tool = $tool; Key = $keys[0] }
    }
    throw "no examine tool reaches a part for $What"
}

function Stale([string]$Name) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "cars"
    $line = try { Wait-ServerLog -Pattern "part records: staleMerged \d+, staleDropped \d+" -After $mark -TimeoutSec 5 } catch { return -1 }
    [int]([regex]::Match($line, "staleDropped (\d+)").Groups[1].Value)
}

function Wait-ServerChange([int]$Mark, [string]$What) {
    try { Wait-ServerLog -Pattern "\[Cars\] Change \d+ from client \d+ on loader $loader" -After $Mark -TimeoutSec 15 | Out-Null } catch { Write-Host "no server change for $What" }
}

function Wait-Same([string]$What) {
    try {
        Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("inventory", "cars") -TimeoutSec 10 | Out-Null
        Check $true "$What`: A and B have equal cars and inventories"
    } catch { Check $false "$What`: $($_.Exception.Message)" }
}

function Check-ServerDigest([string]$What) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "desync check"
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 500
        $lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] cars:$loader" })
    } while (@($lines | Where-Object { $_ -match "for client \d+: (match|mismatch)" }).Count -lt 2 -and (Get-Date) -lt $deadline)
    $matchCount = @($lines | Where-Object { $_ -match "for client \d+: match" }).Count
    Check ($matchCount -eq 2 -and -not ($lines -match "mismatch|differ")) "$What`: the server's car digest matches both players ($matchCount matches)"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("inventory", "cars") -TimeoutSec 30 | Out-Null

$toolKeys = @{}
foreach ($tool in $tools) { $toolKeys[$tool] = @((Cmd $a diag-examine "$loader $tool keys").keys) }
$bolted = @(Cmd $a vfx-parts "$loader" | ForEach-Object { $_.key })
$free = @(Cmd $a vfx-parts "$loader all" | ForEach-Object { $_.key })
$p3 = Pick-Examined "step 3" $bolted
$p1 = Pick-Examined "step 1" $free
$p2 = Pick-Examined "step 2" $free
$p4 = Pick-Examined "step 4" @()
$p6 = Pick-Examined "step 6" @()

# (1) A examines with B's unmount still on its way to A.
$k = $p1.Key
$id = (Part $a $k).id
$itemsBefore = Items $a $id
$staleBefore = Stale
Cmd $a net-hold "on" | Out-Null
$mark = Get-ServerLogMark
Cmd $b part-fast-unmount "$loader $k" | Out-Null
Wait-ServerChange $mark "B's unmount"
$mark = Get-ServerLogMark
Cmd $a diag-examine "$loader $($p1.Tool)" | Out-Null
Wait-ServerChange $mark "A's examine"
Cmd $a net-hold "off" | Out-Null
Start-Sleep -Seconds 3
foreach ($name in $a, $b) { Check ((Part $name $k).unmounted) "step 1: $k is off on $name" }
Check ((Stale) -eq $staleBefore + 1) "step 1: the server dropped A's stale record of $k (staleDropped $staleBefore -> $(Stale))"
Wait-Same "step 1"
foreach ($name in $a, $b) { Check ((Items $name $id) -eq $itemsBefore + 1) "step 1: $name has one new $id ($itemsBefore before, $(Items $name $id) now)" }
Check-ServerDigest "step 1"

# (2) B unmounts while A's examine of that part is on its way to B.
$k = $p2.Key
$id = (Part $a $k).id
$itemsBefore = Items $a $id
Cmd $b net-hold "out" | Out-Null
$mark = Get-ServerLogMark
Cmd $a diag-examine "$loader $($p2.Tool)" | Out-Null
Wait-ServerChange $mark "A's examine"
Cmd $b part-fast-unmount "$loader $k" | Out-Null
Start-Sleep -Milliseconds 800
Cmd $b net-hold "off" | Out-Null
Start-Sleep -Seconds 3
foreach ($name in $a, $b) { Check ((Part $name $k).unmounted) "step 2: $k is off on $name" }
Check ((Part $b $k).examined) "step 2: B has A's examine of $k"
Wait-Same "step 2"
foreach ($name in $a, $b) { Check ((Items $name $id) -eq $itemsBefore + 1) "step 2: $name has one new $id" }
$visuals = (Cmd $b dump).visuals
Check (@($visuals.ghostsActive).Count -eq 0 -and $visuals.renderersHidden -eq 0) "step 2: B shows no ghost and hides no renderer ($(@($visuals.ghostsActive).Count) ghosts, $($visuals.renderersHidden) hidden)"
$probe = Cmd $b vfx-probe "$loader $k"
Check (@($probe.renderers | Where-Object { $_.forceOff }).Count -eq 0) "step 2: none of B's renderers of $k is forced off"

# (3) A examines a part B is unscrewing.
$k = $p3.Key
$id = (Part $a $k).id
$itemsBefore = Items $a $id
Cmd $b lock-take "$loader unmount $k" | Out-Null
Start-Sleep -Seconds 1
Cmd $b vfx-unscrew "$loader $k pause 0.5" | Out-Null
$deadline = (Get-Date).AddSeconds(20)
do { Start-Sleep -Milliseconds 500; $status = Cmd $b vfx-unscrew "$loader $k status" } while ($status.state -ne "paused" -and (Get-Date) -lt $deadline)
Check ($status.state -eq "paused") "step 3: B's unscrew of $k is paused ($($status.state), progress $($status.progress))"
$mark = Get-ServerLogMark
Cmd $a diag-examine "$loader $($p3.Tool)" | Out-Null
Wait-ServerChange $mark "A's examine"
Start-Sleep -Seconds 2
$after = Cmd $b vfx-unscrew "$loader $k status"
Check ($after.state -eq "paused" -and $after.progress -eq $status.progress) "step 3: B's unscrew is untouched by A's examine ($($after.state), progress $($status.progress) -> $($after.progress))"
Check (-not (Part $b $k).unmounted) "step 3: $k is still on B"
Cmd $b vfx-unscrew "$loader $k resume" | Out-Null
$deadline = (Get-Date).AddSeconds(20)
do { Start-Sleep -Milliseconds 500; $status = Cmd $b vfx-unscrew "$loader $k status" } while ($status.state -ne "finished" -and (Get-Date) -lt $deadline)
Start-Sleep -Seconds 3
Cmd $b lock-release "$loader" | Out-Null
foreach ($name in $a, $b) { Check ((Part $name $k).unmounted) "step 3: B's commit took $k off on $name" }
Wait-Same "step 3"
foreach ($name in $a, $b) { Check ((Items $name $id) -eq $itemsBefore + 1) "step 3: $name has one new $id" }

# (4) B's condition edit is not sent yet when A's examine of that part reaches B.
$k = $p4.Key
Cmd $b net-hold "on" | Out-Null
$mark = Get-ServerLogMark
Cmd $a diag-examine "$loader $($p4.Tool)" | Out-Null
Wait-ServerChange $mark "A's examine"
Cmd $b part-condition "$loader $k 0.9" | Out-Null
Cmd $b net-hold "off" | Out-Null
Start-Sleep -Seconds 3
foreach ($name in $a, $b) {
    $part = Part $name $k
    Check ([math]::Abs($part.condition - 0.9) -lt 0.001 -and $part.examined) "step 4: $name has B's condition 0.9 and A's examine of $k (condition $($part.condition), examined $($part.examined))"
}
Wait-Same "step 4"

# (5) A's condition edit of a part that B replaced by one of another quality meanwhile.
$candidates = @(Cmd $a vfx-parts "$loader" | Where-Object { $script:usedKeys -notcontains $_.key })
$k = $candidates[0].key
$script:usedKeys += $k
$old = Part $a $k
$quality = if ($old.quality -eq 3) { 1 } else { 3 }
$staleBefore = Stale
Cmd $a net-hold "out" | Out-Null
Cmd $a part-condition "$loader $k 0.2" | Out-Null
$mark = Get-ServerLogMark
Cmd $b part-fast-unmount "$loader $k" | Out-Null
Wait-ServerChange $mark "B's unmount"
Start-Sleep -Milliseconds 500
$item = Cmd $b give-item "$($old.id) 0.6 $quality"
$mark = Get-ServerLogMark
Cmd $b part-domount "$loader $k $($item.UID)" | Out-Null
Wait-ServerChange $mark "B's mount"
Cmd $a net-hold "off" | Out-Null
Start-Sleep -Seconds 4
foreach ($name in $a, $b) {
    $part = Part $name $k
    Check (-not $part.unmounted -and $part.quality -eq $quality -and [math]::Abs($part.condition - 0.6) -lt 0.001) "step 5: $name has B's new $k (quality $($part.quality), condition $($part.condition); expected $quality, 0.6)"
}
Check ((Stale) -eq $staleBefore + 1) "step 5: the server dropped A's condition edit of the replaced part"
Wait-Same "step 5"
Check-ServerDigest "step 5"

# (6) Locks: an examine during B's lock is accepted; a stale edit in the window before the lock's release is dropped.
$k = $p6.Key
$answer = Request-Lock $b "$loader unmount $k"
Check ($answer.result -eq "granted") "step 6: B holds $k ($($answer.result))"
$mark = Get-ServerLogMark
Cmd $a diag-examine "$loader $($p6.Tool)" | Out-Null
Wait-ServerChange $mark "A's examine"
Start-Sleep -Seconds 2
Check (-not (@(Get-ServerLogLines | Select-Object -Skip $mark) -match "rejected: .*is locked by")) "step 6: A's examine during B's lock is accepted"
foreach ($name in $a, $b) { $part = Part $name $k; Check ($part.examined -and -not $part.unmounted) "step 6: $k is examined and still on on $name" }
Check (@((Get-ServerLocks).Records | Where-Object { $_.X -contains $k }).Count -eq 1) "step 6: B's lock on $k is still held"
Cmd $b lock-release "$loader" | Out-Null
Start-Sleep -Seconds 1

$k7, $k8 = @(Cmd $b vfx-parts "$loader" | Where-Object { $script:usedKeys -notcontains $_.key } | Select-Object -First 2 | ForEach-Object { $_.key })
$answer = Request-Lock $b "$loader unmount $k7 $k8"
Check ($answer.result -eq "granted") "step 6: B holds $k7 and $k8 ($($answer.result))"
$staleBefore = Stale
Cmd $a net-hold "on" | Out-Null
$mark = Get-ServerLogMark
Cmd $b part-fast-unmount "$loader $k7" | Out-Null
Wait-ServerChange $mark "B's unmount under the lock"
$mark = Get-ServerLogMark
Cmd $a part-condition "$loader $k7 0.3" | Out-Null
Wait-ServerChange $mark "A's stale edit"
Cmd $a net-hold "off" | Out-Null
Start-Sleep -Seconds 3
Check (-not (@(Get-ServerLogLines | Select-Object -Skip $mark) -match "rejected: .*is locked by")) "step 6: A's stale edit is not refused as locked"
Check ((Stale) -eq $staleBefore + 1) "step 6: A's stale edit of $k7 is dropped"
Check (@((Get-ServerLocks).Records | Where-Object { $_.X -contains $k8 }).Count -eq 1) "step 6: B's lock is still held"
foreach ($name in $a, $b) { Check ((Part $name $k7).unmounted) "step 6: $k7 is off on $name" }
Cmd $b lock-release "$loader" | Out-Null
Wait-Same "step 6"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

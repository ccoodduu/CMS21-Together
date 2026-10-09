# areas: cars, locks, persistence
# sync-tuning-bonus-and-new-engines group 3 (ROADMAP row 25), guard enforcing. On a Six Once Bulion (bonus slots hood
# and trunk), A fits a spoiler into the trunk slot through the game's SelectPartToMount: B sees it, the item is gone
# from both inventories. A paints the car in the paint shop: B's slot paint equals A's. A removes it through ClickIO:
# B's slot is empty and the item is back once. While A holds the slot's lock, B's fit is refused by name and B keeps
# its item. A and B fit at once: one part on the car, the other player refused and keeping its item. A fits while B
# holds back car details: B's fit is refused as stale ("This slot just changed."), B keeps its item and then shows A's
# part. B's rejoin and a server restart keep the fitted part.
# Old-code failure: with guard-allow for both bonus modes (a no-op on the new code), B's slot stays empty.
param($Ctx, [int]$BoundSec = 5)

$a, $b = $Ctx.Instances
$slot = 1
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Try-Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { "ERROR: $($_.Exception.Message.Split("`n")[0])" } }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "bonus_$Label.json") -Encoding utf8 }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $true }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    return $false
}
function Allow-Bonus { foreach ($name in $Ctx.Instances) { Cmd $name guard-set "enforce" | Out-Null; foreach ($k in "Mode:BonusAssemble", "Mode:BonusDisassemble") { Cmd $name guard-allow $k | Out-Null } } }
function Slot([string]$Name) { @((Cmd $Name bonus-state "0").slots)[$slot] }
function Entry([string]$Name) {
    $car = @((Cmd $Name dump).carDetails) | Where-Object { $_.loader -eq 0 }
    if (-not $car) { return $null }
    $value = $car.entries."x:$slot"
    if ($null -eq $value) { return $null }
    return ($value | ConvertTo-Json -Depth 6 -Compress)
}
function Items([string]$Name) { @((Cmd $Name dump).inventory.items) }
function Has([string]$Name, $Uid) { [bool](Items $Name | Where-Object { "$($_.UID)" -eq "$Uid" }) }
function Wait-Slot([string]$What, [string]$Name, [scriptblock]$Condition, [int]$Seconds = $BoundSec) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $s = Slot $Name
        $ok = & $Condition $s
        if (-not $ok) { Start-Sleep -Milliseconds 250 }
    } while (-not $ok -and $sw.Elapsed.TotalSeconds -lt $Seconds)
    Check $ok "$What ($Name's slot: id $($s.id), unmounted $($s.unmounted), painted $($s.painted), $([int]$sw.Elapsed.TotalMilliseconds) ms)"
    return $s
}
function Same-Entry([string]$What, [int]$Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $ea = Entry $a; $eb = Entry $b
        if ($ea -ne $eb) { Start-Sleep -Milliseconds 300 }
    } while ($ea -ne $eb -and (Get-Date) -lt $deadline)
    Check ($ea -eq $eb -and $ea) "$What`: A and B have the same x:$slot ($ea)"
    if ($ea -ne $eb) { Write-Host "  B: $eb" }
    return $ea
}
function Give([string]$Id) {
    $given = Cmd $a give-item $Id
    $deadline = (Get-Date).AddSeconds(10)
    while (-not (Has $b $given.UID) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    return $given.UID
}
function Bonus-Locks([string]$Name) { @((Cmd $Name dump).locks.mirror | Where-Object { $_.kind -eq "BonusPart" }) }
function Wait-NoBonusLock([string]$Name, [int]$Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while (@(Bonus-Locks $Name).Count -gt 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    return @(Bonus-Locks $Name).Count -eq 0
}
function Empty-Slot($s) { $s.unmounted -and $s.dummy }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
Allow-Bonus
$idA = (Get-HarnessStatus -Instance $a).playerId
$nameA = (Cmd $b dump).roster."$idA".name

Cmd $a car-spawn "0 car_sixoncebulion 0" | Out-Null
Check ((Wait-Ready $a 0) -and (Wait-Ready $b 0)) "the car is ready on A and B"
$base = Slot $b
Check ((Empty-Slot $base) -and $base.uid -eq "bonusPart$slot" -and $base.type -eq "BonusTrunk") "B sees an empty trunk slot bonusPart$slot ($($base.type))"

# Fit.
$uid1 = Give "bonus_trunk_spoiler_1"
Cmd $a bonus-fit "0 $slot $uid1" | Out-Null
Wait-Slot "A's fit is on A's car" $a { param($s) $s.id -eq "bonus_trunk_spoiler_1" -and -not $s.unmounted } | Out-Null
Wait-Slot "A's fit reaches B" $b { param($s) $s.id -eq "bonus_trunk_spoiler_1" -and -not $s.unmounted } | Out-Null
Start-Sleep -Seconds 1
Check (-not (Has $a $uid1) -and -not (Has $b $uid1)) "the fitted spoiler is gone from A's and B's inventory"
Same-Entry "after the fit" | Out-Null
Cmd $a bonus-mode "off" | Out-Null

# Paint shop.
Save "paint" (Try-Cmd $a tool-paint-car "0 0.9,0.1,0.1")
$painted = Wait-Slot "the paint shop paints A's spoiler" $a { param($s) $s.painted -and $s.color -like "0.9,0.1,0.1*" } 10
$entryPaint = Same-Entry "after the paint shop"
Check ($entryPaint -match '"IsPainted":true') "the stored slot is painted ($entryPaint)"
$pb = Slot $b
Check ($pb.painted -and $pb.color -eq $painted.color -and $pb.paintType -eq $painted.paintType) "B's spoiler has A's paint (B $($pb.color) $($pb.paintType), A $($painted.color) $($painted.paintType))"

# Remove.
Cmd $a bonus-remove "0 $slot" | Out-Null
Wait-Slot "A's remove empties A's slot" $a { param($s) Empty-Slot $s } | Out-Null
Wait-Slot "A's remove reaches B" $b { param($s) Empty-Slot $s } | Out-Null
Cmd $a bonus-mode "off" | Out-Null
Start-Sleep -Seconds 1
$backA = @(Items $a | Where-Object { $_.ID -eq "bonus_trunk_spoiler_1" })
$backB = @(Items $b | Where-Object { $_.ID -eq "bonus_trunk_spoiler_1" })
Check ($backA.Count -eq 1 -and $backB.Count -eq 1 -and "$($backA[0].UID)" -eq "$($backB[0].UID)") "the spoiler is back once in the shared inventory (A $($backA.Count), B $($backB.Count))"
Check ($backA.Count -eq 1 -and $backA[0].painted) "the returned spoiler keeps its paint"
Same-Entry "after the remove" | Out-Null

# A holds the slot: B is refused by name and keeps its item.
$uid2 = Give "bonus_trunk_spoiler_2"
$take = Cmd $a lock-take "0 BonusPart x:$slot bare"
$deadline = (Get-Date).AddSeconds(5)
do { Start-Sleep -Milliseconds 200; $answer = Cmd $a lock-take "result $($take.requestId)" } while ($answer.result -eq "pending" -and (Get-Date) -lt $deadline)
Check ($answer.result -eq "granted") "A holds the slot's BonusPart lock ($($answer.result))"
$deadline = (Get-Date).AddSeconds(5)
while (@(Bonus-Locks $b).Count -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
Cmd $b bonus-fit "0 $slot $uid2" | Out-Null
Start-Sleep -Seconds 3
$lastB = (Cmd $b dump).locks.lastMessage
Check ("$lastB" -eq "$nameA is fitting a bonus part here.") "B is told '$nameA is fitting a bonus part here.' ($lastB)"
Check ((Empty-Slot (Slot $b)) -and (Has $b $uid2)) "B's fit did not happen and B keeps its spoiler"
Cmd $b bonus-mode "off" | Out-Null
Cmd $a lock-take "0 BonusPart x:$slot release" | Out-Null
Check (Wait-NoBonusLock $b) "A's slot lock is released"

# Both fit at once.
$uid3 = Give "bonus_trunk_spoiler_3"
$beforeA = (Cmd $a dump).locks.lastMessage; $beforeB = (Cmd $b dump).locks.lastMessage
Cmd $a bonus-fit "0 $slot $uid3" | Out-Null
Cmd $b bonus-fit "0 $slot $uid2" | Out-Null
Start-Sleep -Seconds 4
$sa = Slot $a; $sb = Slot $b
$gone3 = -not (Has $a $uid3); $gone2 = -not (Has $a $uid2)
Save "race_A" $sa; Save "race_B" $sb
Check ($gone2 -ne $gone3) "exactly one of the two spoilers left the inventory (A's $gone3, B's $gone2)"
$winner = if ($gone3) { "bonus_trunk_spoiler_3" } else { "bonus_trunk_spoiler_2" }
Check ($sa.id -eq $winner -and $sb.id -eq $winner) "both show the winner's spoiler $winner (A $($sa.id), B $($sb.id))"
Check (((Has $b $uid2) -eq $gone3) -and ((Has $b $uid3) -eq $gone2)) "B's inventory agrees: the loser keeps its spoiler"
$loser = if ($gone3) { $b } else { $a }
$loserMessage = (Cmd $loser dump).locks.lastMessage
Check ("$loserMessage" -match "is fitting a bonus part here\.$|^This slot just changed\.$") "the loser ($loser) was told why ($loserMessage)"
Same-Entry "after the race" | Out-Null
foreach ($name in $Ctx.Instances) { Cmd $name bonus-mode "off" | Out-Null }
Cmd $a bonus-remove "0 $slot" | Out-Null
Wait-Slot "the race's part is removed on B" $b { param($s) Empty-Slot $s } | Out-Null
Cmd $a bonus-mode "off" | Out-Null
Check (Wait-NoBonusLock $b) "no slot lock is left after the reset"
$spare = if ($gone3) { $uid2 } else { $uid3 }

# Stale: B has not seen A's fit yet.
$uid4 = Give "bonus_trunk_spoiler_4"
Cmd $b net-hold "only CarDetailsUpdate" | Out-Null
Cmd $a bonus-fit "0 $slot $uid4" | Out-Null
Wait-Slot "A fits while B holds back car details" $a { param($s) $s.id -eq "bonus_trunk_spoiler_4" } | Out-Null
Check (Wait-NoBonusLock $b) "A's fit lock is released before B tries"
Check (Empty-Slot (Slot $b)) "B still shows the slot empty"
Cmd $b bonus-fit "0 $slot $spare" | Out-Null
Start-Sleep -Seconds 3
$lastB = (Cmd $b dump).locks.lastMessage
Check ("$lastB" -eq "This slot just changed.") "B is refused as stale ('This slot just changed.', got '$lastB')"
Check ((Empty-Slot (Slot $b)) -and (Has $b $spare)) "B's fit did not happen and B keeps its spoiler"
Cmd $b net-hold "off" | Out-Null
Cmd $b bonus-mode "off" | Out-Null
Wait-Slot "after the held details arrive B shows A's part" $b { param($s) $s.id -eq "bonus_trunk_spoiler_4" } | Out-Null
Check ((Has $a $spare) -and (Has $b $spare) -and (Slot $a).id -eq "bonus_trunk_spoiler_4") "one part on the car and B's spoiler still in the shared inventory"
$fitted = Same-Entry "after the stale refusal"
Cmd $a bonus-mode "off" | Out-Null

# B joins again.
Cmd $b to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 90 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Ready $b 0 | Out-Null
Wait-Slot "after B's rejoin B shows the spoiler" $b { param($s) $s.id -eq "bonus_trunk_spoiler_4" } 15 | Out-Null
Check ((Same-Entry "after B's rejoin") -eq $fitted) "after B's rejoin the slot entry is unchanged"

# Server restart.
$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
Stop-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 30 -What "menu after kill" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
    Cmd $name mp-ui "ok" | Out-Null
}
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
Allow-Bonus
foreach ($name in $Ctx.Instances) { Wait-Ready $name 0 | Out-Null }
Wait-Slot "after the restart A shows the spoiler" $a { param($s) $s.id -eq "bonus_trunk_spoiler_4" } 15 | Out-Null
Wait-Slot "after the restart B shows the spoiler" $b { param($s) $s.id -eq "bonus_trunk_spoiler_4" } 15 | Out-Null
Check ((Same-Entry "after the server restart") -eq $fitted) "after the restart the slot entry is unchanged"
foreach ($name in $Ctx.Instances) { Check ((Cmd $name bonus-state "0").mismatches -eq 0) "$name counted no bonus slot mismatch" }

foreach ($name in $Ctx.Instances) {
    $guardLog = Cmd $name guard-log
    Save "guard_log_$name" $guardLog
    Check (@($guardLog.keys | Where-Object { $_ -match "Bonus" }).Count -eq 0) "the guard blocked nothing on $name's bonus path"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

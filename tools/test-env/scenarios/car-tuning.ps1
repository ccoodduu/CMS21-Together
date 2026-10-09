# areas: cars, locks, persistence
# sync-tuning-bonus-and-new-engines group 2 (ROADMAP row 25), guard enforcing. A puts a Bolt Atlanta with a racing
# gearbox and a racing carburettor (the car has no ECU; both tabs end in PartModule.Tune) on the dyno and opens the
# tune window through the dyno computer's click. B's open on the same car is refused by name and changes nothing. A
# applies gear ratios alone through the gearbox tab's own ApplyAction: B has the same t:gearbox within 2 s, checked
# before any carburettor apply (old code: unchanged, the carburettor apply would carry it along). Then the carburettor
# map, the window's close (B can open it), the idle cap (shortened), the tuned carburettor taken off and fitted to B's
# second car (the tuning stays on the item), B's rejoin and a server restart.
# Old-code failure: with guard-allow Window:Tune (a no-op on the new code), B's t:gearbox is unchanged after 2 s.
param($Ctx, [int]$BoundSec = 2)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Try-Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { "ERROR: $($_.Exception.Message.Split("`n")[0])" } }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "tune_$Label.json") -Encoding utf8 }

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
function Allow-Tune { foreach ($name in $Ctx.Instances) { Cmd $name guard-set "enforce" | Out-Null; Cmd $name guard-allow "Window:Tune" | Out-Null } }
function Tuning([string]$Name, [int]$Loader) {
    $car = @((Cmd $Name dump).carDetails) | Where-Object { $_.loader -eq $Loader }
    $result = [ordered]@{}
    if ($car) { foreach ($p in $car.entries.PSObject.Properties) { if ($p.Name -like "t:*") { $result[$p.Name] = ($p.Value | ConvertTo-Json -Depth 6 -Compress) } } }
    return $result
}
function Entry([string]$Name, [int]$Loader, [string]$Id) { (Tuning $Name $Loader)[$Id] }
function Wait-Entry([string]$What, [int]$Loader, [string]$Id, [string]$Expected, [int]$Seconds = $BoundSec) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $eb = Entry $b $Loader $Id
        if ($eb -ne $Expected) { Start-Sleep -Milliseconds 200 }
    } while ($eb -ne $Expected -and $sw.Elapsed.TotalSeconds -lt $Seconds)
    Check ($eb -eq $Expected) "$What`: B's $Id equals A's within $Seconds s ($([int]$sw.Elapsed.TotalMilliseconds) ms; A $Expected; B $eb)"
}
function Same-Tuning([string]$What, [int]$Loader, [int]$Seconds = 15) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $ta = Tuning $a $Loader; $tb = Tuning $b $Loader
        $ja = $ta | ConvertTo-Json -Compress; $jb = $tb | ConvertTo-Json -Compress
        if ($ja -ne $jb) { Start-Sleep -Milliseconds 500 }
    } while ($ja -ne $jb -and (Get-Date) -lt $deadline)
    Check ($ja -eq $jb -and $ta.Count -gt 0) "$What`: A and B have the same tuning entries on loader $Loader ($($ta.Count) entries)"
    if ($ja -ne $jb) { Write-Host "  A: $ja"; Write-Host "  B: $jb" }
    return $ta
}
function Wait-Window([string]$Name, [bool]$Open, [int]$Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $s = Cmd $Name tune-state
        if ([bool]$s.open -ne $Open) { Start-Sleep -Milliseconds 300 }
    } while ([bool]$s.open -ne $Open -and (Get-Date) -lt $deadline)
    return $s
}
function Tune-Locks([string]$Name) { @((Cmd $Name dump).locks.mirror | Where-Object { $_.kind -eq "Tune" }) }
function Wait-NoTuneLock([string]$Name, [int]$Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while (@(Tune-Locks $Name).Count -gt 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    return @(Tune-Locks $Name).Count -eq 0
}
function Toasts([string]$Name) { @((Cmd $Name dump).session.toasts) }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
Allow-Tune
$idA = (Get-HarnessStatus -Instance $a).playerId
$nameA = (Cmd $b dump).roster."$idA".name
$rule = @(Cmd $a guard-rules) | Where-Object { $_ -match "Window:Tune\b" }
Write-Host "guard rule: $rule"

# Setup: racing gearbox and carburettor, car on the dyno.
Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Check ((Wait-Ready $a 0) -and (Wait-Ready $b 0)) "the car is ready on A and B"
$probe = Cmd $a tune-probe "0"
$gearKey = $probe.gearbox.key
$carbKey = @($probe.modules)[0].part.key
Check ($gearKey -and $carbKey) "the car has a gearbox ($gearKey) and a carburettor ($carbKey)"
Cmd $a tune-part "0 gearbox t_v8_gearbox_stary" | Out-Null
Cmd $a tune-part "0 $carbKey t_v8_gaznik_1" | Out-Null
Cmd $a car-move "0 Dyno" | Out-Null
$deadline = (Get-Date).AddSeconds(30)
do { Start-Sleep -Seconds 1; $pb = Cmd $b tune-probe "0" } while (-not ($pb.place -eq 6 -and $pb.gearbox.tuned -and @($pb.modules)[0].part.tuned) -and (Get-Date) -lt $deadline)
Check ($pb.place -eq 6 -and $pb.gearbox.tuned -and @($pb.modules)[0].part.tuned) "B sees the car on the dyno with the racing gearbox and carburettor"
$baseB = Tuning $b 0
Save "base_B" $baseB

# A opens the window; B is refused by name.
Cmd $a tune-open "0" | Out-Null
$sa = Wait-Window $a $true
Save "open_A" $sa
Check ([bool]$sa.open -and $sa.sameCar -and $sa.gearboxTab.hasGearbox) "A's #dynoTune click opens the tune window on the car with the gearbox tab ready"
$deadline = (Get-Date).AddSeconds(5)
do { Start-Sleep -Milliseconds 300; $locksB = @(Tune-Locks $b) } while ($locksB.Count -eq 0 -and (Get-Date) -lt $deadline)
$lock = $locksB | Select-Object -First 1
Check ($locksB.Count -eq 1 -and $lock.owner -eq $idA -and @($lock.x) -contains "tune" -and @($lock.x) -contains $gearKey -and @($lock.x) -contains $carbKey) "B sees A's Tune lock on tune, the gearbox and the carburettor (owner $($lock.owner), A $idA; $(@($lock.x) -join ','))"
$toastsBefore = @(Toasts $b).Count
Cmd $b tune-open "0" | Out-Null
Start-Sleep -Seconds 3
$sb = Cmd $b tune-state
$lastB = (Cmd $b dump).locks.lastMessage
Check (-not $sb.open -and $sb.mode -ne "UI") "B's click opens no tune window (mode $($sb.mode))"
Check ("$lastB" -eq "$nameA is tuning this car.") "B is told '$nameA is tuning this car.' ($lastB)"
Check (@(Toasts $b | Select-Object -Skip $toastsBefore | Where-Object { $_ -eq "$nameA is tuning this car." }).Count -ge 1) "B shows the refusal as a notice"
Check (((Tuning $b 0) | ConvertTo-Json -Compress) -eq ($baseB | ConvertTo-Json -Compress)) "B's tuning did not change"

# Gearbox alone, before any carburettor apply.
$gear = Cmd $a cardetails-ui "gearbox 0 3.7"
Save "gearbox_A" $gear
$gearA = Entry $a 0 "t:gearbox"
Check ($gear.final -eq 3.7 -and $gearA -match '"FinalDriveRatio":3.7') "A's gearbox tab applied final drive 3.7 ($gearA)"
Wait-Entry "gearbox applied alone" 0 "t:gearbox" $gearA

$carb = Cmd $a tune-ecu "0"
Save "carb_A" $carb
Check ($carb.tab -eq "carb") "A applied the carburettor tab ($($carb.tab))"
$carbA = Entry $a 0 "t:$carbKey"
Check ($carbA -match '"IsTuned":true') "A's carburettor is tuned ($carbA)"
Wait-Entry "carburettor map" 0 "t:$carbKey" $carbA

# Close: the lock ends and B can open the window.
Cmd $a tune-close | Out-Null
Check (-not (Wait-Window $a $false).open) "A's window closes"
Check (Wait-NoTuneLock $b) "A's Tune lock is gone on B after the close"
Cmd $b tune-open "0" | Out-Null
Check ([bool](Wait-Window $b $true).open) "B opens the tune window after A closed it"
Cmd $b tune-close | Out-Null
Check (-not (Wait-Window $b $false).open) "B closes the window"
Check (Wait-NoTuneLock $a) "B's Tune lock is gone on A"

# Idle cap (5 s instead of 5 min).
$idle = Try-Cmd $a tune-idle "5"
Check ("$idle" -notmatch "^ERROR") "A's tune idle cap is set to 5 s ($idle)"
Cmd $a tune-open "0" | Out-Null
Check ([bool](Wait-Window $a $true).open) "A opens the window again"
$closed = Wait-Window $a $false 20
Check (-not $closed.open) "A's window closes by itself after 5 s without an applied change"
Check (Wait-NoTuneLock $b) "A's idle Tune lock is released"
Cmd $a tune-idle "300" | Out-Null
Cmd $b tune-open "0" | Out-Null
Check ([bool](Wait-Window $b $true).open) "B can open the window after A's idle close"
Cmd $b tune-close | Out-Null
Wait-Window $b $false | Out-Null

# The tuned carburettor keeps its tuning on the item and on B's second car.
$carbValues = ($carbA | ConvertFrom-Json).Data
Cmd $a part-fast-unmount "0 $carbKey" | Out-Null
Start-Sleep -Seconds 3
$itemA = @(Cmd $a item-tuning "t_v8_gaznik_1") | Select-Object -First 1
Save "item_A" $itemA
Check ($itemA -and $itemA.tuned -and $itemA.values -eq ($carbValues.Values -join ",")) "A's taken-off carburettor item keeps the map ($($itemA.values))"
$deadline = (Get-Date).AddSeconds(10)
do { Start-Sleep -Milliseconds 500; $itemB = @(Cmd $b item-tuning "$($itemA.uid)") | Select-Object -First 1 } while (-not $itemB -and (Get-Date) -lt $deadline)
Save "item_B" $itemB
Check ($itemB -and $itemB.tuned -and $itemB.values -eq $itemA.values) "B's copy of the item has the same map ($($itemB.values))"
Cmd $b car-spawn "1 car_boltatlanta 0 auto" | Out-Null
Check ((Wait-Ready $b 1) -and (Wait-Ready $a 1)) "B's second car is ready on A and B"
Cmd $b part-fast-unmount "1 $carbKey" | Out-Null
Start-Sleep -Seconds 3
Save "domount_B" (Try-Cmd $b part-domount "1 $carbKey $($itemA.uid)")
$deadline = (Get-Date).AddSeconds(15)
do { Start-Sleep -Milliseconds 500; $carbB1 = Entry $b 1 "t:$carbKey" } while (-not ($carbB1 -match '"IsTuned":true') -and (Get-Date) -lt $deadline)
$fitted = $carbB1 | ConvertFrom-Json
Check ($fitted.Data.IsTuned -and ($fitted.Data.Values -join ",") -eq ($carbValues.Values -join ",")) "the carburettor fitted to B's second car keeps A's map ($carbB1)"
Same-Tuning "after the carburettor moved" 1 | Out-Null

$before0 = Same-Tuning "before the rejoin" 0
$before1 = Tuning $a 1

# B joins again.
Cmd $b to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 90 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Ready $b 0 | Out-Null; Wait-Ready $b 1 | Out-Null
$r0 = Same-Tuning "after B's rejoin" 0
$r1 = Same-Tuning "after B's rejoin" 1
Check ($r0["t:gearbox"] -eq $gearA) "after B's rejoin the gear ratios are A's ($($r0['t:gearbox']))"
Check (($r1 | ConvertTo-Json -Compress) -eq ($before1 | ConvertTo-Json -Compress)) "after B's rejoin the second car's tuning is unchanged"

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
Allow-Tune
foreach ($name in $Ctx.Instances) { Wait-Ready $name 0 | Out-Null; Wait-Ready $name 1 | Out-Null }
$s0 = Same-Tuning "after the server restart" 0
$s1 = Same-Tuning "after the server restart" 1
Check ($s0["t:gearbox"] -eq $gearA) "after the restart the gear ratios are A's ($($s0['t:gearbox']))"
Check ($s1["t:$carbKey"] -eq $carbB1) "after the restart the moved carburettor keeps A's map ($($s1["t:$carbKey"]))"

foreach ($name in $Ctx.Instances) {
    $guardLog = Cmd $name guard-log
    Save "guard_log_$name" $guardLog
    Check (@($guardLog.keys | Where-Object { $_ -match "Window:Tune" }).Count -eq 0) "the guard blocked nothing on $name's tuning path"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

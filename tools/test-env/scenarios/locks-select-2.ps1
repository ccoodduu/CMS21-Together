# areas: locks, parts, guard, placement
# part-locks-2: locks are visible before the click in mount mode, the item chooser and the car pie. Headless games draw
# no pixels, so each step reads the state behind the picture. Mount mode: a free empty slot is shown as a preview (layer
# 16 Part with its collider), a slot in A's lock is hidden (live, and when B enters mount mode) and shown again after
# A's release. Chooser: B's open chooser marks the item in A's lock with the lock overlay and A's name, B's pick of it
# is refused on B's game and the chooser opens again under a new slot lock, and A's release clears the mark. Pie: while
# B holds the car lock, every move option of A's car pie is unavailable (option disabled, element not available) with
# the lock as the reason; after B's release the options are as before. The guard's pie blocks still apply through the
# shared owner of option state. The guard runs on Enforce.
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

function Wait-SameCar([string]$What) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $ra = Cmd $a car-ready "$loader"
        $rb = Cmd $b car-ready "$loader"
    } while ($ra.stateHash -ne $rb.stateHash -and (Get-Date) -lt $deadline)
    Check ($ra.stateHash -eq $rb.stateHash) "$What`: A and B have the same part state"
}

function Wait-Try([string]$Name, [int]$TryId) {
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $r = Cmd $Name lock-try "result $TryId"
        if ($r.result -ne "pending") { return $r }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $r
}

function Wait-Mirror([string]$Name, [int]$Count) {
    Wait-LockMirror $Name { param($l) @($l.mirror).Count -eq $Count } "$Count lock(s)" | Out-Null
    Start-Sleep -Milliseconds 400
}

function Counter([string]$Name, [string]$Counter) { $c = (Get-LockMirror $Name).counters; if ($c.$Counter) { [int]$c.$Counter } else { 0 } }

function Slot($Previews, [string]$Key) { @($Previews | Where-Object { $_.key -eq $Key })[0] }

function Pie([string]$Name) {
    Cmd $Name lock-pie "$loader open" | Out-Null
    Start-Sleep -Seconds 2
    $state = Cmd $Name lock-pie "$loader state"
    Cmd $Name lock-pie "$loader close" | Out-Null
    Start-Sleep -Milliseconds 800
    @($state.options | Where-Object { $_.id -like "move_*" })
}

function Row($Rows, [long]$Uid) { @($Rows.rows | Where-Object { [long]$_.uid -eq $Uid })[0] }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Enforce" | Out-Null }
$idA = (Get-HarnessStatus $a).playerId
$idB = (Get-HarnessStatus $b).playerId

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$candidates = @(Cmd $a vfx-parts "$loader")
$twins = @($candidates | Group-Object id | Where-Object { $_.Count -ge 2 } | Select-Object -First 1)[0]
if (-not $twins) { throw "no two parts of the same kind on $car" }
$slot1, $slot2 = $twins.Group[0].key, $twins.Group[1].key
Write-Host "slots: $slot1 $slot2 ($($twins.Name))"
Cmd $a part-fast-unmount "$loader $slot1" | Out-Null
Start-Sleep -Seconds 2
Cmd $a part-fast-unmount "$loader $slot2" | Out-Null
Start-Sleep -Seconds 3
Wait-SameCar "both slots empty"
$items = @((Cmd $b dump).inventory.items | Where-Object { $_.ID -eq $twins.Name } | ForEach-Object { [long]$_.UID })
Check ($items.Count -ge 2) "both parts are in B's inventory ($($items -join ','))"
$uid1, $uid2 = $items[0], $items[1]

# Mount-mode previews on B.
Cmd $b lock-preview "$loader arm" | Out-Null
Cmd $b lock-probe "mode PartSelectMount $loader" | Out-Null
Start-Sleep -Milliseconds 500
$p = Cmd $b lock-preview "$loader $slot1 $slot2"
Write-Host "  free: $($p | ConvertTo-Json -Compress)"
Check ((Slot $p $slot1).shown -and (Slot $p $slot2).shown) "both free slots are shown as mount previews"

$hidden = Counter $b "blockedAtSelection.preview"
$answer = Request-Lock $a "$loader mount $slot1"
Check ($answer.result -eq "granted") "A holds the slot $slot1"
Wait-Mirror $b 1
$p = Cmd $b lock-preview "$loader $slot1 $slot2"
Write-Host "  A holds $slot1`: $($p | ConvertTo-Json -Compress)"
Check (-not (Slot $p $slot1).shown -and -not (Slot $p $slot1).collider -and (Slot $p $slot1).unmounted) "A's slot is hidden on B at once (layer $((Slot $p $slot1).layer), no collider, still unmounted)"
Check ((Slot $p $slot1).message -match "is working") "B's lock message for the slot names the work ($((Slot $p $slot1).message))"
Check ((Slot $p $slot2).shown) "the other slot stays shown"
Check ((Counter $b "blockedAtSelection.preview") -gt $hidden) "B counted the hidden preview"

Cmd $a lock-release "$loader" | Out-Null
Wait-Mirror $b 0
$p = Cmd $b lock-preview "$loader $slot1"
Check ((Slot $p $slot1).shown) "after A's release the slot is shown again ($($p | ConvertTo-Json -Compress))"

$answer = Request-Lock $a "$loader mount $slot1"
Wait-Mirror $b 1
Cmd $b lock-probe "mode PartSelect $loader" | Out-Null
Start-Sleep -Milliseconds 300
Cmd $b lock-probe "mode PartSelectMount $loader" | Out-Null
Start-Sleep -Milliseconds 500
$p = Cmd $b lock-preview "$loader $slot1 $slot2"
Check (-not (Slot $p $slot1).shown -and (Slot $p $slot2).shown) "entering mount mode while A holds the slot leaves it hidden ($($p | ConvertTo-Json -Compress))"
Cmd $a lock-release "$loader" | Out-Null
Wait-Mirror $b 0
$p = Cmd $b lock-preview "$loader $slot1"
Check ((Slot $p $slot1).shown) "and shown again after the release"

# Item chooser on B.
$open = Cmd $b lock-chooser "$loader $slot2 open"
$r = Wait-Try $b $open.tryId
Check ($r.result -eq "granted" -and $r.started) "B's chooser opens on $slot2 ($($r.result), started $($r.started))"
$rows = Cmd $b lock-chooser-rows
Write-Host "  rows: $($rows | ConvertTo-Json -Compress -Depth 4)"
Check ((Row $rows $uid1) -and -not (Row $rows $uid1).locked -and (Row $rows $uid2) -and -not (Row $rows $uid2).locked) "both items are listed and free"

$marked = Counter $b "blockedAtSelection.chooserItem"
$answer = Request-Lock $a "$loader mount $slot1 items $uid1"
Check ($answer.result -eq "granted") "A holds $slot1 with item $uid1"
Wait-Mirror $b 2
$rows = Cmd $b lock-chooser-rows
Write-Host "  A holds $uid1`: $($rows | ConvertTo-Json -Compress -Depth 4)"
Check ((Row $rows $uid1).locked -and (Row $rows $uid1).text -match "is mounting") "the open chooser marks A's item with the lock overlay ($((Row $rows $uid1).text))"
Check (-not (Row $rows $uid2).locked) "the other item stays free"
Check ((Counter $b "blockedAtSelection.chooserItem") -gt $marked) "B counted the marked row"

$reopened = Counter $b "chooserReopened"
$refused = Counter $b "refusedLocally"
Cmd $b lock-chooser-pick "$uid1" | Out-Null
$deadline = (Get-Date).AddSeconds(6)
do {
    Start-Sleep -Milliseconds 300
    $rows = Cmd $b lock-chooser-rows
} while (-not ($rows.open -and (Counter $b "chooserReopened") -gt $reopened) -and (Get-Date) -lt $deadline)
Check ((Counter $b "refusedLocally") -gt $refused) "B's pick of A's item is refused on B's game"
Check ($rows.open -and (Counter $b "chooserReopened") -eq $reopened + 1) "the chooser opens again after the refused pick (open $($rows.open), mode $($rows.mode))"
$locks = Get-ServerLocks
Check (@($locks.Records | Where-Object { $_.Owner -eq $idB -and $_.X -contains $slot2 }).Count -eq 1) "B holds a new slot lock on $slot2 ($($locks.Line))"
Check ((Row $rows $uid1).locked) "the reopened chooser still marks A's item"
$inventory = @((Cmd $b dump).inventory.items | Where-Object { $_.UID -eq $uid1 }).Count
Check ($inventory -eq 1) "A's item is still in the inventory"

Cmd $a lock-release "$loader" | Out-Null
Wait-Mirror $b 1
$rows = Cmd $b lock-chooser-rows
Check ($rows.open -and -not (Row $rows $uid1).locked) "after A's release the open chooser shows the item free again"
Cmd $b lock-chooser "$loader $slot2 close" | Out-Null
Wait-Mirror $b 0
Cmd $b lock-probe "mode Garage $loader" | Out-Null
Start-Sleep -Milliseconds 500

# Car pie on A.
$base = Pie $a
Write-Host "  pie free: $(($base | ForEach-Object { "$($_.id)=$($_.enabled)/$($_.available)" }) -join ' ')"
Check ($base.Count -ge 3 -and @($base | Where-Object { $_.source }).Count -eq 0) "A's car pie lists the move options without a block ($($base.Count))"
Check (@($base | Where-Object { $_.available }).Count -ge 1) "at least one move option is available on a free car"

$answer = Request-Lock $b "$loader move car"
Check ($answer.result -eq "granted") "B holds the car lock"
Wait-Mirror $a 1
$held = Pie $a
Write-Host "  pie while B holds the car: $(($held | ForEach-Object { "$($_.id)=$($_.enabled)/$($_.available)/$($_.source)" }) -join ' ')"
Check ($held.Count -eq $base.Count -and @($held | Where-Object { $_.available -or $_.enabled }).Count -eq 0) "every move option is unavailable while B holds the car"
Check (@($held | Where-Object { $_.source -eq "locks" -and $_.reason -match "is working on this car" }).Count -eq $held.Count) "the lock is the reason ($(@($held)[0].reason))"
Cmd $b lock-release "$loader" | Out-Null
Wait-Mirror $a 0

$part = @($candidates | Where-Object { $_.key -ne $slot1 -and $_.key -ne $slot2 } | Select-Object -First 1)[0]
$answer = Request-Lock $b "$loader unmount $($part.key)"
Wait-Mirror $a 1
$held = Pie $a
Check (@($held | Where-Object { $_.available -or $_.enabled }).Count -eq 0) "a part lock of B makes the move options unavailable too"
Cmd $b lock-release "$loader" | Out-Null
Wait-Mirror $a 0

$after = Pie $a
$same = @($base | Where-Object { $o = $_; $n = @($after | Where-Object { $_.id -eq $o.id })[0]; $n -and $n.enabled -eq $o.enabled -and $n.available -eq $o.available -and -not $n.source }).Count
Check ($same -eq $base.Count -and $after.Count -eq $base.Count) "after B's release the move options are as before ($same of $($base.Count))"

# The guard's pie blocks still go through the shared owner.
$null = Cmd $a guard-try "PieMenu:EngineStand"
Start-Sleep -Milliseconds 1500
$menu = Cmd $a guard-try "PieState:EngineStand"
Check (@($menu.options | Where-Object { $_ -like "engine_new enabled=False" }).Count -eq 1) "the guard still locks engine_new ($($menu.options -join ', '))"
Check (@($menu.options | Where-Object { $_ -like "engine_add enabled=False" }).Count -eq 0) "and leaves engine_add open"
Cmd $a guard-try "Window:PieMenu" | Out-Null

$locks = Get-ServerLocks
Check ($locks.Count -eq 0 -and (Get-LockCounter $locks "overlapViolations") -eq 0) "no lock is left and none overlapped ($($locks.Line))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

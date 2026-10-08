# areas: placement, parts, persistence
# Race and drift audit gap 10 and C2 (state-merges-and-contention D10): parking keeps the server's record of the car.
# (1) B unmounts a part and changes a fluid while A, who has not seen either, parks the car: after the unpark the part
# is off, its item exists once and the fluid is B's. (2) A unmounts a part and parks at once: the park waits for A's own
# change. (3) The parked record survives a server restart. (4) B unparks and disconnects before its baseline: the car is
# back in the parking, and A's unpark still shows B's earlier unmount.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
$used = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-InMenu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 60 -What "menu, disconnected" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
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

function Part([string]$Name, [string]$Key) { @(@((Dump $Name).cars | Where-Object { $_.index -eq $loader })[0].subParts | Where-Object { $_.key -eq $Key })[0] }

function Items([string]$Name, [string]$Id) { @((Dump $Name).inventory.items | Where-Object { $_.ID -eq $Id }).Count }

function Free-Key() {
    $key = @(Cmd $b vfx-parts "$loader all" | Where-Object { $script:used -notcontains $_.key })[0].key
    $script:used += $key
    $key
}

function Park([string]$What) {
    $mark = Get-ServerLogMark
    Cmd $a park "$loader" | Out-Null
    $line = Wait-ServerLog -Pattern "\[Parking\] Loader $loader \(.*\) parked in slot (\d+)" -After $mark -TimeoutSec 20
    [int]([regex]::Match($line, "parked in slot (\d+)").Groups[1].Value)
}

function Wait-Empty([string]$Name) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $car = @((Dump $Name).cars | Where-Object { $_.index -eq $loader })[0]
        if (-not $car.carToLoad -and $car.syncState -eq "Empty") { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
}

function Unpark([string]$Name, [int]$Slot) {
    Wait-Empty $Name
    Cmd $Name unpark "$Slot $loader" | Out-Null
    Wait-Ready $Name | Out-Null
}

function Wait-Same([string]$What) {
    try {
        Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("inventory", "carDetails") -TimeoutSec 20 | Out-Null
        Check $true "$What`: A and B have equal inventories and details"
    } catch { Check $false "$What`: $($_.Exception.Message)" }
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
Start-Sleep -Seconds 3
$fluid = @((@((Dump $a).carDetails) | Where-Object { $_.loader -eq $loader })[0].entries.PSObject.Properties.Name | Where-Object { $_ -like "f:*" -and $_ -ne "f:Brake.0" })[0]
$fluidType = ($fluid -replace "^f:", "") -replace "\.\d+$", ""
$fluidId = [int](($fluid -split "\.")[-1])

# (1) A parks with B's unmount and fluid change still on their way to A.
$k = Free-Key
$id = (Part $a $k).id
$itemsBefore = Items $a $id
Cmd $a net-hold "on" | Out-Null
$mark = Get-ServerLogMark
Cmd $b part-fast-unmount "$loader $k" | Out-Null
try { Wait-ServerLog -Pattern "\[Cars\] Change \d+ from client \d+ on loader $loader" -After $mark -TimeoutSec 15 | Out-Null } catch { }
Cmd $b cardetails-fluid "$loader $fluidType $fluidId 0.37" | Out-Null
Start-Sleep -Seconds 3
$slot = Park "B's change in flight"
Cmd $a net-hold "off" | Out-Null
Start-Sleep -Seconds 3
Unpark $a $slot
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 4
foreach ($name in $a, $b) {
    Check ((Part $name $k).unmounted) "B's change in flight: $k is off on $name after the unpark"
    Check ((Items $name $id) -eq $itemsBefore + 1) "B's change in flight: $name has one new $id ($itemsBefore before, $(Items $name $id) now)"
    $level = (@((Dump $name).carDetails) | Where-Object { $_.loader -eq $loader })[0].entries.$fluid.Level
    Check ([math]::Abs($level - 0.37) -lt 0.002) "B's change in flight: $name has B's $fluid 0.37 ($level)"
}
Wait-Same "B's change in flight"

# (2) A unmounts a part and parks at once.
$k2 = Free-Key
$id2 = (Part $a $k2).id
$items2 = Items $a $id2
Cmd $a part-fast-unmount "$loader $k2" | Out-Null
$slot = Park "own unsent change"
Start-Sleep -Seconds 2
Unpark $a $slot
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 4
foreach ($name in $a, $b) {
    Check ((Part $name $k2).unmounted) "own unsent change: $k2 is off on $name after the unpark"
    Check ((Items $name $id2) -eq $items2 + 1) "own unsent change: $name has one new $id2 ($items2 before, $(Items $name $id2) now)"
}
Wait-Same "own unsent change"

# (3) The parked record survives a restart.
$k3 = Free-Key
Cmd $a net-hold "on" | Out-Null
$mark = Get-ServerLogMark
Cmd $b part-fast-unmount "$loader $k3" | Out-Null
try { Wait-ServerLog -Pattern "\[Cars\] Change \d+ from client \d+ on loader $loader" -After $mark -TimeoutSec 15 | Out-Null } catch { }
$slot = Park "restart"
Cmd $a net-hold "off" | Out-Null
Start-Sleep -Seconds 2
$mark = Get-ServerLogMark
Send-ServerCommand "save"
try { Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null } catch { }
Stop-TestServer
foreach ($name in $a, $b) { Wait-InMenu $name; Cmd $name mp-ui "ok" | Out-Null }
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
Start-Sleep -Seconds 3
Unpark $a $slot
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 4
foreach ($name in $a, $b) { Check ((Part $name $k3).unmounted) "restart: $k3 is off on $name after the unpark" }

# (4) B unparks and leaves before its baseline; the car goes back with the server's record.
$k4 = Free-Key
Cmd $a net-hold "on" | Out-Null
$mark = Get-ServerLogMark
Cmd $b part-fast-unmount "$loader $k4" | Out-Null
try { Wait-ServerLog -Pattern "\[Cars\] Change \d+ from client \d+ on loader $loader" -After $mark -TimeoutSec 15 | Out-Null } catch { }
$slot = Park "unparker leaves"
Cmd $a net-hold "off" | Out-Null
Start-Sleep -Seconds 3
$mark = Get-ServerLogMark
Cmd $b unpark "$slot $loader" | Out-Null
try { Wait-ServerLog -Pattern "\[Parking\] Slot $slot .* unparked to loader $loader" -After $mark -TimeoutSec 60 | Out-Null } catch { }
Cmd $b disconnect | Out-Null
$back = try { Wait-ServerLog -Pattern "went back to slot (\d+): its unparker left before the baseline" -After $mark -TimeoutSec 20 } catch { $null }
Check ([bool]$back) "unparker leaves: the car went back to the parking ($back)"
if ($back) {
    $slot = [int]([regex]::Match($back, "went back to slot (\d+)").Groups[1].Value)
    Start-Sleep -Seconds 2
    Unpark $a $slot
    Start-Sleep -Seconds 4
    Check ((Part $a $k4).unmounted) "unparker leaves: $k4 is off on A after A's unpark"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

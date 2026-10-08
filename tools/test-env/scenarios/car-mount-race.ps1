# areas: parts, cars
# Playtest 2026-10-07 finding 4: A and B mount the same inventory item on two free slots of one car (two parts with
# the same id). B's incoming packets are held, so B mounts before it sees A's mount; the server accepts A and rejects
# B ("item ... is gone"). B gets A's change first and the rejection second, as in the playtest. B must then equal A
# and the server right away, without a repair or resync: its slot unmounted again and the item gone, not given back.
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

function Items($Dump, [string]$Id) { @($Dump.inventory.items | Where-Object { $_.ID -eq $Id } | ForEach-Object { [long]$_.UID }) }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$twins = Cmd $a part-twins "$loader"
$k1, $k2 = $twins.keys
Write-Host "twin parts $k1 and $k2 ($($twins.id))"
$before = Items (Cmd $a dump) $twins.id
Cmd $a part-fast-unmount "$loader $k1" | Out-Null
Cmd $a part-fast-unmount "$loader $k2" | Out-Null
$dump = Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("inventory", "cars") -TimeoutSec 30
$new = @(Items (Send-HarnessCommand -Instance $a -Verb dump) $twins.id | Where-Object { $before -notcontains $_ })
Check ($new.Count -eq 2) "both parts came off as items ($($new -join ', '))"
$x, $y = $new

Cmd $b net-hold "on" | Out-Null
$mark = Get-ServerLogMark
Cmd $a part-fast-mount "$loader $k1 $x" | Out-Null
$accepted = Wait-ServerLog -Pattern "\[Cars\] Change \d+ from client \d+ on loader $loader`: revision .* -1\)" -After $mark -TimeoutSec 15
Check ([bool]$accepted) "the server accepts A's mount with item $x ($accepted)"
Cmd $b part-fast-mount "$loader $k2 $x" | Out-Null
$rejected = try { Wait-ServerLog -Pattern "rejected: item $x is gone" -After $mark -TimeoutSec 15 } catch { $null }
Check ([bool]$rejected) "the server rejects B's mount of the same item ($rejected)"
Start-Sleep -Seconds 1
Cmd $b net-hold "off" | Out-Null

# The server's own desync repair resends the inventory a few seconds later, so B is read before it can.
Start-Sleep -Milliseconds 1500
$diff = Compare-HarnessDumps (Cmd $a dump) (Cmd $b dump) -Sections @("inventory", "cars")
Check ($diff.Count -eq 0) "B equals A right after the rejection, before any repair (differ: $($diff -join ', '))"
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "after-race"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "after-race"
foreach ($pair in @(@($a, $dumpA), @($b, $dumpB))) {
    $items = Items $pair[1] $twins.id
    Check (($items -notcontains $x) -and ($items -contains $y)) "$($pair[0]) has item $y but not the mounted $x"
}
$ra = Cmd $a car-ready "$loader"; $rb = Cmd $b car-ready "$loader"
Check ($ra.stateHash -eq $rb.stateHash) "A and B agree on the part state ($($ra.stateHash) / $($rb.stateHash))"
$partB = @($dumpB.cars | Where-Object { $_.index -eq $loader })[0].subParts | Where-Object { $_.key -eq $k2 }
Check ($partB.unmounted) "B's slot $k2 is unmounted again"

$repairs = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] .*resending|full sync" })
Check ($repairs.Count -eq 0) "the server did not have to repair B ($($repairs | Select-Object -First 1))"
$mark = Get-ServerLogMark
Send-ServerCommand "desync check"
$inventory = try { Wait-ServerLog -Pattern "\[Desync\] inventory for client \d+: (match|mismatch)" -After $mark -TimeoutSec 10 } catch { $null }
Start-Sleep -Seconds 2
$lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] (inventory|cars:$loader) for client" })
$lines | ForEach-Object { Write-Host "server: $_" }
Check ($lines.Count -ge 4 -and -not ($lines -match "mismatch")) "the server's inventory and car digests match both players"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

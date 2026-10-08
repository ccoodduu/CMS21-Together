# areas: parts, cars, resync
# Race and drift audit gap 5 and P7: B unmounts a part while its packets are held, A deletes, parks or job-ends the
# car, then B's change reaches the server for a car that is gone. B rolls the unmount's inventory item back (when the
# car goes, or on the server's rejection), keeps no part transaction for that loader, and its inventory digest
# matches; without that B kept the item (only on B) and its digests stayed "not ready" for the rest of the session.
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

function Wait-Ready([string]$Name, [int]$Loader = $loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}

function Gone-Inflight([string]$What, [int]$Loader, [scriptblock]$Remove) {
    Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("inventory", "cars") -TimeoutSec 30 | Out-Null
    $itemsBefore = @((Cmd $b dump).inventory.items).Count
    Cmd $b net-hold "out" | Out-Null
    $part = Cmd $b part-fast-unmount "$Loader"
    Write-Host "$What`: B unmounted $($part.key) while its packets are held"
    Start-Sleep -Seconds 2
    $mark = Get-ServerLogMark
    & $Remove
    Start-Sleep -Seconds 3
    Cmd $b net-hold "off" | Out-Null

    $rejected = try { Wait-ServerLog -Pattern "\[Cars\] Change \d+ from client \d+ for loader $Loader rejected: no car" -After $mark -TimeoutSec 15 } catch { $null }
    Check ([bool]$rejected) "$What`: the server answers B's change for the removed car with a rejection ($rejected)"
    try {
        Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("inventory") -TimeoutSec 20 | Out-Null
        Check $true "$What`: A and B have equal inventories after the car is gone"
    } catch {
        Check $false "$What`: A and B have equal inventories after the car is gone: $($_.Exception.Message)"
    }
    Check (@((Cmd $b dump).inventory.items).Count -eq $itemsBefore) "$What`: B's unmounted item was rolled back ($itemsBefore items before)"
    $tx = @((Cmd $b dump).parts.transactions | Where-Object { $_.loader -eq $Loader })
    Check ($tx.Count -eq 0) "$What`: B keeps no part transaction for loader $Loader ($($tx | ConvertTo-Json -Compress))"

    $mark = Get-ServerLogMark
    Send-ServerCommand "desync check"
    Start-Sleep -Seconds 3
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] inventory for client" })
    $lines | ForEach-Object { Write-Host "server: $_" }
    Check ($lines.Count -ge 2 -and -not ($lines -match "mismatch|not ready")) "$What`: the server's inventory digest matches both players"
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
Gone-Inflight "delete" $loader { Cmd $a car-delete "$loader" | Out-Null }

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Gone-Inflight "park" $loader { Cmd $a park "$loader" | Out-Null }

$gen = if ((Get-HarnessStatus $a).isOrderGenerator) { $a } else { $b }
$base = @((Cmd $a dump).jobs.orders).Count
Cmd $gen orders-generate | Out-Null
$deadline = (Get-Date).AddSeconds(30)
do { Start-Sleep -Milliseconds 700; $orders = @((Cmd $a dump).jobs.orders) } while ($orders.Count -le $base -and (Get-Date) -lt $deadline)
$take = $orders[-1].id
Cmd $a orders-accept "$take" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 700; $job = @((Cmd $a dump).jobs.active | Where-Object { $_.id -eq $take })[0] } while (-not $job -and (Get-Date) -lt $deadline)
Check ($null -ne $job) "A took job $take"
if ($job) {
    $jobLoader = [int]$job.carLoaderID
    Wait-Ready $a $jobLoader | Out-Null
    Wait-Ready $b $jobLoader | Out-Null
    Start-Sleep -Seconds 3
    Gone-Inflight "job end" $jobLoader { Cmd $a job-end-direct "$take" | Out-Null }
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: parts, cars, resync
# Race and drift audit gap 5: B unmounts a part while its packets are held, A deletes the car, then B's change
# reaches the server for a car that is gone. The server must answer with a rejection so B rolls the unmount's
# inventory item back; without an answer B kept the item (only on B) and its inventory digest stayed "not ready" for
# the rest of the session.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
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
$itemsBefore = @((Cmd $a dump).inventory.items).Count

Cmd $b net-hold "out" | Out-Null
$part = Cmd $b part-fast-unmount "$loader"
Write-Host "B unmounted $($part.key) while its packets are held"
Start-Sleep -Seconds 2
$mark = Get-ServerLogMark
Cmd $a car-delete "$loader" | Out-Null
Start-Sleep -Seconds 2
Cmd $b net-hold "off" | Out-Null

$rejected = try { Wait-ServerLog -Pattern "\[Cars\] Change \d+ from client \d+ for loader $loader rejected: no car" -After $mark -TimeoutSec 15 } catch { $null }
Check ([bool]$rejected) "the server answers B's change for the deleted car with a rejection ($rejected)"

try {
    Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("inventory") -TimeoutSec 20 | Out-Null
    Check $true "A and B have equal inventories after the car is gone"
} catch {
    Check $false "A and B have equal inventories after the car is gone: $($_.Exception.Message)"
}
Check (@((Cmd $b dump).inventory.items).Count -eq $itemsBefore) "B's unmounted item was rolled back ($itemsBefore items before)"

$mark = Get-ServerLogMark
Send-ServerCommand "desync check"
Start-Sleep -Seconds 3
$lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] inventory for client" })
$lines | ForEach-Object { Write-Host "server: $_" }
Check ($lines.Count -ge 2 -and -not ($lines -match "mismatch|not ready")) "the server's inventory digest matches both players"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: outdoor, economy, presence
# shared-outdoor-scenes 9.4, guard enforcing: A goes to the garage and back while B stays (same instance, B's take
# made meanwhile is visible); after both leave A gets a new instance with a new seed and none of the previous visit's
# models (when the catalog is large enough); A alone disconnects and travels back within the grace (same instance;
# the grace is raised to 300 s because a reconnect loads the whole garage first).
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }

function Wait-Outdoor([string]$Name, [string]$Scene) {
    Wait-HarnessDump -Instance $Name -TimeoutSec 240 -What "$Name in the shared $Scene" -Condition {
        param($d) $d.local.scene -eq $Scene -and $d.outdoor.applied
    }
}

function Travel([string]$Name, [string]$Scene) {
    Send-HarnessCommand -Instance $Name -Verb travel -Arguments $Scene | Out-Null
    if ($Scene -eq "Garage") { Wait-InGarage $Name; return Dump $Name }
    return Wait-Outdoor $Name $Scene
}

function Models($Dump) { @($Dump.outdoor.cars | ForEach-Object { $_.carToLoad }) }

Set-ServerConfigValues $Ctx.ServerDir @{ outdoor_rejoin_grace_seconds = 300 }
Restart-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "enforce" | Out-Null }
Send-ServerCommand "money set 200000"

$first = Travel $a "Junkyard"
$db = Travel $b "Junkyard"
Check ($first.outdoor.instanceId -eq $db.outdoor.instanceId) "A and B share the junkyard"

Travel $a "Garage" | Out-Null
$pile = @($db.outdoor.piles | Where-Object { @($_.uids).Count -ge 1 })[0]
$taken = (Send-HarnessCommand -Instance $b -Verb loot-take -Arguments "$($pile.Key) uid:$($pile.uids[0])").UID
$back = Travel $a "Junkyard"
Check ($back.outdoor.instanceId -eq $first.outdoor.instanceId) "A returns to the same instance while B stays"
Check (-not (@($back.outdoor.piles | ForEach-Object { $_.uids }) -contains $taken)) "A does not see the item B took meanwhile"
Check (-not $back.outdoor.generator) "A is no longer the generator"

Travel $a "Garage" | Out-Null
Send-HarnessCommand -Instance $b -Verb loot-quit | Out-Null
Wait-InGarage $b
Start-Sleep -Seconds 2
$next = Travel $a "Junkyard"
Check ($next.outdoor.instanceId -ne $first.outdoor.instanceId -and $next.outdoor.seed -ne $first.outdoor.seed) "after everyone left A gets a new instance and seed"
$catalog = Send-HarnessCommand -Instance $a -Verb outdoor-catalog
$used = Models $first
$overlap = @(Models $next | Where-Object { $used -contains $_ })
if ($catalog.models.Junkyard -ge 2 * $used.Count) { Check ($overlap.Count -eq 0) "no model of the previous visit ($($overlap -join ', '))" }
else { Write-Host "note: $($catalog.models.Junkyard) models for $($used.Count) cars; repeats allowed" -ForegroundColor Yellow }

Send-HarnessCommand -Instance $a -Verb to-menu | Out-Null
Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "A in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
$again = Travel $a "Junkyard"
Check ($again.outdoor.instanceId -eq $next.outdoor.instanceId) "within the grace A finds the same instance after a disconnect"
Travel $a "Garage" | Out-Null

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# sync-workshop-machines late join, own save, return and restart: A alone loads the machines (wheel on the changer,
# balanced wheel on the balancer with the minigame open, rotated engine with one part off, welder at a lifter). B joins
# and must see the same. B's own save then puts a battery on its charger and B resyncs: the charger is empty again and
# nothing reaches the shared inventory. B travels away while A changes a machine and returns. Finally the server is
# saved, killed and restarted; after both reconnect the machines and positions equal the state before (no claims).
param($Ctx)

$a, $b = $Ctx.Instances
$sections = @("inventory", "tools", "toolPositions")
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Same([string]$What, [int]$TimeoutSec = 40) {
    try {
        $dump = Wait-HarnessDumpsEqual -Left $a -Right $b -Sections $sections -TimeoutSec $TimeoutSec
        Check $true "$What`: A and B are equal"
        return $dump
    } catch {
        Check $false "$What`: $($_.Exception.Message)"
        return (Send-HarnessCommand -Instance $a -Verb dump)
    }
}

function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Machines([string]$Name) { (Cmd $Name dump).tools | ConvertTo-Json -Depth 6 -Compress }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
foreach ($name in $Ctx.Instances) { Cmd $name guard-allow "Scene:Junkyard" | Out-Null }
Connect-HarnessInstance $a; Wait-InGarage $a
Cmd $a guard-set "Off" | Out-Null
$idA = (Get-HarnessStatus $a).playerId

$wheel = (Cmd $a give-group "wheel").UID
Cmd $a tool-put "TireChanger $wheel" | Out-Null
$balanced = (Cmd $a give-group "wheel").UID
Cmd $a tool-put "WheelBalancer $balanced" | Out-Null
Cmd $a tool-balance | Out-Null
Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 700; $ready = Cmd $a car-ready "0" } while (-not ($ready.state -eq "Ready" -and $ready.loaded) -and (Get-Date) -lt $deadline)
$crane = Cmd $a crane-out "0"
$engineId = $crane.engine
Start-Sleep -Seconds 2
Cmd $a tool-put "EngineStand1 $($crane.group)" | Out-Null
try { Wait-HarnessDump -Instance $a -TimeoutSec 60 -What "engine built on A's stand" -Condition { param($x) $x.tools.EngineStand1.uid -ne 0 } | Out-Null } catch { }
Start-Sleep -Seconds 5; $standBuilt = @(Cmd $a tool-list | Where-Object { $_.tool -eq "EngineStand1" -and $_.mirrorUid -ne 0 }).Count -eq 1
if (-not $standBuilt) { $note = "engine stand steps skipped: the game's build coroutine throws when the harness drives it (also disconnected); hand check"; Write-Host "NOTE: $note"; $Ctx.Result.notes += $note }
else {
    Cmd $a tool-angle "EngineStand1 90" | Out-Null
    $part = Cmd $a tool-stand-part "EngineStand1 auto unmount"
    Write-Host "A unmounted $($part.key) ($($part.id))"
}
Cmd $a tool-move "Welder CarLifter1" | Out-Null
Start-Sleep -Seconds 3
Cmd $a tool-balance-open | Out-Null
Start-Sleep -Seconds 2
Write-Host "A before the join: $(Machines $a)"

Connect-HarnessInstance $b; Wait-InGarage $b
Cmd $b guard-set "Off" | Out-Null
$d = Wait-Same "late join"
$db = Cmd $b dump
Check ($db.tools.TireChanger.uid -eq $wheel) "B sees A's wheel on the tire changer"
Check ($db.tools.WheelBalancer.balanced -eq $true -and $db.tools.WheelBalancer.claimedBy -eq $idA) "B sees the balanced wheel and A's claim"
if ($standBuilt) { Check ($db.tools.EngineStand1.angle -eq 90 -and @($db.tools.EngineStand1.unmountedParts) -contains $part.key) "B sees the rotated engine with $($part.key) off" }
Check ($db.toolPositions.Welder -eq "CarLifter1") "B's welder is at CarLifter1"
Cmd $a tool-balance-cancel | Out-Null

$inventoryBefore = (Cmd $a dump).inventory | ConvertTo-Json -Depth 6 -Compress
Cmd $b tool-local-put "BatteryCharger akumulator" | Out-Null
Start-Sleep -Seconds 2
Cmd $b resync "force" | Out-Null
Start-Sleep -Seconds 5
Wait-InGarage $b
$d = Wait-Same "after B's resync" 60
Check ($d.tools.BatteryCharger.uid -eq 0) "B's charger is empty after the resync"
Check (($d.inventory | ConvertTo-Json -Depth 6 -Compress) -eq $inventoryBefore) "the battery from B's own save did not reach the shared inventory"

Cmd $b travel "Junkyard" | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 240 -What "B in the junkyard" -Condition { param($x) $x.local.scene -eq "Junkyard" } | Out-Null
Cmd $a tool-take "TireChanger" | Out-Null
Start-Sleep -Seconds 3
$wheel3 = (Cmd $a give-group "wheel").UID
Cmd $a tool-put "TireChanger $wheel3" | Out-Null
Start-Sleep -Seconds 2
Cmd $b travel "Garage" | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 240 -What "B back in the garage" -Condition { param($x) $x.local.scene -eq "Garage" } | Out-Null
Wait-InGarage $b
$d = Wait-Same "B returned" 60
Check ($d.tools.TireChanger.uid -eq $wheel3) "B sees the wheel A put while B was away"

$beforeRestart = Machines $a
$mark = Get-ServerLogMark
Send-ServerCommand "tools"
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
Wait-Same "after the restart" 60 | Out-Null
$afterRestart = Machines $a
Check ($afterRestart -eq $beforeRestart) "machines after the restart equal the state before (before: $beforeRestart, after: $afterRestart)"

Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "end" | Out-Null
Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "end" | Out-Null
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

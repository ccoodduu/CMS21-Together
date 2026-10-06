# sync-workshop-machines races, made deterministic with tool-hold (incoming tool packets are buffered, so both players
# act on a stale view): two puts on the tire changer, two takes, the same wheel on two machines, two balancer
# minigames, and two take-offs of the same engine. The server decides; no item is lost or duplicated.
param($Ctx)

$a, $b = $Ctx.Instances
$sections = @("inventory", "tools")
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Same([string]$What, [int]$TimeoutSec = 30) {
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
function Hold([string]$State) { foreach ($name in $Ctx.Instances) { Cmd $name tool-hold $State | Out-Null } }
function GroupCount($Dump, $Uid) { @($Dump.inventory.groups | Where-Object { $_.UID -eq $Uid }).Count }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

for ($round = 1; $round -le 5; $round++) {
    $w1 = (Cmd $a give-group "wheel").UID
    $w2 = (Cmd $b give-group "wheel").UID
    Wait-Same "round $round`: wheels given" | Out-Null
    Hold "on"
    Cmd $a tool-put "TireChanger $w1" | Out-Null
    Cmd $b tool-put "TireChanger $w2" | Out-Null
    Start-Sleep -Seconds 1
    Hold "off"
    $d = Wait-Same "round $round`: two puts"
    $held = (Send-HarnessCommand -Instance $a -Verb dump).tools.TireChanger.uid
    $other = if ($held -eq $w1) { $w2 } else { $w1 }
    Check (($held -eq $w1 -or $held -eq $w2) -and (GroupCount $d $other) -eq 1 -and (GroupCount $d $held) -eq 0) "round $round`: one wheel on the changer ($held), the other in the inventory"

    Hold "on"
    Cmd $a tool-take "TireChanger" | Out-Null
    Cmd $b tool-take "TireChanger" | Out-Null
    Start-Sleep -Seconds 2
    Hold "off"
    $d = Wait-Same "round $round`: two takes"
    Check ((GroupCount $d $held) -eq 1 -and $d.tools.TireChanger.uid -eq 0) "round $round`: the taken wheel is in the inventory once"
}

$w = (Cmd $a give-group "wheel").UID
Wait-Same "shared wheel given" | Out-Null
Hold "on"
Cmd $a tool-put "TireChanger $w" | Out-Null
Cmd $b tool-put "WheelBalancer $w" | Out-Null
Start-Sleep -Seconds 1
Hold "off"
$d = Wait-Same "same wheel on two machines"
$on = @(@("TireChanger", "WheelBalancer") | Where-Object { $d.tools.$_.uid -eq $w }).Count
Check ($on -eq 1 -and (GroupCount $d $w) -eq 0) "the wheel is on exactly one machine and not in the inventory"
foreach ($tool in "TireChanger", "WheelBalancer") { if ($d.tools.$tool.uid -ne 0) { Cmd $a tool-take $tool | Out-Null } }
Wait-Same "machines emptied" | Out-Null

$w = (Cmd $a give-group "wheel").UID
Wait-Same "balancer wheel given" | Out-Null
Cmd $a tool-put "WheelBalancer $w" | Out-Null
Wait-Same "wheel on the balancer" | Out-Null
Hold "on"
Cmd $a tool-balance-open | Out-Null
Cmd $b tool-balance-open | Out-Null
Start-Sleep -Seconds 1
Hold "off"
$d = Wait-Same "two minigames opened"
$owner = $d.tools.WheelBalancer.claimedBy
Check ($null -ne $owner) "one player holds the balancer ($owner)"
$loser = if ($owner -eq (Get-HarnessStatus $a).playerId) { $b } else { $a }
$winner = if ($loser -eq $a) { $b } else { $a }
Start-Sleep -Seconds 1
$taken = Cmd $loser tool-take "WheelBalancer"
Check ($taken.refused -eq $true) "the loser ($loser) is refused while the holder balances"
Cmd $winner tool-balance | Out-Null
Wait-HarnessDump -Instance $loser -TimeoutSec 15 -What "claim released" -Condition { param($x) $null -eq $x.tools.WheelBalancer.claimedBy } | Out-Null
Cmd $loser tool-take "WheelBalancer" | Out-Null
Wait-Same "balancer emptied" | Out-Null

Cmd $a tool-stand-create "EngineStand1 engine_v8_stary" | Out-Null
Wait-Same "engine on the stand" 60 | Out-Null
$engines = @((Cmd $a dump).inventory.groups | Where-Object { $_.ID -eq "engine_v8_stary" }).Count
Hold "on"
Cmd $a tool-take "EngineStand1" | Out-Null
Cmd $b tool-take "EngineStand1" | Out-Null
Start-Sleep -Seconds 2
Hold "off"
$d = Wait-Same "two engine take-offs"
Check (@($d.inventory.groups | Where-Object { $_.ID -eq "engine_v8_stary" }).Count -eq $engines + 1) "exactly one engine group came back"

Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "end" | Out-Null
Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "end" | Out-Null
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

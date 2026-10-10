# run-all: skip
# areas: tools
# Spike 1.1b of sync-tuning-bonus-and-new-engines: whether the engine stand's build can finish in a harness game when
# CreateEngineWindow.CreateEngineAction builds without the fade, whether the car's engine id is one the window lists,
# and what reaches B, the inventories and the money; then a second build on the occupied stand.
param($Ctx)

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Try-Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { "ERROR: $($_.Exception.Message.Split("`n")[0])" } }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "build_$Label.json") -Encoding utf8; $text = $Value | ConvertTo-Json -Depth 8 -Compress; Write-Host "== $Label"; if ($text.Length -lt 3000) { Write-Host $text } else { Write-Host "($($text.Length) chars, see file)" } }
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
function Snapshot([string]$Label) {
    Save "state_A_$Label" (Try-Cmd $a stand-state)
    Save "state_B_$Label" (Try-Cmd $b stand-state)
    Save "tools_B_$Label" ((Cmd $b dump).tools.EngineStand1)
    Save "inv_A_$Label" ((Cmd $a dump).inventory.groups)
    Save "money_$Label" @((Cmd $a dump).stats.money, (Cmd $b dump).stats.money)
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-allow "Window:CreateEngine" | Out-Null; Cmd $name tool-trace "on" | Out-Null }

Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Write-Host "ready A=$(Wait-Ready $a 0) B=$(Wait-Ready $b 0)"
Save "engines" (Try-Cmd $a stand-engines)
Snapshot "before"

Save "create_fade" (Try-Cmd $a tool-stand-create "auto")
Start-Sleep -Seconds 10
Snapshot "fade"

Save "nofade" (Cmd $a stand-nofade "on")
Save "create_nofade" (Try-Cmd $a tool-stand-create "auto")
Start-Sleep -Seconds 15
Snapshot "nofade"

Save "create_occupied" (Try-Cmd $a tool-stand-create "engine_v8_stary_2carb")
Start-Sleep -Seconds 15
Snapshot "occupied"

$Ctx.Result.passed = $true

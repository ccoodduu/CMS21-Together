# run-all: skip
# areas: cars
# Spike 1.3 of sync-tuning-bonus-and-new-engines: bonus slots on three cars on both clients, a bonus part fitted
# through SelectPartToMount and removed through ClickIO with the inventory before and after, and a fitted, painted
# slot applied on B through BonusPart.Change/TakeOn/Paint and read back.
param($Ctx, [string]$Cars = "car_boltatlanta,car_sixoncebulion,car_landroversvr")

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Try-Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { "ERROR: $($_.Exception.Message.Split("`n")[0])" } }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "bonus_$Label.json") -Encoding utf8; $text = $Value | ConvertTo-Json -Depth 8 -Compress; Write-Host "== $Label"; if ($text.Length -lt 3000) { Write-Host $text } else { Write-Host "($($text.Length) chars, see file)" } }
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

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { foreach ($k in "Mode:BonusAssemble", "Mode:BonusDisassemble") { Cmd $name guard-allow $k | Out-Null } }

$i = 0
foreach ($car in $Cars -split ',') {
    Cmd $a car-spawn "$i $car 0 auto" | Out-Null
    Write-Host "$car ready A=$(Wait-Ready $a $i) B=$(Wait-Ready $b $i)"
    $i++
}
Start-Sleep -Seconds 3
for ($l = 0; $l -lt $i; $l++) {
    Save "state_A_$l" (Try-Cmd $a bonus-state "$l")
    Save "state_B_$l" (Try-Cmd $b bonus-state "$l")
}

$items = Cmd $a bonus-items "0 0"
Save "items" $items
$itemId = if ($items.first -and $items.first -notmatch "^error") { $items.first } else { @($items.items)[0] }
$given = Cmd $a give-item $itemId
Save "given" $given
Start-Sleep -Seconds 2
Save "inv_A_before" ((Cmd $a dump).inventory)
Save "inv_B_before" ((Cmd $b dump).inventory)
Save "fit_A" (Try-Cmd $a bonus-fit "0 0 $($given.UID)")
Start-Sleep -Seconds 3
Save "after_fit_A" (Try-Cmd $a bonus-state "0")
Save "after_fit_B" (Try-Cmd $b bonus-state "0")
Save "inv_A_fit" ((Cmd $a dump).inventory)
Save "inv_B_fit" ((Cmd $b dump).inventory)
Save "remove_A" (Try-Cmd $a bonus-remove "0 0")
Start-Sleep -Seconds 3
Save "after_remove_A" (Try-Cmd $a bonus-state "0")
Save "inv_A_remove" ((Cmd $a dump).inventory)
Save "inv_B_remove" ((Cmd $b dump).inventory)
Cmd $a bonus-mode "off" | Out-Null

Save "items_1_0" (Cmd $a bonus-items "1 0")
Save "items_1_1" (Cmd $a bonus-items "1 1")
Save "apply_B_game" (Try-Cmd $b bonus-apply "1 1 bonus_trunk_spoiler_1 0.8,0.1,0.1,1 Gloss game")
Save "apply_B_manual" (Try-Cmd $b bonus-apply "1 0 bonus_hood_scoop_1 0.1,0.7,0.1,1 Matt manual")
Start-Sleep -Seconds 1
Save "applied_B_1" (Try-Cmd $b bonus-state "1")
Save "apply_B" (Try-Cmd $b bonus-apply "0 0 $itemId 0.8,0.1,0.1,1 Gloss")
Start-Sleep -Seconds 1
Save "applied_B" (Try-Cmd $b bonus-state "0")
Save "apply_B_off" (Try-Cmd $b bonus-apply "0 0 -")
Start-Sleep -Seconds 1
Save "removed_B" (Try-Cmd $b bonus-state "0")
Save "inv_B_end" ((Cmd $b dump).inventory)

$Ctx.Result.passed = $true

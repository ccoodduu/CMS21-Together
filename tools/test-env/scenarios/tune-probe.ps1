# run-all: skip
# areas: cars
# Spike 1.2 of sync-tuning-bonus-and-new-engines: the tune window at the dyno on a harness car, which racing parts make
# the gearbox and the ECU or carburettor tunable, the tabs' apply actions, and what the game keeps on the item of a
# tuned part that is taken off. -Candidates "gearbox=<id>,<key>=<id>" tunes those parts before the window opens.
param($Ctx, [string]$Car = "car_boltatlanta", [string]$Candidates = "")

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Try-Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { "ERROR: $($_.Exception.Message.Split("`n")[0])" } }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "tune_$Label.json") -Encoding utf8; $text = $Value | ConvertTo-Json -Depth 8 -Compress; Write-Host "== $Label"; if ($text.Length -lt 4000) { Write-Host $text } else { Write-Host "($($text.Length) chars, see file)" } }
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
foreach ($name in $Ctx.Instances) { Cmd $name guard-allow "Window:Tune" | Out-Null }

Cmd $a car-spawn "0 $Car 0" | Out-Null
Write-Host "ready A=$(Wait-Ready $a 0) B=$(Wait-Ready $b 0)"
Cmd $a car-move "0 Dyno" | Out-Null
Start-Sleep -Seconds 8
Save "probe_A" (Try-Cmd $a tune-probe "0")

foreach ($pair in ($Candidates -split ',' | Where-Object { $_ })) {
    $key, $id = $pair -split '=', 2
    Save "tunepart_$($key -replace '[^a-z0-9]', '_')" (Try-Cmd $a tune-part "0 $key $id")
}
Start-Sleep -Seconds 3
Save "probe_A_tuned" (Try-Cmd $a tune-probe "0")
Save "probe_B_tuned" (Try-Cmd $b tune-probe "0")

Save "open_A" (Try-Cmd $a tune-open "0")
Start-Sleep -Seconds 2
Save "state_A" (Try-Cmd $a tune-state)
Save "gearbox_ui_A" (Try-Cmd $a cardetails-ui "gearbox 0 3.7")
Start-Sleep -Seconds 3
Save "probe_B_after_gearbox" (Try-Cmd $b tune-probe "0")
Save "ecu_A" (Try-Cmd $a tune-ecu "0")
Start-Sleep -Seconds 3
Save "details_A" ((Cmd $a dump).carDetails)
Save "details_B" ((Cmd $b dump).carDetails)
Save "close_A" (Try-Cmd $a tune-close)
Start-Sleep -Seconds 2
Save "state_A_closed" (Try-Cmd $a tune-state)

$probe = Cmd $a tune-probe "0"
foreach ($part in @($probe.gearbox) + @($probe.modules | ForEach-Object { $_.part })) {
    if (-not $part -or -not $part.key) { continue }
    Save "unmount_$($part.key -replace '[^a-z0-9]', '_')" (Try-Cmd $a part-fast-unmount "0 $($part.key)")
    Start-Sleep -Seconds 2
    $itemId = if ($part.tunedId) { $part.tunedId } else { $part.id }
    Save "item_$($part.key -replace '[^a-z0-9]', '_')" (Try-Cmd $a item-tuning $itemId)
}
Save "inventory_A" ((Cmd $a dump).inventory)

$Ctx.Result.passed = $true

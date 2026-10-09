# run-all: skip
# areas: driving, tools
# Spike for the single-player feature changes (docs/spikes/singleplayer-features.md), guard off as in every harness
# run: what the garage look, bonus parts and gearbox look like at runtime, whether a created engine on the stand
# reaches the other player today, and what happens when a player takes a car to the race track or speed track.
param($Ctx)

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "sp_$Label.json") -Encoding utf8; $text = $Value | ConvertTo-Json -Depth 6 -Compress; Write-Host "== $Label"; if ($text.Length -lt 3000) { Write-Host $text } else { Write-Host "($($text.Length) chars, see file)" } }
function Try-Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { "ERROR: $($_.Exception.Message)" } }
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
function Wait-Scene([string]$Name, [string]$Pattern, [int]$Seconds = 150) {
    try { Wait-HarnessStatus -Instance $Name -TimeoutSec $Seconds -What $Pattern -Condition { param($s) $s.scene -match $Pattern -and $s.playable } | Out-Null; $true } catch { $false }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b

Save "look_A_before" (Try-Cmd $a look-probe)
Save "look_B_before" (Try-Cmd $b look-probe)
Save "look_set_A" (Try-Cmd $a look-set "0 1")
Start-Sleep -Seconds 3
Save "look_A_after" (Try-Cmd $a look-probe)
Save "look_B_after" (Try-Cmd $b look-probe)

Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
$readyA = Wait-Ready $a 0; $readyB = Wait-Ready $b 0
Write-Host "car ready A=$readyA B=$readyB"
Save "bonus_A" (Try-Cmd $a bonus-probe "0")
Save "gearbox_A" (Try-Cmd $a gearbox-probe "0")

Save "stand_create_A" (Try-Cmd $a tool-stand-create "EngineStand1 auto")
Start-Sleep -Seconds 20
Save "stand_A" ((Cmd $a dump).tools.EngineStand1)
Save "stand_B" ((Cmd $b dump).tools.EngineStand1)
Save "stand_list_A" (Try-Cmd $a tool-list)

foreach ($track in "RaceTrack", "SpeedTrack") {
    Save "${track}_go" (Try-Cmd $a track-go "0 $track")
    $arrived = Wait-Scene $a "(?i)track"
    Write-Host "A on $track : $arrived"
    Start-Sleep -Seconds 12
    Save "${track}_state_A" (Try-Cmd $a track-state)
    Save "${track}_status_A" (Get-HarnessStatus -Instance $a)
    $dumpB = Cmd $b dump
    Save "${track}_B_players" $dumpB.players
    Save "${track}_B_away" $dumpB.away
    Save "${track}_B_remoteCars" $dumpB.remoteCars
    Save "${track}_drive" (Try-Cmd $a testdrive-drive "2500")
    Save "${track}_return" (Try-Cmd $a track-return)
    $back = Wait-Scene $a "^garage$" 180
    Write-Host "A back in the garage after $track : $back"
    $readyA = Wait-Ready $a 0
    Start-Sleep -Seconds 5
    Save "${track}_after_A" (Try-Cmd $a track-state)
    Save "${track}_car_A" ((Cmd $a dump).cars)
    Save "${track}_car_B" ((Cmd $b dump).cars)
}

$Ctx.Result.passed = $true

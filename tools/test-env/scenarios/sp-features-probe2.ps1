# run-all: skip
# areas: driving, tools
# Second spike run for docs/spikes/singleplayer-features.md: the garage-look apply through the material cache, a new
# engine built on the stand from a car's engine id, and the race and speed tracks with the guard opened for them.
param($Ctx)

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "sp2_$Label.json") -Encoding utf8; $text = $Value | ConvertTo-Json -Depth 6 -Compress; Write-Host "== $Label"; if ($text.Length -lt 3000) { Write-Host $text } else { Write-Host "($($text.Length) chars, see file)" } }
function Try-Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { "ERROR: $($_.Exception.Message.Split("`n")[0])" } }
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
function Car-Info([string]$Name) {
    $car = @((Cmd $Name dump).cars) | Where-Object { $_.index -eq 0 } | Select-Object -First 1
    if (-not $car) { return $null }
    $car | Select-Object -Property * -ExcludeProperty parts, bodyParts, partsDetail
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $a, $b) {
    foreach ($key in "Scene:RaceTrack", "Scene:SpeedTrack", "Mode:CarDrive") { Cmd $name guard-allow $key | Out-Null }
}

Save "look_read_before" (Try-Cmd $a look-read "2")
Save "look_apply" (Try-Cmd $a look-apply "2 3")
Start-Sleep -Seconds 3
Save "look_read_after_A" (Try-Cmd $a look-read "2")
Save "look_read_after_B" (Try-Cmd $b look-read "2")

Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Write-Host "car ready A=$(Wait-Ready $a 0) B=$(Wait-Ready $b 0)"
$moneyBefore = (Cmd $a dump).stats
Save "stand_new" (Try-Cmd $a stand-new "0")
Start-Sleep -Seconds 25
Save "stand_A" ((Cmd $a dump).tools.EngineStand1)
Save "stand_B" ((Cmd $b dump).tools.EngineStand1)
Save "stats_before" $moneyBefore
Save "stats_after" ((Cmd $a dump).stats)
Save "car_before_A" (Car-Info $a)

foreach ($track in "RaceTrack", "SpeedTrack") {
    Save "${track}_go" (Try-Cmd $a track-go "0 $track")
    $arrived = Wait-Scene $a "(?i)(track|race|speed)"
    Write-Host "A on $track : $arrived"
    Start-Sleep -Seconds 15
    Save "${track}_state_A" (Try-Cmd $a track-state)
    Save "${track}_status_A" (Get-HarnessStatus -Instance $a)
    $dumpB = Cmd $b dump
    Save "${track}_B_away" $dumpB.away
    Save "${track}_B_remoteCars" $dumpB.remoteCars
    Save "${track}_B_car" (Car-Info $b)
    Save "${track}_drive" (Try-Cmd $a testdrive-drive "2500")
    Start-Sleep -Seconds 2
    Save "${track}_state_A2" (Try-Cmd $a track-state)
    Save "${track}_return" (Try-Cmd $a track-return)
    $back = Wait-Scene $a "^garage$" 180
    Write-Host "A back in the garage after $track : $back"
    Write-Host "ready A=$(Wait-Ready $a 0) B=$(Wait-Ready $b 0)"
    Start-Sleep -Seconds 5
    Save "${track}_after_A" (Try-Cmd $a track-state)
    Save "${track}_car_A" (Car-Info $a)
    Save "${track}_car_B" (Car-Info $b)
}

$Ctx.Result.passed = $true

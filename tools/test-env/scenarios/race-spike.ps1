# areas: driving
# run-all: skip
# track-races spike 1.1: Ann drives to the race track; the states of the game's Prepare and Restart coroutines are
# recorded first as the game runs them headless, then with WaitForEndOfFrame yielded as null; the light sequence is
# started by pressing the throttle, and ten restarts are timed from RunRestart to the throttle wait and from the throttle
# to readySetGo. Output: spike_*.json in the run folder.
param($Ctx, [int]$Runs = 10)

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "spike_$Label.json") -Encoding utf8 }
function Note([string]$Message) { $script:Ctx.Result.notes += $Message; Write-Host "note: $Message" -ForegroundColor Yellow }
function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}
function Wait-Events([scriptblock]$Condition, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $e = Cmd $a race-spike-events
        if (& $Condition $e) { return $e }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $e
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Cmd $a guard-allow "Mode:CarDrive" | Out-Null
Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0
Cmd $a race-spike-eof "keep" | Out-Null

Cmd $a track-go "0 RaceTrack" | Out-Null
Wait-HarnessStatus -Instance $a -TimeoutSec 180 -What "race track" -Condition { param($s) $s.scene -match "(?i)race" -and $s.playable } | Out-Null
Start-Sleep -Seconds 8
$plain = Cmd $a race-spike-events "clear"
Save "1_headless_arrival" $plain
Note "headless arrival: prepare states $(@($plain.events | ForEach-Object { "$($_.detail.from)->$($_.detail.to)" }) -join ' '), readySetGo $($plain.readySetGo), fader $($plain.faderComplete), WaitForSecond $($plain.waitForSecond) s"

Cmd $a race-spike-restart | Out-Null
Start-Sleep -Seconds 8
$plainRestart = Cmd $a race-spike-events "clear"
Save "2_headless_restart" $plainRestart
Note "headless restart: $(@($plainRestart.events | ForEach-Object { "$($_.what) $($_.detail.from)->$($_.detail.to) $($_.detail.current)" }) -join '; ')"

Cmd $a race-spike-eof "null" | Out-Null
$timings = @()
for ($i = 1; $i -le $Runs; $i++) {
    Cmd $a race-spike-events "clear" | Out-Null
    Cmd $a race-spike-throttle "off" | Out-Null
    Cmd $a race-spike-restart | Out-Null
    Wait-Events { param($e) @($e.events | Where-Object { $_.what -eq "prepare" -and $_.detail.to -eq 4 }).Count -ge 1 } 30 | Out-Null
    Start-Sleep -Seconds 1
    Cmd $a race-spike-throttle "on" | Out-Null
    $done = Wait-Events { param($e) @($e.events | Where-Object { $_.what -eq "green" }).Count -ge 1 } 15
    Cmd $a race-spike-throttle "off" | Out-Null
    Save "3_run$i" $done
    $ev = @($done.events)
    $t0 = ($ev | Where-Object { $_.what -eq "run-restart" } | Select-Object -First 1).t
    $atGate = ($ev | Where-Object { $_.what -eq "prepare" -and $_.detail.to -eq 4 } | Select-Object -First 1).t
    $press = ($ev | Where-Object { $_.what -eq "throttle-on" } | Select-Object -First 1).t
    $lightsStart = ($ev | Where-Object { $_.what -eq "prepare" -and $_.detail.to -eq 5 } | Select-Object -First 1).t
    $green = ($ev | Where-Object { $_.what -eq "green" } | Select-Object -First 1).t
    $row = [pscustomobject]@{ run = $i; restartToGate = $atGate - $t0; pressToLights = $lightsStart - $press; lightsToGreen = $green - $lightsStart; pressToGreen = $green - $press; fps = [math]::Round($done.fps) }
    $timings += $row
    Note "run ${i}: $($row | ConvertTo-Json -Compress)"
    Start-Sleep -Seconds 2
}
Save "timings" $timings
Cmd $a race-spike-eof "keep" | Out-Null

$Ctx.Result.passed = $true

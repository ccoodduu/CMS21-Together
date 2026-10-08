# areas: driving
# run-all: skip
# ride-along spike: A and B take two garage cars to the test track (as drive-track does). Both save ride-probe (track
# camera, own car seats and head, observer car seats) before and while A drives, for docs/spikes/ride-along.md.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "ride_$Label.json") -Encoding utf8 }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Wait-Track([string]$Name) {
    try { Wait-HarnessStatus -Instance $Name -TimeoutSec 150 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null; $true } catch { $false }
}
function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}
function Wait-Copy([string]$Observer, [int]$DriverId) {
    $deadline = (Get-Date).AddSeconds(60)
    do {
        $car = @((Cmd $Observer dump).remoteCars.cars) | Where-Object { $_.playerId -eq $DriverId } | Select-Object -First 1
        if ($car -and ($car.visible -or $car.mode -eq "failed")) { return $car }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    return $car
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$idA = (Get-HarnessStatus -Instance $a).playerId
foreach ($name in $a, $b) { Cmd $name guard-allow "Mode:CarDrive" | Out-Null }

Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0; Wait-Ready $b 0
Cmd $b car-spawn "1 car_boltatlanta 0" | Out-Null
Wait-Ready $a 1; Wait-Ready $b 1
Start-Sleep -Seconds 3
Save "garage_A" (Cmd $a ride-probe)

Cmd $a testdrive-go "0" | Out-Null
Check (Wait-Track $a) "A reached the test track"
Start-Sleep -Seconds 4
Cmd $b testdrive-go "1" | Out-Null
Check (Wait-Track $b) "B reached the test track"
$copy = Wait-Copy $b $idA
Check ($copy -and $copy.visible) "B shows A's car"
Save "track_A" (Cmd $a ride-probe)
Save "track_B" (Cmd $b ride-probe)

Cmd $a drive-input "0.6 0 4" | Out-Null
Start-Sleep -Seconds 2
Save "moving_B" (Cmd $b ride-probe)
Save "moving_A" (Cmd $a ride-probe)
Start-Sleep -Seconds 3
Cmd $a drive-stop | Out-Null

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

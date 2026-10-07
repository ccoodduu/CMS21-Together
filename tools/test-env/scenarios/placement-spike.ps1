# run-all: skip
# areas: placement
# Runtime spike for sync-car-placement-and-lifts group 1 (docs/spikes/car-placement.md "Still needs a runtime check"):
# A alone, connected, with the placement trace on. Records lift mapping, lift movement, the quiet place apply, the
# vanilla ChangeCarPos coroutine, park/unpark order, carsOnParking access and the NewCarData codec round trip.
# Everything goes to spike.json in the run folder; the scenario only fails on errors.
param($Ctx)

$a = $Ctx.Instances[0]
$out = [ordered]@{}
function Step([string]$Name, $Value) { $script:out[$Name] = $Value; Write-Host "$Name : $($Value | ConvertTo-Json -Depth 6 -Compress)" }

function Wait-Loaded([int]$Loader, [int]$TimeoutSec = 90) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Send-HarnessCommand -Instance $a -Verb car-loaded -Arguments "$Loader"
        if ($r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "loader $Loader not loaded"
}

function Wait-LiftsStill([int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 500
        $l = @(Send-HarnessCommand -Instance $a -Verb lifters)
    } while (@($l | Where-Object { $_.isMoving }).Count -gt 0 -and (Get-Date) -lt $deadline)
    return $l
}

Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments "Off" | Out-Null
Send-HarnessCommand -Instance $a -Verb placement-trace -Arguments "on" | Out-Null

Step "lifters-empty" @(Send-HarnessCommand -Instance $a -Verb lifters)
Step "lift-empty-up" (Send-HarnessCommand -Instance $a -Verb lift -Arguments "0 up")
Start-Sleep -Seconds 2
Step "lifters-after-empty-up" @(Send-HarnessCommand -Instance $a -Verb lifters)

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 car_boltatlanta 0" | Out-Null
Wait-Loaded 0 | Out-Null
Step "placement-after-spawn" @(Send-HarnessCommand -Instance $a -Verb placement)
Step "car-place-lifter1" (Send-HarnessCommand -Instance $a -Verb car-place -Arguments "0 CarLifter1")
Start-Sleep -Seconds 1
Step "lifters-with-car" @(Send-HarnessCommand -Instance $a -Verb lifters)

$lifters = @(Send-HarnessCommand -Instance $a -Verb lifters)
$withCar = @($lifters | Where-Object { $_.connectedLoader -eq 0 })
$liftIndex = if ($withCar.Count -gt 0) { $withCar[0].index } else { @($lifters | Where-Object { $_.nearestPlace -eq "CarLifter1" } | Sort-Object distance)[0].index }
Step "lift-index-for-car" $liftIndex
Step "lift-up-1" (Send-HarnessCommand -Instance $a -Verb lift -Arguments "$liftIndex up")
Step "lifters-after-up-1" (Wait-LiftsStill)
Step "lift-up-2" (Send-HarnessCommand -Instance $a -Verb lift -Arguments "$liftIndex up")
Step "lifters-after-up-2" (Wait-LiftsStill)
Step "lift-down-1" (Send-HarnessCommand -Instance $a -Verb lift -Arguments "$liftIndex down")
Step "lifters-after-down-1" (Wait-LiftsStill)

Step "car-move-entrance2" (Send-HarnessCommand -Instance $a -Verb car-move -Arguments "0 Entrance2")
Start-Sleep -Seconds 5
Step "placement-after-move" @(Send-HarnessCommand -Instance $a -Verb placement)
Step "lifters-after-move" @(Send-HarnessCommand -Instance $a -Verb lifters)

Step "parking-before" (Send-HarnessCommand -Instance $a -Verb parking)
Step "parking-probe-0-before" (Send-HarnessCommand -Instance $a -Verb parking-probe -Arguments "0")
Step "park" (Send-HarnessCommand -Instance $a -Verb park -Arguments "0")
Start-Sleep -Seconds 8
$parking = Send-HarnessCommand -Instance $a -Verb parking
Step "parking-after-park" $parking
Step "placement-after-park" @(Send-HarnessCommand -Instance $a -Verb placement)
$slot = @($parking.slots | Where-Object { $_.carToLoad -eq "car_boltatlanta" })[0].index
if ($null -ne $slot) {
    Step "parking-probe-slot" (Send-HarnessCommand -Instance $a -Verb parking-probe -Arguments "$slot")
    Step "unpark" (Send-HarnessCommand -Instance $a -Verb unpark -Arguments "$slot 1")
    Wait-Loaded 1 | Out-Null
    Start-Sleep -Seconds 3
    Step "parking-after-unpark" (Send-HarnessCommand -Instance $a -Verb parking)
    Step "placement-after-unpark" @(Send-HarnessCommand -Instance $a -Verb placement)
} else {
    $Ctx.Result.notes += "the parked car was not found in any slot"
}

Send-HarnessCommand -Instance $a -Verb placement-trace -Arguments "off" | Out-Null
$out | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "spike.json") -Encoding utf8
$Ctx.Result.passed = $true

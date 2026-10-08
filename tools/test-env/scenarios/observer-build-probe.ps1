# areas: driving
# run-all: skip
# Probe, no checks: A and B take two garage cars to the test track and each builds the other's observer car while
# stack-sample records where a frozen frame spends its time and method-time (optional, -ScenarioArgs @{ Targets =
# "CarLoader.CreateParts,PartScript.Start"; MinMs = 30 }) times the given game methods. Results go to
# probe_stack_<instance>.json (map the RVAs with the IL2CPP dump) and probe_time_<instance>.json in the run folder.
param($Ctx, [string]$Targets = "", [int]$MinMs = 50)

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments -TimeoutSec 60 }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "probe_$Label.json") -Encoding utf8 }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
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

function Wait-Built([string]$Observer, [int]$DriverId) {
    $deadline = (Get-Date).AddSeconds(90)
    do {
        $car = @((Cmd $Observer dump).remoteCars.cars) | Where-Object { $_.playerId -eq $DriverId } | Select-Object -First 1
        if ($car -and ($car.visible -or $car.mode -eq "failed")) { return $car }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    return $car
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$idA = (Get-HarnessStatus -Instance $a).playerId
$idB = (Get-HarnessStatus -Instance $b).playerId
foreach ($name in $a, $b) { Cmd $name guard-allow "Mode:CarDrive" | Out-Null }

Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0; Wait-Ready $b 0
Cmd $b car-spawn "1 car_boltatlanta 0" | Out-Null
Wait-Ready $a 1; Wait-Ready $b 1
foreach ($name in $a, $b) {
    if ($Targets) { Save "patch_$name" (Cmd $name method-time "on $Targets min=$MinMs") }
}

Cmd $a testdrive-go "0" | Out-Null
Wait-HarnessStatus -Instance $a -TimeoutSec 150 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null
Cmd $a stack-sample "on" | Out-Null
Start-Sleep -Seconds 4
Cmd $b testdrive-go "1" | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 150 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null

Cmd $b stack-sample "on" | Out-Null
$carA = Wait-Built $b $idA
$carB = Wait-Built $a $idB
Start-Sleep -Seconds 2
foreach ($name in $a, $b) {
    Save "stack_$name" (Cmd $name stack-sample "report")
    $report = Cmd $name method-time "report"
    Save "time_$name" $report
    $Ctx.Result.notes += "$name slowest: " + (@($report.top | Select-Object -First 6 | ForEach-Object { "$($_.method) $($_.maxMs) ms" }) -join ", ")
}
$Ctx.Result.notes += "observer on B: $($carA.mode) build $($carA.buildSeconds) s longest frame $($carA.longestFrame) s; on A: $($carB.mode) build $($carB.buildSeconds) s longest frame $($carB.longestFrame) s"
foreach ($name in $a, $b) { Cmd $name method-time "off" | Out-Null; Cmd $name stack-sample "off" | Out-Null }
Cmd $a testdrive-finish "all" | Out-Null
Cmd $b testdrive-finish "all" | Out-Null
$Ctx.Result.passed = $true

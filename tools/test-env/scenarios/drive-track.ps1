# areas: driving, testdrive
# remote-visual-feedback 8.4 and 10.3: A and B take two different garage cars to the test track at the same time (row
# 13 claims both). Each sees the other's car as an inert observer car that follows the driver's path, comes to rest
# where the driver stopped and disappears when the driver leaves; both results apply after the return. 12.2: the
# server's perf lines for the drive packets are saved.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "drive_$Label.json") -Encoding utf8 }

function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
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

function RemoteCar([string]$Observer, [int]$DriverId) {
    @((Cmd $Observer dump).remoteCars.cars) | Where-Object { $_.playerId -eq $DriverId } | Select-Object -First 1
}

function Wait-RemoteCar([string]$Observer, [int]$DriverId, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $car = RemoteCar $Observer $DriverId
        if ($car -and ($car.visible -or $car.mode -eq "failed")) { return $car }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    return $car
}

function Distance($p, $q) { [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }

function Follow-Error([string]$Observer, [string]$Driver, [int]$DriverId, [int]$Samples) {
    $worst = 0.0
    for ($i = 0; $i -lt $Samples; $i++) {
        Start-Sleep -Milliseconds 600
        $car = RemoteCar $Observer $DriverId
        if (-not $car -or -not $car.position) { return 999 }
        $truth = Cmd $Driver drive-history ([string]::Format([cultureinfo]::InvariantCulture, "{0}", $car.renderTime))
        $worst = [math]::Max($worst, (Distance $car.position $truth.position))
    }
    return [math]::Round($worst, 3)
}

function Wait-InputDone([string]$Name, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do { Start-Sleep -Milliseconds 500; $s = Cmd $Name drive-input-state } while ($s.running -and (Get-Date) -lt $deadline)
    return $s
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
Start-Sleep -Seconds 3
$codec = Cmd $a drive-codec-check
Check $codec.passed "drive state codec round trip within the quantization ($($codec | ConvertTo-Json -Compress))"
$mileageA = [int]((Cmd $a cardetails-show "0").Info | ConvertFrom-Json).Mileage
$mileageB = [int]((Cmd $a cardetails-show "1").Info | ConvertFrom-Json).Mileage

Send-ServerCommand "perf reset"
Cmd $a testdrive-go "0" | Out-Null
Check (Wait-Track $a) "A reached the test track with car 0"
Start-Sleep -Seconds 4
Cmd $b testdrive-go "1" | Out-Null
Check (Wait-Track $b) "B reached the test track with car 1"
$grants = @(Get-ServerLogLines | Where-Object { $_ -match "\[Away\] TestTrack on loader (0 granted to client $idA|1 granted to client $idB)" })
$releases = @(Get-ServerLogLines | Where-Object { $_ -match "\[Away\] TestTrack on loader [01] .*released" })
Check ($grants.Count -eq 2 -and $releases.Count -eq 0) "both away claims granted and held at once ($($grants.Count) grants, $($releases.Count) releases)"

$carA = Wait-RemoteCar $b $idA
$carB = Wait-RemoteCar $a $idB
Save "observer_B_start" $carA
Save "observer_A_start" $carB
Check ($carA -and $carA.visible -and $carA.mode -match "^ghost") "B shows A's car (A drove before B arrived; mode $($carA.mode), build $($carA.buildSeconds) s, longest frame $($carA.longestFrame) s, $($carA.buildMb) MB)"
Check ($carB -and $carB.visible -and $carB.mode -match "^ghost") "A shows B's car (mode $($carB.mode))"
Check ($carA.kinematic -and $carA.collidersOff) "A's car on B is kinematic with colliders off"

Cmd $a drive-input "0.6 0 5" | Out-Null
$err1 = Follow-Error $b $a $idA 6
Wait-InputDone $a | Out-Null
Cmd $a drive-input "0.4 0.5 3" | Out-Null
$err2 = Follow-Error $b $a $idA 4
$inputA = Wait-InputDone $a
Check ($err1 -le 1.5 -and $err2 -le 1.5) "B follows A's path within 1.5 m (straight $err1 m, turning $err2 m; A input mode $($inputA.mode))"
$moved = Distance (Cmd $a drive-history "0").position (Cmd $a drive-probe).capturePosition
Check ($moved -gt 5) "A's car moved ($([math]::Round($moved, 1)) m)"

Cmd $a drive-stop | Out-Null
Wait-InputDone $a | Out-Null
Start-Sleep -Seconds 2
$finalA = (Cmd $a drive-probe).capturePosition
$carA = RemoteCar $b $idA
Save "observer_B_stopped" $carA
$rest = Distance $carA.position $finalA
Check ($rest -le 0.3) "B's view of A's car rests where A stopped ($([math]::Round($rest, 3)) m)"
Check ($carA.snaps -eq 0) "no snaps on B (snaps $($carA.snaps), late $($carA.late), max extrapolation $($carA.maxExtrapolatedMs) ms)"
$frontA = (Cmd $a ride-state).own.driverSeat.front
$frontCopy = (Cmd $b ride-state "$idA").copy.driverSeat.front
$facing = if ($frontA -and $frontCopy) { [math]::Round([math]::Acos([math]::Max(-1, [math]::Min(1, $frontA.x * $frontCopy.x + $frontA.y * $frontCopy.y + $frontA.z * $frontCopy.z))) * 180 / [math]::PI, 1) } else { 999 }
Check ($facing -le 5) "B shows A's car facing the way it faces on A ($facing degrees between the fronts; ride-along spike: it was turned 180 degrees)"

$beforeB = (Cmd $b drive-probe).capturePosition
Cmd $b drive-input "0.6 0 3" | Out-Null
Wait-InputDone $b | Out-Null
$afterB = (Cmd $b drive-probe).capturePosition
Check ((Distance $beforeB $afterB) -gt 2) "B's own car drives normally while A's car is shown ($([math]::Round((Distance $beforeB $afterB), 1)) m)"
Cmd $b drive-stop | Out-Null

$mark = Get-ServerLogMark
Send-ServerCommand "perf top 30"
Start-Sleep -Seconds 3
$serverLog = Get-ChildItem -LiteralPath (Join-Path $Ctx.ServerDir "Log") -Filter "Log_*.txt" | Sort-Object LastWriteTime | Select-Object -Last 1
$perf = @(Get-Content -LiteralPath $serverLog.FullName | Select-Object -Skip $mark)
$perf | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "perf_drive.txt") -Encoding utf8
$Ctx.Result.notes += @($perf | Where-Object { $_ -match "CarDrive" } | ForEach-Object { "perf: $($_.Trim())" })

Cmd $a testdrive-drive "3000" | Out-Null
Cmd $a testdrive-finish "all" | Out-Null
$deadline = (Get-Date).AddSeconds(30)
do { Start-Sleep -Seconds 1; $gone = -not (RemoteCar $b $idA) } while (-not $gone -and (Get-Date) -lt $deadline)
Check $gone "A's car disappears from B's track when A leaves"
Wait-InGarage $a 180

Cmd $b testdrive-drive "2000" | Out-Null
Cmd $b testdrive-finish "all" | Out-Null
Wait-InGarage $b 180
$deadline = (Get-Date).AddSeconds(30)
do { Start-Sleep -Seconds 1; $awayA = @((Cmd $a dump).away); $awayB = @((Cmd $b dump).away) } while (($awayA.Count -ne 0 -or $awayB.Count -ne 0) -and (Get-Date) -lt $deadline)
Check ($awayA.Count -eq 0 -and $awayB.Count -eq 0) "both claims released after the return"
Start-Sleep -Seconds 4
$mA0 = [int]((Cmd $a cardetails-show "0").Info | ConvertFrom-Json).Mileage
$mB1 = [int]((Cmd $b cardetails-show "1").Info | ConvertFrom-Json).Mileage
Check ($mA0 -ge $mileageA + 3 -and $mB1 -ge $mileageB + 2) "both drive results applied (car 0 $mileageA -> $mA0 km, car 1 $mileageB -> $mB1 km)"
$differ = Compare-HarnessDumps -Left (Cmd $a dump) -Right (Cmd $b dump) -Sections cars, away
Check ($differ.Count -eq 0) "A and B have the same cars and away sections (differ: $($differ -join ', '))"
Check (@((Cmd $a dump).remoteCars.cars).Count -eq 0 -and @((Cmd $b dump).remoteCars.cars).Count -eq 0) "no observer cars left in the garage"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

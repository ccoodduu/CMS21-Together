# areas: driving, testdrive, presence
# remote-visual-feedback 12.1: A drives on the test track; B joins the session and travels there mid-drive and sees
# A's car within 2 s where A is now (no replay of the path). B then disconnects from the track, goes back to the menu,
# reconnects and travels again while A still drives: the same. The new session does not keep the track car selected
# in the session B left. A's disconnect removes the car on B.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

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

function Distance($p, $q) { [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }

function Arrive([string]$What) {
    Cmd $b testdrive-go "1" | Out-Null
    Check (Wait-Track $b) "$What`: B reached the test track"
    $arrived = Get-Date
    do {
        $car = RemoteCar $b $idA
        if ($car -and $car.visible) { break }
        Start-Sleep -Milliseconds 200
    } while (((Get-Date) - $arrived).TotalSeconds -lt 20)
    $seconds = [math]::Round(((Get-Date) - $arrived).TotalSeconds, 2)
    Check ($car -and $car.visible) "$What`: B shows A's car ($($car.shownAfter) s after the drive reached B, build $($car.buildSeconds) s, mode $($car.mode))"
    Start-Sleep -Milliseconds 800
    $car = RemoteCar $b $idA
    $local = (Cmd $a dump).remoteCars.local
    $truth = Cmd $a drive-history ([string]::Format([cultureinfo]::InvariantCulture, "{0}", $car.renderTime))
    $offPath = Distance $car.position $truth.position
    $behind = $local.time - $car.renderTime
    Check ($offPath -le 1.5 -and $behind -le 1.0) "$What`: A's car on B is where A is now ($([math]::Round($offPath, 2)) m from A's path, $([math]::Round($behind, 2)) s behind A)"
    Check ($car.shownAfter -le 1.5) "$What`: A's car shown within 1.5 s of the drive reaching B ($($car.shownAfter) s)"
    $Ctx.Result.notes += "$What`: car shown $($car.shownAfter) s after the drive reached B (build $($car.buildSeconds) s, longest frame $($car.longestFrame) s)"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
$idA = (Get-HarnessStatus -Instance $a).playerId
Cmd $a guard-allow "Mode:CarDrive" | Out-Null
Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0
Cmd $a car-spawn "1 car_boltatlanta 0" | Out-Null
Wait-Ready $a 1
Start-Sleep -Seconds 3

Cmd $a testdrive-go "0" | Out-Null
Check (Wait-Track $a) "A reached the test track"
Start-Sleep -Seconds 4
Cmd $a drive-input "0.3 0.15 400" | Out-Null
Start-Sleep -Seconds 3

Connect-HarnessInstance $b; Wait-InGarage $b
Cmd $b guard-allow "Mode:CarDrive" | Out-Null
Wait-Ready $b 1
Start-Sleep -Seconds 3
Arrive "travel mid-drive"

Cmd $b disconnect | Out-Null
Cmd $b to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Check ((Cmd $a dump).remoteCars.local.active) "A still drives while B is away"
Connect-HarnessInstance $b; Wait-InGarage $b
$selected = (Cmd $b track-state).selected
Check (-not $selected) "B's new session does not keep the track car selected in the session B left ('$selected')"
Cmd $b guard-allow "Mode:CarDrive" | Out-Null
Wait-Ready $b 1
Start-Sleep -Seconds 3
Arrive "after a reconnect"

Cmd $a disconnect | Out-Null
$deadline = (Get-Date).AddSeconds(20)
do { Start-Sleep -Seconds 1; $gone = -not (RemoteCar $b $idA) } while (-not $gone -and (Get-Date) -lt $deadline)
Check $gone "A's disconnect removes A's car from B's track"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

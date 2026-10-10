# areas: placement, connect, persistence
# A car on an entrance place gets a random extra yaw (CarLoader.SetAdditionalCarRot, from the place's
# CarPlaceRotation). Each game rolled its own, so the same car pointed up to ~15 degrees apart on A and B and its far
# parts sat up to ~0.5 m apart (the ping batch flake). The car root's yaw and the world position of the part farthest
# from the root must agree (0.5 degrees, 2 cm) after: B's late join to two cars A spawned at Entrance1 and Entrance2,
# a move to Entrance3, a park and unpark, and a server restart.
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}

function Wait-Empty([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $c = @((Cmd $Name dump).cars | Where-Object { $_.index -eq $Loader })[0]
        if (-not $c.carToLoad -and $c.syncState -eq "Empty") { return }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not empty"
}

function Distance($p, $q) { [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }
function YawApart([double]$p, [double]$q) { $d = [math]::Abs($p - $q) % 360; [math]::Min($d, 360 - $d) }

function Compare-Pose([string]$What, [int]$Loader, [string]$Place, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $pa = Cmd $a car-pose "$Loader"
        $pb = Cmd $b car-pose "$Loader $($pa.key)"
        $yaw = YawApart $pa.yaw $pb.yaw
        $part = if ($pa.part -and $pb.part) { Distance $pa.part $pb.part } else { 99 }
        $same = $pa.place -eq $Place -and $pb.place -eq $Place -and $yaw -le 0.5 -and $part -le 0.02
        if ($same) { break }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    Write-Host "  $What loader $Loader A: place $($pa.place) range $($pa.placeRotation -join '..') rot $($pa.additionalCarRot) yaw $($pa.yaw) $($pa.key) $($pa.part | ConvertTo-Json -Compress)"
    Write-Host "  $What loader $Loader B: place $($pb.place) range $($pb.placeRotation -join '..') rot $($pb.additionalCarRot) yaw $($pb.yaw) $($pb.key) $($pb.part | ConvertTo-Json -Compress)"
    Check ($pa.place -eq $Place -and $pb.place -eq $Place) "$What`: loader $Loader is at $Place on A and B ($($pa.place), $($pb.place))"
    Check ($null -ne $pa.placeRotation) "$What`: $Place turns the car by a random angle ($($pa.placeRotation -join '..'))"
    Check ($yaw -le 0.5) "$What`: loader $Loader points the same way on A and B ($([math]::Round($yaw, 3)) degrees apart)"
    Check ($part -le 0.02) "$What`: loader $Loader's far part $($pa.key) is at the same spot on A and B ($([math]::Round($part, 4)) m apart)"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Cmd $a guard-set "Off" | Out-Null

Cmd $a car-spawn "0 $car 0 Entrance1" | Out-Null
Wait-Ready $a 0 | Out-Null
Cmd $a car-spawn "1 $car 0 Entrance2" | Out-Null
Wait-Ready $a 1 | Out-Null
Start-Sleep -Seconds 2

Connect-HarnessInstance $b; Wait-InGarage $b
Cmd $b guard-set "Off" | Out-Null
Wait-Ready $b 0 | Out-Null
Wait-Ready $b 1 | Out-Null
Start-Sleep -Seconds 3
Compare-Pose "late join" 0 "Entrance1"
Compare-Pose "late join" 1 "Entrance2"

Cmd $a car-move "0 Entrance3" | Out-Null
Start-Sleep -Seconds 5
Compare-Pose "move" 0 "Entrance3"

$mark = Get-ServerLogMark
Cmd $a park "1" | Out-Null
$line = Wait-ServerLog -Pattern "\[Parking\] Loader 1 \(.*\) parked in slot (\d+)" -After $mark -TimeoutSec 20
$slot = [int]([regex]::Match($line, "parked in slot (\d+)").Groups[1].Value)
Wait-Empty $a 1; Wait-Empty $b 1
Cmd $a unpark "$slot 1" | Out-Null
Wait-Ready $a 1 | Out-Null
Wait-Ready $b 1 | Out-Null
Start-Sleep -Seconds 3
$unparkedAt = (Cmd $a car-pose "1").place
Write-Host "unparked at $unparkedAt"
Compare-Pose "unpark" 1 $unparkedAt

$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
Stop-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 30 -What "menu after kill" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
    Cmd $name mp-ui "ok" | Out-Null
}
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Wait-Ready $name 0 | Out-Null; Wait-Ready $name 1 | Out-Null }
Start-Sleep -Seconds 3
Compare-Pose "restart" 0 "Entrance3"
Compare-Pose "restart" 1 $unparkedAt

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

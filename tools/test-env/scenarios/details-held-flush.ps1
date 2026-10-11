# areas: details
# Soak 2026-10-10 (contention 251, a details change never sent): a change of a section the 1 Hz poll does not read
# (plates) made while the car's details are busy must still reach the other player once they are free. A opens a 3 s
# test apply window, changes the rear plate texture inside it, and B must get the plate.
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "0"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader 0 not Ready"
}

function Plates([string]$Name) { (Send-HarnessCommand -Instance $Name -Verb cardetails-show -Arguments "0").plates }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 3

$before = Plates $b
$flush = Send-HarnessCommand -Instance $a -Verb cardetails-flush -Arguments "0 applying 3000"
Check ($flush.result -eq "Deferred") "A's details are busy for 3 s ($($flush.result))"
$plate = Send-HarnessCommand -Instance $a -Verb cardetails-plate -Arguments "0 rear Monaco"
Check ($plate.plates.LicensePlateRearTex -eq "Monaco") "A's rear plate texture is Monaco"

$deadline = (Get-Date).AddSeconds(20)
do {
    Start-Sleep -Seconds 1
    $plateA = Plates $a
    $plateB = Plates $b
    $same = $plateB -eq $plateA -and $plateB -ne $before
} while (-not $same -and (Get-Date) -lt $deadline)
Check $same "B gets A's rear plate changed during the busy window (A $plateA, B $plateB)"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

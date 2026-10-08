# areas: details, cars, connect
# sync-car-details: A spawns a car; B gets A's details snapshot (fluids, wheels, alignment, tuning, paint, cosmetics,
# plates, info). A changes fluids, alignment, mileage, dust and a plate locally; B follows within a few seconds. B then
# leaves and rejoins and still has A's details (late join through the car-details snapshot).
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

function Wait-SameDetails([string]$What, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Seconds 1
        $da = Send-HarnessCommand -Instance $a -Verb cardetails-show -Arguments "0"
        $db = Send-HarnessCommand -Instance $b -Verb cardetails-show -Arguments "0"
        $differ = @($da.PSObject.Properties.Name | Where-Object { $da.$_ -ne $db.$_ })
    } while ($differ.Count -gt 0 -and (Get-Date) -lt $deadline)
    Check ($differ.Count -eq 0) "$What`: A and B have the same details (differ: $($differ -join ', '))"
    foreach ($section in $differ) { Write-Host "  $section A: $($da.$section)"; Write-Host "  $section B: $($db.$section)" }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Wait-SameDetails "spawn snapshot"

$changed = Send-HarnessCommand -Instance $a -Verb cardetails-randomize -Arguments "0"
Write-Host "A changed details: $($changed | ConvertTo-Json -Compress)"
Wait-SameDetails "live changes"
$mark = Get-ServerLogMark
Send-ServerCommand "cardetails 0"
$stored = try { Wait-ServerLog -Pattern "\[CarDetails\] Loader 0: \{" -After $mark -TimeoutSec 10 } catch { $null }
Check ($stored -match [regex]::Escape($changed.plate)) "the server stores A's plate ($($changed.plate))"

Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 60 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Ready $b | Out-Null
Wait-SameDetails "late join"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

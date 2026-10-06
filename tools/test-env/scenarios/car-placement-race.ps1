# sync-car-placement-and-lifts races (net-hold on both clients, so each acts before seeing the other):
# both raise the same lift (it moves one step), both park a car into the same preferred slot (different slots),
# both take the same parked car onto the same loader (one copy), a parking swap, a priced arrival and a level unlock.
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$Loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}

function Placement([string]$Name) { (Send-HarnessCommand -Instance $Name -Verb dump).placement }

function Wait-SamePlacement([string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 40) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 700
        $pa = Placement $a
        $ja = $pa | ConvertTo-Json -Depth 6 -Compress
        $jb = Placement $b | ConvertTo-Json -Depth 6 -Compress
        if ($ja -eq $jb -and (& $Condition $pa)) { break }
    } while ((Get-Date) -lt $deadline)
    Check ($ja -eq $jb) "$What`: A and B agree ($ja)"
    if ($ja -ne $jb) { Write-Host "  B: $jb" }
    Check ([bool](& $Condition $pa)) "$What`: expected state reached"
    return $pa
}

function Hold([string]$Mode) { foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments $Mode | Out-Null } }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
Wait-Ready $a 0 | Out-Null; Wait-Ready $b 0 | Out-Null
Send-HarnessCommand -Instance $a -Verb car-move -Arguments "0 CarLifter1" | Out-Null
Wait-SamePlacement "car on lift 1" { param($p) @($p.cars | Where-Object { $_.loader -eq 0 -and $_.inPlace -eq "CarLifter1" }).Count -eq 1 } | Out-Null

$mark = Get-ServerLogMark
Hold "on"
Send-HarnessCommand -Instance $a -Verb lift -Arguments "0 up" | Out-Null
Send-HarnessCommand -Instance $b -Verb lift -Arguments "0 up" | Out-Null
Start-Sleep -Seconds 1
Hold "off"
Wait-SamePlacement "both raised lift 0 at once" { param($p) @($p.lifters | Where-Object { $_.index -eq 0 -and $_.state -eq "Middle" }).Count -eq 1 } | Out-Null
Check ([bool](Wait-ServerLog -Pattern "Lift 0 0->1 from client \d+ refused" -After $mark -TimeoutSec 5)) "the second lift press was refused"
Send-HarnessCommand -Instance $a -Verb lift -Arguments "0 down" | Out-Null
Wait-SamePlacement "lift back down" { param($p) @($p.lifters | Where-Object { $_.index -eq 0 -and $_.state -eq "OnFloor" }).Count -eq 1 } | Out-Null

Send-HarnessCommand -Instance $b -Verb car-spawn -Arguments "1 $car 0" | Out-Null
Wait-Ready $b 1 | Out-Null; Wait-Ready $a 1 | Out-Null
Hold "on"
Send-HarnessCommand -Instance $a -Verb park -Arguments "0" | Out-Null
Send-HarnessCommand -Instance $b -Verb park -Arguments "1" | Out-Null
Start-Sleep -Seconds 3
Hold "off"
$p = Wait-SamePlacement "both parked at once" { param($p) @($p.cars).Count -eq 0 -and @($p.parking.slots).Count -eq 2 }

$slot = @($p.parking.slots)[0].index
Hold "on"
Send-HarnessCommand -Instance $a -Verb unpark -Arguments "$slot 2" | Out-Null
Send-HarnessCommand -Instance $b -Verb unpark -Arguments "$slot 2" | Out-Null
Start-Sleep -Seconds 6
Hold "off"
$p = Wait-SamePlacement "both took the same car out" { param($p) @($p.cars | Where-Object { $_.loader -eq 2 }).Count -eq 1 -and @($p.cars).Count -eq 1 -and @($p.parking.slots).Count -eq 1 } 60
foreach ($name in $Ctx.Instances) { Wait-Ready $name 2 | Out-Null }
$ra = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "2"
$rb = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "2"
Check ($ra.stateHash -eq $rb.stateHash) "the unparked car's parts match ($($ra.stateHash) / $($rb.stateHash))"

$left = @($p.parking.slots)[0].index
$target = if ($left -eq 5) { 6 } else { 5 }
Send-HarnessCommand -Instance $a -Verb park-swap -Arguments "$left $target" | Out-Null
Wait-SamePlacement "parking swap" { param($p) @($p.parking.slots | Where-Object { $_.index -eq $target }).Count -eq 1 -and @($p.parking.slots).Count -eq 1 } | Out-Null

$moneyBefore = (Send-HarnessCommand -Instance $a -Verb dump).stats.money
Send-HarnessCommand -Instance $b -Verb park-incoming -Arguments "$target 1000" | Out-Null
Wait-SamePlacement "priced arrival" { param($p) @($p.parking.slots).Count -eq 2 } | Out-Null
Start-Sleep -Seconds 1
$moneyA = (Send-HarnessCommand -Instance $a -Verb dump).stats.money
$moneyB = (Send-HarnessCommand -Instance $b -Verb dump).stats.money
Check ($moneyA -eq $moneyBefore - 1000 -and $moneyB -eq $moneyA) "the arrival cost 1000 once on both ($moneyBefore -> A $moneyA, B $moneyB)"

Send-ServerCommand "money add 100000"
Start-Sleep -Seconds 2
$moneyBefore = (Send-HarnessCommand -Instance $a -Verb dump).stats.money
Hold "on"
Send-HarnessCommand -Instance $a -Verb parking-unlock | Out-Null
Send-HarnessCommand -Instance $b -Verb parking-unlock | Out-Null
Start-Sleep -Seconds 1
Hold "off"
Wait-SamePlacement "both unlocked a level at once" { param($p) $p.parking.levels -eq 2 } | Out-Null
Start-Sleep -Seconds 1
$moneyA = (Send-HarnessCommand -Instance $a -Verb dump).stats.money
$moneyB = (Send-HarnessCommand -Instance $b -Verb dump).stats.money
Check ($moneyA -eq $moneyBefore - 50000 -and $moneyB -eq $moneyA) "the unlock cost 50000 once on both ($moneyBefore -> A $moneyA, B $moneyB)"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

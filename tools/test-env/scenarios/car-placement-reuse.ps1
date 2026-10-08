# areas: placement, cars
# A car on lift 1 is deleted, a second car drives onto lift 1, then a new car spawns into the first car's loader at an
# entrance. Every client must keep lift 1 connected to the second car and be able to raise it (soak 2026-10-07: a
# remote spawn reused the deleted car's place, took over the lift and left the car on it unconnected).
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
        $pb = Placement $b
        $ja = $pa | ConvertTo-Json -Depth 6 -Compress
        $jb = $pb | ConvertTo-Json -Depth 6 -Compress
        if ($ja -eq $jb -and (& $Condition $pa)) { break }
    } while ((Get-Date) -lt $deadline)
    Check ($ja -eq $jb) "$What`: A and B agree ($ja)"
    if ($ja -ne $jb) { Write-Host "  B: $jb" }
    Check ([bool](& $Condition $pa) -and [bool](& $Condition $pb)) "$What`: expected state reached on both"
    return $pa
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "1 $car 0 CarLifter1" | Out-Null
Wait-Ready $a 1 | Out-Null; Wait-Ready $b 1 | Out-Null
$p = Wait-SamePlacement "first car on lift 1" { param($p) @($p.cars | Where-Object { $_.loader -eq 1 -and $_.inPlace -eq "CarLifter1" }).Count -eq 1 }
$lift = @($p.lifters | Where-Object { $_.car -eq 1 })[0].index
Check ($null -ne $lift) "a lift is connected to the first car"

Send-HarnessCommand -Instance $a -Verb car-delete -Arguments "1" | Out-Null
Wait-SamePlacement "first car deleted" { param($p) @($p.cars).Count -eq 0 } | Out-Null

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "2 $car 0 Entrance1" | Out-Null
Wait-Ready $a 2 | Out-Null; Wait-Ready $b 2 | Out-Null
Send-HarnessCommand -Instance $b -Verb car-move -Arguments "2 CarLifter1" | Out-Null
Wait-SamePlacement "second car on lift 1" { param($p) @($p.lifters | Where-Object { $_.index -eq $lift -and $_.car -eq 2 }).Count -eq 1 } | Out-Null

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "1 $car 0 Entrance2" | Out-Null
Wait-Ready $a 1 | Out-Null; Wait-Ready $b 1 | Out-Null
Wait-SamePlacement "new car in the first loader at entrance 2, lift 1 still holds the second car" {
    param($p) @($p.cars | Where-Object { $_.loader -eq 1 -and $_.inPlace -eq "Entrance2" }).Count -eq 1 -and
        @($p.lifters | Where-Object { $_.index -eq $lift -and $_.car -eq 2 }).Count -eq 1
} | Out-Null

Send-HarnessCommand -Instance $b -Verb lift -Arguments "$lift up" | Out-Null
Wait-SamePlacement "B raised lift 1" { param($p) @($p.lifters | Where-Object { $_.index -eq $lift -and $_.state -eq "Middle" }).Count -eq 1 } | Out-Null
Send-HarnessCommand -Instance $b -Verb lift -Arguments "$lift down" | Out-Null
Wait-SamePlacement "B lowered lift 1" { param($p) @($p.lifters | Where-Object { $_.index -eq $lift -and $_.state -eq "OnFloor" }).Count -eq 1 } | Out-Null

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

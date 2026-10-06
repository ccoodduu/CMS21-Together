# sync-car-placement-and-lifts: A spawns a car and moves it onto lift 1 (vanilla ChangeCarPos coroutine), A and B
# raise the lift one step each, A moves the car off the raised lift (the lift goes back to the floor), A parks the
# car, B takes it out of parking onto another loader. After each step both clients' placement dumps must match.
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

function Wait-SamePlacement([string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 30) {
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
    Check ([bool](& $Condition $pa)) "$What`: expected state reached"
    return $pa
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
Wait-Ready $a 0 | Out-Null
Wait-Ready $b 0 | Out-Null

Send-HarnessCommand -Instance $a -Verb car-move -Arguments "0 CarLifter1" | Out-Null
Wait-SamePlacement "car on lift 1" { param($p) @($p.cars | Where-Object { $_.loader -eq 0 -and $_.inPlace -eq "CarLifter1" }).Count -eq 1 } | Out-Null

Send-HarnessCommand -Instance $a -Verb lift -Arguments "0 up" | Out-Null
Wait-SamePlacement "A raised lift 0" { param($p) @($p.lifters | Where-Object { $_.index -eq 0 -and $_.state -eq "Middle" }).Count -eq 1 } | Out-Null
Send-HarnessCommand -Instance $b -Verb lift -Arguments "0 up" | Out-Null
Wait-SamePlacement "B raised lift 0" { param($p) @($p.lifters | Where-Object { $_.index -eq 0 -and $_.state -eq "Up" }).Count -eq 1 } | Out-Null

Send-HarnessCommand -Instance $a -Verb car-move -Arguments "0 Entrance2" | Out-Null
Wait-SamePlacement "car moved off the raised lift" { param($p)
    @($p.cars | Where-Object { $_.loader -eq 0 -and $_.inPlace -eq "Entrance2" }).Count -eq 1 -and @($p.lifters | Where-Object { $_.index -eq 0 -and $_.state -eq "OnFloor" }).Count -eq 1
} | Out-Null

Send-HarnessCommand -Instance $a -Verb park -Arguments "0" | Out-Null
Wait-SamePlacement "car parked" { param($p) @($p.cars).Count -eq 0 -and @($p.parking.slots | Where-Object { $_.carToLoad -eq $car }).Count -eq 1 } | Out-Null
$slot = @((Placement $a).parking.slots | Where-Object { $_.carToLoad -eq $car })[0].index

Send-HarnessCommand -Instance $b -Verb unpark -Arguments "$slot 1" | Out-Null
Wait-Ready $b 1 | Out-Null
Wait-Ready $a 1 | Out-Null
Wait-SamePlacement "B took the car out of parking" { param($p) @($p.cars | Where-Object { $_.loader -eq 1 }).Count -eq 1 -and @($p.parking.slots).Count -eq 0 } | Out-Null
$ra = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "1"
$rb = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "1"
Check ($ra.stateHash -eq $rb.stateHash) "the unparked car's parts match ($($ra.stateHash) / $($rb.stateHash))"

$mark = Get-ServerLogMark
Send-ServerCommand "placement"
Check ([bool](Wait-ServerLog -Pattern "parking: 1 levels, 0/10 slots used" -After $mark -TimeoutSec 10)) "the placement command shows the empty parking"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

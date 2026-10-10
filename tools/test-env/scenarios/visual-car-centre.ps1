# areas: visuals, presence
# Bug from the row 17 screenshots (2026-10-10): the remote visuals took the CarLoader object as the car's centre. That
# object stays at its spawn point (loader 0: the origin, CarLifter1) while the car's root moves with its place, so for
# a car at Entrance1 B showed A turned away from the car while A used the OBD scanner on it, and a body panel's ghost
# pulled away towards the loader object instead of away from the car. Checks, with the car at Entrance1: B's avatar of
# A with the scanner faces the car's root (facingCar < 30 deg), and B's held Off ghost of the hood moves away from the
# car's root (outward < 45 deg). Cars stay equal.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Cmd $Name car-ready "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Wait-Try([string]$Name, [int]$TryId, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Cmd $Name lock-try "result $TryId"
        if ($r.result -ne "pending" -and ($r.result -ne "granted" -or -not $r.started -or $r.finished)) { return $r }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    $r
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$idA = "$((Get-HarnessStatus $a).playerId)"

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2
$where = Cmd $b vfx-car "$loader"
Write-Host "B: loader object $($where.loader -join ','), car root $($where.root -join ',')"
Check ($where.apart -gt 3) "the car at Entrance1 is away from the loader object ($($where.apart) m)"

# A stands beside the car with the OBD scanner; B's avatar of A turns to the car.
Cmd $a vfx-stand "$loader 1.5" | Out-Null
Cmd $b vfx-stand "$loader 4" | Out-Null
Start-Sleep -Seconds 1
Cmd $a vfx-tool "OBD $loader" | Out-Null
try {
    Wait-HarnessDump -Instance $b -TimeoutSec 10 -What "A's scanner" -Condition { param($d) $d.visuals.players.$idA.propActive } | Out-Null
} catch { Check $false "B shows A's OBD scanner" }
Start-Sleep -Seconds 2
$p = (Cmd $b dump).visuals.players.$idA
Check ($null -ne $p.facingCar -and $p.facingCar -lt 30) "B's avatar of A faces the car while examining it (facingCar $($p.facingCar) deg, WorkPose facing $($p.facing) deg, pose $($p.pose))"
Cmd $a vfx-tool "none" | Out-Null
Start-Sleep -Seconds 1

# A takes the hood off; B holds the Off ghost half-way and it has moved away from the car.
Cmd $b vfx-hold "on" | Out-Null
$t = Cmd $a lock-try "$loader body 0 finish"
$r = Wait-Try $a $t.tryId
Write-Host "A hood off: $($r.result) $($r.state)"
try {
    $v = (Wait-HarnessDump -Instance $b -TimeoutSec 20 -What "the hood's Off ghost" -Condition { param($d) @($d.visuals.ghostsActive | Where-Object { $_.kind -eq "Panel" }).Count -ge 1 }).visuals
    $ghost = @($v.ghostsActive | Where-Object { $_.kind -eq "Panel" })[0]
    Check ($null -ne $ghost.outward -and $ghost.outward -lt 45) "B's hood ghost pulls away from the car (outward $($ghost.outward) deg, key $($ghost.key))"
} catch { Check $false "B starts a Panel ghost for the hood ($($_.Exception.Message))" }
Cmd $b vfx-hold "off" | Out-Null
Start-Sleep -Seconds 2

try {
    Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("stats", "cars") -TimeoutSec 30 | Out-Null
    Check $true "A and B have equal stats and cars"
} catch { Check $false $_.Exception.Message }
foreach ($name in $Ctx.Instances) { Check ([int](Cmd $name dump).visuals.leaks -eq 0) "$name has no visual state leak" }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

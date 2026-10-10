# areas: testdrive, cars
# The "<name> has this car on the test track." label over a car that another player has taken away was anchored at
# the CarLoader object, which stays at the loader's spawn point (loader 0: the origin, CarLifter1) while the car's
# root moves with its place. With the car at Entrance1 and A on the test track, B's label anchor must sit over the
# car's root (within 0.5 m on the ground plane). After A's return the claim is gone and cars are equal.
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

function Wait-Away([string]$Name, [int]$Count) {
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 500; $away = @((Cmd $Name dump).away) } while ($away.Count -ne $Count -and (Get-Date) -lt $deadline)
    return $away
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $a, $b) { Cmd $name guard-allow "Mode:CarDrive" | Out-Null }

Cmd $a car-spawn "0 car_boltatlanta 0 Entrance1" | Out-Null
foreach ($name in $a, $b) {
    $deadline = (Get-Date).AddSeconds(120)
    do { Start-Sleep -Milliseconds 700; $r = Cmd $name car-ready "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
}
Start-Sleep -Seconds 3
$where = Cmd $b vfx-car "0"
Check ($where.apart -gt 3) "the car at Entrance1 is away from the loader object ($($where.apart) m)"

Cmd $a testdrive-go "0" | Out-Null
$away = @(Wait-Away $b 1)
Check ($away.Count -eq 1 -and $away[0].kind -eq "TestTrack") "B sees A's test drive claim ($($away | ConvertTo-Json -Compress))"
$label = Cmd $b away-label "0"
Write-Host "B label anchor $($label.anchor -join ','), car root $($label.root -join ',')"
Check $label.lockedForMe "B draws the away label for loader 0 (locked for B)"
Check ($label.offCar -lt 0.5) "B's away label is over the car ($($label.offCar) m from the car's root on the ground plane)"

try {
    Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null
    Start-Sleep -Seconds 3
    Cmd $a testdrive-finish "all" | Out-Null
    Wait-InGarage $a 180
} catch { Check $false "A drives to the test track and back ($($_.Exception.Message))" }
Check (@(Wait-Away $b 0).Count -eq 0) "the claim is released on B"
try {
    Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("cars", "away") -TimeoutSec 30 | Out-Null
    Check $true "A and B have equal cars and away sections"
} catch { Check $false $_.Exception.Message }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

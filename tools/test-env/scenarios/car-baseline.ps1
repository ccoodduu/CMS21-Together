# sync-car-parts baseline and late join: A spawns a car and changes one part before the baseline settles, the
# server stores A's baseline, B joins late and loads the car from the snapshot with the same part state, the car
# (with its baseline) survives a server restart, and a car on the client's own profile never appears.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_astonmartindb5"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$TimeoutSec = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready within $TimeoutSec s (last: $($r | ConvertTo-Json -Compress))"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
$cars = (Send-HarnessCommand -Instance $a -Verb dump).cars | Where-Object { $_.carToLoad }
Check (@($cars).Count -eq 0) "no car from the client's own profile in the shared garage ($(@($cars | ForEach-Object { $_.carToLoad }) -join ', '))"

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "$loader $car 0" | Out-Null
$deadline = (Get-Date).AddSeconds(60)
do { Start-Sleep -Milliseconds 300; $loaded = Send-HarnessCommand -Instance $a -Verb car-loaded -Arguments "$loader" } until ($loaded.loaded -or (Get-Date) -gt $deadline)
$unmounted = Send-HarnessCommand -Instance $a -Verb part-unmount -Arguments "$loader"
Write-Host "A unmounted $($unmounted.key) ($($unmounted.id))"
$readyA = Wait-Ready $a
Send-HarnessCommand -Instance $a -Verb car-baseline -Arguments "$loader" | Out-Null
Start-Sleep -Seconds 1
$readyA = Wait-Ready $a
Check ($readyA.unmounted -ge 1) "A's car has the unmounted part ($($readyA.unmounted) unmounted)"

Connect-HarnessInstance $b; Wait-InGarage $b
$readyB = Wait-Ready $b
Check ($readyB.car -eq $car) "B loaded $car from the snapshot (got $($readyB.car))"
Check ($readyB.registryHash -eq $readyA.registryHash) "same part hierarchy on A and B ($($readyA.registryHash) / $($readyB.registryHash))"
Check ($readyB.stateHash -eq $readyA.stateHash) "same part state on A and B ($($readyA.stateHash) / $($readyB.stateHash))"
if ($readyB.stateHash -ne $readyA.stateHash) {
    Send-HarnessCommand -Instance $a -Verb part-state -Arguments "$loader $(Join-Path $Ctx.RunDir 'parts_A.txt')" | Out-Null
    Send-HarnessCommand -Instance $b -Verb part-state -Arguments "$loader $(Join-Path $Ctx.RunDir 'parts_B.txt')" | Out-Null
}

$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
Stop-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 60 -What "menu after server stop" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
    Send-HarnessCommand -Instance $name -Verb mp-ui -Arguments "ok" | Out-Null
}
Start-TestServer | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
$afterRestart = Wait-Ready $b
Check ($afterRestart.car -eq $car -and $afterRestart.stateHash -eq $readyA.stateHash) "car and part state survive a server restart ($($afterRestart.car), $($afterRestart.stateHash))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

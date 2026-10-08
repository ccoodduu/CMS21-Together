# areas: testdrive, cars
# sync-test-drive-and-diagnostics 8.1: A test-drives a car both players see. While A is away, B sees the claim and
# cannot edit the car; after the return both have the driven kilometres and the claim is gone. Then a refused
# departure (B holds a part claim), an aborted drive, and the fallback when the result is not sent.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Track([string]$Name) {
    try { Wait-HarnessStatus -Instance $Name -TimeoutSec 120 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null; $true } catch { $false }
}

function Mileage([string]$Name) {
    $info = (Send-HarnessCommand -Instance $Name -Verb cardetails-show -Arguments "0").Info | ConvertFrom-Json
    [int]$info.Mileage
}

function Wait-Mileage([int]$Expected, [string]$What) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Seconds 1
        $ma = Mileage $a; $mb = Mileage $b
    } while (($ma -ne $Expected -or $mb -ne $Expected) -and (Get-Date) -lt $deadline)
    Check ($ma -eq $Expected -and $mb -eq $Expected) "$What`: mileage $Expected on both (A $ma, B $mb)"
}

function Wait-Away([string]$Name, [int]$Count) {
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 500; $away = @((Send-HarnessCommand -Instance $Name -Verb dump).away) } while ($away.Count -ne $Count -and (Get-Date) -lt $deadline)
    return $away
}

function Drive([int]$Metres, [string]$Finish) {
    Send-HarnessCommand -Instance $a -Verb testdrive-go -Arguments "0" | Out-Null
    $onTrack = Wait-Track $a
    if ($onTrack) {
        Start-Sleep -Seconds 3
        Send-HarnessCommand -Instance $a -Verb testdrive-drive -Arguments "$Metres" | Out-Null
        Send-HarnessCommand -Instance $a -Verb testdrive-finish -Arguments $Finish | Out-Null
        Wait-InGarage $a 180
        Start-Sleep -Seconds 6
    }
    return $onTrack
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a
Wait-InGarage $a
Connect-HarnessInstance $b
Wait-InGarage $b
$idA = (Get-HarnessStatus -Instance $a).playerId
foreach ($name in $a, $b) {
    Send-HarnessCommand -Instance $name -Verb guard-allow -Arguments "Mode:CarDrive" | Out-Null
}

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 car_boltatlanta 0" | Out-Null
foreach ($name in $a, $b) {
    $deadline = (Get-Date).AddSeconds(120)
    do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $name -Verb car-ready -Arguments "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
}
Start-Sleep -Seconds 3
$repair = Send-HarnessCommand -Instance $b -Verb part-unmount -Arguments "0"
Start-Sleep -Seconds 4
$start = Mileage $a
Check ($start -eq (Mileage $b)) "same mileage before the drive ($start)"

# Drive with the result.
Send-HarnessCommand -Instance $a -Verb testdrive-go -Arguments "0" | Out-Null
$away = @(Wait-Away $b 1)
Check ($away.Count -eq 1 -and $away[0].owner -eq $idA -and $away[0].kind -eq "TestTrack") "B sees A's test drive claim (A is $idA; $($away | ConvertTo-Json -Compress))"
$onTrack = Wait-Track $a
Check $onTrack "A reached the test track"
$try = Send-HarnessCommand -Instance $b -Verb away-try -Arguments "0 unmount $($repair.key)"
Check $try.blocked "B's unmount on the away car is blocked ($($try.what))"
$try = Send-HarnessCommand -Instance $b -Verb away-try -Arguments "0 move Entrance1"
Check $try.blocked "B's move of the away car is blocked ($($try.what))"
if ($onTrack) {
    Start-Sleep -Seconds 3
    Send-HarnessCommand -Instance $a -Verb testdrive-drive -Arguments "5000" | Out-Null
    Send-HarnessCommand -Instance $a -Verb testdrive-finish -Arguments "all" | Out-Null
    Wait-InGarage $a 180
}
Wait-Mileage ($start + 5) "after the drive"
Check (@(Wait-Away $a 0).Count -eq 0 -and @(Wait-Away $b 0).Count -eq 0) "the claim is released on both"
$differ = Compare-HarnessDumps -Left (Send-HarnessCommand -Instance $a -Verb dump) -Right (Send-HarnessCommand -Instance $b -Verb dump) -Sections cars, away
Check ($differ.Count -eq 0) "A and B have the same cars and away sections (differ: $($differ -join ', '))"

# Refused departure: B holds a part claim.
Send-HarnessCommand -Instance $b -Verb lock-take -Arguments "0 unmount $($repair.key) bare" | Out-Null
Start-Sleep -Seconds 1
Send-HarnessCommand -Instance $a -Verb testdrive-go -Arguments "0" | Out-Null
Start-Sleep -Seconds 8
$status = Get-HarnessStatus -Instance $a
Check ($status.scene -eq "garage" -and $status.playable) "A stays in the garage while B works on the car ($($status.scene))"
Send-HarnessCommand -Instance $b -Verb lock-take -Arguments "0 unmount $($repair.key) release" | Out-Null
Start-Sleep -Seconds 1

# Aborted drive: 1500 m.
$before = Mileage $a
Check (Drive 1500 "abort") "A reached the test track (abort)"
$after = Mileage $a
Check ($after - $before -ge 1 -and $after - $before -le 2) "abort adds 1-2 km on A ($before -> $after)"
Wait-Mileage $after "after the aborted drive"

# Fallback: the result is not sent; the return applies NewMileage once.
Send-HarnessCommand -Instance $a -Verb testdrive-skip-result -Arguments "on" | Out-Null
$before = Mileage $a
Check (Drive 2000 "all") "A reached the test track (no result)"
Wait-Mileage ($before + 2) "after the drive without a result"
Check (@(Wait-Away $b 0).Count -eq 0) "the claim is released after the fallback"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# sync-test-drive-and-diagnostics 8.3: dyno (cancel, then measure), test path and examine tools on a car both players
# see. While A has the car on the dyno or the path, B sees the claim and cannot edit it; the measured result,
# specialState and examined flags reach B and a late join.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Away([string]$Name, [int]$Count) {
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 500; $away = @((Send-HarnessCommand -Instance $Name -Verb dump).away) } while ($away.Count -ne $Count -and (Get-Date) -lt $deadline)
    return $away
}

function Dyno([string]$Name) { (Send-HarnessCommand -Instance $Name -Verb cardetails-show -Arguments "0").Dyno }

function Wait-SameDyno([string]$What) {
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Seconds 1; $da = Dyno $a; $db = Dyno $b } while ($da -ne $db -and (Get-Date) -lt $deadline)
    Check ($da -eq $db) "$What`: A and B have the same dyno values"
    if ($da -ne $db) { Write-Host "  A: $da"; Write-Host "  B: $db" }
    return $da
}

function Wait-SameCars([string]$What) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Seconds 1
        $differ = Compare-HarnessDumps -Left (Send-HarnessCommand -Instance $a -Verb dump) -Right (Send-HarnessCommand -Instance $b -Verb dump) -Sections cars, away
    } while ($differ.Count -gt 0 -and (Get-Date) -lt $deadline)
    Check ($differ.Count -eq 0) "$What`: A and B have the same cars and away sections (differ: $($differ -join ', '))"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a
Wait-InGarage $a
Connect-HarnessInstance $b
Wait-InGarage $b
$idA = (Get-HarnessStatus -Instance $a).playerId

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 car_boltatlanta 0" | Out-Null
foreach ($name in $a, $b) {
    $deadline = (Get-Date).AddSeconds(120)
    do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $name -Verb car-ready -Arguments "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
}
Start-Sleep -Seconds 3
$key = ((Send-HarnessCommand -Instance $b -Verb dump).cars | Where-Object { $_.index -eq 0 }).subParts | Where-Object { -not $_.unmounted } | Select-Object -First 1 -ExpandProperty key
Send-HarnessCommand -Instance $a -Verb car-move -Arguments "0 Dyno" | Out-Null
Start-Sleep -Seconds 6
Wait-SameDyno "before the dyno" | Out-Null

# Dyno cancelled: nothing changes.
Send-HarnessCommand -Instance $a -Verb dyno-run -Arguments "0 start" | Out-Null
$away = @(Wait-Away $b 1)
Check ($away.Count -eq 1 -and $away[0].owner -eq $idA -and $away[0].kind -eq "Dyno") "B sees A's dyno claim"
$try = Send-HarnessCommand -Instance $b -Verb away-try -Arguments "0 unmount $key"
Check $try.blocked "B's unmount on the car on the dyno is blocked ($($try.what))"
Start-Sleep -Seconds 3
Send-HarnessCommand -Instance $a -Verb dyno-run -Arguments "0 close" | Out-Null
Check (@(Wait-Away $b 0).Count -eq 0) "the dyno claim is released after a cancel"
$cancelled = Wait-SameDyno "after a cancelled run"
Check ($cancelled -notmatch '"Measured":true') "a cancelled run leaves the car unmeasured"

# Dyno measured: the result reaches B.
Send-HarnessCommand -Instance $a -Verb dyno-run -Arguments "0 start" | Out-Null
Start-Sleep -Seconds 3
Send-HarnessCommand -Instance $a -Verb dyno-run -Arguments "0 measure" | Out-Null
Start-Sleep -Seconds 40
Send-HarnessCommand -Instance $a -Verb dyno-run -Arguments "0 close" | Out-Null
Start-Sleep -Seconds 3
$state = Send-HarnessCommand -Instance $a -Verb dyno-run -Arguments "0 state"
if ($state.mode -ne "Garage") { Send-HarnessCommand -Instance $a -Verb dyno-run -Arguments "0 close" | Out-Null; Start-Sleep -Seconds 3 }
Check (@(Wait-Away $b 0).Count -eq 0) "the dyno claim is released after a measurement"
$measured = Wait-SameDyno "after a measurement"
Check ($measured -match '"Measured":true') "the measured result reached both"

# Test path: claim, specialState and examined flags.
Send-HarnessCommand -Instance $a -Verb car-move -Arguments "0 DiagnosticPath" | Out-Null
Start-Sleep -Seconds 6
Send-HarnessCommand -Instance $a -Verb pathtest-run -Arguments "0 prepare" | Out-Null
$away = @(Wait-Away $b 1)
Check ($away.Count -eq 1 -and $away[0].kind -eq "PathTest") "B sees A's test path claim"
Start-Sleep -Seconds 4
Send-HarnessCommand -Instance $a -Verb pathtest-run -Arguments "0 end" | Out-Null
Start-Sleep -Seconds 6
Send-HarnessCommand -Instance $a -Verb pathtest-run -Arguments "0 report" | Out-Null
Check (@(Wait-Away $b 0).Count -eq 0) "the test path claim is released after the report"
$carB = (Send-HarnessCommand -Instance $b -Verb dump).cars | Where-Object { $_.index -eq 0 }
Check ($carB.specialState -eq 1) "B has specialState 1 after the test path ($($carB.specialState))"
Wait-SameCars "after the test path"

# Examine tools by both at once.
Send-HarnessCommand -Instance $a -Verb diag-examine -Arguments "0 OBD" | Out-Null
Send-HarnessCommand -Instance $b -Verb diag-examine -Arguments "0 Compression" | Out-Null
Wait-SameCars "after OBD and compression"

# Late join keeps the dyno result, specialState and examined flags.
Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Start-Sleep -Seconds 3
Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b
Wait-InGarage $b
$deadline = (Get-Date).AddSeconds(60)
do { Start-Sleep -Seconds 1; $r = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
Wait-SameDyno "after B's late join" | Out-Null
Wait-SameCars "after B's late join"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

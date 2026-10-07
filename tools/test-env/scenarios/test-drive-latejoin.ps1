# areas: testdrive, jobs, connect, persistence
# sync-test-drive-and-diagnostics 8.2: A takes a job and test-drives the customer car; B joins while A is on the track,
# sees the claim and cannot end the job; after the return B has the kilometres. A disconnects on a second drive: the
# claim is released and the car is unchanged. After a server restart A's cars and details are as before.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Track([string]$Name) {
    try { Wait-HarnessStatus -Instance $Name -TimeoutSec 120 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null; $true } catch { $false }
}

function Wait-Away([string]$Name, [int]$Count) {
    $deadline = (Get-Date).AddSeconds(20)
    do { Start-Sleep -Milliseconds 500; $away = @((Send-HarnessCommand -Instance $Name -Verb dump).away) } while ($away.Count -ne $Count -and (Get-Date) -lt $deadline)
    return $away
}

function Mileage([string]$Name, [int]$Loader) {
    [int]((Send-HarnessCommand -Instance $Name -Verb cardetails-show -Arguments "$Loader").Info | ConvertFrom-Json).Mileage
}

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(90)
    do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$Loader" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
    return $r
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Send-HarnessCommand -Instance $a -Verb orders-autogen -Arguments "off" | Out-Null
Send-HarnessCommand -Instance $a -Verb orders-generate -Arguments "300" | Out-Null
Start-Sleep -Seconds 2
$orders = @((Send-HarnessCommand -Instance $a -Verb dump).jobs.orders | Where-Object { -not $_.IsMission })
Send-HarnessCommand -Instance $a -Verb orders-accept -Arguments "$($orders[-1].id)" | Out-Null
$deadline = (Get-Date).AddSeconds(60)
do { Start-Sleep -Milliseconds 700; $active = @((Send-HarnessCommand -Instance $a -Verb dump).jobs.active) } while ($active.Count -eq 0 -and (Get-Date) -lt $deadline)
Check ($active.Count -eq 1) "A took a job"
$loader = $active[0].carLoaderID
$jobId = $active[0].id
Wait-Ready $a $loader | Out-Null
Start-Sleep -Seconds 3
$start = Mileage $a $loader

# B joins while A is on the track.
Send-HarnessCommand -Instance $a -Verb testdrive-go -Arguments "$loader" | Out-Null
Check (Wait-Track $a) "A reached the test track with the job car"
Connect-HarnessInstance $b; Wait-InGarage $b
Send-HarnessCommand -Instance $b -Verb orders-autogen -Arguments "off" | Out-Null
Wait-Ready $b $loader | Out-Null
$away = @(Wait-Away $b 1)
Check ($away.Count -eq 1 -and $away[0].loader -eq $loader -and $away[0].kind -eq "TestTrack") "late joiner B sees A's test drive claim"
Send-HarnessCommand -Instance $b -Verb job-finish -Arguments "$jobId" | Out-Null
Start-Sleep -Seconds 3
$stillActive = @((Send-HarnessCommand -Instance $b -Verb dump).jobs.active | Where-Object { $_.id -eq $jobId })
Check ($stillActive.Count -eq 1) "B cannot end the job of the away car"

Send-HarnessCommand -Instance $a -Verb testdrive-drive -Arguments "3000" | Out-Null
Send-HarnessCommand -Instance $a -Verb testdrive-finish -Arguments "all" | Out-Null
Wait-InGarage $a 180
Check (@(Wait-Away $b 0).Count -eq 0) "the claim is released after the return"
Start-Sleep -Seconds 3
$ma = Mileage $a $loader; $mb = Mileage $b $loader
Check ($ma -eq $start + 3 -and $mb -eq $start + 3) "B has the driven kilometres (start $start, A $ma, B $mb)"

# A disconnects on the track: the claim goes, the car stays as it was.
Send-HarnessCommand -Instance $a -Verb testdrive-go -Arguments "$loader" | Out-Null
Check (Wait-Track $a) "A reached the test track again"
Send-HarnessCommand -Instance $a -Verb testdrive-drive -Arguments "4000" | Out-Null
Send-HarnessCommand -Instance $a -Verb to-menu | Out-Null
Check (@(Wait-Away $b 0).Count -eq 0) "the claim is released when A leaves on the track"
Check ((Mileage $b $loader) -eq $start + 3) "the car keeps its mileage without a result"
Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "A in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Wait-Ready $a $loader | Out-Null
Start-Sleep -Seconds 3
Check ((Mileage $a $loader) -eq $start + 3) "A's car has the server's mileage after the rejoin"

# Restart keeps cars and details.
$beforeDetails = Send-HarnessCommand -Instance $a -Verb cardetails-show -Arguments "$loader" | ConvertTo-Json -Compress
$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
Stop-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 30 -What "menu after kill" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
    Send-HarnessCommand -Instance $name -Verb mp-ui -Arguments "ok" | Out-Null
}
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Wait-Ready $a $loader | Out-Null
Start-Sleep -Seconds 3
$afterDetails = Send-HarnessCommand -Instance $a -Verb cardetails-show -Arguments "$loader" | ConvertTo-Json -Compress
Check ($beforeDetails -eq $afterDetails) "the car's details survived the restart"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: details, cars
# sync-car-details 4.7 (D7): A holds its spawn snapshot and spawns a car. The server has no details for the car, so
# after the 10 s grace it sends A a CarDetailsRequest; A answers with a full snapshot, which the server stores.
param($Ctx)

$a = $Ctx.Instances[0]
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null

Send-HarnessCommand -Instance $a -Verb cardetails-hold -Arguments "on" | Out-Null
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 car_boltatlanta 0" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
Check ($r.state -eq "Ready") "A's loader 0 is Ready"

$early = try { Wait-ServerLog -Pattern "\[CarDetails\] Loader 0: full snapshot" -After $mark -TimeoutSec 3 } catch { $null }
Check ($null -eq $early) "no spawn snapshot while held"

$asked = try { Wait-ServerLog -Pattern "\[CarDetails\] Loader 0 has no details snapshot; asking client" -After $mark -TimeoutSec 25 } catch { $null }
Check ($null -ne $asked) "server asks a client for loader 0's snapshot"
$stored = try { Wait-ServerLog -Pattern "\[CarDetails\] Loader 0: full snapshot from client" -After $mark -TimeoutSec 10 } catch { $null }
Check ($null -ne $stored) "server stores the requested snapshot"

$mark = Get-ServerLogMark
Send-ServerCommand "cardetails 0"
$line = Wait-ServerLog -Pattern "\[CarDetails\] Loader 0: " -After $mark -TimeoutSec 10
Check ($line -match '"HasSnapshot":true') "cardetails 0 shows a snapshot"

Send-HarnessCommand -Instance $a -Verb cardetails-hold -Arguments "off" | Out-Null
$Ctx.Result.passed = ($failures.Count -eq 0)
$Ctx.Result.notes += $failures

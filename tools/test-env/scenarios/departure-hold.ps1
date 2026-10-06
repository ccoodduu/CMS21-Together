# run-all: skip
# sync-test-drive-and-diagnostics spike 1.3 (instance A only, connected): hold the departure coroutine to the test
# track for a second, then release it; then hold and cancel it, and check what the cancel leaves behind; then depart
# again without a hold. Results in departure-hold.json and the client_A.log "testdrive-hold" lines.
param($Ctx)

$a = $Ctx.Instances[0]
$out = [ordered]@{}
function Step([string]$Name, [string]$Verb, [string]$Arguments = "") {
    try { $value = Send-HarnessCommand -Instance $a -Verb $Verb -Arguments $Arguments } catch { $value = "ERROR: $($_.Exception.Message.Split("`n")[0])" }
    $script:out[$Name] = $value
    Write-Host "$Name : $($value | ConvertTo-Json -Depth 6 -Compress)"
}
function Note([string]$Name, $Value) { $script:out[$Name] = $Value; Write-Host "$Name : $($Value | ConvertTo-Json -Depth 6 -Compress)" }
function Wait-Scene([string]$Pattern, [int]$TimeoutSec) {
    try { Wait-HarnessStatus -Instance $a -TimeoutSec $TimeoutSec -What "scene $Pattern" -Condition { param($s) $s.scene -match $Pattern -and $s.playable } | Out-Null; $true } catch { $false }
}

Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb guard-allow -Arguments "Scene:TestTrack" | Out-Null
Send-HarnessCommand -Instance $a -Verb guard-allow -Arguments "Mode:CarDrive" | Out-Null

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 car_boltatlanta 0" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
Start-Sleep -Seconds 2
Step "trace-on" "testdrive-trace" "on"

Step "hold-on" "testdrive-hold" "on"
Step "go-held" "testdrive-go" "0"
Start-Sleep -Seconds 1
Step "held" "testdrive-hold" "state"
Note "held-status" (Get-HarnessStatus -Instance $a)
Step "release" "testdrive-hold" "release"
Note "track-after-release" (Wait-Scene "(?i)track" 120)
Start-Sleep -Seconds 3
Step "finish" "testdrive-finish" "all"
Note "garage-after-release" (Wait-Scene "^garage$" 180)
Start-Sleep -Seconds 8

Step "hold-on-2" "testdrive-hold" "on"
Step "go-cancel" "testdrive-go" "0"
Start-Sleep -Seconds 1
Step "held-2" "testdrive-hold" "state"
Step "cancel" "testdrive-hold" "cancel"
Start-Sleep -Seconds 3
Step "after-cancel" "testdrive-hold" "state"
Note "status-after-cancel" (Get-HarnessStatus -Instance $a)

Step "go-again" "testdrive-go" "0"
Note "track-after-cancel" (Wait-Scene "(?i)track" 120)
Start-Sleep -Seconds 3
Step "finish-2" "testdrive-finish" "all"
Note "garage-end" (Wait-Scene "^garage$" 180)

Step "report" "testdrive-trace" "report"
$out | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "departure-hold.json") -Encoding utf8
$Ctx.Result.passed = $true

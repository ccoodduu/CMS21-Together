# run-all: skip
# areas: testdrive
# sync-test-drive-and-diagnostics spike 1.2 (instance A only, connected, guard enforcing with the test track allowed):
# spawn a car, take it to the test track, drive 5000 m, finish the tests and return; then the same with an abort.
# The trace (client_A.log "testdrive-trace" lines) and the reports in test-drive-trace.json answer the spike questions.
param($Ctx)

$a = $Ctx.Instances[0]
$out = [ordered]@{}
function Step([string]$Name, $Value) { $script:out[$Name] = $Value; Write-Host "$Name : $($Value | ConvertTo-Json -Depth 6 -Compress)" }
function Try-Step([string]$Name, [string]$Verb, [string]$Arguments = "") {
    try { Step $Name (Send-HarnessCommand -Instance $a -Verb $Verb -Arguments $Arguments) } catch { Step $Name "ERROR: $($_.Exception.Message.Split("`n")[0])" }
}
function Wait-Scene([string]$Pattern, [int]$TimeoutSec = 120) {
    Wait-HarnessStatus -Instance $a -TimeoutSec $TimeoutSec -What "scene $Pattern" -Condition { param($s) $s.scene -match $Pattern -and $s.playable } | Out-Null
}

Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb guard-allow -Arguments "Scene:TestTrack" | Out-Null

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 car_boltatlanta 0" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
Start-Sleep -Seconds 2
Step "details-before" (Send-HarnessCommand -Instance $a -Verb cardetails-show -Arguments "0").Info
Step "garageparts" (Send-HarnessCommand -Instance $a -Verb testdrive-partnames -Arguments "0")
Step "trace-on"(Send-HarnessCommand -Instance $a -Verb testdrive-trace -Arguments "on").failures

foreach ($mode in "all", "abort") {
    Try-Step "go-$mode" "testdrive-go" "0"
    try { Wait-Scene "(?i)track" 120 } catch { Step "track-$mode" "did not reach the track: $($_.Exception.Message)"; break }
    Start-Sleep -Seconds 5
    Try-Step "trackparts-$mode" "testdrive-partnames"
    Try-Step "drive-$mode" "testdrive-drive" "5000"
    Start-Sleep -Seconds 2
    Try-Step "finish-$mode" "testdrive-finish" $mode
    Wait-Scene "^garage$" 180
    Start-Sleep -Seconds 8
    Step "details-after-$mode" (Send-HarnessCommand -Instance $a -Verb cardetails-show -Arguments "0").Info
    Step "report-$mode" (Send-HarnessCommand -Instance $a -Verb testdrive-trace -Arguments "report").counts
}

Step "guard-log" (Send-HarnessCommand -Instance $a -Verb guard-log)
$out | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "test-drive-trace.json") -Encoding utf8
$Ctx.Result.passed = $true

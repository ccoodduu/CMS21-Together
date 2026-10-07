# run-all: skip
# areas: testdrive
# sync-test-drive-and-diagnostics spike 1.4 (instance A only, connected, guard LogOnly): dyno (measure and cancel),
# test path (full and early exit) and an OBD examine, with the test drive trace on. The step results in
# diag-trace.json and the client_A.log "testdrive-trace" lines answer the spike questions.
param($Ctx)

$a = $Ctx.Instances[0]
$out = [ordered]@{}
function Step([string]$Name, [string]$Verb, [string]$Arguments = "") {
    try { $value = Send-HarnessCommand -Instance $a -Verb $Verb -Arguments $Arguments } catch { $value = "ERROR: $($_.Exception.Message.Split("`n")[0])" }
    $script:out[$Name] = $value
    Write-Host "$Name : $($value | ConvertTo-Json -Depth 6 -Compress)"
}

Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments "logonly" | Out-Null

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 car_boltatlanta 0" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
Start-Sleep -Seconds 2
Step "trace-on" "testdrive-trace" "on"

Step "obd" "diag-examine" "0 OBD"
Start-Sleep -Seconds 2

Step "move-dyno" "car-move" "0 Dyno"
Start-Sleep -Seconds 6
Step "dyno-before" "dyno-run" "0 state"
Step "dyno-start" "dyno-run" "0 start"
Start-Sleep -Seconds 4
Step "dyno-preview" "dyno-run" "0 state"
Step "dyno-measure" "dyno-run" "0 measure"
Start-Sleep -Seconds 30
Step "dyno-measured" "dyno-run" "0 state"
Step "dyno-close" "dyno-run" "0 close"
Start-Sleep -Seconds 4
Step "dyno-after" "dyno-run" "0 state"
Step "dyno-start-cancel" "dyno-run" "0 start"
Start-Sleep -Seconds 4
Step "dyno-close-cancel" "dyno-run" "0 close"
Start-Sleep -Seconds 4
Step "dyno-after-cancel" "dyno-run" "0 state"

Step "move-path" "car-move" "0 DiagnosticPath"
Start-Sleep -Seconds 6
Step "path-before" "pathtest-run" "0 state"
Step "path-prepare" "pathtest-run" "0 prepare"
Start-Sleep -Seconds 8
Step "path-running" "pathtest-run" "0 state"
Step "path-end" "pathtest-run" "0 end"
Start-Sleep -Seconds 6
Step "path-ended" "pathtest-run" "0 state"
Step "path-exit" "pathtest-run" "0 exit"
Start-Sleep -Seconds 10
Step "path-after" "pathtest-run" "0 state"
Step "path-prepare-abort" "pathtest-run" "0 prepare"
Start-Sleep -Seconds 5
Step "path-exit-abort" "pathtest-run" "0 exit"
Start-Sleep -Seconds 10
Step "path-after-abort" "pathtest-run" "0 state"

Step "report" "testdrive-trace" "report"
Step "guard-log" "guard-log"
$out | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "diag-trace.json") -Encoding utf8
$Ctx.Result.passed = $true

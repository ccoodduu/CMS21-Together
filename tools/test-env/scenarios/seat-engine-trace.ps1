# run-all: skip
# sync-players-and-scenes spike 5.1 (instance A only, connected, guard off): spawn a car, trace on, sit left, start
# and stop the engine, stand up, then sit right and stand up. The trace (client_A.log "seat-trace" lines) and the
# counts in seat-engine-trace.json show which hooks fire once per action. The vanilla UI paths (door click, pie menu)
# still need a manual run with the trace on.
param($Ctx)

$a = $Ctx.Instances[0]
$out = [ordered]@{}
function Step([string]$Name, $Value) { $script:out[$Name] = $Value; Write-Host "$Name : $($Value | ConvertTo-Json -Depth 6 -Compress)" }
function Try-Step([string]$Name, [string]$Verb, [string]$Arguments = "") {
    try { Step $Name (Send-HarnessCommand -Instance $a -Verb $Verb -Arguments $Arguments) } catch { Step $Name "ERROR: $($_.Exception.Message.Split("`n")[0])" }
}
function Snapshot([string]$Name) {
    Step "$Name-trace" (Send-HarnessCommand -Instance $a -Verb seat-trace -Arguments "report").state
    Step "$Name-local" (Send-HarnessCommand -Instance $a -Verb dump).local
}

Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments "Off" | Out-Null

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 car_boltatlanta 0" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
Start-Sleep -Seconds 2
Step "trace-on" (Send-HarnessCommand -Instance $a -Verb seat-trace -Arguments "on").failures

foreach ($side in "left", "right") {
    Try-Step "sit-$side" "sit" "0 $side"
    Start-Sleep -Seconds 5
    Snapshot "seated-$side"
    if ($side -eq "left") {
        Try-Step "engine-on" "engine" "on"
        Start-Sleep -Seconds 5
        Snapshot "engine-on"
        Try-Step "engine-off" "engine" "off"
        Start-Sleep -Seconds 2
        Snapshot "engine-off"
    }
    Try-Step "stand-$side" "stand"
    Start-Sleep -Seconds 5
    Snapshot "standing-$side"
}

Step "counts" (Send-HarnessCommand -Instance $a -Verb seat-trace -Arguments "report").counts
$out | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "seat-engine-trace.json") -Encoding utf8
$Ctx.Result.passed = $true

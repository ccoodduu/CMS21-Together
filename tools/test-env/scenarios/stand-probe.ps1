# run-all: skip
# Engine stand probe (instance A only): take an engine out with the crane, build it on the stand with the trace on,
# and log the stand state every second.
param($Ctx)

$a = $Ctx.Instances[0]
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments "Off" | Out-Null
Send-HarnessCommand -Instance $a -Verb tool-trace -Arguments "on" | Out-Null
Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 car_boltatlanta 0" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
$crane = Send-HarnessCommand -Instance $a -Verb crane-out -Arguments "0"
Write-Host "crane : $($crane | ConvertTo-Json -Compress)"
Start-Sleep -Seconds 2
Write-Host "context : $(Send-HarnessCommand -Instance $a -Verb tool-stand-context | ConvertTo-Json -Compress)"
Write-Host "put : $(Send-HarnessCommand -Instance $a -Verb tool-put -Arguments "EngineStand1 $($crane.group)" | ConvertTo-Json -Compress)"
foreach ($i in 1..8) {
    Start-Sleep -Seconds 1
    Write-Host "list-$i : $(Send-HarnessCommand -Instance $a -Verb tool-list | ConvertTo-Json -Compress -Depth 6)"
}
$Ctx.Result.passed = $true

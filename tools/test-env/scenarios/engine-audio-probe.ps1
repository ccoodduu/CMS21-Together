# run-all: skip
# areas: presence
# Which engine audio clips a client has loaded before and after starting an engine (for the remote engine sound).
param($Ctx)

$a = $Ctx.Instances[0]
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 car_boltatlanta 0" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "0" } while (-not ($r.state -eq "Ready" -and $r.loaded) -and (Get-Date) -lt $deadline)
foreach ($filter in "idle", "engine", "") {
    Write-Host "before-$filter : $(Send-HarnessCommand -Instance $a -Verb audio-clips -Arguments $filter | ConvertTo-Json -Compress)"
}
Send-HarnessCommand -Instance $a -Verb sit -Arguments "0 left" | Out-Null
Start-Sleep -Seconds 4
Send-HarnessCommand -Instance $a -Verb engine -Arguments "on" | Out-Null
Start-Sleep -Seconds 5
foreach ($filter in "idle", "engine") {
    Write-Host "after-$filter : $(Send-HarnessCommand -Instance $a -Verb audio-clips -Arguments $filter | ConvertTo-Json -Compress)"
}
$Ctx.Result.passed = $true

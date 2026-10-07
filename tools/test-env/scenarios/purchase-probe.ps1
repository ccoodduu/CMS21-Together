# run-all: skip
# Probe for row 6 part 2: which windows the junkyard registers, and what GameScript.BuyCar needs there.
param($Ctx)

$a = $Ctx.Instances[0]
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb buy-probe | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "probe_garage.json") -Encoding utf8
Send-HarnessCommand -Instance $a -Verb travel -Arguments "Junkyard" | Out-Null
Wait-HarnessStatus -Instance $a -TimeoutSec 180 -What "junkyard" -Condition { param($s) $s.scene -match "(?i)junkyard" -and $s.playable } | Out-Null
Start-Sleep -Seconds 10
$probe = Send-HarnessCommand -Instance $a -Verb buy-probe
$probe | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "probe_junkyard.json") -Encoding utf8
Write-Host ($probe | ConvertTo-Json -Depth 5)
Send-HarnessCommand -Instance $a -Verb outdoor-cars | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "cars.json") -Encoding utf8
$Ctx.Result.passed = $true

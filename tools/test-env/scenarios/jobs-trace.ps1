# run-all: skip
# areas: jobs
# sync-orders-and-jobs spike 1.3 (instance A only, connected; the job code is still vanilla): generate, decline,
# expire, accept, examine, check and finish orders and a story mission with the jobs trace on. Each step's trace
# report goes to jobs-trace.json in the run folder; the call order is in client_A.log ("jobs-trace" lines).
param($Ctx)

$a = $Ctx.Instances[0]
$out = [ordered]@{}
function Step([string]$Name, $Value) { $script:out[$Name] = $Value; Write-Host "$Name : $($Value | ConvertTo-Json -Depth 6 -Compress)" }
function Report([string]$Name) { Step "trace-$Name" (Send-HarnessCommand -Instance $a -Verb jobs-trace -Arguments "report").counts }
function Try-Step([string]$Name, [string]$Verb, [string]$Arguments = "") {
    try { Step $Name (Send-HarnessCommand -Instance $a -Verb $Verb -Arguments $Arguments) } catch { Step $Name "ERROR: $($_.Exception.Message)" }
}

Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments "Off" | Out-Null
Step "trace-on" (Send-HarnessCommand -Instance $a -Verb jobs-trace -Arguments "on")
Send-HarnessCommand -Instance $a -Verb orders-autogen -Arguments "off" | Out-Null

Try-Step "order-slots" "order-slots"
Try-Step "generate-ttl" "orders-generate" "20"
Try-Step "generate-2" "orders-generate"
Try-Step "generate-3" "orders-generate"
$list = Send-HarnessCommand -Instance $a -Verb orders-list
Step "orders" $list
Report "generated"

$jobs = @($list.jobs | Where-Object { -not $_.IsMission -and $_.timeToEnd -gt 30 })
if ($jobs.Count -ge 2) {
    Try-Step "decline" "orders-decline" "$($jobs[0].id)"
    Report "declined"
    Start-Sleep -Seconds 30
    Step "orders-after-expiry" (Send-HarnessCommand -Instance $a -Verb orders-list)
    Report "expired"

    $take = $jobs[1].id
    Try-Step "accept" "orders-accept" "$take"
    Start-Sleep -Seconds 15
    Step "cars-after-accept" (Send-HarnessCommand -Instance $a -Verb placement)
    Report "accepted"
    Try-Step "examine" "job-examine" "$take"
    Try-Step "repair" "job-repair" "$take"
    Start-Sleep -Seconds 2
    Try-Step "check" "job-check" "$take"
    Report "checked"
    $moneyBefore = (Send-HarnessCommand -Instance $a -Verb dump).stats.money
    Try-Step "finish" "job-finish" "$take"
    Start-Sleep -Seconds 10
    Step "money" @{ before = $moneyBefore; after = (Send-HarnessCommand -Instance $a -Verb dump).stats.money }
    Step "cars-after-finish" (Send-HarnessCommand -Instance $a -Verb placement)
    Report "finished"
} else {
    $Ctx.Result.notes += "fewer than two regular orders"
}

Try-Step "mission" "orders-mission" "1"
$mission = @((Send-HarnessCommand -Instance $a -Verb orders-list).jobs | Where-Object { $_.IsMission })[0]
if ($mission) {
    Try-Step "mission-accept" "orders-accept" "$($mission.id)"
    Start-Sleep -Seconds 15
    Report "mission-accepted"
}

Step "trace-final" (Send-HarnessCommand -Instance $a -Verb jobs-trace -Arguments "off")
$out | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "jobs-trace.json") -Encoding utf8
$Ctx.Result.passed = $true

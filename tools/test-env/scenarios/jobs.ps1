# areas: jobs, economy
# sync-orders-and-jobs 7.1: the elected generator's orders reach both clients with server ids; an order with a 20 s
# TTL expires on both; B declines one; A and B accept the same order at once (net-hold), the server approves one;
# the taker's customer car appears for both, the taker finishes the job, the payout is applied once for both and the
# job and its car disappear for both.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Jobs([string]$Name) { (Send-HarnessCommand -Instance $Name -Verb dump).jobs }

function Wait-Jobs([string]$What, [scriptblock]$Condition, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 700
        $ja = Jobs $a
        $jb = Jobs $b
        $sa = $ja | ConvertTo-Json -Depth 5 -Compress
        $sb = $jb | ConvertTo-Json -Depth 5 -Compress
        if ($sa -eq $sb -and (& $Condition $ja)) { break }
    } while ((Get-Date) -lt $deadline)
    Check ($sa -eq $sb) "$What`: A and B agree ($sa)"
    if ($sa -ne $sb) { Write-Host "  B: $sb" }
    Check ([bool](& $Condition $ja)) "$What`: expected state reached"
    return $ja
}

function Hold([string]$Mode) { foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments $Mode | Out-Null } }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null
    Send-HarnessCommand -Instance $name -Verb orders-autogen -Arguments "off" | Out-Null
}
Start-Sleep -Seconds 2
$genA = (Get-HarnessStatus $a).isOrderGenerator
$genB = (Get-HarnessStatus $b).isOrderGenerator
Check ($genA -and -not $genB) "A (first in the garage) is the order generator (A $genA, B $genB)"
$gen = if ($genB) { $b } else { $a }

$base = @((Jobs $a).orders).Count
Send-HarnessCommand -Instance $gen -Verb orders-generate -Arguments "20" | Out-Null
Send-HarnessCommand -Instance $gen -Verb orders-generate | Out-Null
Send-HarnessCommand -Instance $gen -Verb orders-generate | Out-Null
$jobs = Wait-Jobs "three generated orders" { param($j) @($j.orders).Count -eq $base + 3 }
$ids = @($jobs.orders | ForEach-Object { $_.id })
Write-Host "order ids: $($ids -join ', ')"

$jobs = Wait-Jobs "the 20 s order expired" { param($j) @($j.orders).Count -eq $base + 2 } 40
$open = @($jobs.orders | ForEach-Object { $_.id })
Send-HarnessCommand -Instance $b -Verb orders-decline -Arguments "$($open[0])" | Out-Null
$jobs = Wait-Jobs "B declined one" { param($j) @($j.orders).Count -eq $base + 1 }
$take = @($jobs.orders)[-1].id

$mark = Get-ServerLogMark
Hold "on"
Send-HarnessCommand -Instance $a -Verb orders-accept -Arguments "$take" | Out-Null
Send-HarnessCommand -Instance $b -Verb orders-accept -Arguments "$take" | Out-Null
Start-Sleep -Seconds 1
Hold "off"
$claimed = try { Wait-ServerLog -Pattern "Order $take claimed by client (\d+)" -After $mark -TimeoutSec 10 } catch { $null }
$refused = try { Wait-ServerLog -Pattern "Accept of order $take by client \d+ refused" -After $mark -TimeoutSec 10 } catch { $null }
Check ([bool]$claimed -and [bool]$refused) "one accept approved, the other refused ($claimed / $refused)"
$taker = if ($claimed -match "client 2") { $b } else { $a }
$jobs = Wait-Jobs "the taken job is active for both" { param($j) @($j.active | Where-Object { $_.id -eq $take }).Count -eq 1 -and @($j.orders | Where-Object { $_.id -eq $take }).Count -eq 0 } 90
$loader = @($jobs.active | Where-Object { $_.id -eq $take })[0].carLoaderID
Write-Host "job $take on loader $loader, taken by $taker"
foreach ($name in $Ctx.Instances) {
    $r = Send-HarnessCommand -Instance $name -Verb car-ready -Arguments "$loader"
    Check ($r.loaded) "$name has the customer car on loader $loader ($($r.car))"
}

$moneyBefore = (Send-HarnessCommand -Instance $a -Verb dump).stats.money
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $taker -Verb job-end-direct -Arguments "$take" | Out-Null
$ended = try { Wait-ServerLog -Pattern "Job $take ended by client \d+: payout (\d+)" -After $mark -TimeoutSec 30 } catch { $null }
Check ([bool]$ended) "the server got the job end ($ended)"
Wait-Jobs "the finished job is gone for both" { param($j) @($j.active | Where-Object { $_.id -eq $take }).Count -eq 0 } 30 | Out-Null
Start-Sleep -Seconds 3
$moneyA = (Send-HarnessCommand -Instance $a -Verb dump).stats.money
$moneyB = (Send-HarnessCommand -Instance $b -Verb dump).stats.money
Check ($moneyA -eq $moneyB) "A and B have the same money after the payout ($moneyBefore -> A $moneyA, B $moneyB)"
foreach ($name in $Ctx.Instances) {
    $r = Send-HarnessCommand -Instance $name -Verb car-ready -Arguments "$loader"
    Check (-not $r.loaded) "$name no longer has the customer car"
}
$endLines = @((Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $mark) | Where-Object { $_ -match "Job $take ended" })
Check ($endLines.Count -eq 1) "the job was paid once ($($endLines.Count))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

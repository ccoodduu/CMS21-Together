# areas: jobs, connect, persistence
# sync-orders-and-jobs 7.2/7.3: A alone generates orders and takes one; B joins late and sees the same orders, the active
# job and its customer car; a job car spawn without a claim is refused; B accepts an order and leaves at once, so the
# claim is released; after a server restart both see the same orders and active job as before.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Jobs([string]$Name) { (Send-HarnessCommand -Instance $Name -Verb dump).jobs | ConvertTo-Json -Depth 5 -Compress }

function Wait-SameJobs([string]$What, [int]$TimeoutSec = 40) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 700
        $ja = Jobs $a
        $jb = Jobs $b
    } while ($ja -ne $jb -and (Get-Date) -lt $deadline)
    Check ($ja -eq $jb) "$What`: A and B agree (A: $ja, B: $jb)"
    return $ja
}

function Try-ServerLog([string]$Pattern, [int]$After, [int]$TimeoutSec) {
    try { Wait-ServerLog -Pattern $Pattern -After $After -TimeoutSec $TimeoutSec } catch { $null }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments "Off" | Out-Null
Send-HarnessCommand -Instance $a -Verb orders-autogen -Arguments "off" | Out-Null
Send-HarnessCommand -Instance $a -Verb orders-generate -Arguments "300" | Out-Null
Send-HarnessCommand -Instance $a -Verb orders-generate -Arguments "300" | Out-Null
Start-Sleep -Seconds 2
$orders = @((Send-HarnessCommand -Instance $a -Verb dump).jobs.orders | Where-Object { -not $_.IsMission })
$take = $orders[-1].id
Send-HarnessCommand -Instance $a -Verb orders-accept -Arguments "$take" | Out-Null
$deadline = (Get-Date).AddSeconds(60)
do { Start-Sleep -Milliseconds 700; $active = @((Send-HarnessCommand -Instance $a -Verb dump).jobs.active) } while ($active.Count -eq 0 -and (Get-Date) -lt $deadline)
Check ($active.Count -eq 1) "A took order $take"
$loader = $active[0].carLoaderID
Start-Sleep -Seconds 3

Connect-HarnessInstance $b; Wait-InGarage $b
Send-HarnessCommand -Instance $b -Verb guard-set -Arguments "Off" | Out-Null
Send-HarnessCommand -Instance $b -Verb orders-autogen -Arguments "off" | Out-Null
$before = Wait-SameJobs "late joiner B"
$r = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "$loader"
Check ($r.loaded) "B has the customer car on loader $loader ($($r.car))"

$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb job-spawn-unclaimed -Arguments "3" | Out-Null
Check ([bool](Try-ServerLog "Job car .* for job 9999 .* refused" $mark 10)) "a job car spawn without a claim is refused"

$open = @((Send-HarnessCommand -Instance $b -Verb dump).jobs.orders | Where-Object { -not $_.IsMission })
if ($open.Count -gt 0) {
    $mark = Get-ServerLogMark
    Send-HarnessCommand -Instance $b -Verb orders-accept -Arguments "$($open[0].id)" | Out-Null
    Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
    Check ([bool](Try-ServerLog "Order $($open[0].id) open again \(client \d+ left" $mark 30)) "B's claim is released when B leaves during the take"
    Wait-HarnessStatus -Instance $b -TimeoutSec 60 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
    Connect-HarnessInstance $b; Wait-InGarage $b
    Send-HarnessCommand -Instance $b -Verb orders-autogen -Arguments "off" | Out-Null
    $before = Wait-SameJobs "after B's return"
}

$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
Stop-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 30 -What "menu after kill" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
    Send-HarnessCommand -Instance $name -Verb mp-ui -Arguments "ok" | Out-Null
}
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb orders-autogen -Arguments "off" | Out-Null }
$after = Wait-SameJobs "after the restart"
$beforeIds = (($before | ConvertFrom-Json).orders | ForEach-Object { $_.id }) -join ","
$afterIds = (($after | ConvertFrom-Json).orders | ForEach-Object { $_.id }) -join ","
Check ($beforeIds -eq $afterIds) "the open orders survived the restart ($beforeIds / $afterIds)"
$activeAfter = @(($after | ConvertFrom-Json).active | Where-Object { $_.id -eq $take })
Check ($activeAfter.Count -eq 1) "the active job survived the restart"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

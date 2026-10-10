# areas: jobs, economy
# Soak 20261010-045235_L2 (rule 2, world.exp 183 vs 182): A tried to end a job whose car was not repaired; the game's
# end coroutine refused it, but the job-end context stayed open for its 10 s timeout and took the experience of A's
# next part mount as job XP, which only a committed job end sends. A tries to end an unrepaired job, then takes a part
# off its car at once: A and B (the server's shared experience) must have the same experience. Desync autofix is off.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Jobs([string]$Name) { (Cmd $Name dump).jobs }
function Exp([string]$Name) { [int](Cmd $Name dump).stats.exp }

Set-ServerConfigValues $Ctx.ServerDir @{ desync_check_interval_seconds = 600; desync_autofix = $false }
Restart-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name orders-autogen "off" | Out-Null; Cmd $name guard-set "Off" | Out-Null }

$gen = if ((Get-HarnessStatus $a).isOrderGenerator) { $a } else { $b }
$before = @((Jobs $a).orders | Where-Object { -not $_.IsMission } | ForEach-Object { $_.id })
Cmd $gen orders-generate | Out-Null
$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Seconds 1
    $order = @((Jobs $a).orders | Where-Object { -not $_.IsMission -and $before -notcontains $_.id })[0]
} while (-not $order -and (Get-Date) -lt $deadline)
Check ([bool]$order) "an order was generated ($($order.id))"
if (-not $order) { $Ctx.Result.notes += $failures; $Ctx.Result.passed = $false; return }

Cmd $a orders-accept "$($order.id)" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do {
    Start-Sleep -Seconds 1
    $job = @((Jobs $a).active | Where-Object { $_.id -eq $order.id })[0]
    $ready = $job -and $(try { (Cmd $a car-ready "$($job.carLoaderID)").state -eq "Ready" } catch { $false })
} while (-not $ready -and (Get-Date) -lt $deadline)
Check ([bool]$ready) "A took the job, its car is ready (loader $($job.carLoaderID))"
if (-not $ready) { $Ctx.Result.notes += $failures; $Ctx.Result.passed = $false; return }
$loader = [int]$job.carLoaderID
Start-Sleep -Seconds 3

$expBefore = Exp $a
Check ($expBefore -eq (Exp $b)) "A and B start with the same experience ($expBefore)"
$mark = Get-ServerLogMark
Cmd $a job-finish "$($order.id)" | Out-Null
Start-Sleep -Seconds 2
Check (@((Jobs $a).active | Where-Object { $_.id -eq $order.id }).Count -eq 1) "the game refused to end the unrepaired job"
$off = Cmd $a part-fast-unmount "$loader"
Write-Host "  A took $($off.key) off"
$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Seconds 1
    $ea = Exp $a
    $eb = Exp $b
} while (($ea -eq $expBefore -or $ea -ne $eb) -and (Get-Date) -lt $deadline)
Check ($ea -gt $expBefore) "A gained experience for the part ($expBefore -> $ea)"
Check ($ea -eq $eb) "A and B have the same experience after the part ($ea / $eb)"
Check (-not (Select-String -Path (Join-Path $Ctx.ServerDir "Log\Latest.txt") -Pattern "Job $($order.id) ended" -Quiet)) "the server got no job end"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

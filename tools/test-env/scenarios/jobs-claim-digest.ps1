# areas: jobs
# Soak 2026-10-10 (contention, "order:38.open - vs 1"): while an order is claimed, the other clients have dropped it, so
# the server's jobs digest must not count it as open. A asks for an order but the server's answer is held on A, so the
# order stays claimed. For 25 s (five reconcile rounds) the server must log no jobs mismatch. After the hold, A takes the
# order, and no jobs mismatch is ever confirmed.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Note([string]$Text) { $Ctx.Result.notes += $Text; Write-Host $Text }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Jobs([string]$Name) { (Send-HarnessCommand -Instance $Name -Verb dump).jobs }
function Regular($Jobs) { @($Jobs.orders | Where-Object { -not $_.IsMission }) }
function Has-Order([string]$Name, [int]$Id) { @((Jobs $Name).orders | Where-Object { $_.id -eq $Id }).Count -eq 1 }
function Jobs-Mismatches([int]$After) {
    $lines = Get-ServerLogLines
    @($lines[$After..($lines.Count - 1)] | Where-Object { $_ -match "\[Desync\] jobs for client \d+: mismatch" })
}

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
$gen = if ((Get-HarnessStatus $b).isOrderGenerator) { $b } else { $a }
if (@(Regular (Jobs $a)).Count -eq 0) {
    Send-HarnessCommand -Instance $gen -Verb orders-generate -Arguments "900" | Out-Null
    Start-Sleep -Seconds 3
}
$order = @(Regular (Jobs $a)) | Select-Object -First 1
Check ([bool]$order) "an order is open"
if (-not $order) { $Ctx.Result.notes += $failures; $Ctx.Result.passed = $false; return }
$id = $order.id
Check ((Has-Order $b $id)) "B has order $id"

Send-HarnessCommand -Instance $a -Verb net-hold -Arguments "only OrderActionResult" | Out-Null
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb orders-accept -Arguments "$id" | Out-Null
$claim = try { Wait-ServerLog -Pattern "Order $id claimed by client" -After $mark -TimeoutSec 15 } catch { $null }
Check ([bool]$claim) "the server claimed order $id for A ($claim)"
$deadline = (Get-Date).AddSeconds(15)
while ((Has-Order $b $id) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
Check (-not (Has-Order $b $id)) "B dropped order $id on the claim"

$held = Get-ServerLogMark
Start-Sleep -Seconds 25
$mismatches = Jobs-Mismatches $held
Check ($mismatches.Count -eq 0) "no jobs mismatch while order $id is claimed ($($mismatches.Count): $($mismatches | Select-Object -First 3))"

Send-HarnessCommand -Instance $a -Verb net-hold -Arguments "off" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do {
    Start-Sleep -Milliseconds 700
    $active = @((Jobs $a).active | Where-Object { $_.id -eq $id })
} while ($active.Count -eq 0 -and (Get-Date) -lt $deadline)
Check ($active.Count -eq 1) "A took order $id after the hold"

Start-Sleep -Seconds 15
$after = Jobs-Mismatches $held
$confirmed = @($after | Where-Object { $_ -match "confirmed" })
Check ($confirmed.Count -eq 0) "no confirmed jobs mismatch in the whole run ($($confirmed | Select-Object -First 3))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

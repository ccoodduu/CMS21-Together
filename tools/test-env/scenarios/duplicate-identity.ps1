# areas: connect, persistence
# session-persistence-and-rejoin 7.5 (and 4.2): A connects; B takes A's player key and connects. B must be refused
# with the duplicate reason and stay in the menu while A stays in the session. With its own key B then joins normally.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
$keyA = (Send-HarnessCommand -Instance $a -Verb player-key).key
$shortA = "guid:" + $keyA.Substring(0, [math]::Min(8, $keyA.Length))
Send-HarnessCommand -Instance $b -Verb player-key -Arguments $keyA | Out-Null

$mark = Get-ServerLogMark
Connect-HarnessInstance $b
$refused = $null
try {
    $refused = Wait-HarnessStatus -Instance $b -TimeoutSec 30 -What "refused" -Condition { param($s) $s.joinStatus -eq "Failed" -and -not $s.connected }
} catch { }
Check ($null -ne $refused -and $refused.lastDisconnect.reason -eq "DuplicateIdentity") "B was refused as a duplicate ($($refused.lastDisconnect.reason): $($refused.lastDisconnect.message))"
Start-Sleep -Seconds 2
$statusB = Get-HarnessStatus $b
Check ($statusB.scene -eq "Menu") "B stayed in the menu ($($statusB.scene))"
$serverLine = $null
try { $serverLine = Wait-ServerLog -Pattern "$([regex]::Escape($shortA)) is already connected as Client\[\d+\]" -After $mark -TimeoutSec 5 } catch { }
Check ($null -ne $serverLine) "the server named the duplicate key"

Start-Sleep -Seconds 5
$statusA = Get-HarnessStatus $a
Check ($statusA.connected -and $statusA.syncAcked -and $statusA.joinStatus -eq "InSession") "A is still in the session ($($statusA.joinStatus))"
$lines = Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $mark
Check (@($lines | Where-Object { $_ -match "Player $($statusA.playerId) left" }).Count -eq 0) "the server did not drop A"

Send-HarnessCommand -Instance $b -Verb mp-ui -Arguments "ok" | Out-Null
Send-HarnessCommand -Instance $b -Verb player-key -Arguments "reset" | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
$statusA = Get-HarnessStatus $a
Check ($statusA.connected -and $statusA.syncAcked) "with its own key B joins and A stays"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

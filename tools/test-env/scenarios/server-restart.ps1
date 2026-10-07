# areas: persistence
# Shared state survives a server save, a hard kill and a restart: A changes stats, the server saves and is
# killed, A goes back to the menu, the server starts again, A and B connect and both see A's state.
param($Ctx)

$a, $b = $Ctx.Instances

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
    Write-Host "$Name is in the garage (sync acked)"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

Connect-HarnessInstance $a
Wait-InGarage $a

$before = Send-HarnessCommand -Instance $a -Verb dump
Send-HarnessCommand -Instance $a -Verb stats-add -Arguments "7 300" | Out-Null
$deadline = (Get-Date).AddSeconds(20)
do {
    Start-Sleep -Milliseconds 500
    $changed = Send-HarnessCommand -Instance $a -Verb dump
} while ($changed.stats.scrap -eq $before.stats.scrap -and (Get-Date) -lt $deadline)
if ($changed.stats.scrap -ne $before.stats.scrap + 7) { throw "stats-add did not reach A (scraps $($before.stats.scrap) -> $($changed.stats.scrap))" }

$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
$dumpBeforeKill = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "before-kill"

Stop-TestServer
Write-Host "Server killed"
# The client notices the lost server by itself (TCP close) and returns to the menu.
$lost = Wait-HarnessStatus -Instance $a -TimeoutSec 30 -What "menu after kill" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected }
if ($lost.joinStatus -ne "Disconnected") { $Ctx.Result.notes += "A after the server kill: joinStatus $($lost.joinStatus), expected Disconnected" }
Send-HarnessCommand -Instance $a -Verb mp-ui -Arguments "ok" | Out-Null

Start-TestServer | Out-Null
Write-Host "Server restarted"
$restartLog = Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") -Raw
if ($restartLog -match "Migrating v1 save") { $Ctx.Result.notes += "Second start migrated again" }

Connect-HarnessInstance $a
Wait-InGarage $a
Connect-HarnessInstance $b
Wait-InGarage $b
Start-Sleep -Seconds 3

$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "after-restart"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "after-restart"

$failures = @()
foreach ($pair in @(@($a, $dumpA), @($b, $dumpB))) {
    $diff = Compare-HarnessDumps $dumpBeforeKill $pair[1] -Sections @("stats", "inventory")
    if ($diff.Count -gt 0) { $failures += "$($pair[0]) differs from A before the kill in: $($diff -join ', ')" }
}
$migrations = @(Get-ChildItem -LiteralPath (Join-Path $Ctx.ServerDir "Saves\backups") -Filter "premigration_v1_*.json" -ErrorAction SilentlyContinue)
$Ctx.Result.notes += "premigration copies: $($migrations.Count)"
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0 -and -not ($Ctx.Result.notes -contains "Second start migrated again"))

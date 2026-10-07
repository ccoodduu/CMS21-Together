# run-all: fresh
# Both clients reach the menu, connect to the local server one after the other, load into the garage
# and end up with the same shared state (stats, inventory, cars).
param($Ctx)

$a, $b = $Ctx.Instances
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
    Write-Host "$name is in the main menu"
}

foreach ($name in $Ctx.Instances) {
    Connect-HarnessInstance $name
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "garage after connect" -Condition {
        param($s) $s.connectionValid -and $s.initialSyncFinished -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
    Write-Host "$name is connected and in the garage"
}

Start-Sleep -Seconds 5
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "garage"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "garage"
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "garage"
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "garage"

$differences = Compare-HarnessDumps $dumpA $dumpB
$Ctx.Result.notes += "Remote players visible: $a=$($dumpA.remotePlayers), $b=$($dumpB.remotePlayers)"
if ($differences.Count -gt 0) { $Ctx.Result.notes += "Shared state differs in: $($differences -join ', ')" }
$Ctx.Result.passed = ($differences.Count -eq 0 -and $dumpA.remotePlayers -ge 1 -and $dumpB.remotePlayers -ge 1)

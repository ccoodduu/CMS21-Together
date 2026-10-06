# Multiplayer feature guard: while connected, features that do not sync yet are refused with a message and logged
# (windows, modes, scenes, pie options); LogOnly lets them through with a "would block" entry; after leaving the
# session everything is allowed again. The pause menu offers no save while connected and does not stop the game.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Try-Guard([string]$Instance, [string]$Key, [string]$Expected, [string]$Option = "") {
    $r = Send-HarnessCommand -Instance $Instance -Verb guard-try -Arguments "$Key $Option".Trim()
    Write-Host ("{0} {1,-28} {2,-8} mode={3} shown={4} active={5} msg={6}" -f $Instance, $Key, $r.result, $r.gameMode, $r.shown, $r.windowActive, $r.message)
    if ($r.result -ne $Expected) { $script:failures += "$Instance $Key was $($r.result), expected $Expected" }
    return $r
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
Start-Sleep -Seconds 3
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "before"

Try-Guard $a "Window:Shop" "allowed" | Out-Null
$orders = Try-Guard $a "Window:Orders" "blocked"
if ($orders.windowActive) { $failures += "Orders is active after a blocked Show" }
if (-not $orders.message) { $failures += "no message for the blocked Orders window" }
Start-Sleep -Milliseconds 300
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "orders_blocked"
Start-Sleep -Seconds 2

$mode = Try-Guard $a "Mode:BonusDisassemble" "blocked"
if ($mode.gameMode -ne "Garage") { $failures += "game mode after a blocked BonusDisassemble is $($mode.gameMode)" }
$inventory = Try-Guard $a "Window:Inventory" "allowed"
if (-not $inventory.shown) { $failures += "Inventory did not open after the blocked mode change" }
Start-Sleep -Seconds 2

Try-Guard $a "Pie:wheel_take" "blocked" | Out-Null
$null = Send-HarnessCommand -Instance $a -Verb guard-try -Arguments "PieMenu:TireChanger"
Start-Sleep -Milliseconds 1500
$menu = Send-HarnessCommand -Instance $a -Verb guard-try -Arguments "PieState:TireChanger"
$menu.options | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "pie_tirechanger.txt") -Encoding utf8
if (-not ($menu.options | Where-Object { $_ -like "wheel_take enabled=False" })) { $failures += "tire changer menu does not show wheel_take locked: $($menu.options -join ', ')" }
Start-Sleep -Seconds 1
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "pie_tirechanger"
Send-HarnessCommand -Instance $a -Verb guard-try -Arguments "Window:PieMenu" | Out-Null
Start-Sleep -Seconds 2

Try-Guard $a "Action:SellCar" "blocked" | Out-Null
Try-Guard $a "Scene:Junkyard" "blocked" | Out-Null
Start-Sleep -Seconds 2
Try-Guard $a "Scene:Junkyard" "blocked" "void" | Out-Null
Start-Sleep -Seconds 4
$dumpA = Send-HarnessCommand -Instance $a -Verb dump
if ($dumpA.local.scene -ne "Garage") { $failures += "A left the garage after blocked travel: $($dumpA.local.scene)" }

$log = Send-HarnessCommand -Instance $a -Verb guard-log
$log | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "guard_log_A.json") -Encoding utf8
foreach ($key in @("Window:Orders", "Mode:BonusDisassemble", "Scene:Junkyard", "Pie:wheel_take")) {
    if ($log.keys -notcontains $key) { $failures += "guard-log on A lacks $key" }
}

Try-Guard $b "Window:PauseQuit" "allowed" "keep" | Out-Null
Start-Sleep -Seconds 1
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "pause"
$scrapBefore = (Send-HarnessCommand -Instance $b -Verb dump).stats.scrap
Send-HarnessCommand -Instance $a -Verb stats-add -Arguments "5 0" | Out-Null
try {
    Wait-HarnessDump -Instance $b -TimeoutSec 2 -What "scrap +5 with the pause menu open" -Condition { param($d) $d.stats.scrap -eq $scrapBefore + 5 } | Out-Null
} catch { $failures += "B did not get A's scrap within 2 s with the pause menu open" }
Send-HarnessCommand -Instance $b -Verb guard-try -Arguments "Window:Shop" | Out-Null

$dumpBAfter = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "after"
$diff = Compare-HarnessDumps $dumpB $dumpBAfter -Sections @("inventory", "cars")
if ($diff.Count -gt 0) { $failures += "B's dump changed in: $($diff -join ', ')" }

Send-HarnessCommand -Instance $a -Verb guard-set -Arguments logonly | Out-Null
Try-Guard $a "Window:Orders" "allowed" | Out-Null
$log = Send-HarnessCommand -Instance $a -Verb guard-log
if (-not ($log.blocks | Where-Object { $_ -like "*would block Window:Orders" })) { $failures += "no 'would block Window:Orders' entry in LogOnly" }
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments enforce | Out-Null

foreach ($name in $Ctx.Instances) {
    $status = Get-HarnessStatus $name
    if (-not $status.connectionValid) { $failures += "$name is no longer connected" }
}

Send-HarnessCommand -Instance $a -Verb to-menu | Out-Null
Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
$offline = Send-HarnessCommand -Instance $a -Verb guard-try -Arguments "Window:Orders"
if ($offline.result -ne "allowed") { $failures += "Window:Orders in the menu (not connected) was $($offline.result)" }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

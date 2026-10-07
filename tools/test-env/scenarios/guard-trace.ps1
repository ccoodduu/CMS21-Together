# run-all: skip
# areas: guard
# Spike for multiplayer-guard task 1.1: dumps the pie menu ini and machine entries, and walks windows, modes, scenes
# and pie options with guard-try so the trace log shows what each entry point reaches.
param($Ctx)

$a, $b = $Ctx.Instances

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Save-Json([string]$Label, $Value) {
    $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "$Label.json") -Encoding utf8
}

function Try-Guard([string]$Instance, [string]$Key, [string]$Option = "") {
    $r = Send-HarnessCommand -Instance $Instance -Verb guard-try -Arguments "$Key $Option".Trim()
    $line = "{0} {1,-34} {2,-11} mode={3} msg={4} shown={5} active={6}" -f $Instance, $Key, $r.result, $r.gameMode, $r.message, $r.shown, $r.windowActive
    Write-Host $line
    $script:lines += $line
    return $r
}

$script:lines = @()
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
Start-Sleep -Seconds 3

Send-HarnessCommand -Instance $a -Verb guard-trace -Arguments on | Out-Null
Save-Json "trace_state_start" (Send-HarnessCommand -Instance $a -Verb guard-trace -Arguments state)
(Send-HarnessCommand -Instance $a -Verb guard-trace -Arguments ini) | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "piemenu.ini") -Encoding utf8
Save-Json "trace_machines" (Send-HarnessCommand -Instance $a -Verb guard-trace -Arguments machines)
Save-Json "guard_rules" (Send-HarnessCommand -Instance $a -Verb guard-rules)

Try-Guard $a "Window:Shop" | Out-Null
Try-Guard $a "Window:Orders" | Out-Null
Start-Sleep -Milliseconds 300
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "orders_blocked"
Start-Sleep -Seconds 2
Try-Guard $a "Mode:GarageDisassemble" | Out-Null
Save-Json "trace_state_after_mode" (Send-HarnessCommand -Instance $a -Verb guard-trace -Arguments state)
Try-Guard $a "Window:Inventory" | Out-Null
Start-Sleep -Seconds 2
Try-Guard $a "Pie:wheel_take" | Out-Null
Start-Sleep -Seconds 2
Save-Json "pie_balancer" (Send-HarnessCommand -Instance $a -Verb guard-try -Arguments "PieMenu:WheelBalancer")
Start-Sleep -Seconds 2
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "pie_balancer"
Save-Json "trace_state_pie" (Send-HarnessCommand -Instance $a -Verb guard-trace -Arguments state)
Start-Sleep -Seconds 2
Try-Guard $a "Scene:Junkyard" | Out-Null
Start-Sleep -Seconds 4
Try-Guard $a "Scene:Junkyard" "void" | Out-Null
Start-Sleep -Seconds 4
Save-Json "trace_state_after_scene" (Send-HarnessCommand -Instance $a -Verb guard-trace -Arguments state)
$dumpA = Send-HarnessCommand -Instance $a -Verb dump
$script:lines += "A local scene after scene tries: $($dumpA.local.scene)"

Send-HarnessCommand -Instance $b -Verb guard-trace -Arguments on | Out-Null
Try-Guard $b "Window:PauseQuit" "keep" | Out-Null
Start-Sleep -Seconds 1
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "pause"
Save-Json "trace_state_pause" (Send-HarnessCommand -Instance $b -Verb guard-trace -Arguments state)
$scrapBefore = (Send-HarnessCommand -Instance $b -Verb dump).stats.scrap
Send-HarnessCommand -Instance $a -Verb stats-add -Arguments "5 0" | Out-Null
Start-Sleep -Seconds 2
$scrapAfter = (Send-HarnessCommand -Instance $b -Verb dump).stats.scrap
$script:lines += "B scrap with pause open: $scrapBefore -> $scrapAfter"

Send-HarnessCommand -Instance $a -Verb guard-set -Arguments logonly | Out-Null
foreach ($w in @("Orders", "Map", "Parking", "Upgrades", "Warehouse", "Photo", "CarLocationWindow", "TakenItems", "ParkingManagement", "Tutorials", "Settings")) {
    Try-Guard $a "Window:$w" | Out-Null
    Start-Sleep -Milliseconds 800
}
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "logonly_end"
foreach ($m in @("ExamineCondition", "Garage", "PhotoMode", "Garage")) { Try-Guard $a "Mode:$m" | Out-Null; Start-Sleep -Milliseconds 500 }
Save-Json "guard_log_a" (Send-HarnessCommand -Instance $a -Verb guard-log)
Save-Json "trace_state_end" (Send-HarnessCommand -Instance $a -Verb guard-trace -Arguments state)

Send-HarnessCommand -Instance $a -Verb to-menu | Out-Null
Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
$script:lines += "A reached the menu"
$script:lines | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "trace_lines.txt") -Encoding utf8
$Ctx.Result.notes += $script:lines
$Ctx.Result.passed = $true

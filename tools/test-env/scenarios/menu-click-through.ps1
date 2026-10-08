# areas: connect
# A click on the mod's main-menu panels must not also click the game's menu underneath (playtest 2026-10-08: Host
# opened the CMS 2026 news link). For every panel the probe points at a grid over the panel; at every point the
# game's EventSystem must be off, so no click reaches the game, and it must be on again outside the panels. The
# headless test games have no game button under the panels, so game buttons under a panel are only reported.
param($Ctx)

$a = $Ctx.Instances[0]
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $a -Verb $Verb -Arguments $Arguments }

function Probe([string]$Label) {
    Cmd menu-click-probe | Out-Null
    $deadline = (Get-Date).AddSeconds(120)
    do { Start-Sleep -Milliseconds 500; $r = Cmd menu-click-probe "result" } while ($r.running -and (Get-Date) -lt $deadline)
    Write-Host "$Label`: $($r.points) points, game menu on at $($r.enabledOver), game buttons under $($r.gameUiUnder), leaks $($r.leaks) $($r.leakExamples -join '; ')"
    Check (-not $r.running) "$Label`: the probe finished"
    Check ($r.points -gt 0) "$Label`: the panel covers some points"
    Check ($r.enabledOver -eq 0) "$Label`: the game's menu takes no clicks while the pointer is over the panel ($($r.enabledOver) of $($r.points) points)"
    Check ($r.leaks -eq 0) "$Label`: no click reaches a game button under the panel ($($r.leakExamples -join '; '))"
    Check ($r.eventSystemEnabledAfter -eq $true) "$Label`: the game's menu takes clicks again outside the panel"
    return $r
}

Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null

foreach ($panel in @("join", "host", "friends")) {
    Cmd mp-ui "open $panel" | Out-Null
    Start-Sleep -Milliseconds 500
    Probe "$panel panel" | Out-Null
}
Cmd mp-ui "close" | Out-Null

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

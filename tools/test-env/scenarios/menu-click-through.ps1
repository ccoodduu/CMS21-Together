# areas: connect
# A click on the mod's main-menu panels must not also click the game's menu underneath (playtest 2026-10-08: Host
# opened the CMS 2026 news link). For every panel the probe points at a grid over the panel; no point may have the
# game's EventSystem enabled with a game button under it, and the game's menu must work again outside the panels.
param($Ctx)

$a = $Ctx.Instances[0]
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $a -Verb $Verb -Arguments $Arguments }

function Probe([string]$Label) {
    Cmd menu-click-probe | Out-Null
    $deadline = (Get-Date).AddSeconds(120)
    do { Start-Sleep -Milliseconds 500; $r = Cmd menu-click-probe "result" } while ($r.running -and (Get-Date) -lt $deadline)
    Write-Host "$Label`: $($r.points) points, game buttons under $($r.gameUiUnder), leaks $($r.leaks) $($r.leakExamples -join '; ')"
    Check (-not $r.running) "$Label`: the probe finished"
    Check ($r.points -gt 0) "$Label`: the panel covers some points"
    Check ($r.leaks -eq 0) "$Label`: no click reaches a game button under the panel ($($r.leakExamples -join '; '))"
    Check ($r.eventSystemEnabledAfter -eq $true) "$Label`: the game's menu takes clicks again outside the panel"
    return $r
}

Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null

$under = 0
foreach ($panel in @("join", "host", "friends")) {
    Cmd mp-ui "open $panel" | Out-Null
    Start-Sleep -Milliseconds 500
    $under += (Probe "$panel panel").gameUiUnder
}
Cmd mp-ui "close" | Out-Null
Check ($under -gt 0) "some panel lies over a game button, so the check means something ($under points)"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

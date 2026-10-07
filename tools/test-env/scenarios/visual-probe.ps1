# areas: visuals
# run-all: skip
# remote-visual-feedback spike 1.3 probe: A starts unscrewing a part through vfx-unscrew; the part's renderers, bolt
# states and the game mode are written to probe_*.json before, during and after, with A's and B's vfx-trace reports.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "probe_$Label.json") -Encoding utf8 }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null; Cmd $name vfx-trace "on" | Out-Null }
Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Cmd $a vfx-stand "$loader 1.5" | Out-Null
Cmd $b vfx-stand "$loader 3" | Out-Null

$parts = @(Cmd $a vfx-parts "$loader")
Save "parts" $parts
$keys = @($parts | Select-Object -First 3 | ForEach-Object { $_.key })
foreach ($key in $keys) {
    Save "${key}_A_before".Replace(":", "_") (Cmd $a vfx-probe "$loader $key")
    Save "${key}_B_before".Replace(":", "_") (Cmd $b vfx-probe "$loader $key")
    Cmd $a vfx-unscrew "$loader $key" | Out-Null
    Start-Sleep -Seconds 2
    Save "${key}_A_2s".Replace(":", "_") (Cmd $a vfx-probe "$loader $key")
    Start-Sleep -Seconds 10
    Save "${key}_A_12s".Replace(":", "_") (Cmd $a vfx-probe "$loader $key")
    Save "${key}_A_dump".Replace(":", "_") (Cmd $a dump).visuals
    Save "${key}_B_after".Replace(":", "_") (Cmd $b vfx-probe "$loader $key")
    Save "${key}_B_dump".Replace(":", "_") (Cmd $b dump).visuals
    try { Cmd $a vfx-unscrew "$loader $key undo" | Out-Null } catch { Write-Host "undo: $($_.Exception.Message)" }
    Start-Sleep -Seconds 2
}
Save "trace_A" (Cmd $a vfx-trace "report")
Save "trace_B" (Cmd $b vfx-trace "report")
$Ctx.Result.passed = $true

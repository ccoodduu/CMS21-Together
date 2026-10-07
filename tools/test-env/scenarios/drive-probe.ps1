# areas: driving
# run-all: skip
# remote-visual-feedback spikes 8.1-8.3 (probe, no checks): what the car_drive pie option does in the garage, the track
# car's hierarchy and VPP state while A drives it, and an observer car built three ways next to A's car on the track.
# Results go to probe_*.json in the run folder.
param($Ctx)

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "probe_$Label.json") -Encoding utf8 }
function Try-Save([string]$Label, [scriptblock]$Block) {
    try { Save $Label (& $Block) } catch { Save $Label @{ error = $_.Exception.Message }; Write-Host "${Label}: $($_.Exception.Message)" }
}

function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
Cmd $a guard-set "Off" | Out-Null
Cmd $a drive-trace "on" | Out-Null
Cmd $a car-spawn "0 car_boltatlanta 0" | Out-Null
Wait-Ready $a 0; Wait-Ready $b 0
Cmd $b car-spawn "1 car_boltatlanta 0" | Out-Null
Wait-Ready $a 1; Wait-Ready $b 1
Start-Sleep -Seconds 3

Try-Save "codec" { Cmd $a drive-codec-check }
Try-Save "pie" { Cmd $a drive-pie }
Start-Sleep -Seconds 2
Try-Save "pie_later" { Cmd $a drive-pie "close" }
Send-ServerCommand "save"
Start-Sleep -Seconds 3

Cmd $a guard-allow "Mode:CarDrive" | Out-Null
Cmd $a testdrive-go "0" | Out-Null
Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null
Start-Sleep -Seconds 6
Try-Save "blob" { Cmd $a drive-blob }
Try-Save "track_idle" { Cmd $a drive-probe }
Try-Save "input" { Cmd $a drive-input "0.6 0 6" }
Start-Sleep -Seconds 1
Try-Save "track_1s" { Cmd $a drive-probe }
Start-Sleep -Seconds 2
Try-Save "track_3s" { Cmd $a drive-probe }
Start-Sleep -Seconds 4
Try-Save "track_7s" { Cmd $a drive-probe }
Try-Save "stop" { Cmd $a drive-stop }
Start-Sleep -Seconds 4
Try-Save "track_stopped" { Cmd $a drive-probe }

foreach ($route in "clone", "new", "base") {
    Try-Save "ghost_${route}_start" { Cmd $a drive-ghost-test $route }
    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Seconds 1
        $cars = @((Cmd $a dump).remoteCars.cars)
        $last = $cars | Select-Object -Last 1
    } while ($last -and $last.mode -ne "failed" -and -not $last.ready -and (Get-Date) -lt $deadline)
    Start-Sleep -Seconds 2
    Try-Save "ghost_$route" { (Cmd $a dump).remoteCars }
}
Try-Save "track_with_ghosts" { Cmd $a drive-probe }
Try-Save "ghost_clear" { Cmd $a drive-ghost-test "clear" }
Try-Save "trace" { Cmd $a drive-trace "report" }

try {
    Cmd $a testdrive-finish "all" | Out-Null
    Wait-InGarage $a 180
} catch { Write-Host "return: $($_.Exception.Message)" }
$Ctx.Result.passed = $true

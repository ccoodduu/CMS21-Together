# areas: presence
# run-all: skip
# seated-avatars spike 1.1: which seat handle the game's SitInside(car, left) uses on a right-hand-drive car (Sakura
# Tiara) and a left-hand-drive car (Bolt Atlanta): A sits left and right in each, and A's camera distance to each handle
# is logged with B's view of A's avatar. Logging only; it fails only when a step cannot run.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "probe_$Label.json") -Encoding utf8 }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $true }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $false
}
function Wait-Local([string]$Name, [scriptblock]$Condition) {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $l = try { (Cmd $Name seat-pose).local } catch { $null }
        if ($l -and (& $Condition $l)) { return $true }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $false
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$idA = [int](Get-HarnessStatus -Instance $a).playerId

$loader = 0
foreach ($car in "car_sakuratiara", "car_boltatlanta") {
    Cmd $a car-spawn "$loader $car 0" | Out-Null
    Check ((Wait-Ready $a $loader) -and (Wait-Ready $b $loader)) "$car Ready on both"
    Start-Sleep -Seconds 2
    foreach ($side in "left", "right") {
        Cmd $a sit "$loader $side" | Out-Null
        Check (Wait-Local $a { param($l) $l.seat -eq $loader -and $l.seatedMode }) "A seated in $car ($side)"
        Start-Sleep -Seconds 2
        $handles = Cmd $a seat-handles "$loader"
        $poseB = (Cmd $b seat-pose).players."$idA"
        $handlesB = Cmd $b seat-handles "$loader"
        Write-Host "  $car $side`: rhd $($handles.rightHandDrive), A's camera to left $($handles.cameraToLeft) m, to right $($handles.cameraToRight) m; B: $($poseB.seatPose | ConvertTo-Json -Compress)"
        Save "${car}_$side" @{ handlesA = $handles; poseB = $poseB; handlesB = $handlesB }
        Cmd $a stand | Out-Null
        Check (Wait-Local $a { param($l) $l.seat -eq -1 -and -not $l.seatedMode }) "A stood up from $car ($side)"
        Start-Sleep -Seconds 1
    }
    Cmd $a car-delete "$loader" | Out-Null
    Start-Sleep -Seconds 5
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

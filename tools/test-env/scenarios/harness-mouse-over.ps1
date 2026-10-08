# areas: presence, parts, placement
# The harness verbs sit, stand + car-move and part-fast-unmount of a suspension part skip the mouse and the pie menu.
# Vanilla then reads a missing mouse-over car or restores a stale Interior mode and throws in GameMode.SetCurrentMode
# or PartScript.Hide (soak 2026-10-07, rule 4). None of them may log an exception, and car-move must not restore Interior.
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_sixoncebulion"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$Loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}

function Mode([string]$Name) { (Send-HarnessCommand -Instance $Name -Verb seat-trace -Arguments "report").state.mode }

function LogPath([string]$Name) { Join-Path $env:USERPROFILE "CMS21-TestInstalls\$Name\MelonLoader\Latest.log" }

function New-Exceptions([hashtable]$Marks) {
    foreach ($name in $Ctx.Instances) {
        @(Get-Content -LiteralPath (LogPath $name)) | Select-Object -Skip $Marks[$name] |
            Select-String -Pattern "Exception in Harmony patch of method" | ForEach-Object { "${name}: $($_.Line)" }
    }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "1 $car 0 Entrance1" | Out-Null
Wait-Ready $a 1 | Out-Null; Wait-Ready $b 1 | Out-Null

$marks = @{}
foreach ($name in $Ctx.Instances) { $marks[$name] = @(Get-Content -LiteralPath (LogPath $name)).Count }

Send-HarnessCommand -Instance $a -Verb sit -Arguments "1 left" | Out-Null
Start-Sleep -Seconds 4
Check ((Mode $a) -eq "Interior") "A sits in the car (mode $(Mode $a))"
Send-HarnessCommand -Instance $a -Verb stand | Out-Null
Start-Sleep -Seconds 4
Send-HarnessCommand -Instance $a -Verb car-move -Arguments "1 Entrance2" | Out-Null
Start-Sleep -Seconds 6
$mode = Mode $a
Check ($mode -ne "Interior") "after stand and car-move A is not left in Interior mode (mode $mode)"

$r = Send-HarnessCommand -Instance $b -Verb part-fast-unmount -Arguments "1 s:3.4"
Start-Sleep -Seconds 3
Write-Host "  B unmounted $($r.id)"

$errors = @(New-Exceptions $marks)
$errors | ForEach-Object { Write-Host "  $_" }
Check ($errors.Count -eq 0) "no exception in a Harmony-patched vanilla method ($($errors.Count))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

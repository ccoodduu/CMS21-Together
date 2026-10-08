# areas: parts, latejoin
# Soak 2026-10-08 (confirmed desyncs "s:28.1.unmounted 0 vs 1" and "s:22.1.unmounted 0 vs 1": a power steering cap and
# an oil filter housing cap): the receivers kept a removed drain plug or fill cap on the car, because the game's
# HideBySavegame skips special group 1. A takes such a part off; B must show it off, also after a rejoin, and on
# again after A puts it back. The server's digest of the car must match both players each time.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

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

function Unmounted([string]$Name, [string]$Key) {
    $car = @((Cmd $Name dump).cars | Where-Object { $_.index -eq $loader })[0]
    [bool](@($car.subParts | Where-Object { $_.key -eq $Key })[0].unmounted)
}

function Check-Part([string]$What, [string]$Key, [bool]$Expected) {
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $states = @($Ctx.Instances | ForEach-Object { Unmounted $_ $Key })
        if (@($states | Where-Object { $_ -ne $Expected }).Count -eq 0) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Check (@($states | Where-Object { $_ -ne $Expected }).Count -eq 0) "$What`: $Key is $(if ($Expected) { 'off' } else { 'on' }) for A and B (unmounted: $($states -join ', '))"
    $mark = Get-ServerLogMark
    Send-ServerCommand "desync check"
    Start-Sleep -Seconds 4
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] cars:$loader" })
    Check ($lines.Count -ge 2 -and -not ($lines -match "mismatch|fields differ")) "$What`: the server's digest of the car matches both players ($($lines -join ' | '))"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader car_boltatlanta 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
$caps = @(Cmd $a part-special "$loader 1" | Where-Object { -not $_.unmounted -and $_.unmountWith -eq 0 })
Write-Host "special group 1 parts: $(@($caps | ForEach-Object { "$($_.key) $($_.id)" }) -join ', ')"
if ($caps.Count -eq 0) { throw "no drain plug or cap on loader $loader" }
$key = $caps[0].key

Cmd $a part-fast-unmount "$loader $key" | Out-Null
Check-Part "A took $($caps[0].id) off" $key $true

Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Ready $b | Out-Null
Check-Part "B rejoined" $key $true

Cmd $a part-fast-mount "$loader $key" | Out-Null
Check-Part "A put it back" $key $false

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

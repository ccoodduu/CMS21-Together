# sync-car-parts race: A and B unmount the same part at the same moment (FastUnmount, which skips the claim). The
# server accepts the first change and rejects the second; the loser reverts its own inventory item, so both
# clients end with the part unmounted and exactly one copy of its item.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Count-Item($Dump, [string]$Id) {
    @($Dump.inventory.items | Where-Object { $_.ID -eq $Id }).Count
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$key = "s:2.3"
$before = Send-HarnessCommand -Instance $a -Verb dump
$jobA = Start-Job -ScriptBlock { param($module, $name, $loader, $key) Import-Module $module; Send-HarnessCommand -Instance $name -Verb part-fast-unmount -Arguments "$loader $key" } -ArgumentList (Join-Path $PSScriptRoot "..\HarnessClient.psm1"), $a, $loader, $key
Send-HarnessCommand -Instance $b -Verb part-fast-unmount -Arguments "$loader $key" | Out-Null
$jobA | Wait-Job -Timeout 30 | Out-Null
$unmountA = $jobA | Receive-Job
$jobA | Remove-Job -Force

Start-Sleep -Seconds 12
$readyA = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "$loader"
$readyB = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "$loader"
Check ($readyA.stateHash -eq $readyB.stateHash) "A and B agree on the part state after the race ($($readyA.stateHash) / $($readyB.stateHash))"
Check ($readyA.unmounted -eq 1 -and $readyB.unmounted -eq 1) "the part is unmounted on both ($($readyA.unmounted) / $($readyB.unmounted))"

$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "after-race"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "after-race"
$itemId = $unmountA.id
$extraA = (Count-Item $dumpA $itemId) - (Count-Item $before $itemId)
$extraB = (Count-Item $dumpB $itemId) - (Count-Item $before $itemId)
Check ($extraA -eq 1 -and $extraB -eq 1) "exactly one '$itemId' was added on each client (A +$extraA, B +$extraB)"
$diff = Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory")
Check ($diff.Count -eq 0) "inventories are equal"
$serverLog = Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") -Raw
$clientLogs = ($Ctx.Instances | ForEach-Object { Get-Content -LiteralPath (Join-Path $env:USERPROFILE "CMS21-TestInstalls\$_\MelonLoader\Latest.log") -Raw }) -join "`n"
$resolved = ($serverLog -match "rejected: s:2\.3 changed already") -or ($clientLogs -match "another player changed s:2\.3 first")
Check $resolved "the race was resolved (server rejection or client-side abort of the slower unmount)"

# Server rejection (task 3.7): B holds A's relayed change, so B unmounts the same part without knowing it is gone; the
# server rejects B's change, B restores the server's part state and removes its own item, then applies A's change.
Send-HarnessCommand -Instance $a -Verb part-fast-mount -Arguments "$loader $key" | Out-Null
$deadline = (Get-Date).AddSeconds(20)
do {
    Start-Sleep -Milliseconds 500
    $ra = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "$loader"
    $rb = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "$loader"
} while (-not ($ra.unmounted -eq 0 -and $rb.unmounted -eq 0) -and (Get-Date) -lt $deadline)
Check ($ra.unmounted -eq 0 -and $rb.unmounted -eq 0) "the part is mounted again on both before the second race"
Start-Sleep -Seconds 2
$before = Send-HarnessCommand -Instance $a -Verb dump
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb part-hold-remote -Arguments "on" | Out-Null
Send-HarnessCommand -Instance $a -Verb part-fast-unmount -Arguments "$loader $key" | Out-Null
Wait-ServerLog -Pattern "\[Cars\] Change .* from client \d+ on loader $loader`: revision" -After $mark -TimeoutSec 10 | Out-Null
Send-HarnessCommand -Instance $b -Verb part-fast-unmount -Arguments "$loader $key" | Out-Null
$rejected = Wait-ServerLog -Pattern "rejected: $([regex]::Escape($key)) changed already" -After $mark -TimeoutSec 15
Check ([bool]$rejected) "the server rejects B's stale unmount ($rejected)"
Start-Sleep -Seconds 2
Send-HarnessCommand -Instance $b -Verb part-hold-remote -Arguments "off" | Out-Null
Start-Sleep -Seconds 3
$readyA = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "$loader"
$readyB = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "$loader"
Check ($readyA.stateHash -eq $readyB.stateHash) "A and B agree after the rejection ($($readyA.stateHash) / $($readyB.stateHash))"
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "after-reject"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "after-reject"
$extraA = (Count-Item $dumpA $itemId) - (Count-Item $before $itemId)
$extraB = (Count-Item $dumpB $itemId) - (Count-Item $before $itemId)
Check ($extraA -eq 1 -and $extraB -eq 1) "after the rejection each client has exactly one more '$itemId' (A +$extraA, B +$extraB)"
$diff = Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory")
Check ($diff.Count -eq 0) "inventories are equal after the rejection"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

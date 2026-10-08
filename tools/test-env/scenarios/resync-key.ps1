# areas: resync, parts, locks
# desync-detection-and-resync (c): B's car part state is corrupted locally (no packet), B resyncs (the F7 path) and
# the garage reload brings B back to the server's state; a second resync right away is refused by the cooldown.
# Playtest 2026-10-07 finding 5: world-state packets (money changes) that arrive while B's garage reloads are
# applied without an error in the WorldState handler.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1")

$a, $b = $Ctx.Instances
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
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "0"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader 0 not Ready"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
$readyA = Wait-Ready $a
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

# Row 19 review X4: A and B each hold a part lock when B resyncs. B's own lock is released; A's lock stays on the
# server and is back in B's mirror after the reload.
$lockParts = @(Send-HarnessCommand -Instance $a -Verb vfx-parts -Arguments "0")
$keyA, $keyB = $lockParts[0].key, $lockParts[-1].key
$lockA = Request-Lock $a "0 unmount $keyA"
$lockB = Request-Lock $b "0 unmount $keyB"
Check ($lockA.result -eq "granted" -and $lockB.result -eq "granted") "A holds $keyA and B holds $keyB before the resync ($($lockA.result) / $($lockB.result))"
$idA = (Get-HarnessStatus $a).playerId

$corrupt = Send-HarnessCommand -Instance $b -Verb part-corrupt -Arguments "0"
$corruptB = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "0"
Check ($corruptB.stateHash -ne $readyA.stateHash) "B's car differs after the local corruption of $($corrupt.key)"

$logB = Join-Path $env:USERPROFILE "CMS21-TestInstalls\$b\MelonLoader\Latest.log"
$logStart = @(Get-Content -LiteralPath $logB).Count
$mark = Get-ServerLogMark
$result = Send-HarnessCommand -Instance $b -Verb resync
$end = (Get-Date).AddSeconds(8)
while ((Get-Date) -lt $end) { Send-ServerCommand "money add 1"; Start-Sleep -Milliseconds 400 }
Check ($result -eq "reloading") "B's resync starts ($result)"
$manual = try { Wait-ServerLog -Pattern "manual resync by .*differing at that moment: .*cars:0" -After $mark -TimeoutSec 10 } catch { $null }
Check ([bool]$manual) "the server logs the manual resync with the differing car ($manual)"
Start-Sleep -Seconds 3
Wait-InGarage $b
$readyB = Wait-Ready $b
$readyA = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "0"
Check ($readyB.stateHash -eq $readyA.stateHash) "after the resync B's car matches A's ($($readyA.stateHash) / $($readyB.stateHash))"

$handlerErrors = @(Get-Content -LiteralPath $logB | Select-Object -Skip $logStart | Where-Object { $_ -match "Error in handler" })
Check ($handlerErrors.Count -eq 0) "no packet handler failed during B's reload ($($handlerErrors.Count): $($handlerErrors | Select-Object -First 1))"
$diff = Compare-HarnessDumps (Send-HarnessCommand -Instance $a -Verb dump) (Send-HarnessCommand -Instance $b -Verb dump) -Sections @("stats")
Check ($diff.Count -eq 0) "A and B have the same money after the reload (differ: $($diff -join ', '))"

$locks = Get-ServerLocks
Check ($locks.Count -eq 1 -and @($locks.Records | Where-Object { $_.Owner -eq $idA -and $_.X -contains $keyA }).Count -eq 1) "after B's resync the server holds only A's lock ($($locks.Line))"
$mirrorB = @((Get-LockMirror $b).mirror)
Check ($mirrorB.Count -eq 1 -and $mirrorB[0].owner -eq $idA -and @($mirrorB[0].x) -contains $keyA) "B's mirror has A's lock again after the reload ($($mirrorB | ConvertTo-Json -Compress -Depth 4))"
Send-HarnessCommand -Instance $a -Verb lock-release -Arguments "0" | Out-Null
Wait-LockMirror $b { param($l) @($l.mirror).Count -eq 0 } "no locks" | Out-Null
Check ((Get-ServerLocks).Count -eq 0) "A's release reaches the server and B"

$again = Send-HarnessCommand -Instance $b -Verb resync
Check ("$again" -match "^Wait \d+ s") "a second resync right away is refused ($again)"
Check ((Get-HarnessStatus $a).joinStatus -eq "InSession") "A stayed in the session"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

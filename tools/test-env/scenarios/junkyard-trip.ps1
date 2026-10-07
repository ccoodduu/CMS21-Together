# areas: economy, presence, guard, smoke
# M2 junkyard trip (parts only), with the guard enforcing: A travels to the junkyard; picking a part up there is local
# only (nothing reaches the server or B); buying parts (the BuyPartsAction packet) costs money once and adds them for
# both; after A's return to the garage, A and B have the same inventory and money.
param($Ctx)

$a, $b = $Ctx.Instances
$part = "tuleja_1"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Count-Part($Dump) { @($Dump.inventory.items | Where-Object { $_.ID -eq $part }).Count }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$before = Send-HarnessCommand -Instance $b -Verb dump

Send-HarnessCommand -Instance $a -Verb travel -Arguments "Junkyard" | Out-Null
$s = Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "A in the junkyard" -Condition { param($s) $s.scene -ne "garage" -and $s.playable -and $s.connectionValid }
Check ($s.scene -match "(?i)junkyard") "A reached the junkyard with the guard on ($($s.scene))"

Send-HarnessCommand -Instance $a -Verb inv-add-local -Arguments $part | Out-Null
Start-Sleep -Seconds 3
$afterPickup = Send-HarnessCommand -Instance $b -Verb dump
Check ((Count-Part $afterPickup) -eq (Count-Part $before)) "a part picked up in the junkyard does not reach B"

Send-HarnessCommand -Instance $a -Verb junkyard-buy -Arguments $part | Out-Null
$deadline = (Get-Date).AddSeconds(10)
do {
    Start-Sleep -Milliseconds 500
    $afterBuy = Send-HarnessCommand -Instance $b -Verb dump
} while ((Count-Part $afterBuy) -eq (Count-Part $before) -and (Get-Date) -lt $deadline)
Check ((Count-Part $afterBuy) -eq (Count-Part $before) + 1) "the bought part reaches B"
Check ($afterBuy.stats.money -lt $before.stats.money) "the purchase cost money ($($before.stats.money) -> $($afterBuy.stats.money))"

Send-HarnessCommand -Instance $a -Verb travel -Arguments "Garage" | Out-Null
Start-Sleep -Seconds 3
Wait-InGarage $a
Start-Sleep -Seconds 3
$dumpA = Send-HarnessCommand -Instance $a -Verb dump
$dumpB = Send-HarnessCommand -Instance $b -Verb dump
$diff = Compare-HarnessDumps $dumpA $dumpB -Sections @("inventory", "stats")
Check ($diff.Count -eq 0) "after the return A and B have the same inventory and money (differ: $($diff -join ', '))"
Check ((Count-Part $dumpA) -eq (Count-Part $before) + 1) "A has exactly the bought part, not the picked-up one ($(Count-Part $dumpA))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

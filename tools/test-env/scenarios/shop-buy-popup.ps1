# areas: economy
# Playtest 3 (2026-10-10): buying in the shop showed no "new item" popup and played no sound, because the multiplayer
# buy hook skips the game's ShopBuyWindow.BuyItem. A buys a part (one, then three) through the buy window: each buy
# shows PopUp_NewItem with the item's name (and "x3") and plays Popup once, the item arrives once for both players and
# the money goes down once.
param($Ctx)

$a, $b = $Ctx.Instances
$itemId = "zaciskHamulcowy_1"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Count($Dump) { @($Dump.inventory.items | Where-Object { $_.ID -eq $itemId }).Count }

function Buy([int]$Amount) {
    $countBefore = Count (Cmd $a dump)
    $moneyBefore = (Cmd $a dump).stats.money
    Cmd $a popup-trace "on" | Out-Null
    $r = Cmd $a shop-buy "$itemId $Amount"
    Write-Host "  shop-buy: $($r | ConvertTo-Json -Compress)"
    Start-Sleep -Seconds 2
    $events = @((Cmd $a popup-trace "report").events)
    Cmd $a popup-trace "off" | Out-Null
    $events | ForEach-Object { Write-Host "  event: $($_ | ConvertTo-Json -Compress)" }
    $popups = @($events | Where-Object { $_.kind -eq "popup" -and $_.title -eq "PopUp_NewItem" })
    $sounds = @($events | Where-Object { $_.kind -eq "sound" -and $_.name -eq "Popup" })
    $suffix = if ($Amount -gt 1) { " x$Amount" } else { "" }
    Check ($popups.Count -eq 1 -and $popups[0].text.EndsWith($suffix) -and $popups[0].type -eq "Buy") "buying $Amount shows PopUp_NewItem once ($($popups.Count): $($popups | ForEach-Object { $_.text }))"
    Check ($sounds.Count -eq 1) "buying $Amount plays Popup once ($($sounds.Count))"
    Check (-not $r.openAfter) "the buy window is closed after the buy"
    $dump = Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("inventory", "stats") -TimeoutSec 20
    Check ((Count $dump) -eq $countBefore + $Amount) "both players have the $Amount new item(s) once ($countBefore -> $(Count $dump))"
    Check ($dump.stats.money -lt $moneyBefore) "the money went down ($moneyBefore -> $($dump.stats.money))"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Buy 1
Buy 3

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

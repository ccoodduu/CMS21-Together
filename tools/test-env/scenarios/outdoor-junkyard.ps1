# areas: outdoor, economy, presence
# shared-outdoor-scenes 9.1, guard enforcing: A and B travel to the junkyard and share one instance (same id, cars,
# digest, piles, avatars); a take reaches B within 1 s; a race on one item leaves it with one player; a put back
# returns it; A buys a car and it disappears for B; a simultaneous purchase of one car is accepted once (Taken);
# B leaves without paying and its part returns for A; A buys two parts and pays once.
param($Ctx)

$a, $b = $Ctx.Instances
$price = 5000
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }

function Wait-Outdoor([string]$Name, [string]$Scene) {
    Wait-HarnessDump -Instance $Name -TimeoutSec 240 -What "$Name in the shared $Scene" -Condition {
        param($d) $d.local.scene -eq $Scene -and $d.outdoor.applied
    }
}

function Server-Lines([int]$Mark, [string]$Pattern) { @(Get-ServerLogLines | Select-Object -Skip $Mark | Where-Object { $_ -match $Pattern }) }

function Pile-Uids($Dump) { @($Dump.outdoor.piles | ForEach-Object { $_.uids }) }

function Wait-PileUid([string]$Name, [long]$Uid, [bool]$Present, [int]$TimeoutSec = 5) {
    $started = Get-Date
    $deadline = $started.AddSeconds($TimeoutSec)
    do {
        $inPile = (Pile-Uids (Dump $Name)) -contains $Uid
        if ($inPile -eq $Present) { return ((Get-Date) - $started).TotalSeconds }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return -1
}

function Roster-Avatar($Dump, [string]$PlayerName) {
    $entry = @($Dump.roster.PSObject.Properties | Where-Object { $_.Value.name -eq $PlayerName } | ForEach-Object { $_.Value })[0]
    [bool]($entry -and $entry.avatarActive)
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Send-HarnessCommand -Instance $a -Verb set-name -Arguments "Ann" | Out-Null
Send-HarnessCommand -Instance $b -Verb set-name -Arguments "Bob" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "enforce" | Out-Null }
Send-ServerCommand "money set 500000"
Start-Sleep -Seconds 2
$mark = Get-ServerLogMark

Send-HarnessCommand -Instance $a -Verb travel -Arguments "Junkyard" | Out-Null
$da = Wait-Outdoor $a "Junkyard"
Send-HarnessCommand -Instance $b -Verb travel -Arguments "Junkyard" | Out-Null
$db = Wait-Outdoor $b "Junkyard"
$da = Dump $a

Check ($da.outdoor.shared -and $db.outdoor.shared) "both visits are shared"
Check ($da.outdoor.instanceId -gt 0 -and $da.outdoor.instanceId -eq $db.outdoor.instanceId) "same instance id ($($da.outdoor.instanceId), $($db.outdoor.instanceId))"
Check ($da.outdoor.generator -and -not $db.outdoor.generator) "A is the generator, B is not"
Check (@(Server-Lines $mark "\[Outdoor\] Junkyard #\d+ opened by").Count -eq 1) "the server opened one junkyard"
Check (@(Server-Lines $mark "\[Outdoor\] Junkyard #\d+ joined by").Count -eq 1) "B joined it"
$carsA = ($da.outdoor.cars | ForEach-Object { "$($_.index):$($_.carToLoad)/$($_.version)" }) -join ","
$carsB = ($db.outdoor.cars | ForEach-Object { "$($_.index):$($_.carToLoad)/$($_.version)" }) -join ","
Check ($carsA -and $carsA -eq $carsB) "same cars in the same order (A $carsA; B $carsB)"
$equal = try { Wait-ServerLog -Pattern "\[Outdoor\] Junkyard #\d+: digest of \d+ equals the reference" -After $mark -TimeoutSec 30 } catch { $null }
Check ([bool]$equal) "B's digest equals A's"
Check (@(Server-Lines $mark "digest mismatch").Count -eq 0) "no digest mismatch ($(@(Server-Lines $mark 'digest mismatch') | Select-Object -First 3))"
$differ = @(Compare-HarnessDumps $da $db -Sections @("outdoor"))
Check ($differ.Count -eq 0) "same picks and piles on A and B"
Start-Sleep -Seconds 3
Check (Roster-Avatar (Dump $a) "Bob") "A sees Bob's avatar"
Check (Roster-Avatar (Dump $b) "Ann") "B sees Ann's avatar"
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "shared-junkyard"

# Take, race, put back.
$piles = @($da.outdoor.piles | Where-Object { @($_.uids).Count -ge 3 })
Check ($piles.Count -ge 1) "a pile with at least 3 items exists ($($piles.Count))"
if ($piles.Count -ge 1) {
    $pile = $piles[0]
    $first = (Send-HarnessCommand -Instance $a -Verb loot-take -Arguments "$($pile.Key) uid:$($pile.uids[0])").UID
    $seconds = Wait-PileUid $b $first $false
    Check ($seconds -ge 0 -and $seconds -le 1.5) "A's take is gone from B's pile within 1 s ($seconds s)"

    $race = $pile.uids[1]
    $raceMark = Get-ServerLogMark
    foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments "on" | Out-Null }
    foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb loot-take -Arguments "$($pile.Key) uid:$race" | Out-Null }
    foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments "off" | Out-Null }
    Start-Sleep -Seconds 3
    $heldA = @((Dump $a).outdoor.loot.held) -contains $race
    $heldB = @((Dump $b).outdoor.loot.held) -contains $race
    Check ($heldA -xor $heldB) "exactly one player holds the raced item (A $heldA, B $heldB)"
    Check (@(Server-Lines $raceMark "take of \S+#$race by \d+ refused").Count -eq 1) "the server refused the second take once"

    Send-HarnessCommand -Instance $a -Verb loot-put -Arguments "$first" | Out-Null
    $seconds = Wait-PileUid $b $first $true
    Check ($seconds -ge 0) "A's put back is in B's pile again ($seconds s)"

    $bPart = (Send-HarnessCommand -Instance $b -Verb loot-take -Arguments "$($pile.Key) uid:$($pile.uids[2])").UID
    Check ((Wait-PileUid $a $bPart $false) -ge 0) "B's take is gone for A"
}

# A buys car pick 0; it disappears for B.
$slotsBefore = @((Send-HarnessCommand -Instance $a -Verb parking).slots).Count
$moneyBefore = (Dump $a).stats.money
$carMark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb buy-car-here -Arguments "pick:0 $price" | Out-Null
$sold = try { Wait-ServerLog -Pattern "\[Outdoor\] Junkyard #\d+ car 0 sold to" -After $carMark -TimeoutSec 30 } catch { $null }
Check ([bool]$sold) "the server sold car 0"
$gone = Wait-HarnessDump -Instance $b -TimeoutSec 10 -What "car 0 gone for B" -Condition { param($d) @($d.outdoor.cars | Where-Object { $_.index -eq 0 }).Count -eq 0 }
Check ($null -ne $gone) "car 0 is gone from B's junkyard"
Start-Sleep -Seconds 3
Check (@((Send-HarnessCommand -Instance $b -Verb parking).slots).Count -eq $slotsBefore + 1) "the car is in the shared parking once"
Check ((Dump $b).stats.money -eq $moneyBefore - $price) "money dropped by the price once"

# A and B buy car pick 1 at the same moment: one is accepted, the other is Taken.
$raceMark = Get-ServerLogMark
$moneyBefore = (Dump $a).stats.money
foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments "on" | Out-Null }
foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb buy-car-here -Arguments "pick:1 $price" | Out-Null }
$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Milliseconds 500
    $pressed = @($a, $b | Where-Object { @(Send-HarnessCommand -Instance $_ -Verb buy-car-last) -match "^(pressed|failed|no ask window)" }).Count
} while ($pressed -lt 2 -and (Get-Date) -lt $deadline)
Start-Sleep -Seconds 3
foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments "off" | Out-Null }
Start-Sleep -Seconds 8
Check (@(Server-Lines $raceMark "car 1 sold to").Count -eq 1) "car 1 was sold once"
Check (@(Server-Lines $raceMark "refused: Taken").Count -eq 1) "the other purchase was refused as Taken"
Check ((Dump $a).stats.money -eq $moneyBefore - $price) "money dropped by one price only"

# B leaves without paying; its part returns for A.
if ($bPart) {
    Send-HarnessCommand -Instance $b -Verb loot-quit | Out-Null
    Check ((Wait-PileUid $a $bPart $true 10) -ge 0) "B's unpaid part is back in A's pile"
    Wait-InGarage $b
}

# A buys two parts from the piles and pays once.
$invA = @((Dump $a).inventory.items).Count
$moneyBefore = (Dump $a).stats.money
$buyMark = Get-ServerLogMark
$buy = Send-HarnessCommand -Instance $a -Verb junk-buy -Arguments "* 2"
Wait-InGarage $a
Start-Sleep -Seconds 3
$after = Dump $a
Check (@($after.inventory.items).Count -eq $invA + 2) "A's inventory has the 2 parts once ($invA -> $(@($after.inventory.items).Count))"
Check ($after.stats.money -lt $moneyBefore) "the parts cost money ($moneyBefore -> $($after.stats.money))"
Check (@(Server-Lines $buyMark "items bought by").Count -eq 1) "the server marked the parts bought once"
Check (@(Server-Lines $buyMark "not held by the buyer dropped").Count -eq 0) "no part was dropped as not held"
$differ = @(Compare-HarnessDumps $after (Dump $b) -Sections @("inventory", "stats"))
Check ($differ.Count -eq 0) "A and B have the same inventory and money (differ: $($differ -join ', '))"
Check (@(Server-Lines $mark "\[Outdoor\] Junkyard #\d+ closed").Count -ge 1) "the junkyard closed after both left"

Send-ServerCommand "outdoor"
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: outdoor, economy, presence
# shared-outdoor-scenes 9.5, guard enforcing: A takes 2 items and buys a car in the junkyard before B arrives; B's
# junkyard lacks the sold car and the taken items and its digest equals A's. B joins the auction while A bids on a
# lot and sees the lot's current bid.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

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

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "enforce" | Out-Null }
Send-ServerCommand "money set 500000"
$mark = Get-ServerLogMark

Send-HarnessCommand -Instance $a -Verb travel -Arguments "Junkyard" | Out-Null
$da = Wait-Outdoor $a "Junkyard"
$pile = @($da.outdoor.piles | Where-Object { @($_.uids).Count -ge 2 })[0]
$taken = @()
foreach ($uid in $pile.uids[0], $pile.uids[1]) { $taken += (Send-HarnessCommand -Instance $a -Verb loot-take -Arguments "$($pile.Key) uid:$uid").UID }
Send-HarnessCommand -Instance $a -Verb buy-car-here -Arguments "pick:2 5000" | Out-Null
$sold = try { Wait-ServerLog -Pattern "\[Outdoor\] Junkyard #\d+ car 2 sold to" -After $mark -TimeoutSec 30 } catch { $null }
Check ([bool]$sold) "A bought car 2"

Send-HarnessCommand -Instance $b -Verb travel -Arguments "Junkyard" | Out-Null
$db = Wait-Outdoor $b "Junkyard"
Check ($db.outdoor.instanceId -eq $da.outdoor.instanceId) "B joins A's junkyard"
Check (@($db.outdoor.cars | Where-Object { $_.index -eq 2 }).Count -eq 0) "B's junkyard has no car 2"
$uidsB = @($db.outdoor.piles | ForEach-Object { $_.uids })
Check (@($taken | Where-Object { $uidsB -contains $_ }).Count -eq 0) "B's piles lack the items A holds"
$equal = try { Wait-ServerLog -Pattern "\[Outdoor\] Junkyard #\d+: digest of \d+ equals the reference" -After $mark -TimeoutSec 30 } catch { $null }
Check ([bool]$equal) "B's digest equals A's (sold car ignored)"

Send-HarnessCommand -Instance $a -Verb loot-quit | Out-Null
Send-HarnessCommand -Instance $b -Verb travel -Arguments "Garage" | Out-Null
foreach ($name in $a, $b) { Wait-InGarage $name }

$auctionMark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb travel -Arguments "Auction" | Out-Null
Wait-Outdoor $a "Auction" | Out-Null
Start-Sleep -Seconds 3
Send-HarnessCommand -Instance $a -Verb auction-open -Arguments "normal" | Out-Null
Send-HarnessCommand -Instance $a -Verb auction-start -Arguments "0" | Out-Null
$deadline = (Get-Date).AddSeconds(90)
do { Start-Sleep -Milliseconds 500; $steps = @(Send-HarnessCommand -Instance $a -Verb auction-start-last) } while (-not ($steps -match "^(StartAuction called|failed)") -and (Get-Date) -lt $deadline)
Check (-not ($steps -match "^failed")) "A bids on lot 0 ($($steps -join ' | '))"
Send-HarnessCommand -Instance $b -Verb travel -Arguments "Auction" | Out-Null
Wait-Outdoor $b "Auction" | Out-Null
$state = @((Send-HarnessCommand -Instance $b -Verb auction-state).sync.watched | Where-Object { $_.lot -eq 0 })[0]
Check ($null -ne $state -and $state.bid -gt 0) "B arriving late sees lot 0's current bid ($($state.bid))"
Send-HarnessCommand -Instance $a -Verb auction-finish -Arguments "ai" | Out-Null
Start-Sleep -Seconds 3
foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb travel -Arguments "Garage" | Out-Null }
foreach ($name in $a, $b) { Wait-InGarage $name }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: outdoor, economy, presence
# shared-outdoor-scenes 9.3, guard enforcing: A and B in one auction see equal normal and salvage lots; A runs the
# bidding on lot 1 and B is refused; B sees A's bid within 1 s and raises it once; the team wins, money drops once by
# the final bid and lot 1 is gone for B; A starts lot 2 and leaves, which closes lot 2 for B without moving money.
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

function Wait-InAuction([string]$Name) {
    Wait-HarnessDump -Instance $Name -TimeoutSec 240 -What "$Name in the shared auction" -Condition {
        param($d) $d.local.scene -eq "Auction" -and $d.outdoor.applied
    }
}

function Server-Lines([int]$Mark, [string]$Pattern) { @(Get-ServerLogLines | Select-Object -Skip $Mark | Where-Object { $_ -match $Pattern }) }

function Lots([string]$Name, [string]$Type) {
    (Send-HarnessCommand -Instance $Name -Verb auction-open -Arguments $Type).lots | ForEach-Object { "$($_.lot):$($_.Car)/$($_.Version):$($_.StartingPrice)" }
}

function Wait-Watched([string]$Name, [int]$Lot, [scriptblock]$Condition, [int]$TimeoutSec = 5) {
    $started = Get-Date
    do {
        $state = @((Send-HarnessCommand -Instance $Name -Verb auction-state).sync.watched | Where-Object { $_.lot -eq $Lot })[0]
        if ($state -and (& $Condition $state)) { return @{ State = $state; Seconds = ((Get-Date) - $started).TotalSeconds } }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $started.AddSeconds($TimeoutSec))
    return $null
}

function Wait-Started([string]$Name) {
    $deadline = (Get-Date).AddSeconds(90)
    do {
        Start-Sleep -Milliseconds 500
        $steps = @(Send-HarnessCommand -Instance $Name -Verb auction-start-last)
    } while (-not ($steps -match "^(StartAuction called|failed)") -and (Get-Date) -lt $deadline)
    Write-Host "auction-start steps ($Name): $($steps -join ' | ')"
    return $steps
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

Send-HarnessCommand -Instance $a -Verb travel -Arguments "Auction" | Out-Null
Wait-InAuction $a | Out-Null
Send-HarnessCommand -Instance $b -Verb travel -Arguments "Auction" | Out-Null
Wait-InAuction $b | Out-Null
$lotsMark = try { Wait-ServerLog -Pattern "\[Outdoor\] Auction #\d+: \d+ normal and \d+ salvage lots" -After $mark -TimeoutSec 30 } catch { $null }
Check ([bool]$lotsMark) "the server decided the lots ($lotsMark)"
Start-Sleep -Seconds 2
foreach ($type in "normal", "salvage") {
    $la = (Lots $a $type) -join ","
    $lb = (Lots $b $type) -join ","
    Check ($la -and $la -eq $lb) "$type lots equal on A and B (A $la; B $lb)"
}
Check (@(Server-Lines $mark "lot values .* from").Count -ge 1 -or @(Server-Lines $mark "values of \d+ lots from").Count -ge 1) "the generator uploaded the lot values"

Send-HarnessCommand -Instance $a -Verb auction-open -Arguments "normal" | Out-Null
Send-HarnessCommand -Instance $a -Verb auction-start -Arguments "1" | Out-Null
$steps = Wait-Started $a
Check (-not ($steps -match "^failed")) "A started the bidding on lot 1"
$claimMark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb auction-open -Arguments "normal" | Out-Null
try { Send-HarnessCommand -Instance $b -Verb auction-start -Arguments "1" | Out-Null; Wait-Started $b | Out-Null } catch { Write-Host "B's start: $($_.Exception.Message)" }
Check (@(Server-Lines $claimMark "claim of lot 1 by \d+ refused").Count -ge 1) "B's claim on lot 1 was refused"
$seen = Wait-Watched $b 1 { param($s) $s.bid -gt 0 }
Check ($null -ne $seen -and $seen.Seconds -le 1.5) "B sees A's bid on lot 1 within 1 s ($($seen.Seconds) s)"

$before = (Send-HarnessCommand -Instance $a -Verb auction-state).local.snapshot.CurrentBid
$raise = Wait-Watched $b 1 { param($s) -not $s.teamLeads } 20
if ($raise) {
    Send-HarnessCommand -Instance $b -Verb auction-raise -Arguments "1" | Out-Null
    $raised = Wait-Watched $b 1 { param($s) $s.teamLeads -and $s.bid -gt $raise.State.bid } 5
    Check ($null -ne $raised) "B's raise counts as the team's bid in A's auction"
} else { Check $false "another bidder never led lot 1, so B could not raise" }

$moneyBefore = (Dump $a).stats.money
$final = (Send-HarnessCommand -Instance $a -Verb auction-finish -Arguments "team").bid
$won = try { Wait-ServerLog -Pattern "\[Outdoor\] Auction #\d+: lot 1 Won" -After $claimMark -TimeoutSec 60 } catch { $null }
Check ([bool]$won) "lot 1 is won through the purchase path ($won)"
Start-Sleep -Seconds 3
Check ((Dump $b).stats.money -eq $moneyBefore - $final) "money dropped once by the final bid $final"
Check ((Send-HarnessCommand -Instance $b -Verb auction-state).sync.closed -contains 1) "lot 1 is closed for B"

$leaveMark = Get-ServerLogMark
$moneyBefore = (Dump $a).stats.money
Send-HarnessCommand -Instance $a -Verb auction-open -Arguments "normal" | Out-Null
Send-HarnessCommand -Instance $a -Verb auction-start -Arguments "2" | Out-Null
Wait-Started $a | Out-Null
Send-HarnessCommand -Instance $a -Verb travel -Arguments "Garage" | Out-Null
$lost = try { Wait-ServerLog -Pattern "\[Outdoor\] Auction #\d+: lot 2 Lost" -After $leaveMark -TimeoutSec 30 } catch { $null }
Check ([bool]$lost) "lot 2 is lost when its bidder leaves ($lost)"
Wait-InGarage $a
Check ((Send-HarnessCommand -Instance $b -Verb auction-state).sync.closed -contains 2) "lot 2 is closed for B"
Check ((Dump $b).stats.money -eq $moneyBefore) "no money moved for lot 2"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

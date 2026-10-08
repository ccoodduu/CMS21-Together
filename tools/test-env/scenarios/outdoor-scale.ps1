# run-all: lane 3
# areas: outdoor
# shared-outdoor-scenes 10.1: every instance of the lane (A-D) travels to the junkyard one after the other; the
# outdoor sections agree; all take from one pile at once for 10 rounds and every item ends with exactly one holder;
# C is killed while holding 3 items and they return for the others; D buys a car the others look at; C relaunches,
# reconnects and arrives late with the same outdoor section. The server's lock wait stays under 50 ms.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")

$names = @($Ctx.Instances)
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }

function Server-Lines([int]$Mark, [string]$Pattern) { @(Get-ServerLogLines | Select-Object -Skip $Mark | Where-Object { $_ -match $Pattern }) }

function Enter-Junkyard([string]$Name) {
    Send-HarnessCommand -Instance $Name -Verb travel -Arguments "Junkyard" | Out-Null
    Wait-HarnessDump -Instance $Name -TimeoutSec 300 -What "$Name in the shared junkyard" -Condition { param($d) $d.local.scene -eq "Junkyard" -and $d.outdoor.applied }
}

function Pile-Uids($Dump) { @($Dump.outdoor.piles | ForEach-Object { $_.uids }) }

Wait-AllInMenu $names
Connect-ScaleInstances $names | Out-Null
foreach ($name in $names) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "enforce" | Out-Null }
Send-ServerCommand "money set 500000"
Send-ServerCommand "perf reset"
$mark = Get-ServerLogMark

foreach ($name in $names) { Enter-Junkyard $name | Out-Null }
$dumps = @{}
foreach ($name in $names) { $dumps[$name] = Dump $name }
foreach ($name in $names | Select-Object -Skip 1) {
    $differ = @(Compare-HarnessDumps $dumps[$names[0]] $dumps[$name] -Sections @("outdoor"))
    Check ($differ.Count -eq 0) "$name has the same outdoor section as $($names[0])"
}

$pile = @($dumps[$names[0]].outdoor.piles | Sort-Object { -@($_.uids).Count })[0]
$uids = @($pile.uids)
Write-Host "racing on pile $($pile.Key) with $($uids.Count) items"
$rounds = [Math]::Min(10, $uids.Count)
for ($round = 0; $round -lt $rounds; $round++) {
    foreach ($name in $names) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments "on" | Out-Null }
    foreach ($name in $names) {
        try { Send-HarnessCommand -Instance $name -Verb loot-take -Arguments "$($pile.Key) uid:$($uids[$round])" | Out-Null }
        catch { Write-Host "$name round $round`: $($_.Exception.Message)" }
    }
    foreach ($name in $names) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments "off" | Out-Null }
}
Start-Sleep -Seconds 5
$holders = @{}
foreach ($name in $names) { foreach ($uid in @((Dump $name).outdoor.loot.held)) { $holders[$uid] = @($holders[$uid]) + $name } }
$raced = @($uids | Select-Object -First $rounds)
Check (@($raced | Where-Object { @($holders[$_] | Where-Object { $_ }).Count -ne 1 }).Count -eq 0) "every raced item has exactly one holder"

$c = $names[2]
$cHeld = @((Dump $c).outdoor.loot.held)
$extra = @(Pile-Uids (Dump $c) | Select-Object -First ([Math]::Max(0, 3 - $cHeld.Count)))
foreach ($uid in $extra) {
    $owner = @((Dump $c).outdoor.piles | Where-Object { @($_.uids) -contains $uid })[0]
    Send-HarnessCommand -Instance $c -Verb loot-take -Arguments "$($owner.Key) uid:$uid" | Out-Null
}
$cHeld = @((Dump $c).outdoor.loot.held)
$process = Get-InstanceGameProcess $c
if ($process) { Stop-Process -Id $process.Id -Force }
$released = try { Wait-ServerLog -Pattern "\(disconnected by \d+\)" -After $mark -TimeoutSec 30 } catch { $null }
Check ([bool]$released) "the server released C's items after the kill"
Start-Sleep -Seconds 3
foreach ($name in $names | Where-Object { $_ -ne $c }) {
    $inPiles = Pile-Uids (Dump $name)
    Check (@($cHeld | Where-Object { $inPiles -notcontains $_ }).Count -eq 0) "$name has C's $($cHeld.Count) items back in the piles"
}

$d = $names[3]
$carMark = Get-ServerLogMark
Send-HarnessCommand -Instance $d -Verb buy-car-here -Arguments "pick:0 5000" | Out-Null
$sold = try { Wait-ServerLog -Pattern "car 0 sold to" -After $carMark -TimeoutSec 30 } catch { $null }
Check ([bool]$sold) "D bought car 0"
Start-Sleep -Seconds 3
foreach ($name in $names | Where-Object { $_ -ne $c -and $_ -ne $d }) {
    Check (@((Dump $name).outdoor.cars | Where-Object { $_.index -eq 0 }).Count -eq 0) "$name no longer has car 0"
}

$launch = Start-HarnessInstance -Lane $Ctx.Launch.Lane -Instance $c -Window $Ctx.Launch.Window -Sound:$Ctx.Launch.Sound -Headless:(@($Ctx.Launch.Headless) -contains $c) -NoWait
$late = @(Wait-HarnessInstances -Launches @($launch))
Check ($late.Count -eq 0) "C relaunched"
Connect-ScaleInstance $c | Out-Null
$dc = Enter-Junkyard $c
$differ = @(Compare-HarnessDumps (Dump $names[0]) $dc -Sections @("outdoor"))
Check ($differ.Count -eq 0) "C arriving late has the same outdoor section"

Send-ServerCommand "perf"
Start-Sleep -Seconds 2
$lockLines = @(Server-Lines $mark "\[Perf\].*(LootTake|OutdoorEnter).*wait")
Write-Host ($lockLines -join "`n")
$worst = @($lockLines | ForEach-Object { if ($_ -match "wait (?:max )?([\d.]+)") { [double]$Matches[1] } }) | Measure-Object -Maximum
Check (-not $worst.Maximum -or $worst.Maximum -lt 50) "lock wait under 50 ms during the take rounds ($($worst.Maximum))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

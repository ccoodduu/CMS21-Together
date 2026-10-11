# areas: economy, placement
# event-stats: the stats of a purchase, sale or scrap on shared things are counted by every player, once.
# A buys a car in the car salon while B stays in the garage: A's game counts stat_buy_carsalon itself, B gets it from
# the server's EventStatsAward, and A is not awarded again. A scraps a part (the mod skips the game's MakeScrap and
# asks the server): both get the server's scrap amount as stat_scrap. B buys two parts in the parts shop (the mod
# skips the game's BuyItem): both get stat_buy_parts 2. stats-trace counts at SteamAchievements.IncrementStat with the
# Steam writes blocked by StatsGuard.
param($Ctx)

$a, $b = $Ctx.Instances
$part = "tuleja_1"
$shopItem = "zaciskHamulcowy_1"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

$Watched = @("stat_buy_carsalon", "stat_scrap", "stat_buy_parts")

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Wait-InSalon([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 180 -What "$Name in the car salon" -Condition {
        param($s) $s.scene -match "(?i)salon" -and $s.playable -and $s.connectionValid
    } | Out-Null
}
function Count([string]$Name, [string]$Stat) { $c = (Cmd $Name stats-trace report).counts; if ($c.$Stat) { [int]$c.$Stat } else { 0 } }
function Wait-Count([string]$Stat, [int]$Expected, [string]$What) {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $now = @(foreach ($name in $Ctx.Instances) { Count $name $Stat })
        if (@($now | Where-Object { $_ -ne $Expected }).Count -eq 0) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Start-Sleep -Seconds 2
    foreach ($name in $Ctx.Instances) {
        $n = Count $name $Stat
        Check ($n -eq $Expected) "$name`: $Stat counted $n, expected $Expected ($What)"
    }
}
function Wait-SalonBuy([string]$Name, [int]$TimeoutSec = 90) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 700
        $steps = @(Cmd $Name buy-car-last)
    } while (-not ($steps -match "^(pressed|failed|no ask window)") -and (Get-Date) -lt $deadline)
    Write-Host "salon-buy steps: $($steps -join ' | ')"
    return $steps
}
function Count-Items([string]$Name, [string]$Id) { @((Cmd $Name dump).inventory.items | Where-Object { $_.ID -eq $Id }).Count }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Cmd $name guard-set "enforce" | Out-Null
    $trace = Cmd $name stats-trace "on"
    Check ($trace.patched -and $trace.guard) "$name`: the trace is patched and StatsGuard blocks the Steam writes ($($trace.error))"
}
Send-ServerCommand "money set 2000000"
Start-Sleep -Seconds 2

# 1. A car bought in the salon by A, with B in the garage.
Cmd $a travel "Salon" | Out-Null
Wait-InSalon $a
Start-Sleep -Seconds 3
$mark = Get-ServerLogMark
Cmd $a salon-buy "multi - -" | Out-Null
$steps = Wait-SalonBuy $a
Check ([bool]($steps -match "^pressed parking")) "A bought a car in the salon and chose the parking"
$arrived = try { Wait-ServerLog -Pattern "\[Parking\] .* arrived in slot \d+ from client \d+ for \d+\." -After $mark -TimeoutSec 20 } catch { $null }
Check ([bool]$arrived) "the server took the car into the parking ($arrived)"
$award = try { Wait-ServerLog -Pattern "\[Stats\] car purchase by client \d+: \[stat_buy_carsalon \+1\] to clients" -After $mark -TimeoutSec 10 } catch { $null }
Check ([bool]$award) "the server awarded stat_buy_carsalon ($award)"
Wait-Count stat_buy_carsalon 1 "A bought one car in the salon"
Cmd $a travel "Garage" | Out-Null
Wait-InGarage $a

# 2. A scraps a part.
$uid = (Cmd $a inv-add-local $part).uid
$deadline = (Get-Date).AddSeconds(20)
while ((Count-Items $b $part) -lt 1 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
Check ((Count-Items $b $part) -ge 1) "B sees A's $part"
$mark = Get-ServerLogMark
Cmd $a econ-scrap "$uid 0" | Out-Null
$line = try { Wait-ServerLog -Pattern "\[Economy\] client \d+ ScrapItem\(0\) money .* scraps \+\d+" -After $mark -TimeoutSec 20 } catch { $null }
Check ([bool]$line) "the server accepted the scrap ($line)"
$scraps = if ($line -match "scraps \+(\d+)") { [int]$Matches[1] } else { 0 }
Check ($scraps -gt 0) "the scrap gave $scraps scrap"
Wait-Count stat_scrap $scraps "A scrapped one $part"

# 3. B buys two parts in the parts shop.
$mark = Get-ServerLogMark
Cmd $b shop-buy "$shopItem 2" | Out-Null
$bought = try { Wait-ServerLog -Pattern "\[Stats\] parts purchase by client \d+: \[stat_buy_parts \+1\]" -After $mark -TimeoutSec 15 } catch { $null }
Check ([bool]$bought) "the server awarded stat_buy_parts ($bought)"
Wait-Count stat_buy_parts 2 "B bought two parts"

# 4. Nothing more: other stats did not move and no event stat was counted twice.
foreach ($stat in $Watched) {
    foreach ($name in $Ctx.Instances) { Write-Host "$name $stat $(Count $name $stat)" }
}
Check ((Count $a "stat_buy_carsalon") -eq 1 -and (Count $b "stat_buy_carsalon") -eq 1) "stat_buy_carsalon is still 1 on A and B"

$errors = @(Get-ServerLogLines | Where-Object { $_ -match "\[(ERROR|EXCEPTION)\]" })
Check ($errors.Count -eq 0) "no server errors ($($errors | Select-Object -First 3))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

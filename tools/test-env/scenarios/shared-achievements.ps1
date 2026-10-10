# areas: economy, placement
# shared-achievements: an achievement that follows from shared save state reaches every player, counted once.
# A buys every garage upgrade, unlocks every skill and the tenth parking level; A and B each get stat_full_garage,
# stat_unlock_allupgrade and stat_unlock_parking once, and nothing before the last one. More garage and world states
# add nothing, and lowering the shared level never sends a negative stat_level. stats-trace counts at
# SteamAchievements.IncrementStat with the Steam writes blocked; "stats-trace zero" clears the in-memory values first,
# because the game skips the checks when the user's own account already has the stat.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

$Watched = @("stat_full_garage", "stat_unlock_allupgrade", "stat_unlock_parking")

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Count([string]$Name, [string]$Stat) { $c = (Cmd $Name stats-trace report).counts; if ($c.$Stat) { [int]$c.$Stat } else { 0 } }
function Expect([string]$Stat, [int]$Expected, [string]$What) {
    foreach ($name in $Ctx.Instances) {
        $n = Count $name $Stat
        Check ($n -eq $Expected) "$name`: $Stat counted $n, expected $Expected ($What)"
    }
}
function Missing([string]$Name, [string]$Kind) { @(Cmd $Name upgrades-missing $Kind) | Where-Object { $_ } }
function Wait-Missing([string]$Kind, [int]$Count) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $left = @(foreach ($name in $Ctx.Instances) { @(Missing $name $Kind).Count })
        if (@($left | Where-Object { $_ -ne $Count }).Count -eq 0) { return $true }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Write-Host "missing $Kind upgrades: $($left -join ', ') (want $Count)"
    return $false
}
function Request([string]$Kind, [string]$Entry) {
    $id, $level = $Entry -split ":"
    Cmd $a upgrade-request "$Kind $id $level" | Out-Null
    Start-Sleep -Milliseconds 250
}
function Wait-ParkingLevels([int]$Levels) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $now = @(foreach ($name in $Ctx.Instances) { (Cmd $name parking).levels })
        if (@($now | Where-Object { $_ -ne $Levels }).Count -eq 0) { return $true }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Write-Host "parking levels: $($now -join ', ') (want $Levels)"
    return $false
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Cmd $name guard-set "Off" | Out-Null
    $trace = Cmd $name stats-trace "on"
    Check ($trace.patched -and $trace.guard) "$name`: the trace is patched and StatsGuard blocks the Steam writes ($($trace.error))"
    $zeroed = Cmd $name stats-trace "zero $($Watched -join ' ')"
    Check (@($Watched | Where-Object { $zeroed.values.$_ -ne 0 }).Count -eq 0) "$name`: in-memory values cleared ($($zeroed.values | ConvertTo-Json -Compress))"
}
Send-ServerCommand "money set 200000000"
Send-ServerCommand "level set 30"
Start-Sleep -Seconds 3

# 1. Garage upgrades: everything but one (bus_upgrade is outside the game's full-garage check, so it goes first).
$money = @(Missing $a money)
Write-Host "missing garage upgrades: $($money.Count)"
Check ($money.Count -ge 2) "A has at least two garage upgrades to buy ($($money.Count))"
$last = @($money | Where-Object { $_ -notmatch "^bus_upgrade:" })[-1]
foreach ($entry in $money) { if ($entry -ne $last) { Request money $entry } }
Check (Wait-Missing money 1) "both see one garage upgrade left ($last)"
Start-Sleep -Seconds 2
Expect stat_full_garage 0 "one upgrade still missing"
Request money $last
Check (Wait-Missing money 0) "both see every garage upgrade"
Start-Sleep -Seconds 3
Expect stat_full_garage 1 "A bought the last garage upgrade"

# 2. Skills: every one but one, then the last.
$points = @(Missing $a points)
Write-Host "missing skills: $($points.Count)"
Check ($points.Count -ge 2) "A has at least two skills to unlock ($($points.Count))"
$lastSkill = $points[-1]
foreach ($entry in $points) { if ($entry -ne $lastSkill) { Request points $entry } }
Check (Wait-Missing points 1) "both see one skill left ($lastSkill)"
Start-Sleep -Seconds 2
Expect stat_unlock_allupgrade 0 "one skill still locked"
Request points $lastSkill
Check (Wait-Missing points 0) "both see every skill"
Start-Sleep -Seconds 3
Expect stat_unlock_allupgrade 1 "A unlocked the last skill"

# 3. Parking: up to nine levels nothing, the tenth counts.
$levels = [int](Cmd $a parking).levels
Check ($levels -lt 10) "the parking starts below ten levels ($levels)"
while ($levels -lt 9) { Cmd $a parking-unlock | Out-Null; $levels++; Check (Wait-ParkingLevels $levels) "both see $levels parking levels" }
Start-Sleep -Seconds 2
Expect stat_unlock_parking 0 "nine parking levels"
Cmd $a parking-unlock | Out-Null
Check (Wait-ParkingLevels 10) "both see ten parking levels"
Start-Sleep -Seconds 3
Expect stat_unlock_parking 1 "A unlocked the tenth parking level"

# 4. No second count: a refused upgrade answers B with the garage state, the world state goes out again.
$entry = ($money | Select-Object -First 1) -split ":"
Cmd $b upgrade-request "money $($entry[0]) $($entry[1])" | Out-Null
Send-ServerCommand "money add 1"
Start-Sleep -Seconds 3
foreach ($stat in $Watched) { Expect $stat 1 "after more garage and world states" }

# 5. Lowering the shared level never lowers the platform's stat_level.
$before = @{}; foreach ($name in $Ctx.Instances) { $before[$name] = Count $name stat_level }
Send-ServerCommand "level set 2"
Start-Sleep -Seconds 3
foreach ($name in $Ctx.Instances) {
    $after = Count $name stat_level
    Check ($after -ge $before[$name]) "$name`: stat_level was not lowered (counted $($before[$name]) before, $after after)"
}

$errors = @(Get-ServerLogLines | Where-Object { $_ -match "\[(ERROR|EXCEPTION)\]" })
Check ($errors.Count -eq 0) "no server errors ($($errors | Select-Object -First 3))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

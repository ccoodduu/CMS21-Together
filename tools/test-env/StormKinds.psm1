# Disconnect storm kinds K1-K8 of multiplayer-soak-and-scale design D8, shared by scenarios\storm.ps1 and the soak
# (-StormEveryMinutes). Each kind takes a reference (all clients equal) before it starts, runs its steps with a
# seeded Random, checks what D8 lists and appends one record to storms.jsonl. A killed game's log is copied to
# client_<X>_storm<n>.log and the game is relaunched with Start-HarnessInstance. Uses HarnessClient, TestLanes and
# ScaleSession as the scenario imported them.

$script:AllKinds = @("K1", "K2", "K3", "K4", "K5", "K6", "K7", "K8")
$script:Invariant = [Globalization.CultureInfo]::InvariantCulture

function Get-StormKinds([int]$InstanceCount) {
    if ($InstanceCount -lt 2) { return @() }
    @($script:AllKinds | Where-Object { $_ -ne "K4" -or $InstanceCount -ge 3 })
}

function Add-StormCheck($Record, [bool]$Ok, [string]$Message) {
    $Record.checks += "$(if ($Ok) { 'ok' } else { 'FAIL' }): $Message"
    if ($Ok) { Write-Host "ok: $Message" } else { $Record.failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red }
}

function Get-Pick($Rng, $Items) { $list = @($Items); if ($list.Count -eq 0) { $null } else { $list[$Rng.Next($list.Count)] } }

function Get-ShortKey([string]$Instance) {
    $key = (Send-HarnessCommand -Instance $Instance -Verb player-key).key
    "guid:" + $key.Substring(0, [math]::Min(8, $key.Length))
}

function Get-Reference($Record, [string[]]$Names) {
    try { return Wait-HarnessDumpsAllEqual -Instances $Names -TimeoutSec 60 }
    catch { Add-StormCheck $Record $false "clients equal before the storm: $($_.Exception.Message)"; return Get-LastHarnessDumps }
}

function Test-AllEqual($Record, [string[]]$Names, [string]$What) {
    try {
        $dumps = Wait-HarnessDumpsAllEqual -Instances $Names -TimeoutSec 90
        Add-StormCheck $Record $true "$What`: all clients equal"
        return $dumps
    } catch {
        Add-StormCheck $Record $false "$What`: $($_.Exception.Message)"
        return Get-LastHarnessDumps
    }
}

function Test-SameAsReference($Record, $Reference, $After, [string]$First, [string]$What) {
    if (-not $Reference -or -not $After) { Add-StormCheck $Record $false "$What`: no dumps to compare"; return }
    $differ = @(Compare-StableDumps $Reference[$First] $After[$First])
    Add-StormCheck $Record ($differ.Count -eq 0) "$What`: shared state equals the reference (differ: $($differ -join ', '))"
}

# Kills one game, waits for the process to end and copies its logs into the run folder.
function Stop-StormInstance($Ctx, [string]$Name, [string]$Tag) {
    $process = Get-InstanceGameProcess $Name
    if ($process) { Stop-Process -Id $process.Id -Force }
    return [pscustomobject]@{ Name = $Name; Process = $process; At = Get-Date; Tag = $Tag }
}

function Save-KilledLogs($Ctx, $Killed) {
    if ($Killed.Process) { try { $Killed.Process.WaitForExit(20000) | Out-Null } catch { } }
    $log = Get-ClientLogPath $Killed.Name
    if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $Ctx.RunDir "client_$($Killed.Name)_storm$($Killed.Tag).log") -Force }
    $playerLog = Join-Path (Get-InstanceSaveDir $Killed.Name) "Player.log"
    if (Test-Path -LiteralPath $playerLog) { Copy-Item -LiteralPath $playerLog -Destination (Join-Path $Ctx.RunDir "player_$($Killed.Name)_storm$($Killed.Tag).log") -Force }
}

function Start-StormInstances($Ctx, [string[]]$Names) {
    $launches = foreach ($name in $Names) {
        Start-HarnessInstance -Lane $Ctx.Launch.Lane -Instance $name -Window $Ctx.Launch.Window -Sound:$Ctx.Launch.Sound `
            -Headless:(@($Ctx.Launch.Headless) -contains $name) -NoWait
    }
    $late = @(Wait-HarnessInstances -Launches @($launches))
    if ($late.Count -gt 0) { throw "$($late -join ', ') did not reach the main menu after the relaunch" }
}

# A few changes by the clients that stay, while others are away.
function Invoke-StormWork($Rng, [string[]]$Workers, [int]$Steps = 3) {
    $done = @()
    for ($i = 0; $i -lt $Steps; $i++) {
        $worker = Get-Pick $Rng $Workers
        if (-not $worker) { break }
        try {
            switch ($Rng.Next(3)) {
                0 { Send-HarnessCommand -Instance $worker -Verb stats-add -Arguments "$($Rng.Next(1, 10)) $($Rng.Next(1, 50))" | Out-Null; $done += "$worker stats-add" }
                1 { Send-HarnessCommand -Instance $worker -Verb give-item -Arguments "tarczaHamulcowa_1 0.5" | Out-Null; $done += "$worker give-item" }
                2 {
                    $car = @(Send-HarnessCommand -Instance $worker -Verb placement) | Select-Object -First 1
                    if ($car) { Send-HarnessCommand -Instance $worker -Verb part-fast-unmount -Arguments "$($car.loader)" | Out-Null; $done += "$worker part-fast-unmount $($car.loader)" }
                }
            }
        } catch { $done += "$worker error: $($_.Exception.Message)" }
        Start-Sleep -Milliseconds 700
    }
    return $done
}

function Test-RosterCount($Record, [string[]]$Names, [int]$Expected, [string]$What, [int]$TimeoutSec = 20) {
    foreach ($name in $Names) {
        $count = -1
        try {
            $dump = Wait-HarnessDump -Instance $name -TimeoutSec $TimeoutSec -What $What -Condition ({ param($d) $d.remotePlayers -eq $Expected }.GetNewClosure())
            $count = $dump.remotePlayers
        } catch { $count = (Send-HarnessCommand -Instance $name -Verb dump).remotePlayers }
        Add-StormCheck $Record ($count -eq $Expected) "$What`: $name shows $Expected remote players ($count)"
    }
}

function Test-ReturningJoin($Record, [string]$ShortKey, [int]$Mark, [string]$Name) {
    $line = $null
    try { $line = Wait-ServerLog -Pattern "joined as $([regex]::Escape($ShortKey)) \(returning\)" -After $Mark -TimeoutSec 10 } catch { }
    Add-StormCheck $Record ([bool]$line) "$Name came back with the same identity $ShortKey"
}

function Wait-ReadyCar($Ctx, [string[]]$Names, [string]$Spawner) {
    $car = @(Send-HarnessCommand -Instance $Spawner -Verb placement) | Select-Object -First 1
    if (-not $car) {
        $loaders = 0..3
        Send-HarnessCommand -Instance $Spawner -Verb car-spawn -Arguments "$($loaders[0]) car_boltatlanta 0 auto" | Out-Null
        $car = [pscustomobject]@{ loader = 0 }
    }
    $problems = @(Wait-CarsReady $Names 180)
    if ($problems.Count -gt 0) { throw "no ready car: $($problems -join '; ')" }
    return [int]$car.loader
}

function Invoke-K1($Ctx, $Record, $Rng) {
    $names = @($Ctx.Instances)
    $victim = Get-Pick $Rng $names
    $others = @($names | Where-Object { $_ -ne $victim })
    $Record.victims = @($victim)
    $id = [int](Get-HarnessStatus $victim).playerId
    $shortKey = Get-ShortKey $victim
    $before = (Send-HarnessCommand -Instance $victim -Verb dump).local
    $records = @(Get-ServerPlayerRecords).Count
    $mark = Get-ServerLogMark
    $killed = Stop-StormInstance $Ctx $victim $Record.index
    $left = $null
    try { $left = Wait-ServerLog -Pattern "Player $id left" -After $mark -TimeoutSec 15 } catch { }
    Add-StormCheck $Record ([bool]$left) "the server logged the crash of $victim (slot $id) within 15 s"
    Test-RosterCount $Record $others ($names.Count - 2) "after the crash" 15
    $Record.work = @(Invoke-StormWork $Rng $others 4)
    Save-KilledLogs $Ctx $killed
    Start-StormInstances $Ctx @($victim)
    $mark = Get-ServerLogMark
    Connect-ScaleInstance $victim | Out-Null
    Test-ReturningJoin $Record $shortKey $mark $victim
    Add-StormCheck $Record (@(Get-ServerPlayerRecords).Count -eq $records) "the player records count is unchanged ($records)"
    if ($before.scene -eq "Garage" -and $before.position) {
        $now = (Send-HarnessCommand -Instance $victim -Verb dump).local.position
        $distance = Get-Distance $now $before.position
        Add-StormCheck $Record ($distance -lt 0.5) "$victim is back at its garage position ($([math]::Round($distance, 2)) m)"
    }
    Test-RosterCount $Record $names ($names.Count - 1) "after the rejoin"
    Test-AllEqual $Record $names "after K1" | Out-Null
}

function Invoke-K2($Ctx, $Record, $Rng) {
    $names = @($Ctx.Instances)
    $victim = Get-Pick $Rng $names
    $Record.victims = @($victim)
    $shortKey = Get-ShortKey $victim
    $mark = Get-ServerLogMark
    for ($i = 1; $i -le 3; $i++) {
        Send-HarnessCommand -Instance $victim -Verb to-menu | Out-Null
        if (-not (Wait-InMenu $victim 60)) { Add-StormCheck $Record $false "flap $i`: $victim reached the menu"; return }
        Start-Sleep -Seconds 2
        Connect-ScaleInstance $victim | Out-Null
        Start-Sleep -Seconds 2
    }
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark)
    $acked = @($lines | Where-Object { $_ -match "Client\[\d+\] snapshot \d+ acked after" }).Count
    $returning = @($lines | Where-Object { $_ -match "joined as $([regex]::Escape($shortKey)) \(returning\)" }).Count
    Add-StormCheck $Record ($acked -ge 3) "each of the three rejoins acked a new snapshot ($acked)"
    Add-StormCheck $Record ($returning -ge 3) "each rejoin kept the identity $shortKey ($returning)"
    Test-RosterCount $Record $names ($names.Count - 1) "after the flaps (no stray avatar)"
    Test-AllEqual $Record $names "after K2" | Out-Null
}

function Invoke-K3($Ctx, $Record, $Rng) {
    $names = @($Ctx.Instances)
    $victim = Get-Pick $Rng $names
    $others = @($names | Where-Object { $_ -ne $victim })
    $Record.victims = @($victim)
    $id = [int](Get-HarnessStatus $victim).playerId
    $shortKey = Get-ShortKey $victim
    $records = @(Get-ServerPlayerRecords).Count
    $mark = Get-ServerLogMark
    $stalledAt = Get-Date
    Send-HarnessCommand -Instance $victim -Verb net-hold -Arguments "out" | Out-Null
    $timedOut = $null
    try { $timedOut = Wait-ServerLog -Pattern "Client\[$id\] timed out" -After $mark -TimeoutSec 25 } catch { }
    Add-StormCheck $Record ([bool]$timedOut) "the server timed $victim out ($([math]::Round(((Get-Date) - $stalledAt).TotalSeconds, 1)) s after the stall)"
    $menu = Wait-InMenu $victim 40
    Add-StormCheck $Record ([bool]$menu) "$victim reached the menu by itself ($($menu.lastDisconnect.reason): $($menu.lastDisconnect.message))"
    $Record.work = @(Invoke-StormWork $Rng $others 3)
    try { Send-HarnessCommand -Instance $victim -Verb net-hold -Arguments "off" | Out-Null } catch { }
    $mark = Get-ServerLogMark
    Connect-ScaleInstance $victim | Out-Null
    Test-ReturningJoin $Record $shortKey $mark $victim
    Add-StormCheck $Record (@(Get-ServerPlayerRecords).Count -eq $records) "the player records count is unchanged ($records)"
    Test-RosterCount $Record $names ($names.Count - 1) "after the rejoin"
    Test-AllEqual $Record $names "after K3" | Out-Null
}

function Invoke-K4($Ctx, $Record, $Rng) {
    $names = @($Ctx.Instances)
    if ($names.Count -lt 3) { $Record.notes += "K4 needs three or more clients; skipped"; return }
    $worker = Get-Pick $Rng $names
    $leavers = @($names | Where-Object { $_ -ne $worker } | Select-Object -First 3)
    $toKill = @($leavers | Select-Object -First ($leavers.Count - 1))
    $toMenu = $leavers[-1]
    $Record.victims = $leavers
    $Record.worker = $worker
    $keys = @{}
    foreach ($name in $leavers) { $keys[$name] = Get-ShortKey $name }
    $killed = @()
    $started = Get-Date
    foreach ($name in $toKill) { $killed += Stop-StormInstance $Ctx $name $Record.index }
    Send-HarnessCommand -Instance $toMenu -Verb to-menu | Out-Null
    $Record.leaveSpanMs = [math]::Round(((Get-Date) - $started).TotalMilliseconds)
    Add-StormCheck $Record ($Record.leaveSpanMs -le 1500) "$($leavers.Count) clients left within about 1 s ($($Record.leaveSpanMs) ms)"
    $Record.work = @(Invoke-StormWork $Rng @($worker) 5)
    foreach ($k in $killed) { Save-KilledLogs $Ctx $k }
    Wait-InMenu $toMenu 60 | Out-Null
    Start-StormInstances $Ctx $toKill
    $mark = Get-ServerLogMark
    $burstAt = Get-Date
    foreach ($name in $leavers) { Clear-MenuMessage $name; Connect-HarnessInstance $name }
    foreach ($name in $leavers) {
        if (-not (Test-InGarage (Get-HarnessStatus $name))) {
            try { Wait-InGarage $name 300 | Out-Null } catch { Connect-ScaleInstance $name | Out-Null }
        }
        Send-HarnessCommand -Instance $name -Verb fps-cap -Arguments "30" | Out-Null
        Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null
    }
    $Record.burstJoinS = [math]::Round(((Get-Date) - $burstAt).TotalSeconds, 1)
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark)
    $acks = @($lines | Where-Object { $_ -match "snapshot \d+ acked after (\d+) ms, (\d+) bytes" } | ForEach-Object { $_ })
    $Record.snapshots = $acks
    Add-StormCheck $Record ($acks.Count -ge $leavers.Count) "$($leavers.Count) snapshots back to back ($($acks.Count) acked, all in $($Record.burstJoinS) s)"
    foreach ($name in $leavers) {
        Add-StormCheck $Record ([bool]@($lines | Where-Object { $_ -match "joined as $([regex]::Escape($keys[$name])) \(returning\)" }).Count) "$name came back with the same identity"
    }
    $Record.notes += "lock wait during the burst: see perf_server.jsonl timings between $($burstAt.ToString('s')) and $((Get-Date).ToString('s'))"
    Test-AllEqual $Record $names "after K4 (the worker's changes reached everyone)" | Out-Null
}

function Invoke-K5($Ctx, $Record, $Rng) {
    $names = @($Ctx.Instances)
    $victim = Get-Pick $Rng $names
    $helper = Get-Pick $Rng @($names | Where-Object { $_ -ne $victim })
    $Record.victims = @($victim)
    $Record.helper = $helper
    $loader = Wait-ReadyCar $Ctx $names $helper
    $id = [int](Get-HarnessStatus $victim).playerId
    $car = @((Send-HarnessCommand -Instance $victim -Verb dump).cars | Where-Object { $_.index -eq $loader })[0]
    $key = @($car.subParts | Where-Object { -not $_.unmounted -and -not $_.blocked } | ForEach-Object { $_.key }) | Select-Object -First 1
    try { Send-HarnessCommand -Instance $helper -Verb tool-take -Arguments "WheelBalancer" | Out-Null } catch { }
    Send-HarnessCommand -Instance $victim -Verb part-claim -Arguments "$loader $key" | Out-Null
    $wheel = (Send-HarnessCommand -Instance $victim -Verb give-group -Arguments "wheel").UID
    Send-HarnessCommand -Instance $victim -Verb tool-put -Arguments "WheelBalancer $wheel" | Out-Null
    Send-HarnessCommand -Instance $victim -Verb tool-balance | Out-Null
    Send-HarnessCommand -Instance $victim -Verb tool-balance-open | Out-Null
    $held = $null
    try {
        $held = Wait-HarnessDump -Instance $helper -TimeoutSec 15 -What "victim's claim and balancer lock" -Condition ({ param($d)
            $c = @($d.cars | Where-Object { $_.index -eq $loader })[0]
            @($c.claims | Where-Object { $_.key -eq $key -and $_.owner -eq $id }).Count -eq 1 -and $d.tools.WheelBalancer.claimedBy -eq $id
        }.GetNewClosure())
    } catch { }
    Add-StormCheck $Record ([bool]$held) "$helper sees $victim's claim on $key and its balancer lock"

    $killed = Stop-StormInstance $Ctx $victim $Record.index
    $released = $null
    try {
        $released = Wait-HarnessDump -Instance $helper -TimeoutSec 15 -What "claims released" -Condition ({ param($d)
            $c = @($d.cars | Where-Object { $_.index -eq $loader })[0]
            @($c.claims | Where-Object { $_.owner -eq $id }).Count -eq 0 -and $d.tools.WheelBalancer.claimedBy -ne $id
        }.GetNewClosure())
    } catch { }
    Add-StormCheck $Record ([bool]$released) "the claim and the balancer lock of the killed $victim are released within 15 s"
    $helperId = [int](Get-HarnessStatus $helper).playerId
    Send-HarnessCommand -Instance $helper -Verb part-claim -Arguments "$loader $key" | Out-Null
    $claimed = $null
    try { $claimed = Wait-HarnessDump -Instance $helper -TimeoutSec 10 -What "helper's claim" -Condition ({ param($d) $c = @($d.cars | Where-Object { $_.index -eq $loader })[0]; @($c.claims | Where-Object { $_.key -eq $key -and $_.owner -eq $helperId }).Count -eq 1 }.GetNewClosure()) } catch { }
    Add-StormCheck $Record ([bool]$claimed) "$helper can claim $key afterwards"
    Send-HarnessCommand -Instance $helper -Verb part-claim -Arguments "$loader $key release" | Out-Null
    $take = Send-HarnessCommand -Instance $helper -Verb tool-take -Arguments "WheelBalancer"
    Add-StormCheck $Record (-not $take.refused) "$helper can take the wheel off the balancer"
    Save-KilledLogs $Ctx $killed
    Start-StormInstances $Ctx @($victim)
    Connect-ScaleInstance $victim | Out-Null

    $loader = Wait-ReadyCar $Ctx $names $helper
    Send-HarnessCommand -Instance $victim -Verb testdrive-go -Arguments "$loader" | Out-Null
    $away = $null
    $victimId = [int](Get-HarnessStatus $victim).playerId
    try { $away = Wait-HarnessDump -Instance $helper -TimeoutSec 20 -What "test drive claim" -Condition ({ param($d) @($d.away | Where-Object { $_.loader -eq $loader -and $_.owner -eq $victimId }).Count -eq 1 }.GetNewClosure()) } catch { }
    Add-StormCheck $Record ([bool]$away) "$helper sees $victim's test drive of loader $loader"
    try { Wait-HarnessStatus -Instance $victim -TimeoutSec 120 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null } catch { }
    $killed = Stop-StormInstance $Ctx $victim "$($Record.index)b"
    $back = $null
    try {
        $back = Wait-HarnessDump -Instance $helper -TimeoutSec 30 -What "test drive car back" -Condition ({ param($d) @($d.away | Where-Object { $_.loader -eq $loader }).Count -eq 0 }.GetNewClosure())
    } catch { }
    $ready = Send-HarnessCommand -Instance $helper -Verb car-ready -Arguments "$loader"
    Add-StormCheck $Record ([bool]$back -and $ready.loaded) "the test-drive car of the killed $victim is back in the garage ($($ready.state))"
    Save-KilledLogs $Ctx $killed
    Start-StormInstances $Ctx @($victim)
    Connect-ScaleInstance $victim | Out-Null
    Test-AllEqual $Record $names "after K5" | Out-Null
}

function Restart-AllClients($Record, [string[]]$Names) {
    foreach ($name in $Names) { Clear-MenuMessage $name }
    foreach ($name in $Names) {
        try { Connect-ScaleInstance $name | Out-Null } catch { Add-StormCheck $Record $false "$name rejoins after the restart: $($_.Exception.Message)" }
    }
}

function Invoke-K6($Ctx, $Record, $Rng) {
    $names = @($Ctx.Instances)
    $reference = Get-Reference $Record $names
    $savePath = Join-Path $Ctx.ServerDir "Saves\server_save.json"
    $saveBefore = (Get-Item -LiteralPath $savePath).LastWriteTimeUtc
    Start-Sleep -Seconds 1
    Send-ServerCommand "stop"
    foreach ($name in $names) {
        $menu = Wait-InMenu $name 60
        Add-StormCheck $Record ($menu -and $menu.lastDisconnect.reason -eq "ServerShutdown") "$name reached the menu with the shutdown reason ($($menu.lastDisconnect.reason))"
    }
    $exited = Wait-TestServerExit 30
    Add-StormCheck $Record $exited "the server process ended after stop"
    if (-not $exited) { Stop-TestServer }
    Add-StormCheck $Record ((Get-Item -LiteralPath $savePath).LastWriteTimeUtc -gt $saveBefore) "stop wrote a newer save"
    Start-TestServer | Out-Null
    Restart-AllClients $Record $names
    $after = Test-AllEqual $Record $names "after K6"
    Test-SameAsReference $Record $reference $after $names[0] "K6 graceful restart"
}

function Test-MainSaveLoaded($Record, [string]$What) {
    $lines = @(Get-ServerLogLines)
    $loaded = @($lines | Where-Object { $_ -match "Game session loaded from .*server_save\.json\." }).Count -gt 0
    $bad = @($lines | Where-Object { $_ -match "Loaded the fallback|Cannot load|Saves/corrupt|Moved .* to " })
    Add-StormCheck $Record ($loaded -and $bad.Count -eq 0) "$What`: the server loaded its main save (no fallback, no quarantine)$(if ($bad) { ': ' + ($bad -join ' / ') })"
}

function Invoke-K7($Ctx, $Record, $Rng) {
    $names = @($Ctx.Instances)
    $reference = Get-Reference $Record $names
    $mark = Get-ServerLogMark
    Send-ServerCommand "save"
    try { Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null } catch { Add-StormCheck $Record $false "K7 (i): the save before the kill finished" }
    $killedAt = Get-Date
    Stop-TestServer
    foreach ($name in $names) {
        $menu = Wait-InMenu $name 30
        $seconds = ((Get-Date) - $killedAt).TotalSeconds
        Add-StormCheck $Record ([bool]$menu -and $seconds -le 15) "K7 (i): $name reached the menu after the kill ($([math]::Round($seconds, 1)) s)"
    }
    Start-TestServer | Out-Null
    Test-MainSaveLoaded $Record "K7 (i)"
    Restart-AllClients $Record $names
    $after = Test-AllEqual $Record $names "after K7 (i)"
    Test-SameAsReference $Record $reference $after $names[0] "K7 (i) save + kill"

    $config = Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "server_config.ini") | Where-Object { $_ -match "^\s*autosave_interval_seconds\s*=\s*(\d+)" } | Select-Object -First 1
    $autosave = if ($config -match "=\s*(\d+)") { [int]$Matches[1] } else { 300 }
    $worker = Get-Pick $Rng $names
    $scrapStart = [int](Send-HarnessCommand -Instance $worker -Verb dump).stats.scrap
    $changes = @()
    $until = (Get-Date).AddSeconds([math]::Min(120, $autosave + 15))
    $n = 0
    while ((Get-Date) -lt $until) {
        Send-HarnessCommand -Instance $worker -Verb stats-add -Arguments "1 1" | Out-Null
        $n++
        $changes += [pscustomobject]@{ At = Get-Date; Scrap = $scrapStart + $n }
        Start-Sleep -Seconds 3
    }
    $killedAt = Get-Date
    Stop-TestServer
    foreach ($name in $names) { Wait-InMenu $name 30 | Out-Null }
    Start-TestServer | Out-Null
    Test-MainSaveLoaded $Record "K7 (ii)"
    Restart-AllClients $Record $names
    $after = Test-AllEqual $Record $names "after K7 (ii)"
    $scrapAfter = if ($after) { [int]$after[$names[0]].stats.scrap } else { -1 }
    $mustHave = @($changes | Where-Object { ($killedAt - $_.At).TotalSeconds -gt $autosave + 5 } | Select-Object -Last 1)
    $expected = if ($mustHave.Count) { $mustHave[0].Scrap } else { $scrapStart }
    $Record.lossWindow = [ordered]@{ autosaveS = $autosave; changes = $n; scrapStart = $scrapStart; scrapLastSent = $scrapStart + $n; scrapAfter = $scrapAfter; mustHave = $expected }
    Add-StormCheck $Record ($scrapAfter -ge $expected -and $scrapAfter -le $scrapStart + $n) "K7 (ii): loss bounded by the autosave interval ($autosave s): scrap after the restart $scrapAfter, sent up to $($scrapStart + $n), older than $($autosave + 5) s: $expected"
}

function Invoke-K8($Ctx, $Record, $Rng) {
    $names = @($Ctx.Instances)
    $victim = Get-Pick $Rng $names
    $Record.victims = @($victim)
    $records = @(Get-ServerPlayerRecords).Count
    Send-HarnessCommand -Instance $victim -Verb to-menu | Out-Null
    Wait-InMenu $victim 60 | Out-Null
    Send-HarnessCommand -Instance $victim -Verb net-delay -Arguments "2000" | Out-Null
    $mark = Get-ServerLogMark
    Clear-MenuMessage $victim
    Connect-HarnessInstance $victim
    $snapshot = $null
    try { $snapshot = Wait-ServerLog -Pattern "Client\[\d+\] snapshot \d+: " -After $mark -TimeoutSec 180 } catch { }
    Add-StormCheck $Record ([bool]$snapshot) "the server built $victim's snapshot ($snapshot)"
    $statusAtKill = Get-HarnessStatus $victim
    Stop-TestServer
    $Record.victimAtKill = [ordered]@{ scene = $statusAtKill.scene; syncAcked = $statusAtKill.syncAcked; joinStatus = $statusAtKill.joinStatus }
    $menu = Wait-InMenu $victim 40
    Add-StormCheck $Record ([bool]$menu -and -not $menu.syncAcked) "$victim left the half-finished join for the menu ($($menu.lastDisconnect.reason): $($menu.lastDisconnect.message))"
    foreach ($name in @($names | Where-Object { $_ -ne $victim })) { Wait-InMenu $name 30 | Out-Null }
    try { Send-HarnessCommand -Instance $victim -Verb net-delay -Arguments "0" | Out-Null } catch { }
    Start-TestServer | Out-Null
    Restart-AllClients $Record $names
    Add-StormCheck $Record (@(Get-ServerPlayerRecords).Count -eq $records) "the player records count is unchanged ($records)"
    Test-AllEqual $Record $names "after K8" | Out-Null
}

function Invoke-StormKind {
    param($Ctx, [string]$Kind, [int]$Index, [int]$Seed)
    $rng = New-Object System.Random($Seed)
    $record = [ordered]@{
        index = $Index; kind = $Kind; seed = $Seed; t = (Get-Date).ToString("s"); instances = @($Ctx.Instances)
        victims = @(); passed = $false; failures = @(); checks = @(); notes = @(); durationS = 0
    }
    Write-Host "STORM $Index $Kind (seed $Seed)"
    $started = Get-Date
    try {
        switch ($Kind) {
            "K1" { Invoke-K1 $Ctx $record $rng }
            "K2" { Invoke-K2 $Ctx $record $rng }
            "K3" { Invoke-K3 $Ctx $record $rng }
            "K4" { Invoke-K4 $Ctx $record $rng }
            "K5" { Invoke-K5 $Ctx $record $rng }
            "K6" { Invoke-K6 $Ctx $record $rng }
            "K7" { Invoke-K7 $Ctx $record $rng }
            "K8" { Invoke-K8 $Ctx $record $rng }
            default { throw "Unknown storm kind $Kind (known: $($script:AllKinds -join ', '))" }
        }
    } catch {
        Add-StormCheck $record $false "$Kind stopped: $($_.Exception.Message)"
    }
    $record.durationS = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
    $record.passed = $record.failures.Count -eq 0
    Add-JsonLine (Join-Path $Ctx.RunDir "storms.jsonl") $record
    Write-Host ("STORM {0} {1}: {2} in {3} s" -f $Index, $Kind, $(if ($record.passed) { "passed" } else { "FAILED" }), $record.durationS)
    return [pscustomobject]$record
}

Export-ModuleMember -Function Get-StormKinds, Invoke-StormKind

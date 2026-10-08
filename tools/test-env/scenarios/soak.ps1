# run-all: lane 3
# areas: connect, cars, parts, placement, economy, tools, details, jobs, presence, resync
# multiplayer-soak-and-scale D5/D6 (tasks 4.1-4.3, 6.4): a seeded random mix of real player actions by every client
# for -Minutes, with sampling (perf_processes.csv every 10 s, frames.csv every 30 s, the server's perf log every
# 10 s), a quiesced checkpoint every -CheckEveryMinutes (all clients equal on every shared section plus a forced
# digest round), and optionally a storm kind every -StormEveryMinutes. Every issued verb goes to actions.jsonl;
# -Replay <actions.jsonl> issues the same verbs, actors and arguments in the same order. The run fails on the rules
# of D6 (allow-list: soak-allow.txt) but goes on to the end unless -StopOnFailure. Every mount makes a new part and
# every unmount keeps one, so after each checkpoint the soak sells single items down to -InventoryCap (0 = no cap);
# the sales are ordinary steps in actions.jsonl, so a replay repeats them instead of capping again.
param(
    $Ctx,
    [double]$Minutes = 10,
    [int]$Seed = 0,
    [double]$CheckEveryMinutes = 0,
    [double]$StormEveryMinutes = 0,
    [string]$Replay = "",
    [int]$InventoryCap = 300,
    [switch]$StopOnFailure,
    [switch]$NoAutofix
)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")
Import-Module (Join-Path $PSScriptRoot "..\PerfSampler.psm1")
$stormModule = Join-Path $PSScriptRoot "..\StormKinds.psm1"
if (Test-Path -LiteralPath $stormModule) { Import-Module $stormModule }

$names = @($Ctx.Instances)
$runDir = $Ctx.RunDir
$actionsFile = Join-Path $runDir "actions.jsonl"
if ($Seed -eq 0) { $Seed = [int](Get-Date -Format "MMddHHmmss") }
if ($CheckEveryMinutes -le 0) { $CheckEveryMinutes = if ($Minutes -ge 60) { 10 } else { 2 } }
$rng = New-Object System.Random($Seed)
$replaySteps = if ($Replay) { @(Get-Content -LiteralPath $Replay | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json }) } else { $null }
$Ctx.Result["seed"] = $Seed
$Ctx.Result["minutes"] = $Minutes
Write-Host ("SOAK seed {0}, {1} min, checkpoint every {2} min, storm every {3} min, {4} clients{5}" -f $Seed, $Minutes, $CheckEveryMinutes, $StormEveryMinutes, $names.Count, $(if ($Replay) { ", replay of $Replay ($($replaySteps.Count) lines)" } else { "" }))

$failures = New-Object System.Collections.Generic.List[string]
$script:firstFailureDone = $false
$script:stop = $false
function Add-Failure([int]$Rule, [string]$Message) {
    $text = "rule $Rule`: $Message"
    $failures.Add($text)
    Write-Host "FAIL: $text" -ForegroundColor Red
    if (-not $script:firstFailureDone) {
        $script:firstFailureDone = $true
        try {
            $report = Send-HarnessCommand -Instance $names[0] -Verb bug-report -TimeoutSec 60
            $Ctx.Result.notes += "bug report on the first failure: $($report | ConvertTo-Json -Compress)"
        } catch { Write-Host "bug-report: $($_.Exception.Message)" }
        foreach ($name in $names) { try { Save-HarnessDump -Instance $name -RunDir $runDir -Label "first-failure" | Out-Null } catch { } }
    }
    if ($StopOnFailure) { $script:stop = $true }
}

Set-ServerConfigValues $Ctx.ServerDir @{
    perf_log_interval_seconds = 10; autosave_interval_seconds = 30; desync_check_interval_seconds = 5; desync_autofix = (-not $NoAutofix)
}
$scenarioStart = Get-Date
Restart-TestServer
$clientMarks = Get-ClientLogMarks $names
$allow = Read-AllowList (Join-Path $PSScriptRoot "soak-allow.txt")

Wait-AllInMenu $names
$joinTimes = Connect-ScaleInstances $names
$Ctx.Result.notes += "join times (s): $(($joinTimes.Keys | ForEach-Object { "$_ $($joinTimes[$_])" }) -join ', ')"
Send-ServerCommand "money add 2000000"
$models = @(Get-BaseGameCars $names[0])
$shuffled = @($models | Select-Object -Skip 1 | Sort-Object { $rng.Next() })
$models = @($models[0]) + @($shuffled | Select-Object -First 4)
$lifterCount = [math]::Max(1, @(Send-HarnessCommand -Instance $names[0] -Verb lifters).Count)
$homeSpot = (Send-HarnessCommand -Instance $names[0] -Verb dump).local.position
Write-Host "models: $($models -join ', '); lifters: $lifterCount; home $(Format-Position $homeSpot)"

$places = @("Entrance1", "Entrance2", "Entrance3", "CarLifter1", "CarLifter2")
$garageLoaders = 0..3
$itemIds = @("tarczaHamulcowa_1", "akumulator")
$movableTools = @("Welder", "Oilbin", "EngineCrane")

$results = @{}
$verbStats = @{}
$script:stepNo = 0
$loopStart = Get-Date
function Elapsed { [math]::Round(((Get-Date) - $loopStart).TotalSeconds, 1) }

function Resolve-Template([string]$Template) {
    [regex]::Replace($Template, '\{step:(\d+):(\w+)\}', {
        param($m)
        $r = $results[[int]$m.Groups[1].Value]
        if ($null -eq $r) { throw "step $($m.Groups[1].Value) has no result" }
        "$($r.($m.Groups[2].Value))"
    })
}

# Issues one verb and logs it. "args" is the template when the arguments refer to an earlier step's result, so a
# replay logs the same text; "sent" is what was sent.
function Invoke-Step([string]$Actor, [string]$Verb, [string]$Arguments = "", [string]$Template = "", [string]$Action = "", [int]$Step = 0) {
    $script:stepNo = if ($Step -gt 0) { $Step } else { $script:stepNo + 1 }
    $entry = [ordered]@{ step = $script:stepNo; t = (Elapsed); actor = $Actor; verb = $Verb; args = $(if ($Template) { $Template } else { $Arguments }); sent = $Arguments; action = $Action; ok = $false; error = $null; ms = 0 }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $result = $null
    try {
        if ($Template) { $Arguments = Resolve-Template $Template; $entry.sent = $Arguments }
        $result = Send-HarnessCommand -Instance $Actor -Verb $Verb -Arguments $Arguments -TimeoutSec 30
        $results[$script:stepNo] = $result
        $entry.ok = $true
    } catch {
        $entry.error = ($_.Exception.Message -replace "^Harness command '[^']+' failed on \w+ : ", "")
    }
    $entry.ms = $watch.ElapsedMilliseconds
    Add-JsonLine $actionsFile $entry
    if (-not $verbStats.ContainsKey($Verb)) { $verbStats[$Verb] = @{ n = 0; errors = 0 } }
    $verbStats[$Verb].n++
    if (-not $entry.ok) { $verbStats[$Verb].errors++ }
    return $result
}

function Add-Marker([string]$Kind, $Data) {
    Add-JsonLine $actionsFile ([ordered]@{ step = 0; t = (Elapsed); actor = ""; verb = $Kind; args = ($Data | ConvertTo-Json -Compress -Depth 4); ok = $true })
}

# --- session model -------------------------------------------------------------------------------------------
$open = New-Object System.Collections.Generic.List[object]
$pending = New-Object System.Collections.Generic.List[object]
$seated = @{}
$partCache = @{}
$script:netBusy = $null
$script:away = $null
$script:tireUid = 0
$script:jobActive = $null
$expectedAway = @{}

function Get-Placement([string]$Actor) { @(Send-HarnessCommand -Instance $Actor -Verb placement) }
function Test-Ready([string]$Actor, [int]$Loader) { $r = Send-HarnessCommand -Instance $Actor -Verb car-ready -Arguments "$Loader"; $r.state -eq "Ready" -and $r.loaded }
function Get-JobLoaders([string]$Actor) { @((Send-HarnessCommand -Instance $Actor -Verb dump).jobs.active | ForEach-Object { [int]$_.carLoaderID }) }
function Get-OpenCount([int]$Loader) { @($open | Where-Object { $_.Loader -eq $Loader }).Count }
function Pick($Items) { $list = @($Items); if ($list.Count -eq 0) { $null } else { $list[$rng.Next($list.Count)] } }
function Add-Pending([double]$InSeconds, [string]$Actor, [string]$Verb, [string]$Arguments, [string]$Kind, [string]$Action = "") {
    $pending.Add([pscustomobject]@{ Due = (Get-Date).AddSeconds($InSeconds); Actor = $Actor; Verb = $Verb; Args = $Arguments; Kind = $Kind; Action = $Action })
}

function Get-ReadyCars([string]$Actor) {
    @(Get-Placement $Actor | Where-Object { $seated.Values -notcontains $_.loader } | Where-Object { Test-Ready $Actor $_.loader })
}

function Get-EligibleActors {
    @($names | Where-Object { $_ -ne $script:away -and (Test-InGarage (Get-HarnessStatus $_)) })
}

function Invoke-Parts([string]$Actor) {
    $cars = @(Get-ReadyCars $Actor | Where-Object { (Get-OpenCount $_.loader) -lt 3 })
    $car = Pick $cars
    if (-not $car) { return $false }
    $key = ""
    if ($rng.NextDouble() -lt 0.5) {
        $cache = $partCache[$car.loader]
        if (-not $cache -or $cache.Car -ne $car.carToLoad) {
            $dumpCar = @((Send-HarnessCommand -Instance $Actor -Verb dump).cars | Where-Object { $_.index -eq $car.loader })[0]
            $cache = @{ Car = $car.carToLoad; Keys = @($dumpCar.subParts | Where-Object { -not $_.unmounted -and -not $_.blocked } | ForEach-Object { $_.key }) }
            $partCache[$car.loader] = $cache
        }
        $openKeys = @($open | ForEach-Object { $_.Key })
        $key = Pick @($cache.Keys | Where-Object { $openKeys -notcontains $_ })
    }
    $r = Invoke-Step $Actor part-fast-unmount ("$($car.loader) $key".Trim()) -Action "parts"
    if ($r) { $open.Add([pscustomobject]@{ Loader = $car.loader; Car = $car.carToLoad; Key = $r.key; Step = $script:stepNo; By = $Actor; Due = (Get-Date).AddSeconds($rng.Next(2, 21)) }) }
    return $true
}

function Invoke-DueMounts([switch]$All) {
    foreach ($part in @($open | Where-Object { $All -or $_.Due -le (Get-Date) })) {
        $open.Remove($part) | Out-Null
        $actors = @(Get-EligibleActors)
        if ($actors.Count -eq 0) { continue }
        $actor = if ($actors -contains $part.By -and $rng.NextDouble() -lt 0.7) { $part.By } else { Pick $actors }
        $still = @(Get-Placement $actor | Where-Object { $_.loader -eq $part.Loader -and $_.carToLoad -eq $part.Car }).Count -eq 1
        if (-not $still) { continue }
        Invoke-Step $actor part-fast-mount "$($part.Loader) $($part.Key)" -Template "$($part.Loader) {step:$($part.Step):key}" -Action "parts" | Out-Null
    }
}

function Invoke-Cars([string]$Actor) {
    $placement = @(Get-Placement $Actor)
    $used = @($placement | ForEach-Object { [int]$_.loader })
    $free = @($garageLoaders | Where-Object { $used -notcontains $_ })
    if ($free.Count -gt 0 -and ($placement.Count -lt 2 -or ($placement.Count -lt 4 -and $rng.NextDouble() -lt 0.6))) {
        Invoke-Step $Actor car-spawn "$(Pick $free) $(Pick $models) 0 auto" -Action "cars" | Out-Null
        return $true
    }
    if ($placement.Count -le 2) { return $false }
    $jobLoaders = Get-JobLoaders $Actor
    $car = Pick @($placement | Where-Object { $jobLoaders -notcontains [int]$_.loader -and (Get-OpenCount $_.loader) -eq 0 -and $seated.Values -notcontains $_.loader })
    if (-not $car) { return $false }
    Invoke-Step $Actor car-delete "$($car.loader)" -Action "cars" | Out-Null
    return $true
}

function Invoke-Placement([string]$Actor) {
    if ($rng.NextDouble() -lt 0.5) {
        Invoke-Step $Actor lift "$($rng.Next($lifterCount)) $(Pick @('up', 'down'))" -Action "placement" | Out-Null
        return $true
    }
    $placement = @(Get-Placement $Actor)
    $taken = @($placement | ForEach-Object { $_.inPlace } | Where-Object { $_ })
    $place = Pick @($places | Where-Object { $taken -notcontains $_ })
    $car = Pick @(Get-ReadyCars $Actor)
    if (-not $place -or -not $car) { return $false }
    Invoke-Step $Actor car-move "$($car.loader) $place" -Action "placement" | Out-Null
    return $true
}

function Invoke-Parking([string]$Actor) {
    $parking = Send-HarnessCommand -Instance $Actor -Verb parking
    $placement = @(Get-Placement $Actor)
    $used = @($placement | ForEach-Object { [int]$_.loader })
    $free = @($garageLoaders | Where-Object { $used -notcontains $_ })
    $slots = @($parking.slots)
    $canPark = $placement.Count -gt 2 -and $slots.Count -lt [int]$parking.max
    $canUnpark = $slots.Count -gt 0 -and $placement.Count -lt 4 -and $free.Count -gt 0
    if ($canPark -and (-not $canUnpark -or $rng.NextDouble() -lt 0.5)) {
        $jobLoaders = Get-JobLoaders $Actor
        $car = Pick @(Get-ReadyCars $Actor | Where-Object { $jobLoaders -notcontains [int]$_.loader -and (Get-OpenCount $_.loader) -eq 0 })
        if (-not $car) { return $false }
        Invoke-Step $Actor park "$($car.loader)" -Action "parking" | Out-Null
        return $true
    }
    if (-not $canUnpark) { return $false }
    Invoke-Step $Actor unpark "$((Pick $slots).index) $(Pick $free)" -Action "parking" | Out-Null
    return $true
}

function Invoke-Economy([string]$Actor) {
    switch ($rng.Next(4)) {
        0 { Invoke-Step $Actor stats-add "$($rng.Next(1, 21)) $($rng.Next(0, 201))" -Action "economy" | Out-Null }
        1 {
            $car = Pick @(Get-ReadyCars $Actor)
            if (-not $car) { return $false }
            Invoke-Step $Actor econ-fee "spill $($car.loader)" -Action "economy" | Out-Null
        }
        2 { Invoke-Step $Actor give-item ([string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0} {1:0.00}", (Pick $itemIds), 0.2 + 0.8 * $rng.NextDouble())) -Action "economy" | Out-Null }
        3 { Invoke-Step $Actor sell-item -Action "economy" | Out-Null }
    }
    return $true
}

function Invoke-Machines([string]$Actor) {
    if ($rng.NextDouble() -lt 0.3) {
        $tool = Pick $movableTools
        $withCar = @(Get-Placement $Actor | ForEach-Object { $_.inPlace } | Where-Object { $_ })
        $place = if ($withCar.Count -gt 0 -and $rng.NextDouble() -lt 0.6) { Pick $withCar } else { "default" }
        Invoke-Step $Actor tool-move "$tool $place" -Action "machines" | Out-Null
        return $true
    }
    if ($script:tireUid -eq 0) {
        $group = Invoke-Step $Actor give-group "wheel" -Action "machines"
        if (-not $group) { return $true }
        $put = Invoke-Step $Actor tool-put "TireChanger $($group.UID)" -Template "TireChanger {step:$($script:stepNo):UID}" -Action "machines"
        if ($put) { $script:tireUid = [long]$group.UID }
    } else {
        $taken = Invoke-Step $Actor tool-take "TireChanger" -Action "machines"
        if ($taken -and -not $taken.refused) { $script:tireUid = 0 }
    }
    return $true
}

function Invoke-Details([string]$Actor) {
    $car = Pick @(Get-ReadyCars $Actor)
    if (-not $car) { return $false }
    Invoke-Step $Actor cardetails-randomize "$($car.loader)" -Action "details" | Out-Null
    return $true
}

function Invoke-Presence([string]$Actor) {
    if ($seated.ContainsKey($Actor)) {
        if ($rng.NextDouble() -lt 0.5) { Invoke-Step $Actor engine (Pick @("on", "off")) -Action "presence" | Out-Null }
        else {
            Invoke-Step $Actor engine "off" -Action "presence" | Out-Null
            Invoke-Step $Actor stand -Action "presence" | Out-Null
            $seated.Remove($Actor)
        }
        return $true
    }
    if ($rng.NextDouble() -lt 0.6) {
        $target = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:0.00},{1:0.00},{2:0.00},{3}",
            $homeSpot.x + 6 * $rng.NextDouble() - 3, $homeSpot.y, $homeSpot.z + 6 * $rng.NextDouble() - 3, $rng.Next(360))
        Invoke-Step $Actor teleport $target -Action "presence" | Out-Null
        return $true
    }
    $car = Pick @(Get-ReadyCars $Actor | Where-Object { (Get-OpenCount $_.loader) -eq 0 })
    if (-not $car) { return $false }
    if (Invoke-Step $Actor sit "$($car.loader) $(Pick @('left', 'right'))" -Action "presence") { $seated[$Actor] = [int]$car.loader }
    return $true
}

function Invoke-Jobs([string]$Actor) {
    if ($script:jobActive) {
        $r = Invoke-Step $Actor job-finish "$($script:jobActive)" -Action "jobs"
        $script:jobActive = $null
        return $true
    }
    $generator = @($names | Where-Object { (Get-HarnessStatus $_).isOrderGenerator } | Select-Object -First 1)
    $orders = @((Send-HarnessCommand -Instance $Actor -Verb orders-list).jobs | Where-Object { -not $_.IsMission })
    if ($orders.Count -gt 0 -and @(Get-Placement $Actor).Count -lt 4 -and $rng.NextDouble() -lt 0.6) {
        $order = Pick $orders
        if (Invoke-Step $Actor orders-accept "$($order.id)" -Action "jobs") { $script:jobActive = $order.id }
        return $true
    }
    if ($generator.Count -eq 0) { return $false }
    Invoke-Step $generator[0] orders-generate -Action "jobs" | Out-Null
    return $true
}

function Invoke-Travel([string]$Actor) {
    if ($script:away -or $seated.ContainsKey($Actor)) { return $false }
    if (-not (Invoke-Step $Actor travel "Junkyard" -Action "travel")) { return $true }
    $script:away = $Actor
    Add-Pending ($rng.Next(20, 61)) $Actor travel "Garage" "travel" "travel"
    return $true
}

function Invoke-Network([string]$Actor) {
    if ($script:netBusy) { return $false }
    $script:netBusy = $Actor
    if ($rng.NextDouble() -lt 0.5) {
        Invoke-Step $Actor net-delay "$($rng.Next(0, 251))" -Action "network" | Out-Null
        Add-Pending ($rng.Next(20, 61)) $Actor net-delay "0" "network" "network"
    } else {
        Invoke-Step $Actor net-hold "on" -Action "network" | Out-Null
        Add-Pending ($rng.Next(2, 6)) $Actor net-hold "off" "network" "network"
    }
    return $true
}

$catalogue = @(
    @{ Weight = 25; Name = "parts"; Run = ${function:Invoke-Parts} }
    @{ Weight = 10; Name = "cars"; Run = ${function:Invoke-Cars} }
    @{ Weight = 8; Name = "placement"; Run = ${function:Invoke-Placement} }
    @{ Weight = 8; Name = "parking"; Run = ${function:Invoke-Parking} }
    @{ Weight = 8; Name = "economy"; Run = ${function:Invoke-Economy} }
    @{ Weight = 6; Name = "machines"; Run = ${function:Invoke-Machines} }
    @{ Weight = 5; Name = "details"; Run = ${function:Invoke-Details} }
    @{ Weight = 5; Name = "presence"; Run = ${function:Invoke-Presence} }
    @{ Weight = 3; Name = "jobs"; Run = ${function:Invoke-Jobs} }
    @{ Weight = 2; Name = "travel"; Run = ${function:Invoke-Travel} }
    @{ Weight = 5; Name = "network"; Run = ${function:Invoke-Network} }
)
$totalWeight = ($catalogue | ForEach-Object { $_.Weight } | Measure-Object -Sum).Sum

function Invoke-RandomAction {
    $actors = @(Get-EligibleActors)
    if ($actors.Count -eq 0) { return }
    for ($try = 0; $try -lt 4; $try++) {
        $roll = $rng.Next($totalWeight)
        $row = $null
        foreach ($candidate in $catalogue) { if ($roll -lt $candidate.Weight) { $row = $candidate; break }; $roll -= $candidate.Weight }
        $actor = Pick $actors
        if ($seated.ContainsKey($actor) -and $row.Name -ne "presence") { continue }
        try { if (& $row.Run $actor) { return } }
        catch { Write-Host "driver ($($row.Name) on $actor): $($_.Exception.Message)" }
    }
}

function Invoke-DuePending([switch]$All) {
    foreach ($item in @($pending | Where-Object { $All -or $_.Due -le (Get-Date) } | Sort-Object Due)) {
        $pending.Remove($item) | Out-Null
        Invoke-Step $item.Actor $item.Verb $item.Args -Action $item.Action | Out-Null
        if ($item.Kind -eq "network") { $script:netBusy = $null }
        if ($item.Kind -eq "travel") {
            try { Wait-InGarage $item.Actor 240 | Out-Null } catch { Write-Host "$($item.Actor) did not get back to the garage: $($_.Exception.Message)" }
            $script:away = $null
        }
    }
}

# --- checkpoints, sampling, rules ----------------------------------------------------------------------------
$script:checkpointIndex = 0
$script:capSold = 0
$desyncSeen = @{}
function Invoke-Quiesce {
    Invoke-DuePending -All
    Invoke-DueMounts -All
    foreach ($name in @($seated.Keys)) {
        Invoke-Step $name engine "off" -Action "quiesce" | Out-Null
        Invoke-Step $name stand -Action "quiesce" | Out-Null
    }
    $seated.Clear()
    foreach ($name in $names) {
        try { Send-HarnessCommand -Instance $name -Verb net-delay -Arguments "0" | Out-Null; Send-HarnessCommand -Instance $name -Verb net-hold -Arguments "off" | Out-Null } catch { }
    }
    $script:netBusy = $null
}

function Test-ConfirmedDesyncs {
    foreach ($line in @(Find-ServerLogLines -ServerDir $Ctx.ServerDir -Since $scenarioStart -Pattern "\[Desync\] .*(resending|is persistent|confirmed \(autofix off\))")) {
        if ($desyncSeen.ContainsKey($line)) { continue }
        $desyncSeen[$line] = $true
        Add-Failure 2 "confirmed desync: $line"
    }
}

function Invoke-Checkpoint([string]$Label = "") {
    Invoke-Quiesce
    Add-Marker "checkpoint" @{ index = $script:checkpointIndex + 1; label = $Label }
    $script:checkpointIndex++
    $checkpoint = Invoke-ScaleCheckpoint -Ctx $Ctx -Index $script:checkpointIndex -Label $Label -Instances $names
    if (-not $checkpoint.Passed) { Add-Failure 1 "checkpoint $($script:checkpointIndex): $($checkpoint.Record.problems -join '; ')" }
    Test-ConfirmedDesyncs
    if ($checkpoint.Dumps) {
        $first = $checkpoint.Dumps[$names[0]]
        $script:tireUid = [long]$first.tools.TireChanger.uid
        $active = @($first.jobs.active)
        $script:jobActive = if ($active.Count -gt 0) { $active[0].id } else { $null }
        if (-not $replaySteps -and $InventoryCap -gt 0 -and $Label -ne "end") { Invoke-InventoryCap @($first.inventory.items).Count }
    }
    $partCache.Clear()
}

function Invoke-InventoryCap([int]$Items) {
    $excess = $Items - $InventoryCap
    if ($excess -le 0) { return }
    $actor = Pick (Get-EligibleActors)
    if (-not $actor) { return }
    Write-Host "inventory $Items items > cap $InventoryCap; $actor sells $excess"
    for ($i = 0; $i -lt $excess; $i++) {
        if (-not (Invoke-Step $actor sell-item -Action "inventory-cap")) { break }
        $script:capSold++
    }
}

$script:watchdogReason = $null
$script:lastSample = [datetime]::MinValue
$script:lastFrames = [datetime]::MinValue
$reported = @{}
function Invoke-Sampling {
    $now = Get-Date
    if (($now - $script:lastSample).TotalSeconds -ge 10) {
        $script:lastSample = $now
        $sample = Add-PerfSample -RunDir $runDir -Instances $names -ServerDir $Ctx.ServerDir
        $reason = Test-PerfWatchdog $sample
        if ($reason) {
            $script:watchdogReason = $reason
            $Ctx.Result.notes += "$reason; last sample: $($sample | ConvertTo-Json -Compress)"
            Add-Failure 6 $reason
            $script:stop = $true
        }
        foreach ($name in $names) {
            if ($expectedAway.ContainsKey($name)) { continue }
            $s = Get-HarnessStatus $name
            if ($s -and -not $s.connected -and -not $reported.ContainsKey($name)) {
                $reported[$name] = $true
                Add-Failure 3 "$name left the session ($($s.lastDisconnect.reason): $($s.lastDisconnect.message))"
                try { Connect-ScaleInstance $name | Out-Null; $reported.Remove($name) } catch { Write-Host "reconnect $name`: $($_.Exception.Message)" }
            }
        }
    }
    if (($now - $script:lastFrames).TotalSeconds -ge 30) {
        $script:lastFrames = $now
        Add-FrameSample -RunDir $runDir -Instances $names | Out-Null
    }
}

$script:stormIndex = 0
function Invoke-Storm([string]$Kind = "", [int]$StormSeed = 0) {
    if (-not (Get-Command Invoke-StormKind -ErrorAction SilentlyContinue)) { Write-Host "storms need StormKinds.psm1"; return }
    Invoke-Quiesce
    if (-not $Kind) { $Kind = Pick (Get-StormKinds -InstanceCount $names.Count) }
    if ($StormSeed -eq 0) { $StormSeed = $rng.Next(1, [int]::MaxValue) }
    $script:stormIndex++
    Add-Marker "storm" @{ index = $script:stormIndex; kind = $Kind; seed = $StormSeed }
    foreach ($name in $names) { $expectedAway[$name] = $true }
    try {
        $storm = Invoke-StormKind -Ctx $Ctx -Kind $Kind -Index $script:stormIndex -Seed $StormSeed
        if (-not $storm.passed) { Add-Failure 5 "storm $($script:stormIndex) $Kind`: $($storm.failures -join '; ')" }
    } catch {
        Add-Failure 5 "storm $($script:stormIndex) $Kind`: $($_.Exception.Message)"
    } finally {
        $expectedAway.Clear()
        $reported.Clear()
    }
    foreach ($name in $names) {
        if (-not (Test-InGarage (Get-HarnessStatus $name))) { try { Connect-ScaleInstance $name | Out-Null } catch { Add-Failure 3 "$name could not rejoin after the storm: $($_.Exception.Message)" } }
    }
    $script:tireUid = [long](Send-HarnessCommand -Instance $names[0] -Verb dump).tools.TireChanger.uid
    $script:jobActive = $null
    $partCache.Clear()
}

# --- main loop -----------------------------------------------------------------------------------------------
if (-not $replaySteps) { Invoke-Checkpoint "start" }
$end = $loopStart.AddMinutes($Minutes)
$nextCheck = (Get-Date).AddMinutes($CheckEveryMinutes)
$nextStorm = if ($StormEveryMinutes -gt 0) { (Get-Date).AddMinutes($StormEveryMinutes) } else { [datetime]::MaxValue }

if ($replaySteps) {
    $previousT = 0.0
    foreach ($entry in $replaySteps) {
        if ($script:stop) { break }
        $wait = [math]::Min(10.0, [math]::Max(0.0, [double]$entry.t - $previousT))
        $previousT = [double]$entry.t
        $until = (Get-Date).AddSeconds($wait)
        while ((Get-Date) -lt $until) { Invoke-Sampling; Start-Sleep -Milliseconds 500 }
        switch ($entry.verb) {
            "checkpoint" { $data = $entry.args | ConvertFrom-Json; Invoke-Checkpoint $data.label }
            "storm" { $data = $entry.args | ConvertFrom-Json; Invoke-Storm $data.kind ([int]$data.seed) }
            default {
                $template = if ($entry.args -match '\{step:\d+:\w+\}') { $entry.args } else { "" }
                $arguments = if ($template) { "" } else { "$($entry.args)" }
                Invoke-Step $entry.actor $entry.verb $arguments -Template $template -Action $entry.action -Step ([int]$entry.step) | Out-Null
                if ($entry.verb -eq "travel" -and $entry.args -eq "Garage") { try { Wait-InGarage $entry.actor 240 | Out-Null } catch { } }
            }
        }
        Invoke-Sampling
    }
} else {
    while (-not $script:stop -and (Get-Date) -lt $end) {
        Invoke-Sampling
        if ($script:stop) { break }
        if ((Get-Date) -ge $nextStorm) {
            Invoke-Storm
            $nextStorm = (Get-Date).AddMinutes($StormEveryMinutes)
            Invoke-Checkpoint "after-storm"
            $nextCheck = (Get-Date).AddMinutes($CheckEveryMinutes)
            continue
        }
        if ((Get-Date) -ge $nextCheck) {
            Invoke-Checkpoint
            $nextCheck = (Get-Date).AddMinutes($CheckEveryMinutes)
            continue
        }
        Invoke-DuePending
        Invoke-DueMounts
        Invoke-RandomAction
        $until = (Get-Date).AddMilliseconds($rng.Next(2000, 5001))
        while ((Get-Date) -lt $until) { Start-Sleep -Milliseconds 250 }
    }
}

if (-not $script:watchdogReason -and -not $replaySteps) { Invoke-Checkpoint "end" }
Test-ConfirmedDesyncs
foreach ($line in @(Find-LogErrors -Ctx $Ctx -Since $scenarioStart -ClientMarks $clientMarks -Allow $allow | Select-Object -First 20)) { Add-Failure 4 $line }

$steps = $script:stepNo
$errorRates = @($verbStats.Keys | Sort-Object | ForEach-Object { "$_ $($verbStats[$_].errors)/$($verbStats[$_].n)" })
$noisy = @($verbStats.Keys | Where-Object { $verbStats[$_].n -ge 3 -and $verbStats[$_].errors / $verbStats[$_].n -gt 0.2 })
$coveredActions = @(Get-Content -LiteralPath $actionsFile | ForEach-Object { ($_ | ConvertFrom-Json).action } | Where-Object { $_ } | Select-Object -Unique)
$missingActions = @($catalogue | ForEach-Object { $_.Name } | Where-Object { $coveredActions -notcontains $_ })
$Ctx.Result.notes += "seed $Seed, $steps steps, $($script:checkpointIndex) checkpoints, $($script:stormIndex) storms; replay: -ScenarioArgs @{ Replay = '$actionsFile' }"
$Ctx.Result.notes += "verb errors: $($errorRates -join ', ')"
if ($script:capSold -gt 0) { $Ctx.Result.notes += "inventory cap $InventoryCap`: sold $($script:capSold) items after checkpoints" }
if ($noisy.Count -gt 0) { $Ctx.Result.notes += "verbs over 20 % errors (driver bug or a real block): $($noisy -join ', ')" }
if ($missingActions.Count -gt 0) { $Ctx.Result.notes += "action rows not covered: $($missingActions -join ', ')" }
$Ctx.Result.notes += @($failures | Select-Object -First 30)
if ($failures.Count -gt 30) { $Ctx.Result.notes += "... and $($failures.Count - 30) more failures" }
$Ctx.Result.passed = ($failures.Count -eq 0)

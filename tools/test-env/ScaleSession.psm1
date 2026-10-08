# Helpers for the scale-lane scenarios (scale-connect, soak, storm, latejoin-full): sessions of two to four clients,
# server config for a run, quiesced checkpoints with N-way dump equality and a forced digest round, and the failure
# rules of design D6. They use HarnessClient.psm1 and TestLanes.psm1 as Run-Session.ps1 imported them.

$script:TestRoot = "$env:USERPROFILE\CMS21-TestInstalls"
$script:CheckpointDirs = @{}
$script:Invariant = [Globalization.CultureInfo]::InvariantCulture

function Add-JsonLine([string]$Path, $Object) {
    [System.IO.File]::AppendAllText($Path, ($Object | ConvertTo-Json -Depth 12 -Compress) + "`r`n")
}

function Test-InGarage($Status) { $Status -and $Status.connectionValid -and $Status.syncAcked -and $Status.scene -eq "garage" -and $Status.playable }

function Wait-InGarage([string]$Instance, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Instance -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition { param($s) Test-InGarage $s }
}

function Wait-InMenu([string]$Instance, [int]$TimeoutSec = 60) {
    try {
        Wait-HarnessStatus -Instance $Instance -TimeoutSec $TimeoutSec -What "menu, disconnected" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected }
    } catch { $null }
}

function Wait-AllInMenu([string[]]$Instances, [int]$TimeoutSec = 300) {
    foreach ($name in $Instances) {
        Wait-HarnessStatus -Instance $name -TimeoutSec $TimeoutSec -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
    }
}

# Dismisses a message left in the multiplayer menu (server lost, refused); harmless when there is none.
function Clear-MenuMessage([string]$Instance) {
    try { Send-HarnessCommand -Instance $Instance -Verb mp-ui -Arguments "ok" | Out-Null } catch { }
}

# Connects one client and waits for the garage. A refusal as DuplicateIdentity (the server still holds the slot of a
# killed game until its heartbeat timeout) is retried for -DuplicateRetrySec. Returns the seconds to the garage.
function Connect-ScaleInstance {
    param([string]$Instance, [int]$FpsCap = 30, [int]$DuplicateRetrySec = 20, [int]$TimeoutSec = 300)
    $started = Get-Date
    $retryUntil = $started.AddSeconds($DuplicateRetrySec)
    while ($true) {
        Clear-MenuMessage $Instance
        Connect-HarnessInstance $Instance
        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        $refused = $null
        while ((Get-Date) -lt $deadline) {
            $s = Get-HarnessStatus $Instance
            if (Test-InGarage $s) { break }
            if ($s -and $s.joinStatus -eq "Failed" -and -not $s.connected) { $refused = $s; break }
            Start-Sleep -Milliseconds 500
        }
        if ($refused) {
            if ($refused.lastDisconnect.reason -eq "DuplicateIdentity" -and (Get-Date) -lt $retryUntil) {
                Write-Host "$Instance refused as a duplicate identity; retrying"
                Start-Sleep -Seconds 2
                continue
            }
            throw "$Instance could not join: $($refused.lastDisconnect.reason) $($refused.lastDisconnect.message)"
        }
        if (-not (Test-InGarage (Get-HarnessStatus $Instance))) { throw "$Instance did not reach the garage within $TimeoutSec s" }
        break
    }
    $elapsed = ((Get-Date) - $started).TotalSeconds
    if ($FpsCap -gt 0) { Send-HarnessCommand -Instance $Instance -Verb fps-cap -Arguments "$FpsCap" | Out-Null }
    Send-HarnessCommand -Instance $Instance -Verb guard-set -Arguments "Off" | Out-Null
    return $elapsed
}

function Connect-ScaleInstances([string[]]$Instances, [int]$FpsCap = 30) {
    $times = [ordered]@{}
    foreach ($name in $Instances) {
        $times[$name] = [math]::Round((Connect-ScaleInstance -Instance $name -FpsCap $FpsCap), 1)
        Write-Host "$name is in the garage after $($times[$name]) s"
    }
    return $times
}

function Get-PlayerIds([string[]]$Instances) {
    $ids = [ordered]@{}
    foreach ($name in $Instances) { $ids[$name] = [int](Get-HarnessStatus $name).playerId }
    return $ids
}

# Sets keys in the lane server's server_config.ini (Run-Session restores the file after the run); the server reads
# it at start, so callers restart it afterwards.
function Set-ServerConfigValues([string]$ServerDir, [hashtable]$Values) {
    $config = Join-Path $ServerDir "server_config.ini"
    $lines = @(Get-Content -LiteralPath $config)
    foreach ($key in $Values.Keys) {
        $value = $Values[$key]
        if ($value -is [bool]) { $value = if ($value) { "True" } else { "False" } }
        $pattern = "^\s*$([regex]::Escape($key))\s*="
        if (@($lines | Where-Object { $_ -match $pattern }).Count -gt 0) { $lines = @($lines | ForEach-Object { if ($_ -match $pattern) { "$key = $value" } else { $_ } }) }
        else { $lines += "$key = $value" }
    }
    Set-Content -LiteralPath $config -Value $lines -Encoding ascii
}

function Restart-TestServer {
    Stop-TestServer
    Start-TestServer | Out-Null
}

function Wait-TestServerExit([int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-TestServerProcesses) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    return -not (Get-TestServerProcesses)
}

# The player records the server's "players" command lists (one "<key> '<name>' last seen ..." line each).
function Get-ServerPlayerRecords([int]$TimeoutSec = 10) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "players"
    Wait-ServerLog -Pattern "\] Players:" -After $mark -TimeoutSec $TimeoutSec | Out-Null
    Start-Sleep -Milliseconds 700
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark)
    $start = [array]::FindIndex([string[]]$lines, [Predicate[string]] { param($l) $l -match "\] Players:" })
    $records = @()
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "\]\s+(\S+) '(.*)' last seen (.*)$") { $records += [pscustomobject]@{ Key = $Matches[1]; Name = $Matches[2]; Rest = $Matches[3] } }
        elseif ($lines[$i] -match "\]\s+\(none\)|no records") { break }
        elseif ($records.Count -gt 0) { break }
    }
    return $records
}

# For each loaded car of each client: Ready and loaded (car-ready). Returns the problems.
function Wait-CarsReady([string[]]$Instances, [int]$TimeoutSec = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $problems = @()
    do {
        $problems = @()
        foreach ($name in $Instances) {
            $loaders = @((Send-HarnessCommand -Instance $name -Verb placement) | ForEach-Object { $_.loader })
            foreach ($loader in $loaders) {
                $r = Send-HarnessCommand -Instance $name -Verb car-ready -Arguments "$loader"
                if (-not ($r.state -eq "Ready" -and $r.loaded)) { $problems += "$name loader $loader is $($r.state) (loaded $($r.loaded))" }
            }
        }
        if ($problems.Count -eq 0) { return @() }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    return $problems
}

# Forces one digest round ("desync check", verbose) and returns per (client, key) what the server logged for every
# digest key (state-merges-and-contention D11: the global keys plus cars:L and car-details:L of every loaded car),
# plus the problems: a mismatch, or no line within -TimeoutSec.
function Invoke-ForcedDigestCheck([string[]]$Instances, [int]$TimeoutSec = 20) {
    $ids = @($Instances | ForEach-Object { $s = Get-HarnessStatus $_; if (Test-InGarage $s) { [int]$s.playerId } })
    $keys = @("world", "inventory", "car-placement", "workshop-tools", "warehouse", "garage", "jobs")
    $loaders = @(try { Send-HarnessCommand -Instance $Instances[0] -Verb placement | ForEach-Object { [int]$_.loader } } catch { })
    foreach ($loader in $loaders) { $keys += "cars:$loader"; $keys += "car-details:$loader" }
    $mark = Get-ServerLogMark
    Send-ServerCommand "desync check"
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $seen = @{}
    $mismatches = @()
    $notReady = @()
    do {
        Start-Sleep -Milliseconds 700
        foreach ($line in @(Get-ServerLogLines | Select-Object -Skip $mark)) {
            if ($line -match "\[Desync\] (\S+) for client (\d+): (match|not ready|mismatch)") {
                $seen["$($Matches[2])|$($Matches[1])"] = $Matches[3]
                if ($Matches[3] -eq "mismatch") { $mismatches += $line }
                if ($Matches[3] -eq "not ready") { $notReady += "$($Matches[1]) for client $($Matches[2])" }
            }
        }
        $missing = @(foreach ($id in $ids) { foreach ($key in $keys) { if (-not $seen.ContainsKey("$id|$key")) { "$key for client $id" } } })
    } while ($missing.Count -gt 0 -and (Get-Date) -lt $deadline)
    $problems = @($mismatches | Select-Object -Unique) + @($missing | ForEach-Object { "no digest result: $_" })
    [pscustomobject]@{
        ok = $problems.Count -eq 0
        clients = $ids
        results = @($seen.Keys | Sort-Object | ForEach-Object { "$_=$($seen[$_])" })
        notReady = @($notReady | Select-Object -Unique)
        problems = $problems
    }
}

# A projection of the shared sections without the per-session counters (car revision, spawn sequence, sync state)
# and without the open orders, which the generator replaces over time: for comparing a session with itself across a
# server restart.
function Get-StableSections($Dump, [string[]]$Sections = (Get-SharedDumpSections)) {
    $result = [ordered]@{}
    foreach ($section in $Sections) {
        $value = $Dump.$section
        if ($section -eq "cars" -and $value) {
            $value = @($value | ForEach-Object { $_ | Select-Object -Property * -ExcludeProperty revision, spawnSeq, syncState })
        }
        if ($section -eq "jobs" -and $value) { $value = [pscustomobject]@{ active = $value.active } }
        $result[$section] = $value
    }
    return $result
}

function Compare-StableDumps($Left, $Right, [string[]]$Sections = (Get-SharedDumpSections)) {
    $a = Get-StableSections $Left $Sections
    $b = Get-StableSections $Right $Sections
    @(foreach ($section in $Sections) {
        if (($a[$section] | ConvertTo-Json -Depth 10 -Compress) -ne ($b[$section] | ConvertTo-Json -Depth 10 -Compress)) { $section }
    })
}

function Save-Dumps($Dumps, [string]$Dir) {
    if (-not $Dumps) { return }
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null
    foreach ($name in $Dumps.Keys) { $Dumps[$name] | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Dir "dump_$name.json") -Encoding utf8 }
}

# Quiesced checkpoint (design D5): every client in the garage, every loaded car Ready everywhere, no part claim,
# 5 s settle, N-way equal shared sections within -TimeoutSec, then a forced digest round. Appends the record to
# checkpoints.jsonl. Dumps are kept for the first and every failed checkpoint and for the latest one (the previous
# kept-only-as-latest folder is deleted when the next checkpoint is written).
function Invoke-ScaleCheckpoint {
    param($Ctx, [int]$Index, [string]$Label = "", [string[]]$Instances = $Ctx.Instances, [int]$TimeoutSec = 60,
        [string[]]$Sections = (Get-SharedDumpSections), [switch]$NoDigest)
    $started = Get-Date
    $problems = @()
    $notInGarage = @($Instances | Where-Object { -not (Test-InGarage (Get-HarnessStatus $_)) })
    foreach ($name in $notInGarage) {
        try { Wait-InGarage $name 120 | Out-Null } catch { $problems += "$name is not in the garage: $($_.Exception.Message)" }
    }
    $problems += @(Wait-CarsReady $Instances)
    Start-Sleep -Seconds 5
    $dumps = $null
    $differ = @()
    try {
        $dumps = Wait-HarnessDumpsAllEqual -Instances $Instances -Sections $Sections -TimeoutSec $TimeoutSec
    } catch {
        $dumps = Get-LastHarnessDumps
        $differ = @(if ($_.Exception.Message -match "differ: (.*)\)$") { $Matches[1] -split ', ' } else { $_.Exception.Message })
        $problems += "dumps differ after $TimeoutSec s: $($differ -join ', ')"
    }
    if ($dumps) {
        $first = $dumps[$Instances[0]]
        $claims = @($first.cars | ForEach-Object { $car = $_; @($car.claims) | Where-Object { $_ } | ForEach-Object { "loader $($car.index) $($_.key) by $($_.owner)" } })
        if ($claims.Count -gt 0) { $problems += "part claims still held: $($claims -join '; ')" }
    }
    $digest = if ($NoDigest) { $null } else { Invoke-ForcedDigestCheck $Instances }
    if ($digest -and -not $digest.ok) { $problems += @($digest.problems | ForEach-Object { "digest: $_" }) }

    $passed = $problems.Count -eq 0
    $dir = Join-Path $Ctx.RunDir ("checkpoint_{0:D3}{1}" -f $Index, $(if ($Label) { "_$Label" } else { "" }))
    Save-Dumps $dumps $dir
    $state = $script:CheckpointDirs[$Ctx.RunDir]
    if ($state -and -not $state.Keep -and (Test-Path -LiteralPath $state.Dir)) { Remove-Item -LiteralPath $state.Dir -Recurse -Force }
    $script:CheckpointDirs[$Ctx.RunDir] = @{ Dir = $dir; Keep = (-not $state) -or (-not $passed) }

    $record = [ordered]@{
        index = $Index; label = $Label; t = $started.ToString("s"); durationS = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
        instances = $Instances; passed = $passed; differ = $differ; problems = $problems
        digest = if ($digest) { [ordered]@{ ok = $digest.ok; results = $digest.results; notReady = $digest.notReady } } else { $null }
        cars = if ($dumps) { @($dumps[$Instances[0]].placement.cars).Count } else { $null }
        parked = if ($dumps) { @($dumps[$Instances[0]].placement.parking.slots).Count } else { $null }
        items = if ($dumps) { @($dumps[$Instances[0]].inventory.items).Count } else { $null }
    }
    Add-JsonLine (Join-Path $Ctx.RunDir "checkpoints.jsonl") $record
    Write-Host ("CHECKPOINT {0}{1}: {2} in {3} s{4}" -f $Index, $(if ($Label) { " ($Label)" }), $(if ($passed) { "equal" } else { "FAILED" }), $record.durationS, $(if ($passed) { "" } else { " - " + ($problems -join "; ") }))
    [pscustomobject]@{ Record = $record; Passed = $passed; Dumps = $dumps; Dir = $dir }
}

function Get-ClientLogPath([string]$Instance) { Join-Path $script:TestRoot "$Instance\MelonLoader\Latest.log" }

function Get-ClientLogMarks([string[]]$Instances) {
    $marks = @{}
    foreach ($name in $Instances) {
        $path = Get-ClientLogPath $name
        $marks[$name] = if (Test-Path -LiteralPath $path) { [pscustomobject]@{ Length = (Get-Item -LiteralPath $path).Length; Created = (Get-Item -LiteralPath $path).CreationTimeUtc } } else { $null }
    }
    return $marks
}

function Read-FileFrom([string]$Path, [long]$Offset) {
    try {
        $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
        try {
            if ($Offset -gt $stream.Length) { $Offset = 0 }
            $stream.Position = $Offset
            $reader = New-Object System.IO.StreamReader($stream)
            return @($reader.ReadToEnd() -split "\r?\n")
        } finally { $stream.Dispose() }
    } catch { return @() }
}

# Allow-list lines: "<regex> | <reason> | <owner>"; # starts a comment.
function Read-AllowList([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    @(Get-Content -LiteralPath $Path | Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith("#") } | ForEach-Object { ($_ -split '\|')[0].Trim() })
}

# Every server start writes its own Log\Log_<time>.txt (Latest.txt is the current one), so a scenario that restarts
# the server reads all of them.
function Get-ServerLogFiles([string]$ServerDir, [datetime]$Since) {
    @(Get-ChildItem -LiteralPath (Join-Path $ServerDir "Log") -Filter "Log_*.txt" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $Since } | Sort-Object Name)
}

function Find-ServerLogLines([string]$ServerDir, [datetime]$Since, [string]$Pattern) {
    foreach ($file in (Get-ServerLogFiles $ServerDir $Since)) {
        foreach ($line in @(Read-FileFrom $file.FullName 0)) { if ($line -match $Pattern) { $line } }
    }
}

# Rule 4 of design D6: server [ERROR] lines (every server log since -Since), client [ERROR] lines and
# HarmonyExceptions written since the client marks, except those an allow-list pattern matches. Logs of killed games
# copied to the run folder (client_<X>_<n>.log) are read whole.
function Find-LogErrors {
    param($Ctx, [datetime]$Since, $ClientMarks, [string[]]$Allow = @())
    $allowed = { param($line) foreach ($pattern in $Allow) { if ($line -match $pattern) { return $true } }; return $false }
    $found = @()
    foreach ($line in @(Find-ServerLogLines $Ctx.ServerDir $Since "\[ERROR\]")) {
        if (-not (& $allowed $line)) { $found += "server: $line" }
    }
    foreach ($name in $Ctx.Instances) {
        $path = Get-ClientLogPath $name
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $mark = $ClientMarks[$name]
        $offset = if ($mark -and (Get-Item -LiteralPath $path).CreationTimeUtc -eq $mark.Created) { $mark.Length } else { 0 }
        foreach ($line in @(Read-FileFrom $path $offset)) {
            if ($line -match "\[ERROR\]|HarmonyException" -and -not (& $allowed $line)) { $found += "client $name`: $line" }
        }
    }
    foreach ($copy in @(Get-ChildItem -LiteralPath $Ctx.RunDir -Filter "client_*_*.log" -File -ErrorAction SilentlyContinue)) {
        foreach ($line in @(Get-Content -LiteralPath $copy.FullName)) {
            if ($line -match "\[ERROR\]|HarmonyException" -and -not (& $allowed $line)) { $found += "$($copy.Name): $line" }
        }
    }
    return $found
}

function Format-Position($Position) {
    [string]::Format($script:Invariant, "{0},{1},{2}", $Position.x, $Position.y, $Position.z)
}

function Get-Distance($p, $q) { [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }

# Garage cars models: the game's car list minus DLC and mod cars, car_boltatlanta first (every scenario uses it).
function Get-BaseGameCars([string]$Instance) {
    $dlc = @(Send-HarnessCommand -Instance $Instance -Verb car-dlc-cars | ForEach-Object { $_.car })
    $all = @(Send-HarnessCommand -Instance $Instance -Verb car-list | Where-Object { -not $_.mod -and $_.id -notin $dlc } | ForEach-Object { $_.id })
    @("car_boltatlanta") + @($all | Where-Object { $_ -ne "car_boltatlanta" } | Sort-Object)
}

Export-ModuleMember -Function Add-JsonLine, Test-InGarage, Wait-InGarage, Wait-InMenu, Wait-AllInMenu, Clear-MenuMessage,
    Connect-ScaleInstance, Connect-ScaleInstances, Get-PlayerIds, Set-ServerConfigValues, Restart-TestServer,
    Wait-TestServerExit, Get-ServerPlayerRecords, Wait-CarsReady, Invoke-ForcedDigestCheck, Get-StableSections,
    Compare-StableDumps, Save-Dumps, Invoke-ScaleCheckpoint, Get-ClientLogPath, Get-ClientLogMarks, Read-FileFrom,
    Read-AllowList, Get-ServerLogFiles, Find-ServerLogLines, Find-LogErrors, Format-Position, Get-Distance, Get-BaseGameCars

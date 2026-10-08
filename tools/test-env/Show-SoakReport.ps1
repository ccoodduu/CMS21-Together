#Requires -Version 5.1
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$RunDir,
    [int]$Top = 10,
    [double]$SlopeSkipMinutes = 10,
    [double]$BaselineMinutes = 10,
    [string]$AllowList = ""
)

$ErrorActionPreference = "Stop"
if (-not $AllowList) { $AllowList = Join-Path $PSScriptRoot "scenarios\soak-allow.txt" }
$RunDir = (Resolve-Path -LiteralPath $RunDir).Path
$KB = 1024.0
$MB = 1024.0 * 1024.0

$Budgets = [ordered]@{
    avgDownloadKBs = 50; peakDownloadKBs = 1024; avgUploadKBs = 20; serverCpuPct = 25
    handlerMaxMs = 50; lockWaitMaxMs = 100; lateJoinS = 60; gamePrivateSlopeMbPerH = 200
}

function Read-JsonLines([string]$Name) {
    $path = Join-Path $RunDir $Name
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    @(Get-Content -LiteralPath $path -Encoding UTF8 | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
}

function Read-CsvFile([string]$Name) {
    $path = Join-Path $RunDir $Name
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    @(Import-Csv -LiteralPath $path)
}

function Get-Number($Value) {
    if ($null -eq $Value -or "$Value" -eq "") { return $null }
    [double]::Parse("$Value", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-Max([object[]]$Values) {
    $numbers = @($Values | Where-Object { $null -ne $_ })
    if ($numbers.Count -eq 0) { return $null }
    ($numbers | Measure-Object -Maximum).Maximum
}

function Get-Average([object[]]$Values) {
    $numbers = @($Values | Where-Object { $null -ne $_ })
    if ($numbers.Count -eq 0) { return $null }
    ($numbers | Measure-Object -Average).Average
}

function Get-Percentile([object[]]$Values, [double]$Percent) {
    $numbers = @($Values | Where-Object { $null -ne $_ } | Sort-Object)
    if ($numbers.Count -eq 0) { return $null }
    $numbers[[math]::Min($numbers.Count - 1, [math]::Max(0, [math]::Ceiling($numbers.Count * $Percent / 100) - 1))]
}

function Get-SlopePerHour($Points) {
    $points = @($Points | Where-Object { $null -ne $_.Y })
    $late = @($points | Where-Object { $_.X -ge $SlopeSkipMinutes * 60 })
    if ($late.Count -ge 3) { $points = $late }
    if ($points.Count -lt 2) { return $null }
    $n = $points.Count
    $meanX = ($points | Measure-Object X -Average).Average
    $meanY = ($points | Measure-Object Y -Average).Average
    $sxy = 0.0; $sxx = 0.0
    foreach ($p in $points) { $sxy += ($p.X - $meanX) * ($p.Y - $meanY); $sxx += ($p.X - $meanX) * ($p.X - $meanX) }
    if ($sxx -eq 0) { return $null }
    $sxy / $sxx * 3600
}

function Get-GrowthPerHour($Points) {
    $points = @($Points | Where-Object { $null -ne $_.Y })
    if ($points.Count -lt 2) { return $null }
    $grown = 0.0
    for ($i = 1; $i -lt $points.Count; $i++) {
        $delta = $points[$i].Y - $points[$i - 1].Y
        if ($delta -ge 0) { $grown += $delta } else { $grown += $points[$i].Y }
    }
    $hours = ($points[-1].X - $points[0].X) / 3600
    if ($hours -le 0) { return $null }
    $grown / $hours
}

function Round-To($Value, [int]$Digits = 1) { if ($null -eq $Value) { $null } else { [math]::Round([double]$Value, $Digits) } }

# Samples: Key (a PID or ""), Age (uptime in s, or elapsed s when -AgeFromFirstSample), Time, PrivateMb, SaveBytes.
# A new process starts where the key changes or the uptime goes back.
function Get-ServerProcesses($Samples, [switch]$AgeFromFirstSample) {
    $segments = New-Object System.Collections.Generic.List[object]
    $current = $null
    foreach ($sample in $Samples) {
        if (-not $current -or "$($sample.Key)" -ne "$($current.Key)" -or ($null -ne $sample.Age -and $sample.Age -lt $current.LastAge)) {
            $current = [pscustomobject]@{ Key = $sample.Key; LastAge = $sample.Age; Samples = New-Object System.Collections.Generic.List[object] }
            $segments.Add($current)
        }
        $current.Samples.Add($sample)
        $current.LastAge = $sample.Age
    }
    $index = 0
    foreach ($segment in $segments) {
        $index++
        $s = $segment.Samples.ToArray()
        $offset = if ($AgeFromFirstSample) { $s[0].Age } else { 0 }
        $base = @($s | Where-Object { $_.Age - $offset -ge $BaselineMinutes * 60 } | Select-Object -First 1)
        $ratio = if ($base.Count -and $base[0].PrivateMb -gt 0) { $s[-1].PrivateMb / $base[0].PrivateMb } else { $null }
        [pscustomobject][ordered]@{
            process = $index
            pid = $segment.Key
            started = $s[0].Time
            lifeMin = Round-To (($s[-1].Age - $offset) / 60)
            privateStartMb = Round-To $s[0].PrivateMb
            privateBaseMb = if ($base.Count) { Round-To $base[0].PrivateMb } else { $null }
            privateEndMb = Round-To $s[-1].PrivateMb
            privateMaxMb = Round-To (Get-Max @($s | ForEach-Object { $_.PrivateMb }))
            endPerBase = Round-To $ratio 3
            saveBaseMb = if ($base.Count -and $null -ne $base[0].SaveBytes) { Round-To ($base[0].SaveBytes / $MB) 2 } else { $null }
            saveEndMb = if ($null -ne $s[-1].SaveBytes) { Round-To ($s[-1].SaveBytes / $MB) 2 } else { $null }
        }
    }
}

function New-Verdict([string]$Name, $Value, $Limit, [string]$Unit, [string]$Kind = "budget") {
    $verdict = if ($null -eq $Value) { "n/a" } elseif ($Value -le $Limit) { "PASS" } elseif ($Kind -eq "rule") { "FAIL" } else { "WARN" }
    [pscustomobject][ordered]@{ name = $Name; value = Round-To $Value 2; limit = $Limit; unit = $Unit; verdict = $verdict }
}

$summary = [ordered]@{ runDir = $RunDir; generated = (Get-Date).ToString("s") }

$perfLines = Read-JsonLines "perf_server.jsonl"
$snapshotKeys = @("snapshot-build", "AskForSync")
$elapsed = 0.0
$serverPoints = foreach ($line in $perfLines) {
    $elapsed += [double]$line.intervalS
    $inSnapshot = ([int]$line.clients.syncing -gt 0) -or [bool]@($line.timings | Where-Object { $snapshotKeys -contains $_.key }).Count
    [pscustomobject]@{ X = $elapsed; Line = $line; InSnapshot = $inSnapshot }
}
$serverDuration = $elapsed

if ($perfLines.Count -gt 0) {
    $privateAt10 = @($serverPoints | Where-Object { $_.X -ge 600 } | Select-Object -First 1)
    $summary.server = [ordered]@{
        lines = $perfLines.Count
        durationMin = Round-To ($serverDuration / 60)
        cpuAvgPct = Round-To (($perfLines | ForEach-Object { [double]$_.cpuPct * [double]$_.intervalS } | Measure-Object -Sum).Sum / [math]::Max($serverDuration, 0.001))
        cpuP95Pct = Round-To (Get-Percentile @($perfLines | ForEach-Object { [double]$_.cpuPct }) 95)
        privateStartMb = Round-To ($perfLines[0].privateBytes / $MB)
        privateAt10MinMb = if ($privateAt10.Count) { Round-To ($privateAt10[0].Line.privateBytes / $MB) } else { $null }
        privateEndMb = Round-To ($perfLines[-1].privateBytes / $MB)
        privateSlopeMbPerH = Round-To (Get-SlopePerHour @($serverPoints | ForEach-Object { [pscustomobject]@{ X = $_.X; Y = $_.Line.privateBytes / $MB } }))
        managedHeapSlopeMbPerH = Round-To (Get-SlopePerHour @($serverPoints | ForEach-Object { [pscustomobject]@{ X = $_.X; Y = $_.Line.managedHeap / $MB } }))
        threadsMax = Get-Max @($perfLines | ForEach-Object { $_.threads })
        handlesMax = Get-Max @($perfLines | ForEach-Object { $_.handles })
        gcEnd = $perfLines[-1].gc
        lastSave = $perfLines[-1].save
        logGrowthMbPerH = Round-To ((Get-GrowthPerHour @($serverPoints | ForEach-Object { [pscustomobject]@{ X = $_.X; Y = $_.Line.logBytes / $MB } })))
        desyncRecordsAdded = [int]$perfLines[-1].desyncRecords - [int]$perfLines[0].desyncRecords
    }

    $slots = @{}
    foreach ($point in $serverPoints) {
        $interval = [math]::Max([double]$point.Line.intervalS, 0.001)
        foreach ($row in $point.Line.slots) {
            $key = [int]$row.slot
            if (-not $slots.ContainsKey($key)) { $slots[$key] = [ordered]@{ slot = $key; down = 0.0; up = 0.0; downMsgs = 0; upMsgs = 0; peakDown = 0.0; peakUp = 0.0; peakDownAll = 0.0 } }
            $s = $slots[$key]
            $s.down += $row.down; $s.up += $row.up; $s.downMsgs += $row.downMsgs; $s.upMsgs += $row.upMsgs
            $s.peakDownAll = [math]::Max($s.peakDownAll, $row.down / $interval)
            if (-not $point.InSnapshot) {
                $s.peakDown = [math]::Max($s.peakDown, $row.down / $interval)
                $s.peakUp = [math]::Max($s.peakUp, $row.up / $interval)
            }
        }
    }
    $summary.clients = @($slots.Keys | Sort-Object | Where-Object { $_ -gt 0 } | ForEach-Object {
        $s = $slots[$_]
        [pscustomobject][ordered]@{
            slot = $s.slot
            avgDownKBs = Round-To ($s.down / $serverDuration / $KB) 2; peakDownKBs = Round-To ($s.peakDown / $KB) 2; peakDownWithSnapshotsKBs = Round-To ($s.peakDownAll / $KB) 2
            avgUpKBs = Round-To ($s.up / $serverDuration / $KB) 2; peakUpKBs = Round-To ($s.peakUp / $KB) 2
            totalDownMb = Round-To ($s.down / $MB) 2; totalUpMb = Round-To ($s.up / $MB) 2; downMsgs = $s.downMsgs; upMsgs = $s.upMsgs
        }
    })

    $types = @{}
    foreach ($line in $perfLines) {
        foreach ($row in $line.traffic) {
            if (-not $types.ContainsKey($row.type)) { $types[$row.type] = [ordered]@{ type = $row.type; sentMsgs = 0; sentBytes = 0.0; recvMsgs = 0; recvBytes = 0.0 } }
            $t = $types[$row.type]
            if ($row.dir -eq "sent") { $t.sentMsgs += $row.msgs; $t.sentBytes += $row.bytes } else { $t.recvMsgs += $row.msgs; $t.recvBytes += $row.bytes }
        }
    }
    $summary.packetTypes = @($types.Values | Sort-Object { $_.sentBytes + $_.recvBytes } -Descending | Select-Object -First $Top | ForEach-Object {
        [pscustomobject][ordered]@{ type = $_.type; sentMsgs = $_.sentMsgs; sentMb = Round-To ($_.sentBytes / $MB) 3; recvMsgs = $_.recvMsgs; recvMb = Round-To ($_.recvBytes / $MB) 3 }
    })

    $timings = @{}
    foreach ($point in $serverPoints) {
        foreach ($row in $point.Line.timings) {
            if (-not $timings.ContainsKey($row.key)) {
                $timings[$row.key] = [ordered]@{ key = $row.key; n = 0; runMs = 0.0; runMaxMs = 0.0; runP99MaxMs = 0.0; waitMaxMs = 0.0; waitP99MaxMs = 0.0; waitMaxOutsideSnapshotsMs = 0.0 }
            }
            $t = $timings[$row.key]
            $t.n += $row.n; $t.runMs += $row.runMs
            $t.runMaxMs = [math]::Max($t.runMaxMs, $row.runMaxMs); $t.runP99MaxMs = [math]::Max($t.runP99MaxMs, $row.runP99Ms)
            $t.waitMaxMs = [math]::Max($t.waitMaxMs, $row.waitMaxMs); $t.waitP99MaxMs = [math]::Max($t.waitP99MaxMs, $row.waitP99Ms)
            if (-not $point.InSnapshot) { $t.waitMaxOutsideSnapshotsMs = [math]::Max($t.waitMaxOutsideSnapshotsMs, $row.waitMaxMs) }
        }
    }
    $summary.timings = @($timings.Values | Sort-Object { $_.runMs } -Descending | Select-Object -First $Top | ForEach-Object {
        [pscustomobject][ordered]@{
            key = $_.key; n = $_.n; runTotalMs = Round-To $_.runMs; runAvgMs = Round-To ($_.runMs / [math]::Max($_.n, 1)) 3; runMaxMs = Round-To $_.runMaxMs 2
            runP99MaxMs = Round-To $_.runP99MaxMs 2; waitMaxMs = Round-To $_.waitMaxMs 2; waitP99MaxMs = Round-To $_.waitP99MaxMs 2
        }
    })
    $outside = @($timings.Values | Where-Object { $snapshotKeys -notcontains $_.key })
    $handlerMax = Get-Max @($outside | ForEach-Object { $_.runMaxMs })
    $lockWaitMax = Get-Max @($outside | ForEach-Object { $_.waitMaxOutsideSnapshotsMs })
    $worstHandler = @($outside | Sort-Object { $_.runMaxMs } -Descending | Select-Object -First 1)
    $worstWait = @($outside | Sort-Object { $_.waitMaxOutsideSnapshotsMs } -Descending | Select-Object -First 1)
    $summary.worst = [ordered]@{
        handler = if ($worstHandler.Count) { "$($worstHandler[0].key) $(Round-To $worstHandler[0].runMaxMs 2) ms" } else { $null }
        lockWaitOutsideSnapshots = if ($worstWait.Count) { "$($worstWait[0].key) $(Round-To $worstWait[0].waitMaxOutsideSnapshotsMs 2) ms" } else { $null }
    }
}

$processRows = Read-CsvFile "perf_processes.csv"
$processDuration = 0.0
if ($processRows.Count -gt 0) {
    $processDuration = (Get-Number $processRows[-1].elapsedS) - (Get-Number $processRows[0].elapsedS)
    $roles = @($processRows[0].PSObject.Properties.Name | Where-Object { $_ -match '^(.+)_privateMb$' } | ForEach-Object { $_ -replace '_privateMb$', '' })
    $summary.system = [ordered]@{ samples = $processRows.Count; minCommitHeadroomGb = $null; minFreeRamGb = $null }
    $commit = @($processRows | ForEach-Object { Get-Number $_.commitHeadroomGb } | Where-Object { $null -ne $_ })
    $free = @($processRows | ForEach-Object { Get-Number $_.freeRamGb } | Where-Object { $null -ne $_ })
    if ($commit.Count) { $summary.system.minCommitHeadroomGb = ($commit | Measure-Object -Minimum).Minimum }
    if ($free.Count) { $summary.system.minFreeRamGb = ($free | Measure-Object -Minimum).Minimum }

    $summary.processes = @(foreach ($role in $roles) {
        $points = @($processRows | Where-Object { $_."${role}_running" -eq "1" } | ForEach-Object {
            [pscustomobject]@{ X = Get-Number $_.elapsedS; Y = Get-Number $_."${role}_privateMb"; W = Get-Number $_."${role}_workingMb"; Cpu = Get-Number $_."${role}_cpuPct"; H = Get-Number $_."${role}_handles" }
        })
        $logs = [ordered]@{}
        foreach ($column in @($processRows[0].PSObject.Properties.Name | Where-Object { $_ -like "${role}_*Bytes" })) {
            $logPoints = @($processRows | ForEach-Object { [pscustomobject]@{ X = Get-Number $_.elapsedS; Y = (Get-Number $_.$column) / $MB } })
            $logs[($column -replace "^${role}_", '') -replace 'Bytes$', 'GrowthMbPerH'] = Round-To (Get-GrowthPerHour $logPoints) 2
        }
        [pscustomobject][ordered]@{
            role = $role
            pids = @($processRows | Where-Object { $_."${role}_running" -eq "1" } | ForEach-Object { $_."${role}_pid" } | Select-Object -Unique).Count
            samples = $points.Count
            cpuAvgPct = Round-To (Get-Average @($points | ForEach-Object { $_.Cpu }))
            cpuP95Pct = Round-To (Get-Percentile @($points | ForEach-Object { $_.Cpu }) 95)
            privateStartMb = if ($points.Count) { $points[0].Y } else { $null }
            privateEndMb = if ($points.Count) { $points[-1].Y } else { $null }
            privateMaxMb = Get-Max @($points | ForEach-Object { $_.Y })
            privateSlopeMbPerH = Round-To (Get-SlopePerHour $points)
            workingSlopeMbPerH = Round-To (Get-SlopePerHour @($points | ForEach-Object { [pscustomobject]@{ X = $_.X; Y = $_.W } }))
            handlesMax = Get-Max @($points | ForEach-Object { $_.H })
            logs = $logs
        }
    })
}

$frameRows = Read-CsvFile "frames.csv"
if ($frameRows.Count -gt 0) {
    $summary.frames = @($frameRows | Group-Object instance | ForEach-Object {
        $ok = @($_.Group | Where-Object { -not $_.error })
        [pscustomobject][ordered]@{
            instance = $_.Name; samples = $ok.Count; errors = $_.Count - $ok.Count
            avgMs = Round-To (Get-Average @($ok | ForEach-Object { Get-Number $_.avgMs })) 2
            p95MaxMs = Round-To (Get-Max @($ok | ForEach-Object { Get-Number $_.p95Ms })) 2
            maxMs = Round-To (Get-Max @($ok | ForEach-Object { Get-Number $_.maxMs })) 2
            il2cppHeapEndMb = if ($ok.Count) { Round-To ((Get-Number $ok[-1].il2cppHeapUsed) / $MB) } else { $null }
        }
    })
}

$actions = Read-JsonLines "actions.jsonl"
if ($actions.Count -gt 0) {
    $perVerb = @($actions | Group-Object verb | ForEach-Object {
        $errors = @($_.Group | Where-Object { $_.ok -eq $false }).Count
        [pscustomobject][ordered]@{ verb = $_.Name; n = $_.Count; errors = $errors; errorRate = Round-To ($errors / $_.Count) 3 }
    } | Sort-Object n -Descending)
    $summary.actions = [ordered]@{
        steps = $actions.Count
        errors = @($actions | Where-Object { $_.ok -eq $false }).Count
        perVerb = $perVerb
        verbsOver20PctErrors = @($perVerb | Where-Object { $_.errorRate -gt 0.2 } | ForEach-Object { $_.verb })
    }
}

function Get-Failed($Entry) { ($Entry.passed -eq $false) -or ($Entry.ok -eq $false) }

$checkpoints = Read-JsonLines "checkpoints.jsonl"
if ($checkpoints.Count -gt 0) { $summary.checkpoints = [ordered]@{ n = $checkpoints.Count; failed = @($checkpoints | Where-Object { Get-Failed $_ }).Count } }

$storms = Read-JsonLines "storms.jsonl"
if ($storms.Count -gt 0) {
    $summary.storms = [ordered]@{
        n = $storms.Count
        failed = @($storms | Where-Object { Get-Failed $_ }).Count
        byKind = @($storms | Group-Object kind | ForEach-Object { [pscustomobject]@{ kind = $_.Name; n = $_.Count; failed = @($_.Group | Where-Object { Get-Failed $_ }).Count } })
    }
}

$lateJoinPath = Join-Path $RunDir "latejoin.json"
if (Test-Path -LiteralPath $lateJoinPath) { $summary.latejoin = Get-Content -LiteralPath $lateJoinPath -Raw | ConvertFrom-Json }

$serverLogs = @(Get-ChildItem -LiteralPath $RunDir -Filter "server*.log" -File -ErrorAction SilentlyContinue)
$joins = @(foreach ($log in $serverLogs) {
    foreach ($match in (Select-String -LiteralPath $log.FullName -Pattern 'Client\[(\d+)\] snapshot (\d+) acked after (\d+) ms, (\d+) bytes, built in ([\d.]+) ms')) {
        $g = $match.Matches[0].Groups
        [pscustomobject][ordered]@{ slot = [int]$g[1].Value; snapshot = [int]$g[2].Value; ackMs = [int]$g[3].Value; bytes = [long]$g[4].Value; buildMs = Get-Number $g[5].Value }
    }
})
if ($joins.Count -gt 0) { $summary.joins = $joins }

$allow = @()
if ($AllowList -and (Test-Path -LiteralPath $AllowList)) {
    $allow = @(Get-Content -LiteralPath $AllowList | Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith("#") } | ForEach-Object { ($_ -split '\|')[0].Trim() })
}
function Test-Allowed([string]$Line) { foreach ($pattern in $allow) { if ($Line -match $pattern) { return $true } }; return $false }

$desyncLines = @($serverLogs | ForEach-Object { Select-String -LiteralPath $_.FullName -Pattern '\[Desync\].*(resending|is persistent)' } | ForEach-Object { $_.Line })
$errorLines = @($serverLogs | ForEach-Object { Select-String -LiteralPath $_.FullName -Pattern '\[ERROR\]' } | ForEach-Object { $_.Line } | Where-Object { -not (Test-Allowed $_) })
$harmonyLines = @(Get-ChildItem -LiteralPath $RunDir -Filter "client_*.log" -File -ErrorAction SilentlyContinue |
    ForEach-Object { Select-String -LiteralPath $_.FullName -Pattern 'HarmonyException' } | ForEach-Object { $_.Line } | Where-Object { -not (Test-Allowed $_) })

$resultPath = Join-Path $RunDir "result.json"
$memoryAbort = $null
if (Test-Path -LiteralPath $resultPath) {
    $memoryAbort = @((Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json).notes | Where-Object { "$_" -match 'aborted: memory' }) | Select-Object -First 1
}

$serverProcesses = @()
if ($perfLines.Count -gt 0) {
    $saveBytes = $null
    $serverProcesses = @(Get-ServerProcesses @($serverPoints | ForEach-Object {
        if ($_.Line.save -and $null -ne $_.Line.save.bytes) { $saveBytes = [double]$_.Line.save.bytes }
        $age = if ($null -ne $_.Line.uptimeS) { [double]$_.Line.uptimeS } else { $_.X }
        [pscustomobject]@{ Key = ""; Age = $age; Time = $_.Line.t; PrivateMb = $_.Line.privateBytes / $MB; SaveBytes = $saveBytes }
    }))
    $summary.server.processes = $serverProcesses.Count
} elseif ($processRows.Count -gt 0 -and $processRows[0].PSObject.Properties.Name -contains "server_pid") {
    $serverProcesses = @(Get-ServerProcesses -AgeFromFirstSample @($processRows | Where-Object { $_.server_running -eq "1" } | ForEach-Object {
        [pscustomobject]@{ Key = $_.server_pid; Age = Get-Number $_.elapsedS; Time = $_.time; PrivateMb = Get-Number $_.server_privateMb; SaveBytes = $null }
    }))
}
if ($serverProcesses.Count) { $summary.serverProcesses = $serverProcesses }

$durationS = [math]::Max($serverDuration, $processDuration)
$longRun = $durationS -ge 3600
$rules = @(
    [pscustomobject][ordered]@{ rule = 1; name = "checkpoints equal"; verdict = if (-not $summary.checkpoints) { "n/a" } elseif ($summary.checkpoints.failed) { "FAIL" } else { "PASS" }; detail = if ($summary.checkpoints) { "$($summary.checkpoints.failed) of $($summary.checkpoints.n) failed" } else { "" } }
    [pscustomobject][ordered]@{ rule = 2; name = "no confirmed desync"; verdict = if (-not $serverLogs.Count) { "n/a" } elseif ($desyncLines.Count) { "FAIL" } else { "PASS" }; detail = @($desyncLines | Select-Object -First 3) -join " / " }
    [pscustomobject][ordered]@{ rule = 3; name = "no unexpected leave"; verdict = "n/a"; detail = "checked by the scenario" }
    [pscustomobject][ordered]@{ rule = 4; name = "no errors"; verdict = if (-not $serverLogs.Count) { "n/a" } elseif ($errorLines.Count + $harmonyLines.Count) { "FAIL" } else { "PASS" }; detail = "$($errorLines.Count) server [ERROR], $($harmonyLines.Count) HarmonyException (not on the allow-list)" }
    [pscustomobject][ordered]@{ rule = 5; name = "storms pass"; verdict = if (-not $summary.storms) { "n/a" } elseif ($summary.storms.failed) { "FAIL" } else { "PASS" }; detail = if ($summary.storms) { "$($summary.storms.failed) of $($summary.storms.n) failed" } else { "" } }
    [pscustomobject][ordered]@{ rule = 6; name = "no watchdog stop"; verdict = if ($memoryAbort) { "FAIL" } elseif (Test-Path -LiteralPath $resultPath) { "PASS" } else { "n/a" }; detail = "$memoryAbort" }
)
$rule7 = @()
if ($longRun) {
    $judged = @($serverProcesses | Where-Object { $null -ne $_.endPerBase })
    if ($judged.Count) {
        $worst = $judged | Sort-Object endPerBase -Descending | Select-Object -First 1
        $short = $serverProcesses.Count - $judged.Count
        $save = if ($null -ne $worst.saveBaseMb) { ", save $($worst.saveBaseMb) -> $($worst.saveEndMb) MB" } else { "" }
        $name = "server private end / minute $BaselineMinutes per process (worst of $($judged.Count)$(if ($short) { ", $short shorter not judged" }): #$($worst.process) $($worst.privateBaseMb) -> $($worst.privateEndMb) MB$save)"
        $rule7 += New-Verdict $name $worst.endPerBase 1.5 "x" "rule"
    }
    if ($summary.server) { $rule7 += New-Verdict "server Log/ growth" $summary.server.logGrowthMbPerH 50 "MB/h" "rule" }
    foreach ($process in @($summary.processes)) {
        if ($process.logs.Contains("latestLogGrowthMbPerH")) { $rule7 += New-Verdict "$($process.role) Latest.log growth" $process.logs["latestLogGrowthMbPerH"] 50 "MB/h" "rule" }
        if ($process.logs.Contains("logGrowthMbPerH")) { $rule7 += New-Verdict "$($process.role) log growth" $process.logs["logGrowthMbPerH"] 50 "MB/h" "rule" }
    }
}
$rules += [pscustomobject][ordered]@{
    rule = 7; name = "memory and logs over hours"
    verdict = if (-not $longRun) { "n/a" } elseif (@($rule7 | Where-Object verdict -eq "FAIL").Count) { "FAIL" } elseif ($rule7.Count) { "PASS" } else { "n/a" }
    detail = if ($longRun) { @($rule7 | ForEach-Object { "$($_.name) $($_.value) $($_.unit) ($($_.verdict))" }) -join "; " } else { "run shorter than 60 min" }
}
$summary.rules = $rules

$games = @($summary.processes | Where-Object { $_.role -ne "server" })
$budgetRows = @(
    New-Verdict "average download per client" (Get-Max @($summary.clients | ForEach-Object { $_.avgDownKBs })) $Budgets.avgDownloadKBs "kB/s"
    New-Verdict "peak 10 s download outside snapshots" (Get-Max @($summary.clients | ForEach-Object { $_.peakDownKBs })) $Budgets.peakDownloadKBs "kB/s"
    New-Verdict "average upload per client" (Get-Max @($summary.clients | ForEach-Object { $_.avgUpKBs })) $Budgets.avgUploadKBs "kB/s"
    New-Verdict "server CPU (one core = 100)" $summary.server.cpuAvgPct $Budgets.serverCpuPct "%"
    New-Verdict "handler max outside snapshots" $handlerMax $Budgets.handlerMaxMs "ms"
    New-Verdict "lock wait max outside snapshots" $lockWaitMax $Budgets.lockWaitMaxMs "ms"
    New-Verdict "late join (slowest SyncAck)" $(if ($joins.Count) { (Get-Max @($joins | ForEach-Object { $_.ackMs })) / 1000 } else { $null }) $Budgets.lateJoinS "s"
    New-Verdict "game private bytes slope" (Get-Max @($games | ForEach-Object { $_.privateSlopeMbPerH })) $Budgets.gamePrivateSlopeMbPerH "MB/h"
)
$summary.budgets = $budgetRows
$summary.verdict = if (@($rules | Where-Object verdict -eq "FAIL").Count) { "FAIL" } elseif (@($budgetRows | Where-Object verdict -eq "WARN").Count) { "PASS with WARN" } else { "PASS" }
$summary.durationMin = Round-To ($durationS / 60)

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $RunDir "summary.json") -Encoding utf8

Write-Host "Soak report: $RunDir ($($summary.durationMin) min)"
if ($summary.server) {
    $s = $summary.server
    Write-Host ("Server: CPU avg {0} % / p95 {1} %, private {2} -> {3} MB ({4} MB/h), Log/ {5} MB/h, last save {6} bytes" -f $s.cpuAvgPct, $s.cpuP95Pct, $s.privateStartMb, $s.privateEndMb, $s.privateSlopeMbPerH, $s.logGrowthMbPerH, $s.lastSave.bytes)
    Write-Host "Clients (kB/s; peak = 10 s window outside snapshots):"
    $summary.clients | Format-Table slot, avgDownKBs, peakDownKBs, avgUpKBs, peakUpKBs, totalDownMb, totalUpMb -AutoSize | Out-String -Width 200 | Write-Host
    Write-Host "Top packet types by bytes:"
    $summary.packetTypes | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    Write-Host "Timings (ms):"
    $summary.timings | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}
if ($serverProcesses.Count) {
    Write-Host "Server processes (rule 7 compares each with its own minute $BaselineMinutes; save = last save written so far):"
    $serverProcesses | Format-Table process, pid, started, lifeMin, privateStartMb, privateBaseMb, privateEndMb, privateMaxMb, endPerBase, saveBaseMb, saveEndMb -AutoSize | Out-String -Width 220 | Write-Host
}
if ($summary.processes) {
    Write-Host ("Processes (min commit headroom {0} GB, min free RAM {1} GB):" -f $summary.system.minCommitHeadroomGb, $summary.system.minFreeRamGb)
    $summary.processes | Format-Table role, pids, samples, cpuAvgPct, cpuP95Pct, privateStartMb, privateEndMb, privateSlopeMbPerH, workingSlopeMbPerH, handlesMax -AutoSize | Out-String -Width 200 | Write-Host
}
if ($summary.frames) { $summary.frames | Format-Table -AutoSize | Out-String -Width 200 | Write-Host }
if ($summary.joins) { Write-Host "Joins:"; $summary.joins | Format-Table -AutoSize | Out-String -Width 200 | Write-Host }
if ($summary.actions) { Write-Host ("Actions: {0} steps, {1} errors; over 20 % errors: {2}" -f $summary.actions.steps, $summary.actions.errors, ($summary.actions.verbsOver20PctErrors -join ", ")) }
Write-Host "Rules:"
$rules | Format-Table rule, name, verdict, detail -AutoSize | Out-String -Width 220 | Write-Host
Write-Host "Budgets:"
$budgetRows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
Write-Host "VERDICT: $($summary.verdict)"

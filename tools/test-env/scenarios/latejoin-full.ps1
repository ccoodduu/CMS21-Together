# run-all: lane 3
# areas: connect, persistence, cars, parts, placement, tools, jobs
# multiplayer-soak-and-scale D7 (tasks 5.1-5.3): late joins into a full garage and parking. The lane's server starts
# from the full-garage fixture (built here first when it is missing, outdated or -Rebuild; see full-garage-fixture).
# Measured -Repeats times each: (a) the first client joins the loaded session alone; (b) with every other client in
# the session and working, the last one joins; (c) the last one leaves and rejoins at once. Per join: time to
# SyncAck, snapshot bytes, build time and item counts (the server's ack line), the client's connect-to-acked and
# connect-to-playable, the working clients' max frame time, the server's max lock wait and the joining slot's
# traffic per packet type (perf log). Medians go to latejoin.json. Fails on a join over -FailSeconds or without an
# ack, or unequal clients afterwards; over -WarnSeconds is a WARN.
param($Ctx, [int]$ParkingLevels = 2, [switch]$Rebuild, [int]$Repeats = 3, [int]$WarnSeconds = 60, [int]$FailSeconds = 120, [int]$Seed = 0)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")
Import-Module (Join-Path $PSScriptRoot "..\PerfSampler.psm1")
Import-Module (Join-Path $PSScriptRoot "..\FullGarage.psm1")

$names = @($Ctx.Instances)
$first = $names[0]
$last = $names[-1]
$others = @($names | Select-Object -SkipLast 1)
if ($Seed -eq 0) { $Seed = [int](Get-Date -Format "MMddHHmmss") }
$rng = New-Object System.Random($Seed)
$failures = @()
$warnings = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

Set-ServerConfigValues $Ctx.ServerDir @{ perf_log_interval_seconds = 5 }
$scenarioStart = Get-Date
Restart-TestServer
$clientMarks = Get-ClientLogMarks $names
$version = Get-SaveVersionTag $Ctx.ServerDir
$fixture = Get-FullGarageFixturePath $ParkingLevels $version.Tag
$report = [ordered]@{ fixture = $fixture; versions = $version.Text; parkingLevels = $ParkingLevels; seed = $Seed; budget = @{ warnS = $WarnSeconds; failS = $FailSeconds } }
Wait-AllInMenu $names

$valid = (-not $Rebuild) -and [bool](Test-FullGarageFixture $Ctx.ServerDir $fixture)
if (-not $valid) {
    Write-Host "Building the fixture $fixture"
    Connect-ScaleInstance $first | Out-Null
    $fill = Invoke-FullGarageFill -Filler $first -ParkingLevels $ParkingLevels
    $report.fill = $fill
    Save-FullGarageFixture $Ctx.ServerDir $fixture $version.Text | Out-Null
    Send-HarnessCommand -Instance $first -Verb to-menu | Out-Null
    Wait-InMenu $first 60 | Out-Null
    Check ([bool](Test-FullGarageFixture $Ctx.ServerDir $fixture)) "--check-save loads the new fixture"
}
$report.fixtureBytes = (Get-Item -LiteralPath $fixture).Length
$install = Install-FullGarageFixture $Ctx.ServerDir $fixture
$report.serverStartS = $install.StartS
$report.serverLoad = $install.Loaded
$report.migrations = $install.Migrations
Write-Host "Server started from the fixture in $($install.StartS) s: $($install.Loaded)"
Check ($install.Loaded -match "Game session loaded from" -and $install.Loaded -notmatch "fallback|Cannot load") "the server loaded the fixture as its main save"
foreach ($name in $names) { Clear-MenuMessage $name }

function Get-PerfLines([datetime]$From, [datetime]$To) {
    foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $Ctx.ServerDir "Log") -Filter "perf_*.jsonl" -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $From })) {
        foreach ($line in @(Get-Content -LiteralPath $file.FullName)) {
            if (-not $line.Trim()) { continue }
            $entry = $line | ConvertFrom-Json
            $t = [datetime]::Parse($entry.t, [Globalization.CultureInfo]::InvariantCulture)
            if ($t -ge $From -and $t -le $To) { $entry }
        }
    }
}

function Invoke-Work([string[]]$Workers) {
    $worker = $Workers[$rng.Next($Workers.Count)]
    try {
        switch ($rng.Next(3)) {
            0 { Send-HarnessCommand -Instance $worker -Verb stats-add -Arguments "1 5" | Out-Null }
            1 { Send-HarnessCommand -Instance $worker -Verb give-item -Arguments "akumulator 0.5" | Out-Null }
            2 {
                $car = @(Send-HarnessCommand -Instance $worker -Verb placement) | Select-Object -First 1
                if ($car) {
                    $part = Send-HarnessCommand -Instance $worker -Verb part-fast-unmount -Arguments "$($car.loader)"
                    Send-HarnessCommand -Instance $worker -Verb part-fast-mount -Arguments "$($car.loader) $($part.key)" | Out-Null
                }
            }
        }
    } catch { }
}

# One measured join of -Joiner while -Workers keep acting.
function Measure-Join([string]$Label, [string]$Joiner, [string[]]$Workers) {
    $mark = Get-ServerLogMark
    $started = Get-Date
    Clear-MenuMessage $Joiner
    Connect-HarnessInstance $Joiner
    $acked = $null
    $playable = $null
    $refused = $null
    $frameMax = 0.0
    $nextWork = Get-Date
    $nextFrames = (Get-Date).AddSeconds(5)
    $deadline = $started.AddSeconds($FailSeconds + 60)
    while ((Get-Date) -lt $deadline) {
        $s = Get-HarnessStatus $Joiner
        if (-not $acked -and $s.syncAcked) { $acked = Get-Date }
        if (Test-InGarage $s) { $playable = Get-Date; if (-not $acked) { $acked = $playable }; break }
        if ($s -and $s.joinStatus -eq "Failed" -and -not $s.connected) { $refused = $s.lastDisconnect; break }
        if ($Workers.Count -gt 0 -and (Get-Date) -ge $nextWork) { Invoke-Work $Workers; $nextWork = (Get-Date).AddSeconds(2) }
        if ($Workers.Count -gt 0 -and (Get-Date) -ge $nextFrames) {
            foreach ($worker in $Workers) { try { $frameMax = [math]::Max($frameMax, [double](Send-HarnessCommand -Instance $worker -Verb perf).maxMs) } catch { } }
            $nextFrames = (Get-Date).AddSeconds(5)
        }
        Start-Sleep -Milliseconds 250
    }
    foreach ($worker in $Workers) { try { $frameMax = [math]::Max($frameMax, [double](Send-HarnessCommand -Instance $worker -Verb perf).maxMs) } catch { } }
    $ended = Get-Date
    $ackLine = $null
    try { $ackLine = Wait-ServerLog -Pattern "Client\[\d+\] snapshot \d+ acked after \d+ ms" -After $mark -TimeoutSec 10 } catch { }
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark)
    $noProgress = @($lines | Where-Object { $_ -match "(?i)no progress" })
    $slot = [int](Get-HarnessStatus $Joiner).playerId
    $row = [ordered]@{
        label = $Label; joiner = $Joiner; workers = $Workers; slot = $slot; t = $started.ToString("s")
        connectToAckedS = if ($acked) { [math]::Round(($acked - $started).TotalSeconds, 1) } else { $null }
        connectToPlayableS = if ($playable) { [math]::Round(($playable - $started).TotalSeconds, 1) } else { $null }
        refused = $refused; noProgress = $noProgress
        serverAckMs = $null; bytes = $null; buildMs = $null; items = $null
        othersMaxFrameMs = if ($Workers.Count -gt 0) { $frameMax } else { $null }
    }
    if ($ackLine -match "snapshot (\d+) acked after (\d+) ms, (\d+) bytes, built in ([\d.]+) ms(.*)$") {
        $row.serverAckMs = [int]$Matches[2]; $row.bytes = [long]$Matches[3]
        $row.buildMs = [double]::Parse($Matches[4], [Globalization.CultureInfo]::InvariantCulture); $row.items = $Matches[5].Trim(" ()")
    }
    Start-Sleep -Seconds 6
    $perf = @(Get-PerfLines $started.AddSeconds(-5) (Get-Date))
    $row.lockWaitMaxMs = (@($perf | ForEach-Object { $_.timings } | Where-Object { $_ } | ForEach-Object { [double]$_.waitMaxMs }) + 0 | Measure-Object -Maximum).Maximum
    $types = @{}
    foreach ($entry in @($perf | ForEach-Object { $_.slotTraffic } | Where-Object { $_ -and [int]$_.slot -eq $slot })) {
        $key = "$($entry.type) $($entry.dir)"
        if (-not $types.ContainsKey($key)) { $types[$key] = [long]0 }
        $types[$key] += [long]$entry.bytes
    }
    $row.slotTrafficTop = @($types.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 8 | ForEach-Object { "$($_.Key) $($_.Value)" })
    $seconds = if ($null -ne $row.serverAckMs) { $row.serverAckMs / 1000.0 } else { $row.connectToAckedS }
    Check ($null -ne $seconds -and $seconds -le $FailSeconds -and $noProgress.Count -eq 0 -and -not $refused) "$Label`: $Joiner acked its snapshot in $seconds s (limit $FailSeconds s; $($row.bytes) bytes, built in $($row.buildMs) ms$(if ($refused) { "; refused $($refused.reason)" }))"
    if ($null -ne $seconds -and $seconds -gt $WarnSeconds) { $script:warnings += "WARN $Label`: $seconds s > $WarnSeconds s" }
    if ($playable) {
        Send-HarnessCommand -Instance $Joiner -Verb fps-cap -Arguments "30" | Out-Null
        Send-HarnessCommand -Instance $Joiner -Verb guard-set -Arguments "Off" | Out-Null
        $inSession = @($names | Where-Object { Test-InGarage (Get-HarnessStatus $_) })
        try { Wait-HarnessDumpsAllEqual -Instances $inSession -TimeoutSec 90 | Out-Null; Check $true "$Label`: $($inSession -join ', ') agree after the join" }
        catch { Check $false "$Label`: $($_.Exception.Message)"; Save-Dumps (Get-LastHarnessDumps) (Join-Path $Ctx.RunDir "differ_$Label") }
    }
    Write-Host "JOIN $($row | ConvertTo-Json -Compress -Depth 4)"
    return [pscustomobject]$row
}

function Leave([string]$Name) {
    Send-HarnessCommand -Instance $Name -Verb to-menu | Out-Null
    Wait-InMenu $Name 90 | Out-Null
}

$joins = @()
for ($r = 1; $r -le $Repeats; $r++) {
    $joins += Measure-Join "a$r" $first @()
    Leave $first
    Start-Sleep -Seconds 5
}
foreach ($name in $others) { Connect-ScaleInstance $name | Out-Null }
for ($r = 1; $r -le $Repeats; $r++) {
    $joins += Measure-Join "b$r" $last $others
    Leave $last
    Start-Sleep -Seconds 5
}
$joins += Measure-Join "c0" $last $others
for ($r = 1; $r -le $Repeats; $r++) {
    Leave $last
    $joins += Measure-Join "c$r" $last $others
}

function Get-Median([object[]]$Values) {
    $sorted = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    if ($sorted.Count % 2) { $sorted[($sorted.Count - 1) / 2] } else { ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2 }
}
$medians = [ordered]@{}
foreach ($kind in "a", "b", "c") {
    $rows = @($joins | Where-Object { $_.label -match "^$kind[1-9]" })
    $medians[$kind] = [ordered]@{
        n = $rows.Count
        serverAckS = Get-Median @($rows | ForEach-Object { if ($null -ne $_.serverAckMs) { $_.serverAckMs / 1000.0 } })
        connectToAckedS = Get-Median @($rows | ForEach-Object { $_.connectToAckedS })
        connectToPlayableS = Get-Median @($rows | ForEach-Object { $_.connectToPlayableS })
        snapshotMb = Get-Median @($rows | ForEach-Object { if ($_.bytes) { [math]::Round($_.bytes / 1MB, 3) } })
        buildMs = Get-Median @($rows | ForEach-Object { $_.buildMs })
        lockWaitMaxMs = Get-Median @($rows | ForEach-Object { $_.lockWaitMaxMs })
        othersMaxFrameMs = Get-Median @($rows | ForEach-Object { $_.othersMaxFrameMs })
        topTraffic = @($rows | Select-Object -First 1 | ForEach-Object { $_.slotTrafficTop })
    }
}
$report.joins = $joins
$report.medians = $medians
$report.warnings = $warnings
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "latejoin.json") -Encoding utf8
Write-Host "Medians: $($medians | ConvertTo-Json -Compress -Depth 4)"

$errors = @(Find-LogErrors -Ctx $Ctx -Since $scenarioStart -ClientMarks $clientMarks -Allow (Read-AllowList (Join-Path $PSScriptRoot "soak-allow.txt")))
Check ($errors.Count -eq 0) "no errors outside the allow-list ($($errors.Count)$(if ($errors) { ': ' + (@($errors | Select-Object -First 5) -join ' / ') }))"
$Ctx.Result.notes += "medians: $($medians | ConvertTo-Json -Compress -Depth 4)"
$Ctx.Result.notes += $warnings
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

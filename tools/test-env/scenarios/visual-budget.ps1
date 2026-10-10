# run-all: lane 3
# areas: visuals
# remote-visual-feedback 7.3: the bandwidth and frame-time budget of the work visuals with every instance of the lane
# (two to four). Each client spawns nothing itself; the first client spawns one car per client (loaders 0..N-1) and
# every client then loops on its own car for -Minutes: vfx-unscrew a part to its end (bolts turning on the others),
# part-fast-mount it back (On ghost), vfx-tool with a hand tool for 3 s, vfx-tool none. The server's perf counters are
# reset before the loop and read after it. Checks (design D8): PlayerActivity upload <= 2.4 kB/s average per client,
# every client's total download <= 50 kB/s average, no activity packet dropped by the client cap or the server's
# 8/s limit, visuals.leaks 0 and no "[Visuals] state leak" line, all shared dump sections equal at the end. The
# measured bytes per packet type go to budget.json and the notes (for design.md "Measurements"). With graphics
# (-Visible, or -CompareFrames) a second loop of -OffMinutes runs with vfx-enable off on every client, and the frame
# time p95 with visuals is compared with it (WARN above 1.2x); headless games render nothing, so it is skipped there.
param(
    $Ctx,
    [double]$Minutes = 3,
    [double]$OffMinutes = 1,
    [switch]$CompareFrames,
    [string]$Car = "car_boltatlanta"
)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")

$names = @($Ctx.Instances)
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Note([string]$Message) { $script:Ctx.Result.notes += $Message; Write-Host "note: $Message" -ForegroundColor Yellow }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments -TimeoutSec 30 }
function Visuals([string]$Name) { (Cmd $Name dump).visuals }

$hands = @("OBD", "Multimeter", "Compression", "TireTreadDepthTester", "OilBayonet")
$activityUpLimit = 2.4 * 1024
$downLimit = 50 * 1024
# D8: 0.4-0.6 kB per PlayerActivity packet; over twice the upper end goes to QUESTIONS.md.
$activityEstimate = 0.6 * 1024
$headless = @($Ctx.Launch.Headless).Count -ge $names.Count
$compare = $CompareFrames -or -not $headless

$scenarioStart = Get-Date
$clientMarks = Get-ClientLogMarks $names
Wait-AllInMenu $names
$joinTimes = Connect-ScaleInstances $names
$Ctx.Result.notes += "join times (s): $(($joinTimes.Keys | ForEach-Object { "$_ $($joinTimes[$_])" }) -join ', ')"

# --- one car per client ---------------------------------------------------------------------------------------
$loaderOf = [ordered]@{}
for ($i = 0; $i -lt $names.Count; $i++) {
    $loaderOf[$names[$i]] = $i
    Cmd $names[0] car-spawn "$i $Car 0 auto" | Out-Null
    $notReady = @(Wait-CarsReady $names 180)
    if ($notReady.Count -gt 0) { throw "car on loader $i not Ready: $($notReady -join '; ')" }
}

$keysOf = @{}
foreach ($name in $names) {
    $loader = $loaderOf[$name]
    Cmd $name vfx-stand "$loader 1.5" | Out-Null
    $exclude = @(try { Cmd $name wheel-parts "$loader" | ForEach-Object { $_.key } } catch { }) + @(try { Cmd $name part-groups "$loader" | ForEach-Object { $_.key; $_.members } } catch { })
    $keys = @(Cmd $name vfx-parts "$loader" | Where-Object { $exclude -notcontains $_.key } | Select-Object -First 6 | ForEach-Object { $_.key })
    if ($keys.Count -eq 0) { throw "$name has no part to unscrew on loader $loader" }
    $keysOf[$name] = $keys
    Write-Host "$name works on loader $loader, parts $($keys -join ', ')"
}

# --- the work loop --------------------------------------------------------------------------------------------
# Per client: unscrew -> (finished) mount -> tool -> (3 s) none -> (1 s) next part. One step per client per pass.
$stats = @{}
$work = @{}
function Reset-Work {
    foreach ($name in $names) {
        $work[$name] = [pscustomobject]@{ Phase = "next"; Key = $null; Index = 0; Until = [datetime]::MinValue; Started = [datetime]::MinValue }
        $stats[$name] = @{ unscrews = 0; mounts = 0; tools = 0; errors = 0 }
    }
}

function Step-Client([string]$Name) {
    $w = $work[$Name]
    $loader = $loaderOf[$Name]
    $s = $stats[$Name]
    try {
        switch ($w.Phase) {
            "next" {
                $keys = $keysOf[$Name]
                $w.Key = $keys[$w.Index % $keys.Count]
                $w.Index++
                $r = Cmd $Name vfx-unscrew "$loader $($w.Key)"
                if ($r.blocked) { $s.errors++; $w.Phase = "tool"; return }
                $w.Phase = "unscrewing"
                $w.Started = Get-Date
            }
            "unscrewing" {
                $r = Cmd $Name vfx-unscrew "$loader $($w.Key) status"
                if ($r.state -eq "finished") { $s.unscrews++; $w.Phase = "mount"; return }
                if ($r.state -in @("timeout", "part not committed") -or ((Get-Date) - $w.Started).TotalSeconds -gt 60) {
                    Write-Host "$Name unscrew $($w.Key): $($r.state)"
                    $s.errors++
                    try { Cmd $Name vfx-unscrew "$loader $($w.Key) undo" | Out-Null } catch { }
                    $w.Phase = "tool"
                }
            }
            "mount" {
                Cmd $Name part-fast-mount "$loader $($w.Key)" | Out-Null
                $s.mounts++
                $w.Phase = "tool"
            }
            "tool" {
                Cmd $Name vfx-tool "$($hands[$w.Index % $hands.Count]) $loader" | Out-Null
                $s.tools++
                $w.Phase = "holding"
                $w.Until = (Get-Date).AddSeconds(3)
            }
            "holding" {
                if ((Get-Date) -lt $w.Until) { return }
                Cmd $Name vfx-tool "none" | Out-Null
                $w.Phase = "pause"
                $w.Until = (Get-Date).AddSeconds(1)
            }
            "pause" { if ((Get-Date) -ge $w.Until) { $w.Phase = "next" } }
        }
    } catch {
        $s.errors++
        Write-Host "$Name $($w.Phase): $($_.Exception.Message.Split("`n")[0])"
        $w.Phase = if ($w.Phase -eq "unscrewing") { "unscrewing" } else { "pause" }
        $w.Until = (Get-Date).AddSeconds(1)
    }
}

# Lets every running unscrew end, mounts what came off and puts the tools away.
function Stop-Work {
    $deadline = (Get-Date).AddSeconds(90)
    while (@($names | Where-Object { $work[$_].Phase -eq "unscrewing" }).Count -gt 0 -and (Get-Date) -lt $deadline) {
        foreach ($name in @($names | Where-Object { $work[$_].Phase -eq "unscrewing" })) { Step-Client $name }
        Start-Sleep -Milliseconds 500
    }
    foreach ($name in $names) {
        if ($work[$name].Phase -eq "mount") { Step-Client $name }
        try { Cmd $name vfx-tool "none" | Out-Null } catch { }
    }
}

function Invoke-WorkLoop([double]$LoopMinutes, [string]$Label) {
    Reset-Work
    $frames = @{}
    foreach ($name in $names) { $frames[$name] = New-Object System.Collections.Generic.List[double] }
    $end = (Get-Date).AddMinutes($LoopMinutes)
    $nextFrames = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $end) {
        foreach ($name in $names) { Step-Client $name }
        if ((Get-Date) -ge $nextFrames) {
            foreach ($name in $names) { try { $frames[$name].Add([double](Cmd $name perf).p95Ms) } catch { } }
            $nextFrames = (Get-Date).AddSeconds(10)
        }
        Start-Sleep -Milliseconds 200
    }
    Write-Host "$Label loop: $(($names | ForEach-Object { "$_ $($stats[$_].unscrews) unscrews, $($stats[$_].mounts) mounts, $($stats[$_].tools) tools, $($stats[$_].errors) errors" }) -join '; ')"
    return $frames
}

function Get-Median($Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return 0 }
    $sorted[[int][math]::Floor(($sorted.Count - 1) / 2)]
}

function Get-Bytes([double]$Value, [string]$Unit) { if ($Unit -eq "MB") { $Value * 1048576 } else { $Value * 1024 } }

$before = @{}
foreach ($name in $names) { $before[$name] = Visuals $name }
Send-ServerCommand "perf reset"
Start-Sleep -Seconds 1
$loopStart = Get-Date
$framesOn = Invoke-WorkLoop $Minutes "visuals on"
$elapsed = ((Get-Date) - $loopStart).TotalSeconds
$mark = Get-ServerLogMark
Send-ServerCommand "perf"
Send-ServerCommand "perf top 40"
$onStats = @{}
foreach ($name in $names) { $onStats[$name] = $stats[$name].Clone() }
Stop-Work

Start-Sleep -Seconds 2
$perfLines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Perf\]" })
$perfLines | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "perf_budget.txt") -Encoding utf8

# --- bytes ----------------------------------------------------------------------------------------------------
$types = [ordered]@{}
foreach ($line in $perfLines) {
    if ($line -match "\[Perf\]\s+(\w+)\s+sent\s+(\d+) msgs\s+([\d.]+) (kB|MB)\s+recv\s+(\d+) msgs\s+([\d.]+) (kB|MB)") {
        $types[$Matches[1]] = [ordered]@{
            sentMsgs = [int]$Matches[2]; sentBytes = [math]::Round((Get-Bytes ([double]$Matches[3]) $Matches[4]))
            recvMsgs = [int]$Matches[5]; recvBytes = [math]::Round((Get-Bytes ([double]$Matches[6]) $Matches[7]))
        }
    }
}
$clients = [ordered]@{}
foreach ($line in $perfLines) {
    if ($line -match "Client\[(\d+)\] '([^']*)'[^:]*: down ([\d.]+) kB/s, up ([\d.]+) kB/s") {
        $clients[$Matches[2]] = [ordered]@{ id = [int]$Matches[1]; downBps = [double]$Matches[3] * 1024; upBps = [double]$Matches[4] * 1024 }
    }
}
Check ($types.Count -gt 0 -and $clients.Count -gt 0) "the server's perf output was read ($($types.Count) packet types, $($clients.Count) clients; perf_budget.txt)"

$activity = $types["PlayerActivity"]
$activityPerMsg = 0
if ($activity -and $activity.recvMsgs -gt 0) {
    $activityPerMsg = $activity.recvBytes / $activity.recvMsgs
    $upAvg = $activity.recvBytes / $names.Count / $elapsed
    Check ($upAvg -le $activityUpLimit) ("PlayerActivity upload {0:0.00} kB/s average per client (limit 2.4; {1} msgs, {2:0} B each)" -f ($upAvg / 1024), $activity.recvMsgs, $activityPerMsg)
} else { Check $false "the server counted PlayerActivity packets" }

foreach ($name in $names) {
    $after = Visuals $name
    $sent = [int]$after.activitySent - [int]$before[$name].activitySent
    $dropped = [int]$after.activityDropped - [int]$before[$name].activityDropped
    $rate = $sent / $elapsed
    if ($activityPerMsg -gt 0) {
        Check (($rate * $activityPerMsg) -le $activityUpLimit) ("{0} sends {1:0.00} activity packets/s, {2:0.00} kB/s" -f $name, $rate, ($rate * $activityPerMsg / 1024))
    }
    Check ($dropped -eq 0) "$name dropped no activity packet at the client cap ($dropped)"
    Check ([int]$after.leaks -eq 0) "$name has no visual state leak (leaks $($after.leaks))"
}
foreach ($client in $clients.Keys) {
    $c = $clients[$client]
    Check ($c.downBps -le $downLimit) ("client {0} '{1}' downloads {2:0.0} kB/s average (limit 50), uploads {3:0.0} kB/s" -f $c.id, $client, ($c.downBps / 1024), ($c.upBps / 1024))
}
$serverDrops = @(Find-ServerLogLines $Ctx.ServerDir $scenarioStart "\[Visuals\] Client \d+ sends more than")
Check ($serverDrops.Count -eq 0) "the server dropped no activity packet ($($serverDrops.Count) warnings)"

$leakLines = @()
foreach ($name in $names) {
    $path = Get-ClientLogPath $name
    $markInfo = $clientMarks[$name]
    $offset = if ($markInfo -and (Get-Item -LiteralPath $path).CreationTimeUtc -eq $markInfo.Created) { $markInfo.Length } else { 0 }
    $leakLines += @(Read-FileFrom $path $offset | Where-Object { $_ -match "\[Visuals\] state leak" } | ForEach-Object { "$name`: $_" })
}
Check ($leakLines.Count -eq 0) "no '[Visuals] state leak' line in any client log ($($leakLines.Count))"
$leakLines | Select-Object -First 5 | ForEach-Object { Note $_ }

# --- frames ---------------------------------------------------------------------------------------------------
$framesReport = [ordered]@{}
if ($compare) {
    foreach ($name in $names) { Cmd $name vfx-enable "off" | Out-Null }
    $framesOff = Invoke-WorkLoop $OffMinutes "visuals off"
    Stop-Work
    foreach ($name in $names) { Cmd $name vfx-enable "on" | Out-Null }
    foreach ($name in $names) {
        $on = Get-Median $framesOn[$name]
        $off = Get-Median $framesOff[$name]
        $ratio = if ($off -gt 0) { [math]::Round($on / $off, 2) } else { 0 }
        $framesReport[$name] = [ordered]@{ p95On = $on; p95Off = $off; ratio = $ratio }
        $text = "$name frame p95 (median of 10 s windows): $on ms with visuals, $off ms without ($ratio x)"
        if ($ratio -gt 1.2) { Note "WARN: $text" } else { Note $text }
    }
} else {
    foreach ($name in $names) { $framesReport[$name] = [ordered]@{ p95On = (Get-Median $framesOn[$name]) } }
    Note "frame comparison skipped: headless games render nothing (run with -Visible or -CompareFrames)"
}

# --- end state ------------------------------------------------------------------------------------------------
Start-Sleep -Seconds 3
try {
    Wait-HarnessDumpsAllEqual -Instances $names -TimeoutSec 90 | Out-Null
    Check $true "all $($names.Count) clients agree on $((Get-SharedDumpSections) -join ', ') at the end"
} catch {
    Check $false $_.Exception.Message
    Save-Dumps (Get-LastHarnessDumps) (Join-Path $Ctx.RunDir "differ")
}
foreach ($name in $names) { Save-HarnessDump -Instance $name -RunDir $Ctx.RunDir -Label "end" | Out-Null }

$errors = @(Find-LogErrors -Ctx $Ctx -Since $scenarioStart -ClientMarks $clientMarks)
if ($errors.Count -gt 0) { Note "$($errors.Count) [ERROR] lines in the logs, first: $($errors[0])" }

# --- report ---------------------------------------------------------------------------------------------------
$rows = [ordered]@{}
foreach ($type in $types.Keys) {
    $t = $types[$type]
    $rows[$type] = [ordered]@{
        sentMsgs = $t.sentMsgs; sentBytes = $t.sentBytes; recvMsgs = $t.recvMsgs; recvBytes = $t.recvBytes
        bytesPerMsg = if ($t.recvMsgs -gt 0) { [math]::Round($t.recvBytes / $t.recvMsgs) } elseif ($t.sentMsgs -gt 0) { [math]::Round($t.sentBytes / $t.sentMsgs) } else { 0 }
        downPerClientBps = [math]::Round($t.sentBytes / $names.Count / $elapsed, 1)
        upPerClientBps = [math]::Round($t.recvBytes / $names.Count / $elapsed, 1)
    }
}
$report = [ordered]@{
    clients = $names.Count; seconds = [math]::Round($elapsed, 1); headless = $headless
    work = $onStats; types = $rows; perClient = $clients; frames = $framesReport
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "budget.json") -Encoding utf8
foreach ($type in @($rows.Keys | Select-Object -First 8)) {
    $r = $rows[$type]
    Note ("bytes {0}: {1} B/msg, down {2:0.00} kB/s and up {3:0.00} kB/s per client" -f $type, $r.bytesPerMsg, ($r.downPerClientBps / 1024), ($r.upPerClientBps / 1024))
}
if ($activityPerMsg -gt 2 * $activityEstimate) { Note ("WARN: PlayerActivity {0:0} B per packet is over twice D8's 0.6 kB: record it in QUESTIONS.md" -f $activityPerMsg) }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

#Requires -Version 5.1
<#
Summarises a remote-car-spike run: per observer build the wait, build time, steps (ms / StopPhysics ms), the frames
during and after the build (real clock, from frame-log), the remote-stage groups and the copy counts.
#>
param([string]$RunDir, [double]$Limit = 130)

$inv = [cultureinfo]::InvariantCulture
$date = (Split-Path $RunDir -Leaf).Substring(0, 8)

function LogRealtime([string]$Log, [string]$Pattern, $Clock) {
    $offsetMs = $Clock.wallMs - $Clock.realtime * 1000
    $out = @()
    foreach ($line in (Select-String -LiteralPath $Log -Pattern $Pattern)) {
        if ($line.Line -match '^\[(\d\d:\d\d:\d\d\.\d\d\d)\]') {
            $dt = [datetime]::ParseExact("$date $($Matches[1])", "yyyyMMdd HH:mm:ss.fff", $inv)
            $ms = ([DateTimeOffset]$dt).ToUnixTimeMilliseconds()
            $out += [math]::Round(($ms - $offsetMs) / 1000, 3)
        }
    }
    $out
}

foreach ($file in Get-ChildItem -LiteralPath $RunDir -Filter "spike_*_?.json" | Sort-Object Name) {
    $d = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $frames = @($d.frames.frames)
    $events = @($d.trace.events)
    $log = Join-Path $RunDir "client_$($d.instance).log"
    $ready = @(LogRealtime $log 'Test_track_1 ready as TestTrack' $d.frames.clock | Where-Object { $_ -ge $d.mark.realtime })
    Write-Host "== $($file.Name)  wait=$($d.wait) frames in window $($d.frames.framesInWindow) oldest kept $($d.frames.oldestKept) mark $($d.mark.realtime)"
    if ($ready.Count) { Write-Host "   own scene ready at $($ready[0])" }
    $spawn = $null; $create = $null; $steps = @()
    foreach ($e in $events) {
        switch ($e.what) {
            "Spawn" { $spawn = $e.end }
            "CreateLoader" { $create = $e.end - $e.ms / 1000; $steps = @() }
            "step" {
                $steps += $e
                if (-not $e.more -and $create) {
                    $end = $e.end
                    $base = @($frames | Where-Object { $_[0] -lt $spawn -and $_[0] -ge $spawn - 3 } | ForEach-Object { $_[1] } | Sort-Object)
                    $median = if ($base.Count) { $base[[int]($base.Count / 2)] } else { "?" }
                    $build = @($frames | Where-Object { $_[0] -gt $create -and $_[0] -le $end + 0.3 })
                    $post = @($frames | Where-Object { $_[0] -gt $end + 0.3 -and $_[0] -le $end + 3 })
                    $max = ($build | ForEach-Object { $_[1] } | Measure-Object -Maximum).Maximum
                    $postMax = ($post | ForEach-Object { $_[1] } | Measure-Object -Maximum).Maximum
                    $big = @($build | Where-Object { $_[1] -ge 25 } | ForEach-Object { "$([math]::Round($_[0] - $create, 3)):$($_[1])" }) -join " "
                    $stepText = @($steps | ForEach-Object { "$($_.ms)/$($_.stopPhysicsMs)" }) -join " "
                    $arr = if ($ready.Count) { " arrival->spawn $([math]::Round($spawn - $ready[0], 2)) s," } else { "" }
                    Write-Host ("   build:{0} wait {1:N2} s, build {2:N3} s, {3} steps [ms/stopPhysics: {4}]" -f $arr, ($create - $spawn), ($end - $create), $steps.Count, $stepText)
                    Write-Host ("     frames: baseline median {0} ms; during build max {1} ms ({2} frames, >={3}: {4}); after build max {5} ms; >=25 ms at t+ {6}" -f $median, $max, $build.Count, $Limit, @($build | Where-Object { $_[1] -ge $Limit }).Count, $postMax, $big)
                    $create = $null
                }
            }
        }
    }
    if ($d.stage) { Write-Host "   stage: heldFrames $($d.stage.heldFrames) errors [$($d.stage.errors -join "; ")] groups: $(@($d.stage.groups | Where-Object { $_.end -ge $d.mark.realtime } | ForEach-Object { "$($_.calls)=$($_.ms)ms@f$($_.frame)" }) -join " | ")" }
    $helpers = @($events | Where-Object { $_.what -notin "step", "Spawn" } | Group-Object what | ForEach-Object { "$($_.Name) max $((($_.Group | ForEach-Object { $_.ms }) | Measure-Object -Maximum).Maximum) ms" }) -join ", "
    Write-Host "   helpers: $helpers"
    foreach ($c in @($d.counts)) { if ($c) { Write-Host "   copy $($c.playerId): $($c.counts | ConvertTo-Json -Compress) park $($c.park | ConvertTo-Json -Compress)" } }
}

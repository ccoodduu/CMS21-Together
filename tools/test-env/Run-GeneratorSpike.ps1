#Requires -Version 5.1
<#
Generator-client spike (docs/spikes/generator-client.md §5): starts lane 1's instance A alone, offline, in each launch
variant, loads the garage from the menu, idles there and samples memory/CPU every 2 s and the order list every 30 s.
Results: tools\runs\<timestamp>_generator-spike\<variant>.json and summary.json.
Variants: normal (960x540), nographics (-batchmode -nographics), nographics15 (the same, capped at 15 fps), batchmode
(-batchmode), lowfx (320x180, cameras off, 15 fps).
#>
param(
    [string[]]$Variants = @("normal", "nographics", "batchmode", "lowfx"),
    [int]$IdleSeconds = 240,
    [int]$StartTimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"
$TestRoot = "$env:USERPROFILE\CMS21-TestInstalls"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Import-Module (Join-Path $PSScriptRoot "HarnessClient.psm1") -Force
Import-Module (Join-Path $PSScriptRoot "TestLanes.psm1") -Force

$lane = Get-TestLane 1
$name = $lane.Instances[0]
$dir = Join-Path $TestRoot $name
$exe = Join-Path $dir "Car Mechanic Simulator 2021.exe"
$runDir = Join-Path $repo ("tools\runs\{0}_generator-spike" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$gameMutex = New-Object System.Threading.Mutex($false, "Global\CMS21TogetherGameLane")
try { if (-not $gameMutex.WaitOne([TimeSpan]::FromMinutes(30))) { throw "lane busy" } } catch [System.Threading.AbandonedMutexException] { }

function Sample($Process) {
    $Process.Refresh()
    [pscustomobject]@{
        t = [math]::Round(((Get-Date) - $Process.StartTime).TotalSeconds)
        commitMb = [math]::Round($Process.PagedMemorySize64 / 1MB)
        workingMb = [math]::Round($Process.WorkingSet64 / 1MB)
        peakCommitMb = [math]::Round($Process.PeakPagedMemorySize64 / 1MB)
        cpuSeconds = [math]::Round($Process.TotalProcessorTime.TotalSeconds, 1)
    }
}

Assert-InstanceIsolated $name
$realFingerprint = Get-RealProfileFingerprint
$summary = @()
try {
    foreach ($variant in $Variants) {
        Write-Host "=== $variant"
        Reset-InstanceProfile $name
        $harnessDir = Join-Path $dir "UserData\TestHarness"
        Remove-Item -LiteralPath (Join-Path $harnessDir "status.json") -ErrorAction SilentlyContinue
        $arguments = @("--melonloader.disablestartscreen", "--melonloader.agfoffline", "--harness.name=$name", "--harness.mute")
        switch ($variant) {
            "normal" { $arguments += @("-screen-fullscreen", "0", "-screen-width", "960", "-screen-height", "540", "--harness.window=960x540") }
            "nographics" { $arguments += @("-batchmode", "-nographics", "-logFile", (Join-Path $runDir "$variant.unity.log")) }
            "nographics15" { $arguments += @("-batchmode", "-nographics", "-logFile", (Join-Path $runDir "$variant.unity.log")) }
            "batchmode" { $arguments += @("-batchmode", "-logFile", (Join-Path $runDir "$variant.unity.log")) }
            "lowfx" { $arguments += @("-screen-fullscreen", "0", "-screen-width", "320", "-screen-height", "180", "--harness.window=320x180") }
        }
        $process = Start-Process -FilePath $exe -WorkingDirectory $dir -ArgumentList $arguments -PassThru
        $samples = New-Object System.Collections.ArrayList
        $result = [ordered]@{ variant = $variant; reachedMenu = $false; reachedGarage = $false; notes = @() }
        $start = Get-Date
        try {
            $deadline = $start.AddSeconds($StartTimeoutSeconds)
            while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
                [void]$samples.Add((Sample $process))
                $s = Get-HarnessStatus $name
                if ($s -and $s.scene -eq "Menu" -and $s.playable) { $result.reachedMenu = $true; break }
                Start-Sleep -Seconds 2
            }
            if (-not $result.reachedMenu) { throw "did not reach the menu (exited: $($process.HasExited))" }
            $result.menuSeconds = [math]::Round(((Get-Date) - $start).TotalSeconds)

            Send-HarnessCommand -Instance $name -Verb travel -Arguments "Garage" | Out-Null
            $deadline = (Get-Date).AddSeconds($StartTimeoutSeconds)
            while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
                [void]$samples.Add((Sample $process))
                $s = Get-HarnessStatus $name
                if ($s -and $s.scene -eq "garage" -and $s.playable) { $result.reachedGarage = $true; break }
                Start-Sleep -Seconds 2
            }
            if (-not $result.reachedGarage) { throw "did not reach the garage (exited: $($process.HasExited))" }
            $result.garageSeconds = [math]::Round(((Get-Date) - $start).TotalSeconds)
            if ($variant -eq "lowfx" -or $variant -eq "nographics15") { $result.lowfx = Send-HarnessCommand -Instance $name -Verb lowfx -Arguments "15" }

            $probes = @()
            $idleStart = Get-Date
            $cpuStart = (Sample $process).cpuSeconds
            while (((Get-Date) - $idleStart).TotalSeconds -lt $IdleSeconds -and -not $process.HasExited) {
                [void]$samples.Add((Sample $process))
                if ($samples.Count % 15 -eq 0) { $probes += Send-HarnessCommand -Instance $name -Verb gen-probe }
                Start-Sleep -Seconds 2
            }
            $last = Sample $process
            $result.idleCpuPercentOfCore = [math]::Round(100 * ($last.cpuSeconds - $cpuStart) / [math]::Max(1, ((Get-Date) - $idleStart).TotalSeconds))
            $result.peakCommitMb = ($samples | Measure-Object peakCommitMb -Maximum).Maximum
            $result.peakWorkingMb = ($samples | Measure-Object workingMb -Maximum).Maximum
            $result.idleWorkingMb = $last.workingMb
            $result.idleCommitMb = $last.commitMb
            $result.probes = $probes
            $result.ordersAtEnd = @($probes | Select-Object -Last 1)[0].jobs.Count
            $result.graphics = @($probes | Select-Object -Last 1)[0].graphics
        }
        catch {
            $result.notes += "ERROR: $($_.Exception.Message)"
            Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        }
        finally {
            try { Send-HarnessCommand -Instance $name -Verb quit -TimeoutSec 5 | Out-Null } catch { }
            Start-Sleep -Seconds 5
            if (-not $process.HasExited) { $process | Stop-Process -Force }
            $log = Join-Path $dir "MelonLoader\Latest.log"
            if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $runDir "$variant.melon.log") }
        }
        $result.samples = $samples
        $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runDir "$variant.json") -Encoding utf8
        $line = [ordered]@{}
        foreach ($key in "variant", "reachedMenu", "reachedGarage", "menuSeconds", "garageSeconds", "peakCommitMb", "peakWorkingMb", "idleWorkingMb", "idleCommitMb", "idleCpuPercentOfCore", "ordersAtEnd", "graphics", "notes") { $line[$key] = $result[$key] }
        $summary += [pscustomobject]$line
        Write-Host ($line | ConvertTo-Json -Compress)
    }
}
finally {
    if ((Get-RealProfileFingerprint) -ne $realFingerprint) { Write-Host "WARNING: the real save folder or registry key changed during the spike" -ForegroundColor Red }
    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runDir "summary.json") -Encoding utf8
    Write-Host "Run folder: $runDir"
    $gameMutex.ReleaseMutex()
}

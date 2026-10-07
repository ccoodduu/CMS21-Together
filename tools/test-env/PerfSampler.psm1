$script:TestRoot = "$env:USERPROFILE\CMS21-TestInstalls"
$script:GameProcessName = "Car Mechanic Simulator 2021"
$script:ServerProcessName = "CMS21_Together_Server"
$script:PreviousCpu = @{}
$script:RunStart = @{}

Import-Module (Join-Path $PSScriptRoot "TestLanes.psm1")
if (-not (Get-Command Send-HarnessCommand -ErrorAction SilentlyContinue)) { Import-Module (Join-Path $PSScriptRoot "HarnessClient.psm1") }

function Get-SystemMemory {
    $os = Get-CimInstance Win32_OperatingSystem
    [pscustomobject]@{
        CommitHeadroomGb = [math]::Round($os.FreeVirtualMemory / 1MB, 2)
        FreeRamGb = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
    }
}

function Get-ProcessPerf($Process) {
    $Process.Refresh()
    [pscustomobject]@{
        Id = $Process.Id
        StartTime = $Process.StartTime
        CpuSeconds = [math]::Round($Process.TotalProcessorTime.TotalSeconds, 2)
        PrivateMb = [math]::Round($Process.PagedMemorySize64 / 1MB)
        PeakPrivateMb = [math]::Round($Process.PeakPagedMemorySize64 / 1MB)
        WorkingMb = [math]::Round($Process.WorkingSet64 / 1MB)
        Handles = $Process.HandleCount
    }
}

function Get-FileLength([string]$Path) {
    try { (Get-Item -LiteralPath $Path -ErrorAction Stop).Length } catch { 0 }
}

function Get-InstanceProcess([string]$Instance) {
    $dir = (Join-Path $script:TestRoot $Instance) + "\"
    Get-Process -Name $script:GameProcessName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
}

function Get-ServerProcess([string]$ServerDir) {
    $exe = Join-Path $ServerDir "$script:ServerProcessName.exe"
    Get-Process -Name $script:ServerProcessName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path -ieq $exe } | Select-Object -First 1
}

function Add-ProcessColumns($Row, [string]$Role, $Process, [datetime]$Now) {
    if ($null -eq $Process) {
        $Row["${Role}_running"] = 0
        foreach ($column in "pid", "cpuS", "cpuPct", "privateMb", "workingMb", "handles") { $Row["${Role}_$column"] = "" }
        return
    }
    $perf = Get-ProcessPerf $Process
    $key = "$Role/$($perf.Id)"
    $cpuPct = ""
    if ($script:PreviousCpu.ContainsKey($key)) {
        $previous = $script:PreviousCpu[$key]
        $wall = ($Now - $previous.Time).TotalSeconds
        if ($wall -gt 0) { $cpuPct = [math]::Round(100 * ($perf.CpuSeconds - $previous.CpuSeconds) / $wall, 1) }
    }
    $script:PreviousCpu[$key] = @{ Time = $Now; CpuSeconds = $perf.CpuSeconds }
    $Row["${Role}_running"] = 1
    $Row["${Role}_pid"] = $perf.Id
    $Row["${Role}_cpuS"] = $perf.CpuSeconds
    $Row["${Role}_cpuPct"] = $cpuPct
    $Row["${Role}_privateMb"] = $perf.PrivateMb
    $Row["${Role}_workingMb"] = $perf.WorkingMb
    $Row["${Role}_handles"] = $perf.Handles
}

function Get-PerfSample {
    param([string[]]$Instances, [string]$ServerDir, [string]$RunDir = "")
    $now = Get-Date
    if ($RunDir -and -not $script:RunStart.ContainsKey($RunDir)) { $script:RunStart[$RunDir] = $now }
    $start = if ($RunDir) { $script:RunStart[$RunDir] } else { $now }
    $memory = Get-SystemMemory

    $row = [ordered]@{
        time = $now.ToString("yyyy-MM-ddTHH:mm:ss")
        elapsedS = [math]::Round(($now - $start).TotalSeconds)
        commitHeadroomGb = $memory.CommitHeadroomGb
        freeRamGb = $memory.FreeRamGb
    }
    foreach ($instance in $Instances) {
        Add-ProcessColumns $row $instance (Get-InstanceProcess $instance) $now
        $row["${instance}_latestLogBytes"] = Get-FileLength (Join-Path $script:TestRoot "$instance\MelonLoader\Latest.log")
        $row["${instance}_playerLogBytes"] = Get-FileLength (Join-Path (Get-InstanceSaveDir $instance) "Player.log")
    }
    Add-ProcessColumns $row "server" (Get-ServerProcess $ServerDir) $now
    $row["server_logBytes"] = Get-FileLength (Join-Path $ServerDir "Log\Latest.txt")
    [pscustomobject]$row
}

function Add-PerfSample {
    param([string]$RunDir, [string[]]$Instances, [string]$ServerDir)
    $sample = Get-PerfSample -Instances $Instances -ServerDir $ServerDir -RunDir $RunDir
    $sample | Export-Csv -LiteralPath (Join-Path $RunDir "perf_processes.csv") -Append -NoTypeInformation -Encoding UTF8
    return $sample
}

function Test-PerfWatchdog {
    param($Sample, [double]$MinCommitHeadroomGb = 4, [double]$MinFreeRamGb = 1.5)
    if ($null -eq $Sample) {
        $memory = Get-SystemMemory
        $Sample = [pscustomobject]@{ commitHeadroomGb = $memory.CommitHeadroomGb; freeRamGb = $memory.FreeRamGb }
    }
    $reasons = @()
    if ([double]$Sample.commitHeadroomGb -lt $MinCommitHeadroomGb) { $reasons += "commit headroom $($Sample.commitHeadroomGb) GB < $MinCommitHeadroomGb GB" }
    if ([double]$Sample.freeRamGb -lt $MinFreeRamGb) { $reasons += "free RAM $($Sample.freeRamGb) GB < $MinFreeRamGb GB" }
    if ($reasons.Count -eq 0) { return $null }
    return "aborted: memory ($($reasons -join ', '))"
}

function Add-FrameSample {
    param([string]$RunDir, [string[]]$Instances, [int]$TimeoutSec = 10)
    $now = Get-Date
    if (-not $script:RunStart.ContainsKey($RunDir)) { $script:RunStart[$RunDir] = $now }
    $rows = foreach ($instance in $Instances) {
        $row = [ordered]@{
            time = $now.ToString("yyyy-MM-ddTHH:mm:ss"); elapsedS = [math]::Round(($now - $script:RunStart[$RunDir]).TotalSeconds); instance = $instance
            frames = ""; avgMs = ""; p95Ms = ""; maxMs = ""; fps = ""; managedHeap = ""; il2cppHeapUsed = ""; il2cppHeapSize = ""
            scene = ""; syncAcked = ""; error = ""
        }
        try {
            $perf = Send-HarnessCommand -Instance $instance -Verb perf -TimeoutSec $TimeoutSec
            foreach ($key in "frames", "avgMs", "p95Ms", "maxMs", "fps", "managedHeap", "il2cppHeapUsed", "il2cppHeapSize", "scene", "syncAcked") { $row[$key] = $perf.$key }
        } catch {
            $row.error = $_.Exception.Message
        }
        [pscustomobject]$row
    }
    $rows | Export-Csv -LiteralPath (Join-Path $RunDir "frames.csv") -Append -NoTypeInformation -Encoding UTF8
    return $rows
}

Export-ModuleMember -Function Get-SystemMemory, Get-ProcessPerf, Get-PerfSample, Add-PerfSample, Test-PerfWatchdog, Add-FrameSample

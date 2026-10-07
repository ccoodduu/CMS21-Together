# A test lane is a set of test installs plus its own dedicated server and port, so lanes 1 and 2 can run at the
# same time; lane 3 (the scale lane) uses all four installs and waits for both. Every install has its own Unity company name, which gives it its own save folder
# (LocalLow\<company>\Car Mechanic Simulator 2021) and its own registry key (HKCU\Software\<company>\...).
# The real game's save folder and registry key are never written by a test run.

$script:TestRoot = "$env:USERPROFILE\CMS21-TestInstalls"
$script:GameCompany = "Red Dot Games"
$script:ProductName = "Car Mechanic Simulator 2021"
$script:DataDirName = "Car Mechanic Simulator 2021_Data"
$script:RealSaveDir = "$env:USERPROFILE\AppData\LocalLow\Red Dot Games\Car Mechanic Simulator 2021"
$script:RealRegistryKey = "HKCU\Software\Red Dot Games\Car Mechanic Simulator 2021"

$script:SteamLog = "C:\Program Files (x86)\Steam\logs\console_log.txt"
$script:LaunchMutexName = "Global\CMS21TogetherGameLane"

# MinCommitGb/MinFreeGb: the memory a lane waits for before it starts its games. Locks: the lane locks a run holds
# for its whole duration, taken in this order.
$script:Lanes = @{
    1 = @{ Instances = @("A", "B"); Server = "Server"; Port = 7777; MinCommitGb = 22; MinFreeGb = 6; Locks = @(1) }
    2 = @{ Instances = @("C", "D"); Server = "Server2"; Port = 7787; MinCommitGb = 22; MinFreeGb = 10; Locks = @(2) }
    3 = @{ Instances = @("A", "B", "C", "D"); Server = "Server3"; Port = 7797; MinCommitGb = 44; MinFreeGb = 16; Locks = @(1, 2) }
}

function Get-TestLanes { @($script:Lanes.Keys | Sort-Object) }

function Get-TestLane([int]$Lane) {
    if (-not $script:Lanes.ContainsKey($Lane)) { throw "Unknown test lane $Lane (known: $((Get-TestLanes) -join ', '))" }
    $l = $script:Lanes[$Lane]
    [pscustomobject]@{
        Lane = $Lane
        Instances = $l.Instances
        ServerDir = Join-Path $script:TestRoot $l.Server
        Port = $l.Port
        ConnectAddress = "127.0.0.1:$($l.Port)"
        MinCommitHeadroomGb = $l.MinCommitGb
        MinFreeMemoryGb = $l.MinFreeGb
        LockLanes = $l.Locks
    }
}

# Same length as "Red Dot Games" (13 characters), so the patched settings file keeps its layout.
function Get-InstanceCompany([string]$Instance) {
    $company = "RDGTogether-$Instance"
    if ($company.Length -ne $script:GameCompany.Length) { throw "Instance name must be one character: $Instance" }
    return $company
}

function Get-InstanceSaveDir([string]$Instance) {
    Join-Path "$env:USERPROFILE\AppData\LocalLow" "$(Get-InstanceCompany $Instance)\$script:ProductName"
}

function Get-InstanceRegistryKey([string]$Instance) { "HKCU\Software\$(Get-InstanceCompany $Instance)\$script:ProductName" }

function Find-Bytes([byte[]]$Haystack, [string]$Needle) {
    $text = [System.Text.Encoding]::GetEncoding(28591).GetString($Haystack)
    $found = @()
    $index = $text.IndexOf($Needle, [StringComparison]::Ordinal)
    while ($index -ge 0) {
        $found += $index
        $index = $text.IndexOf($Needle, $index + 1, [StringComparison]::Ordinal)
    }
    return $found
}

# Replaces the hard-linked globalgamemanagers of an install with a real copy whose company name is the
# instance's own. Writing through the hard link would change the Steam install, so the link is removed first.
function Set-InstanceCompany([string]$Instance) {
    $file = Join-Path $script:TestRoot "$Instance\$script:DataDirName\globalgamemanagers"
    $bytes = [System.IO.File]::ReadAllBytes($file)
    $company = Get-InstanceCompany $Instance
    $ascii = [System.Text.Encoding]::ASCII
    if ((Find-Bytes $bytes $company).Count -eq 1) { return }

    $offsets = @(Find-Bytes $bytes $script:GameCompany)
    if ($offsets.Count -ne 1) { throw "Expected one '$($script:GameCompany)' in $file, found $($offsets.Count)" }
    $ascii.GetBytes($company).CopyTo($bytes, $offsets[0])
    Remove-Item -LiteralPath $file -Force
    [System.IO.File]::WriteAllBytes($file, $bytes)
    Write-Host "Install $Instance uses company '$company'"
}

function Assert-InstanceIsolated([string]$Instance) {
    $file = Join-Path $script:TestRoot "$Instance\$script:DataDirName\globalgamemanagers"
    $bytes = [System.IO.File]::ReadAllBytes($file)
    if ((Find-Bytes $bytes (Get-InstanceCompany $Instance)).Count -ne 1) {
        throw "Install $Instance is not isolated (run Setup-TestInstalls.ps1): it would use the real save folder"
    }
}

function Get-SeedDir { Join-Path $script:TestRoot "seed" }

# The seed is a one-time copy of the real save folder (without logs) and registry settings. Every run starts
# each instance from it.
function New-ProfileSeed([switch]$Force) {
    $seed = Get-SeedDir
    if ((Test-Path -LiteralPath $seed) -and -not $Force) { return }
    if (Test-Path -LiteralPath $seed) { Remove-Item -LiteralPath $seed -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $seed | Out-Null
    Copy-Item -LiteralPath $script:RealSaveDir -Destination (Join-Path $seed "LocalLow") -Recurse
    Get-ChildItem -LiteralPath (Join-Path $seed "LocalLow") -File -Filter "Player*.log" | Remove-Item -Force
    cmd /c "reg export `"$script:RealRegistryKey`" `"$(Join-Path $seed 'settings.reg')`" /y >nul 2>&1"
    if ($LASTEXITCODE -ne 0) { throw "Registry export for the seed failed" }
    Write-Host "Created profile seed in $seed"
}

function Reset-InstanceProfile([string]$Instance) {
    $seed = Get-SeedDir
    if (-not (Test-Path -LiteralPath $seed)) { throw "No profile seed (run Setup-TestInstalls.ps1)" }

    $saveDir = Get-InstanceSaveDir $Instance
    $companyDir = Split-Path $saveDir -Parent
    if (Test-Path -LiteralPath $companyDir) { Remove-Item -LiteralPath $companyDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $companyDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $seed "LocalLow") -Destination $saveDir -Recurse

    $key = Get-InstanceRegistryKey $Instance
    $realKeyText = "HKEY_CURRENT_USER\Software\$script:GameCompany\"
    $instanceKeyText = "HKEY_CURRENT_USER\Software\$(Get-InstanceCompany $Instance)\"
    $reg = (Get-Content -LiteralPath (Join-Path $seed "settings.reg") -Raw -Encoding Unicode).Replace($realKeyText, $instanceKeyText)
    $tmp = Join-Path $env:TEMP "cms21-together-$Instance.reg"
    Set-Content -LiteralPath $tmp -Value $reg -Encoding Unicode
    cmd /c "reg delete `"HKCU\Software\$(Get-InstanceCompany $Instance)`" /f >nul 2>&1 & reg import `"$tmp`" >nul 2>&1"
    if ($LASTEXITCODE -ne 0) { throw "Registry reset for instance $Instance failed" }
    Remove-Item -LiteralPath $tmp -Force
}

# A fingerprint of the real save folder and registry key, to prove a run did not touch them.
function Get-RealProfileFingerprint {
    $files = Get-ChildItem -LiteralPath $script:RealSaveDir -Recurse -File |
        Where-Object { $_.Name -notlike "Player*.log" } |
        Sort-Object FullName |
        ForEach-Object { "{0}|{1}|{2}" -f $_.FullName, $_.Length, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA1).Hash }
    $tmp = Join-Path $env:TEMP "cms21-together-real.reg"
    cmd /c "reg export `"$script:RealRegistryKey`" `"$tmp`" /y >nul 2>&1"
    $registry = (Get-FileHash -LiteralPath $tmp -Algorithm SHA1).Hash
    Remove-Item -LiteralPath $tmp -Force
    return (($files -join "`n") + "`nregistry|$registry")
}

function Set-LaneServerConfig($LaneInfo) {
    $config = Join-Path $LaneInfo.ServerDir "server_config.ini"
    if (-not (Test-Path -LiteralPath $config)) {
        Set-Content -LiteralPath $config -Encoding ascii -Value @(
            "max_players = 4",
            "use_steam = False",
            'GSLT_Token = ""',
            "log_level = 1"
        )
    }
    $lines = @(Get-Content -LiteralPath $config | Where-Object { $_ -notmatch '^\s*port\s*=' }) + "port = $($LaneInfo.Port)"
    Set-Content -LiteralPath $config -Encoding ascii -Value $lines
}

# Files only a release zip installs; Deploy-Mod.ps1 removes them so dev runs stay Steam-free.
$script:ReleaseOnlyInstanceFiles = @(
    "UserLibs\steam_api64.dll", "TogetherServer", "CMS21-Together-TRY-IT.txt", "CMS21-Together-release.json",
    "Mods\CMS21-Together.pdb", "UserLibs\CMS21_Together_Core.pdb", "Collect-Logs.ps1", "Collect-Logs.bat"
)
$script:ReleaseOnlyServerFiles = @("steam_api64.dll", "TRY-IT.txt", "release.json", "Collect-Logs.ps1", "Collect-Logs.bat")

function Remove-ReleaseOnlyFiles($LaneInfo) {
    foreach ($name in $LaneInfo.Instances) {
        foreach ($relative in $script:ReleaseOnlyInstanceFiles) {
            $path = Join-Path (Join-Path $script:TestRoot $name) $relative
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        }
    }
    foreach ($relative in $script:ReleaseOnlyServerFiles) {
        $path = Join-Path $LaneInfo.ServerDir $relative
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}

function Get-LaneGameProcesses($LaneInfo) {
    $dirs = $LaneInfo.Instances | ForEach-Object { (Join-Path $script:TestRoot $_) + "\" }
    Get-Process -Name $script:ProductName -ErrorAction SilentlyContinue | Where-Object {
        $path = $_.Path
        $path -and ($dirs | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) })
    }
}

# A scenario needs its own freshly started games (no batch) when it has per-instance launch arguments or a
# "# run-all: fresh" line.
function Test-ScenarioNeedsFreshGame([string]$ScenarioFile) {
    if (Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($ScenarioFile, ".launch.psd1"))) { return $true }
    return [bool](Select-String -LiteralPath $ScenarioFile -Pattern '^\s*#\s*run-all:\s*fresh\b' -Quiet)
}

function Get-InstanceGameProcess([string]$Instance) {
    $dir = (Join-Path $script:TestRoot $Instance) + "\"
    Get-Process -Name $script:ProductName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
}

# Lane locks: a run holds the named mutex of every lane it uses for its whole duration. Mutexes are owned per
# thread and re-entrant, so a script that calls Run-Session or Deploy-Mod again on the same thread does not block.
function Enter-LaneLocks {
    param([int]$Lane, [double]$WaitMinutes = 120, [switch]$NoWait)
    $held = New-Object System.Collections.Generic.List[System.Threading.Mutex]
    try {
        foreach ($lockLane in (Get-TestLane $Lane).LockLanes) {
            $mutex = New-Object System.Threading.Mutex($false, "Global\CMS21TogetherLane$lockLane")
            $acquired = $false
            try { $acquired = $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $acquired = $true }
            if (-not $acquired -and -not $NoWait) {
                Write-Host "Lane $lockLane is busy; waiting up to $WaitMinutes min for it."
                try { $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes($WaitMinutes)) } catch [System.Threading.AbandonedMutexException] { $acquired = $true }
            }
            if (-not $acquired) {
                $mutex.Dispose()
                if ($NoWait) { throw "lane $lockLane is busy" }
                throw "Lane $lockLane stayed busy for $WaitMinutes minutes; giving up."
            }
            $held.Add($mutex)
        }
    } catch {
        Exit-LaneLocks $held
        throw
    }
    return ,$held
}

function Exit-LaneLocks($Held) {
    if (-not $Held) { return }
    for ($i = $Held.Count - 1; $i -ge 0; $i--) {
        try { $Held[$i].ReleaseMutex() } catch { }
        $Held[$i].Dispose()
    }
    $Held.Clear()
}

function Get-MemoryHeadroom {
    $os = Get-CimInstance Win32_OperatingSystem
    [pscustomobject]@{ CommitGb = $os.FreeVirtualMemory / 1MB; FreeGb = $os.FreePhysicalMemory / 1MB }
}

function Wait-MemoryHeadroom {
    param([double]$MinCommitHeadroomGb, [double]$MinFreeMemoryGb, [double]$TimeoutMinutes = 30)
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ($true) {
        $headroom = Get-MemoryHeadroom
        if ($headroom.CommitGb -ge $MinCommitHeadroomGb -and $headroom.FreeGb -ge $MinFreeMemoryGb) { return $headroom }
        if ((Get-Date) -gt $deadline) {
            throw ("Only {0:N1} GB commit and {1:N1} GB RAM free for {4} minutes (need {2} and {3}); not starting." -f $headroom.CommitGb, $headroom.FreeGb, $MinCommitHeadroomGb, $MinFreeMemoryGb, $TimeoutMinutes)
        }
        Start-Sleep -Seconds 5
    }
}

function Get-SteamLogMark {
    if (Test-Path -LiteralPath $script:SteamLog) { @(Get-Content -LiteralPath $script:SteamLog -ErrorAction SilentlyContinue).Count } else { 0 }
}

function Test-SteamKick([int]$FromLine) {
    if (-not (Test-Path -LiteralPath $script:SteamLog)) { return $false }
    $lines = @(Get-Content -LiteralPath $script:SteamLog -ErrorAction SilentlyContinue)
    if ($lines.Count -le $FromLine) { return $false }
    return [bool]($lines[$FromLine..($lines.Count - 1)] | Select-String -SimpleMatch "waiting for user response to KickingOtherSession")
}

function Get-InstanceStatus([string]$Instance) {
    $path = Join-Path $script:TestRoot "$Instance\UserData\TestHarness\status.json"
    try { Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json } catch { $null }
}

# Starts one game of a lane with the lane's launch arguments under the launch mutex (re-entrant, so Run-Session's
# Start-LaneGames can call it while it holds the mutex). -NoWait returns at once; Wait-HarnessInstances waits.
function Start-HarnessInstance {
    param(
        [Parameter(Mandatory = $true)][int]$Lane,
        [Parameter(Mandatory = $true)][string]$Instance,
        [switch]$Headless,
        [string]$Window = "960x540",
        [switch]$Sound,
        [string[]]$ExtraArguments = @(),
        [switch]$NoWait,
        [switch]$SkipGate,
        [double]$MinCommitHeadroomGb = 11,
        [double]$MinFreeMemoryGb = 4
    )
    $laneInfo = Get-TestLane $Lane
    if ($laneInfo.Instances -notcontains $Instance) { throw "Instance $Instance is not in lane $Lane ($($laneInfo.Instances -join ', '))" }
    if (Get-InstanceGameProcess $Instance) { throw "Instance $Instance is already running." }
    Assert-InstanceIsolated $Instance

    $mutex = New-Object System.Threading.Mutex($false, $script:LaunchMutexName)
    try {
        if (-not $mutex.WaitOne([TimeSpan]::FromMinutes(30))) { throw "Another lane has been starting its games for 30 minutes; giving up." }
    } catch [System.Threading.AbandonedMutexException] { }
    try {
        if (-not $SkipGate) { Wait-MemoryHeadroom -MinCommitHeadroomGb $MinCommitHeadroomGb -MinFreeMemoryGb $MinFreeMemoryGb | Out-Null }
        $dir = Join-Path $script:TestRoot $Instance
        $harnessDir = Join-Path $dir "UserData\TestHarness"
        Remove-Item -LiteralPath (Join-Path $harnessDir "status.json") -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $harnessDir -Filter "reply_*.json" -ErrorAction SilentlyContinue | Remove-Item -Force
        Remove-Item -LiteralPath (Join-Path $harnessDir "command.txt") -ErrorAction SilentlyContinue
        $size = $Window.Split('x')
        $arguments = @(
            "--melonloader.disablestartscreen", "--melonloader.agfoffline",
            "-screen-fullscreen", "0", "-screen-width", $size[0], "-screen-height", $size[1],
            "--harness.name=$Instance", "--harness.window=$Window"
        )
        if (-not $Sound) { $arguments += "--harness.mute" }
        if ($Headless) { $arguments += @("-batchmode", "-nographics") }
        $arguments += @($ExtraArguments | ForEach-Object { $_.Replace("{port}", "$($laneInfo.Port)") })
        $launch = [pscustomobject]@{ Instance = $Instance; Started = Get-Date; SteamMark = Get-SteamLogMark; Headless = [bool]$Headless }
        Start-Process -FilePath (Join-Path $dir "$script:ProductName.exe") -WorkingDirectory $dir -ArgumentList $arguments | Out-Null
        Write-Host "Started instance $Instance$(if ($Headless) { ' (headless)' })"
        if (-not $NoWait) {
            $late = @(Wait-HarnessInstances -Launches @($launch))
            if ($late.Count -gt 0) { throw "Instance $Instance did not reach the main menu." }
        }
    } finally {
        $mutex.ReleaseMutex()
        $mutex.Dispose()
    }
    return $launch
}

# Waits until the started games write a status and reach the main menu; returns the names still not in the menu.
function Wait-HarnessInstances {
    param($Launches, [int]$StatusTimeoutSec = 60, [int]$MenuTimeoutSec = 180)
    $names = @($Launches | ForEach-Object { $_.Instance })
    $steamMark = (@($Launches | ForEach-Object { $_.SteamMark }) | Measure-Object -Minimum).Minimum
    $deadline = (Get-Date).AddSeconds($StatusTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-SteamKick $steamMark) {
            & (Join-Path $PSScriptRoot "Cancel-SteamLaunch.ps1") | Out-Null
            throw "Steam asks to end a session on another device (KickingOtherSession); run aborted. Set Steam offline on the other device."
        }
        if (@($names | Where-Object { Get-InstanceStatus $_ }).Count -eq $names.Count) { break }
        Start-Sleep -Seconds 2
    }
    $inMenu = { param($n) $s = Get-InstanceStatus $n; $s -and $s.scene -eq "Menu" -and $s.playable }
    $deadline = (Get-Date).AddSeconds($MenuTimeoutSec)
    while ((Get-Date) -lt $deadline -and @($names | Where-Object { & $inMenu $_ }).Count -lt $names.Count) { Start-Sleep -Seconds 2 }
    foreach ($launch in @($Launches | Where-Object { $_.Headless })) {
        if (& $inMenu $launch.Instance) {
            try { Send-HarnessCommand -Instance $launch.Instance -Verb lowfx -Arguments "15" | Out-Null } catch { Write-Host "lowfx on $($launch.Instance): $($_.Exception.Message)" }
        }
    }
    return @($names | Where-Object { -not (& $inMenu $_) })
}

function Get-DeployedBuild([string]$Dir) {
    $path = Join-Path $Dir "deployed.json"
    try { Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json } catch { $null }
}

# The deployed.json of every install of a lane and of its server; Mixed is true when they name different builds.
function Get-LaneDeployedBuilds($LaneInfo) {
    $builds = [ordered]@{}
    foreach ($name in $LaneInfo.Instances) { $builds[$name] = Get-DeployedBuild (Join-Path $script:TestRoot "$name\UserData\TestHarness") }
    $builds["server"] = Get-DeployedBuild $LaneInfo.ServerDir
    $keys = @($builds.Values | ForEach-Object { if ($_) { "$($_.repo)|$($_.commit)|$($_.dirty)|$($_.time)" } else { "none" } } | Select-Object -Unique)
    [pscustomobject]@{ Builds = $builds; Mixed = $keys.Count -gt 1 }
}

Export-ModuleMember -Function Get-TestLane, Get-TestLanes, Get-InstanceCompany, Get-InstanceSaveDir, Get-InstanceRegistryKey,
    Set-InstanceCompany, Assert-InstanceIsolated, New-ProfileSeed, Reset-InstanceProfile, Get-RealProfileFingerprint,
    Get-LaneGameProcesses, Set-LaneServerConfig, Remove-ReleaseOnlyFiles, Test-ScenarioNeedsFreshGame,
    Get-InstanceGameProcess, Enter-LaneLocks, Exit-LaneLocks, Get-MemoryHeadroom, Wait-MemoryHeadroom, Get-SteamLogMark,
    Test-SteamKick, Start-HarnessInstance, Wait-HarnessInstances, Get-DeployedBuild, Get-LaneDeployedBuilds

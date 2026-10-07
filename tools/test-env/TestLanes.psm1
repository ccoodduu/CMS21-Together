# A test lane is a pair of test installs plus its own dedicated server and port, so two lanes can run at the
# same time. Every install has its own Unity company name, which gives it its own save folder
# (LocalLow\<company>\Car Mechanic Simulator 2021) and its own registry key (HKCU\Software\<company>\...).
# The real game's save folder and registry key are never written by a test run.

$script:TestRoot = "$env:USERPROFILE\CMS21-TestInstalls"
$script:GameCompany = "Red Dot Games"
$script:ProductName = "Car Mechanic Simulator 2021"
$script:DataDirName = "Car Mechanic Simulator 2021_Data"
$script:RealSaveDir = "$env:USERPROFILE\AppData\LocalLow\Red Dot Games\Car Mechanic Simulator 2021"
$script:RealRegistryKey = "HKCU\Software\Red Dot Games\Car Mechanic Simulator 2021"

$script:Lanes = @{
    1 = @{ Instances = @("A", "B"); Server = "Server"; Port = 7777 }
    2 = @{ Instances = @("C", "D"); Server = "Server2"; Port = 7787 }
}

function Get-TestLane([int]$Lane) {
    if (-not $script:Lanes.ContainsKey($Lane)) { throw "Unknown test lane $Lane (known: $($script:Lanes.Keys -join ', '))" }
    $l = $script:Lanes[$Lane]
    [pscustomobject]@{
        Lane = $Lane
        Instances = $l.Instances
        ServerDir = Join-Path $script:TestRoot $l.Server
        Port = $l.Port
        ConnectAddress = "127.0.0.1:$($l.Port)"
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
    "Mods\CMS21-Together.pdb", "UserLibs\CMS21_Together_Core.pdb"
)
$script:ReleaseOnlyServerFiles = @("steam_api64.dll", "TRY-IT.txt", "release.json")

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

Export-ModuleMember -Function Get-TestLane, Get-InstanceCompany, Get-InstanceSaveDir, Get-InstanceRegistryKey,
    Set-InstanceCompany, Assert-InstanceIsolated, New-ProfileSeed, Reset-InstanceProfile, Get-RealProfileFingerprint,
    Get-LaneGameProcesses, Set-LaneServerConfig, Remove-ReleaseOnlyFiles, Test-ScenarioNeedsFreshGame

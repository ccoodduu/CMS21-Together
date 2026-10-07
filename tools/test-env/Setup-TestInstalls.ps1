#Requires -Version 5.1
<#
Creates (or refreshes) side-by-side test installs of CMS21 for local multiplayer tests (A and B for lane 1, C and D
for lane 2, all four for lane 3), and the server folder of every lane whose installs all exist (Server, Server2,
Server3) with its server_config.ini (port, max_players = 4). Deploy-Mod.ps1 -Lane <n> then copies the build into them.

Game binaries and data are NTFS hard links to the Steam install, so each install costs almost no
disk space. Every file a test run may write (MelonLoader, Mods, UserData, UserLibs, boot.config)
is a real copy: writing through a hard link would modify the Steam install.
#>
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021",
    [string]$TestRoot = "$env:USERPROFILE\CMS21-TestInstalls",
    [string[]]$Instances = @("A", "B", "C", "D"),
    [int[]]$Lanes = @(1, 2, 3),
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "TestLanes.psm1") -Force
$dataDirName = "Car Mechanic Simulator 2021_Data"
$copiedDirs = @("MelonLoader", "UserData")
# UserData\CMS21Together holds the per-install player key (player.json): a copy would give two installs one identity.
$instanceOwnedDirs = @("CMS21Together")
$extraMods = @("CMS21LoadOptimizer.dll", "LoadOptimizer.cfg")

Add-Type -Namespace Native -Name Fs -MemberDefinition @'
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
'@

function New-HardLink([string]$Path, [string]$Existing) {
    if ([System.IO.File]::Exists($Path)) { return }
    if (-not [Native.Fs]::CreateHardLink($Path, $Existing, [IntPtr]::Zero)) {
        throw "CreateHardLink failed ($([Runtime.InteropServices.Marshal]::GetLastWin32Error())): $Path"
    }
}

function New-HardLinkTree([string]$Source, [string]$Target) {
    Get-ChildItem -LiteralPath $Source -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($Source.Length).TrimStart('\')
        $dest = Join-Path $Target $relative
        [System.IO.Directory]::CreateDirectory((Split-Path $dest -Parent)) | Out-Null
        New-HardLink $dest $_.FullName
    }
}

if (-not (Test-Path -LiteralPath $GameDir)) { throw "Game not found: $GameDir" }

foreach ($name in $Instances) {
    $dir = Join-Path $TestRoot $name
    if ($Force -and (Test-Path -LiteralPath $dir)) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Write-Host "== Install $name -> $dir"

    Get-ChildItem -LiteralPath $GameDir -File | ForEach-Object { New-HardLink (Join-Path $dir $_.Name) $_.FullName }

    $dataDir = Join-Path $dir $dataDirName
    New-HardLinkTree (Join-Path $GameDir $dataDirName) $dataDir

    $bootConfig = Join-Path $dataDir "boot.config"
    $lines = Get-Content -LiteralPath $bootConfig | Where-Object { $_ -notmatch '^single-instance=' }
    Remove-Item -LiteralPath $bootConfig -Force
    Set-Content -LiteralPath $bootConfig -Value $lines -Encoding ascii

    foreach ($sub in $copiedDirs) {
        $target = Join-Path $dir $sub
        if (-not (Test-Path -LiteralPath $target)) {
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            Get-ChildItem -LiteralPath (Join-Path $GameDir $sub) | Where-Object { $_.Name -notin $instanceOwnedDirs } |
                Copy-Item -Destination $target -Recurse
        }
    }
    $copiedKey = Join-Path $dir "UserData\CMS21Together\player.json"
    $realKey = Join-Path $GameDir "UserData\CMS21Together\player.json"
    if ((Test-Path -LiteralPath $copiedKey) -and (Test-Path -LiteralPath $realKey) -and
        (Get-FileHash -LiteralPath $copiedKey).Hash -eq (Get-FileHash -LiteralPath $realKey).Hash) {
        Remove-Item -LiteralPath $copiedKey -Force
        Write-Host "   removed the player key copied from the game install; the mod creates its own"
    }
    $loaderLogs = Join-Path $dir "MelonLoader\Logs"
    if (Test-Path -LiteralPath $loaderLogs) { Get-ChildItem -LiteralPath $loaderLogs -File | Remove-Item -Force }

    foreach ($sub in @("Mods", "UserLibs", "Plugins")) { New-Item -ItemType Directory -Force -Path (Join-Path $dir $sub) | Out-Null }
    Copy-Item -Path (Join-Path $GameDir "UserLibs\*") -Destination (Join-Path $dir "UserLibs") -Force
    foreach ($mod in $extraMods) {
        $src = Join-Path $GameDir "Mods\$mod"
        if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $dir "Mods") -Force }
    }

    Set-Content -LiteralPath (Join-Path $dir "steam_appid.txt") -Value "1190000" -Encoding ascii
    Set-InstanceCompany $name
}

New-ProfileSeed

foreach ($lane in $Lanes) {
    $laneInfo = Get-TestLane $lane
    $missing = @($laneInfo.Instances | Where-Object { -not (Test-Path -LiteralPath (Join-Path $TestRoot $_)) })
    if ($missing) { Write-Host "Lane ${lane}: no server folder yet (installs missing: $($missing -join ', '))"; continue }
    New-Item -ItemType Directory -Force -Path $laneInfo.ServerDir | Out-Null
    Set-LaneServerConfig $laneInfo
    Write-Host "Lane ${lane}: $($laneInfo.Instances -join ', ') + $($laneInfo.ServerDir), port $($laneInfo.Port)"
}
New-Item -ItemType Directory -Force -Path (Join-Path $TestRoot "fixtures") | Out-Null
Write-Host "Done. Deploy the mod with Deploy-Mod.ps1 -Lane <n> (-Lane 3 fills all four installs and Server3)."

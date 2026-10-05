#Requires -Version 5.1
<#
Creates (or refreshes) two side-by-side test installs of CMS21 for local multiplayer tests.

Game binaries and data are NTFS hard links to the Steam install, so each install costs almost no
disk space. Every file a test run may write (MelonLoader, Mods, UserData, UserLibs, boot.config)
is a real copy: writing through a hard link would modify the Steam install.
#>
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Car Mechanic Simulator 2021",
    [string]$TestRoot = "$env:USERPROFILE\CMS21-TestInstalls",
    [string[]]$Instances = @("A", "B"),
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$dataDirName = "Car Mechanic Simulator 2021_Data"
$copiedDirs = @("MelonLoader", "UserData")
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
            Copy-Item -LiteralPath (Join-Path $GameDir $sub) -Destination $target -Recurse
        }
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
}

Write-Host "Done. Deploy the mod with Deploy-Mod.ps1."

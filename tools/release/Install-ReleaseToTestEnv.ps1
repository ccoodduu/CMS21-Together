#Requires -Version 5.1
<#
Installs release zips from Build-Release.ps1 into one test lane: the client zip into the lane's two test installs
(plus the test harness, built from this worktree), the server zip into the lane's server folder, keeping its
server_config.ini (port set to the lane's), Saves\ and Log\. Then run
tools\test-env\Run-Session.ps1 -Scenario release-smoke -Lane <n> -AllowForeignDeploy (the release is not this
worktree's build output). Deploy-Mod.ps1 -Lane <n> restores the dev build.

Without -ClientZip/-ServerZip the newest client zip in tools\release\out and its server zip are used.
-NoSteamLib removes UserLibs\steam_api64.dll from the test installs again (Steam off, as in dev runs).
#>
param(
    [int]$Lane = 1,
    [string]$ClientZip,
    [string]$ServerZip,
    [switch]$NoSteamLib,
    [string]$TestRoot = "$env:USERPROFILE\CMS21-TestInstalls"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Import-Module (Join-Path $repo "tools\test-env\TestLanes.psm1") -Force
$laneInfo = Get-TestLane $Lane

if (Get-LaneGameProcesses $laneInfo) { throw "A game instance of lane $Lane is running; close it first." }
$serverExe = Join-Path $laneInfo.ServerDir "CMS21_Together_Server.exe"
if (Get-Process -Name "CMS21_Together_Server" -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $serverExe }) {
    throw "The lane's server ($serverExe) is running; stop it first."
}

$outDir = Join-Path $PSScriptRoot "out"
if (-not $ClientZip) {
    $ClientZip = Get-ChildItem -LiteralPath $outDir -Filter "CMS21-Together-*-client.zip" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $ClientZip) { throw "No client zip in $outDir (run Build-Release.ps1 first)" }
}
if (-not $ServerZip) { $ServerZip = $ClientZip -replace '-client\.zip$', '-server.zip' }
foreach ($zip in @($ClientZip, $ServerZip)) { if (-not (Test-Path -LiteralPath $zip)) { throw "Zip not found: $zip" } }

$output = & dotnet build (Join-Path $repo "tools\TestHarness\TestHarness.csproj") -c Release -nologo -v q 2>&1
if ($LASTEXITCODE -ne 0) {
    $output | Select-String -Pattern "error" | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
    throw "Build failed: TestHarness"
}
$harness = Join-Path $repo "tools\TestHarness\bin\Release\TogetherTestHarness.dll"

$extractRoot = Join-Path $outDir "install-L$Lane"
if (Test-Path -LiteralPath $extractRoot) { Remove-Item -LiteralPath $extractRoot -Recurse -Force }
$clientExtract = Join-Path $extractRoot "client"
$serverExtract = Join-Path $extractRoot "server"
[System.IO.Compression.ZipFile]::ExtractToDirectory($ClientZip, $clientExtract)
[System.IO.Compression.ZipFile]::ExtractToDirectory($ServerZip, $serverExtract)

function Copy-Tree([string]$Source, [string]$Target) {
    $prefix = $Source.TrimEnd('\') + '\'
    Get-ChildItem -LiteralPath $Source -Recurse -File | ForEach-Object {
        $dest = Join-Path $Target $_.FullName.Substring($prefix.Length)
        [System.IO.Directory]::CreateDirectory((Split-Path $dest -Parent)) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
    }
}

foreach ($name in $laneInfo.Instances) {
    $dir = Join-Path $TestRoot $name
    if (-not (Test-Path -LiteralPath $dir)) { throw "Test install missing: $dir (run Setup-TestInstalls.ps1)" }
    Assert-InstanceIsolated $name
    $oldServer = Join-Path $dir "TogetherServer"
    if (Test-Path -LiteralPath $oldServer) { Remove-Item -LiteralPath $oldServer -Recurse -Force }
    Copy-Tree $clientExtract $dir
    Copy-Item -LiteralPath $harness -Destination (Join-Path $dir "Mods") -Force
    if ($NoSteamLib) { Remove-Item -LiteralPath (Join-Path $dir "UserLibs\steam_api64.dll") -Force }
}

$serverDir = $laneInfo.ServerDir
New-Item -ItemType Directory -Force -Path $serverDir | Out-Null
Get-ChildItem -LiteralPath $serverDir | Where-Object { $_.Name -notin @("server_config.ini", "Saves", "Log", "BugReports") } |
    Remove-Item -Recurse -Force
Copy-Tree $serverExtract $serverDir
Set-LaneServerConfig $laneInfo
Remove-Item -LiteralPath $extractRoot -Recurse -Force

$manifest = Get-Content -LiteralPath (Join-Path $serverDir "release.json") -Raw | ConvertFrom-Json
Write-Host ("Installed {0} into lane {1} ({2}, {3}, port {4}){5}" -f $manifest.fullVersion, $Lane,
    ($laneInfo.Instances -join ', '), $serverDir, $laneInfo.Port, $(if ($NoSteamLib) { ", without steam_api64.dll" } else { "" }))
Write-Host "Next: tools\test-env\Run-Session.ps1 -Scenario release-smoke -Lane $Lane -AllowForeignDeploy"

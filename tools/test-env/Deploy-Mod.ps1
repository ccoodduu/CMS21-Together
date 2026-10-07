#Requires -Version 5.1
<#
Builds client, server and test harness, then copies them into the test installs and the server folder of
one test lane. Run it from the worktree whose build the lane should test. Files that only a release install
(tools\release\Install-ReleaseToTestEnv.ps1) adds, such as UserLibs\steam_api64.dll and TogetherServer\, are removed.

It takes the lane locks (lane 3: lanes 1 and 2) and stops at once with "lane N is busy" while a test run holds one;
Run-Session.ps1 -Deploy calls it inside its own locks. Every install and the server folder get a deployed.json
(repo, commit, dirty, time). -NoBuild deploys the existing build; -BuildOnly builds and copies nothing.
#>
param(
    [int]$Lane = 1,
    [string]$TestRoot = "$env:USERPROFILE\CMS21-TestInstalls",
    [string]$Configuration = "Release",
    [switch]$NoBuild,
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Import-Module (Join-Path $PSScriptRoot "TestLanes.psm1") -Force
$laneInfo = Get-TestLane $Lane

$projects = @(
    "CMS21-Together-Client\CMS21-Together.csproj",
    "CMS21-Together-Server\CMS21-Together-Server.csproj",
    "tools\TestHarness\TestHarness.csproj"
)

$locks = $null
if (-not $BuildOnly) {
    try { $locks = Enter-LaneLocks -Lane $Lane -NoWait }
    catch { throw "Deploy to lane ${Lane}: $($_.Exception.Message); nothing copied." }
}
try {
    if (-not $NoBuild) {
        foreach ($project in $projects) {
            $output = & dotnet build (Join-Path $repo $project) -c $Configuration -nologo -v q 2>&1
            if ($LASTEXITCODE -ne 0) {
                $output | Select-String -Pattern "error" | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
                throw "Build failed: $project"
            }
        }
    }
    if ($BuildOnly) { Write-Host "Built $repo"; return }

    $clientBin = Join-Path $repo "CMS21-Together-Client\bin\$Configuration"
    $harnessBin = Join-Path $repo "tools\TestHarness\bin\$Configuration"
    $serverBin = Join-Path $repo "CMS21-Together-Server\bin\$Configuration"

    $commit = (git -C $repo rev-parse --short HEAD 2>$null)
    $dirty = [bool](git -C $repo status --porcelain --untracked-files=no 2>$null)
    $deployed = [ordered]@{ repo = "$repo"; commit = "$commit"; dirty = $dirty; time = (Get-Date).ToString("s"); lane = $Lane } | ConvertTo-Json

    foreach ($name in $laneInfo.Instances) {
        $dir = Join-Path $TestRoot $name
        if (-not (Test-Path -LiteralPath $dir)) { throw "Test install missing: $dir (run Setup-TestInstalls.ps1)" }
        Assert-InstanceIsolated $name
        Copy-Item (Join-Path $clientBin "CMS21-Together.dll") (Join-Path $dir "Mods") -Force
        Copy-Item (Join-Path $harnessBin "TogetherTestHarness.dll") (Join-Path $dir "Mods") -Force
        foreach ($lib in @("CMS21_Together_Core.dll", "Facepunch.Steamworks.Win64.dll")) {
            Copy-Item (Join-Path $clientBin $lib) (Join-Path $dir "UserLibs") -Force
        }
        $harnessDir = Join-Path $dir "UserData\TestHarness"
        New-Item -ItemType Directory -Force -Path $harnessDir | Out-Null
        Set-Content -LiteralPath (Join-Path $harnessDir "deployed.json") -Value $deployed -Encoding utf8
    }

    $serverDir = $laneInfo.ServerDir
    New-Item -ItemType Directory -Force -Path $serverDir | Out-Null
    Get-ChildItem -LiteralPath $serverBin | Where-Object { $_.Name -notin @("Log", "server_config.ini", "Saves") } |
        Copy-Item -Destination $serverDir -Recurse -Force
    Remove-ReleaseOnlyFiles $laneInfo
    Set-LaneServerConfig $laneInfo
    Set-Content -LiteralPath (Join-Path $serverDir "deployed.json") -Value $deployed -Encoding utf8

    Write-Host "Deployed lane $Lane ($($laneInfo.Instances -join ', '), $serverDir, port $($laneInfo.Port)) from $repo ($commit$(if ($dirty) { ', dirty' }))"
}
finally {
    Exit-LaneLocks $locks
}

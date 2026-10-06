#Requires -Version 5.1
<#
Builds client, server and test harness, then copies them into the test installs and the server folder of
one test lane. Run it from the worktree whose build the lane should test.
#>
param(
    [int]$Lane = 1,
    [string]$TestRoot = "$env:USERPROFILE\CMS21-TestInstalls",
    [string]$Configuration = "Release"
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
foreach ($project in $projects) {
    $output = & dotnet build (Join-Path $repo $project) -c $Configuration -nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Select-String -Pattern "error" | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
        throw "Build failed: $project"
    }
}

$clientBin = Join-Path $repo "CMS21-Together-Client\bin\$Configuration"
$harnessBin = Join-Path $repo "tools\TestHarness\bin\$Configuration"
$serverBin = Join-Path $repo "CMS21-Together-Server\bin\$Configuration"

foreach ($name in $laneInfo.Instances) {
    $dir = Join-Path $TestRoot $name
    if (-not (Test-Path -LiteralPath $dir)) { throw "Test install missing: $dir (run Setup-TestInstalls.ps1)" }
    Assert-InstanceIsolated $name
    Copy-Item (Join-Path $clientBin "CMS21-Together.dll") (Join-Path $dir "Mods") -Force
    Copy-Item (Join-Path $harnessBin "TogetherTestHarness.dll") (Join-Path $dir "Mods") -Force
    foreach ($lib in @("CMS21_Together_Core.dll", "Facepunch.Steamworks.Win64.dll")) {
        Copy-Item (Join-Path $clientBin $lib) (Join-Path $dir "UserLibs") -Force
    }
}

$serverDir = $laneInfo.ServerDir
New-Item -ItemType Directory -Force -Path $serverDir | Out-Null
Get-ChildItem -LiteralPath $serverBin | Where-Object { $_.Name -notin @("Log", "server_config.ini", "Saves") } |
    Copy-Item -Destination $serverDir -Recurse -Force
$config = Join-Path $serverDir "server_config.ini"
if (-not (Test-Path -LiteralPath $config)) {
    Set-Content -LiteralPath $config -Encoding ascii -Value @(
        "max_players = 4",
        "use_steam = False",
        'GSLT_Token = ""',
        "log_level = 1"
    )
}
$lines = @(Get-Content -LiteralPath $config | Where-Object { $_ -notmatch '^\s*port\s*=' }) + "port = $($laneInfo.Port)"
Set-Content -LiteralPath $config -Encoding ascii -Value $lines

Write-Host "Deployed lane $Lane ($($laneInfo.Instances -join ', '), $serverDir, port $($laneInfo.Port)) from $repo"

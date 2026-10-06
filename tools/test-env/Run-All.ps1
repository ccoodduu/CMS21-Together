#Requires -Version 5.1
<#
Regression run: deploys this worktree to the given lanes, runs every scenario (spread round-robin over the
lanes, which take turns with the game, see Run-Session.ps1) plus the server-only save checks, reruns a failed
scenario once to tell flaky from broken, and writes tools\runs\<timestamp>_regression.json.
#>
param(
    [int[]]$Lanes = @(1),
    [string[]]$Scenarios,
    [switch]$SkipDeploy
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$scenarioDir = Join-Path $PSScriptRoot "scenarios"
if (-not $Scenarios) {
    $Scenarios = Get-ChildItem -LiteralPath $scenarioDir -Filter "*.ps1" | Sort-Object Name |
        Where-Object { -not (Select-String -LiteralPath $_.FullName -Pattern "^# run-all: skip" -Quiet) } |
        ForEach-Object { $_.BaseName }
}

if (-not $SkipDeploy) {
    foreach ($lane in $Lanes) { & (Join-Path $PSScriptRoot "Deploy-Mod.ps1") -Lane $lane | Out-Host }
}

$started = Get-Date
$runSession = Join-Path $PSScriptRoot "Run-Session.ps1"
$laneWork = {
    param($RunSession, $Lane, $Names)
    foreach ($name in $Names) {
        $output = & $RunSession -Scenario $name -Lane $Lane *>&1 | Out-String
        $passed = $output -match "RESULT L$Lane $([regex]::Escape($name)): PASSED"
        if (-not $passed) {
            $retry = & $RunSession -Scenario $name -Lane $Lane *>&1 | Out-String
            $retryPassed = $retry -match "RESULT L$Lane $([regex]::Escape($name)): PASSED"
            $output += "`n--- rerun ---`n" + $retry
            [pscustomobject]@{ Scenario = $name; Lane = $Lane; Status = $(if ($retryPassed) { "FLAKY" } else { "FAILED" }); Output = $output }
        } else {
            [pscustomobject]@{ Scenario = $name; Lane = $Lane; Status = "PASSED"; Output = $output }
        }
    }
}

$jobs = @()
for ($i = 0; $i -lt $Lanes.Count; $i++) {
    $names = @(for ($j = $i; $j -lt $Scenarios.Count; $j += $Lanes.Count) { $Scenarios[$j] })
    if ($names.Count -gt 0) { $jobs += Start-Job -ScriptBlock $laneWork -ArgumentList $runSession, $Lanes[$i], $names }
}
$serverJob = Start-Job -ScriptBlock { param($script) & $script *>&1 | Out-String } -ArgumentList (Join-Path $PSScriptRoot "Test-ServerSaves.ps1")

$results = @($jobs | Wait-Job | Receive-Job)
$serverOutput = $serverJob | Wait-Job | Receive-Job
$results += [pscustomobject]@{
    Scenario = "server-saves"; Lane = 0
    Status = $(if ($serverOutput -match "RESULT server-saves: PASSED") { "PASSED" } else { "FAILED" })
    Output = $serverOutput
}
Get-Job | Remove-Job -Force

$runDir = Join-Path $repo "tools\runs"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$summary = [ordered]@{
    started = $started.ToString("s"); finished = (Get-Date).ToString("s")
    commit = (git -C $repo rev-parse --short HEAD)
    passed = -not ($results | Where-Object { $_.Status -eq "FAILED" })
    results = @($results | ForEach-Object { [ordered]@{ scenario = $_.Scenario; lane = $_.Lane; status = $_.Status } })
}
$file = Join-Path $runDir ("{0}_regression.json" -f $started.ToString("yyyyMMdd-HHmmss"))
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $file -Encoding utf8
foreach ($r in $results | Where-Object { $_.Status -ne "PASSED" }) {
    Set-Content -LiteralPath ($file -replace "\.json$", "_$($r.Scenario).log") -Value $r.Output -Encoding utf8
}

$results | ForEach-Object { Write-Host ("{0,-7} L{1} {2}" -f $_.Status, $_.Lane, $_.Scenario) }
Write-Host ("REGRESSION {0} ({1:mm\:ss}) -> {2}" -f $(if ($summary.passed) { "PASSED" } else { "FAILED" }), ((Get-Date) - $started), $file)
if ($summary.passed) { exit 0 } else { exit 1 }

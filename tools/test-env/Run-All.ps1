#Requires -Version 5.1
<#
Regression run: deploys this worktree to the given lanes, runs every scenario (spread round-robin over the
lanes, which take turns with the game, see Run-Session.ps1) plus the server-only save checks, reruns a failed
scenario once to tell flaky from broken, and writes tools\runs\<timestamp>_regression.json.
By default each lane runs its scenarios as one batch (Run-Session.ps1 -Scenarios: the games start once); scenarios
that need fresh games run as single sessions after the batch. A scenario that fails in the batch is rerun alone in a
fresh session: PASSED there makes it FLAKY with failedInBatch, so batch contamination shows apart from a real
failure. -Fresh runs every scenario as its own session, as before.
Scenarios whose first line is "# run-all: skip" only run when named in -Scenarios. -List prints the scenarios
that would run and exits.
#>
param(
    [int[]]$Lanes = @(1),
    [string[]]$Scenarios,
    [switch]$SkipDeploy,
    [switch]$Fresh,
    [switch]$List
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$scenarioDir = Join-Path $PSScriptRoot "scenarios"
Import-Module (Join-Path $PSScriptRoot "TestLanes.psm1") -Force
if (-not $Scenarios) {
    $Scenarios = Get-ChildItem -LiteralPath $scenarioDir -Filter "*.ps1" | Sort-Object Name |
        Where-Object { (Get-Content -LiteralPath $_.FullName -TotalCount 1) -ne "# run-all: skip" } |
        ForEach-Object { $_.BaseName }
}
if ($List) {
    $Scenarios | ForEach-Object {
        $single = -not $Fresh -and (Test-ScenarioNeedsFreshGame (Join-Path $scenarioDir "$_.ps1"))
        Write-Host ("{0}{1}" -f $_, $(if ($single) { " (single session)" } else { "" }))
    }
    return
}

if (-not $SkipDeploy) {
    foreach ($lane in $Lanes) { & (Join-Path $PSScriptRoot "Deploy-Mod.ps1") -Lane $lane | Out-Host }
}

$started = Get-Date
$runSession = Join-Path $PSScriptRoot "Run-Session.ps1"
$laneWork = {
    param($RunSession, $Lane, $Names, $Batch, $MinFreeMemoryGb)
    $gate = @{ Lane = $Lane }
    if ($MinFreeMemoryGb) { $gate.MinFreeMemoryGb = $MinFreeMemoryGb }

    function Get-Result([string]$Output, [string]$Name) {
        $found = [regex]::Matches($Output, "RESULT L$Lane $([regex]::Escape($Name)): (PASSED|FAILED)( \(batch\))?")
        if ($found.Count -eq 0) { return [pscustomobject]@{ Passed = $false; Batch = $false } }
        $last = $found[$found.Count - 1]
        [pscustomobject]@{ Passed = $last.Groups[1].Value -eq "PASSED"; Batch = $last.Groups[2].Success }
    }

    function Get-ScenarioOutput([string]$Output, [string]$Name) {
        $lines = $Output -split "\r?\n"
        $start = [array]::LastIndexOf($lines, "SCENARIO L$Lane $Name")
        if ($start -lt 0) { return $Output }
        $end = $start + 1
        while ($end -lt $lines.Count -and -not $lines[$end].StartsWith("SCENARIO L")) { $end++ }
        return ($lines[$start..($end - 1)] -join "`n")
    }

    function Invoke-RunSession([hashtable]$Arguments) {
        $collected = New-Object System.Collections.Generic.List[object]
        try { & $RunSession @Arguments @gate *>&1 | ForEach-Object { $collected.Add($_) } } catch { $collected.Add("ERROR: $($_.Exception.Message)") }
        $collected | Out-String
    }
    function Invoke-Single([string]$Name) { Invoke-RunSession @{ Scenario = $Name } }

    $batchOutput = if ($Batch) { Invoke-RunSession @{ Scenarios = $Names } } else { $null }
    foreach ($name in $Names) {
        if ($Batch) {
            $first = Get-Result $batchOutput $name
            $output = Get-ScenarioOutput $batchOutput $name
        } else {
            $output = Invoke-Single $name
            $first = Get-Result $output $name
        }
        if ($first.Passed) {
            [pscustomobject]@{ Scenario = $name; Lane = $Lane; Status = "PASSED"; Batch = $first.Batch; FailedInBatch = $false; Output = $output }
            continue
        }
        $retry = Invoke-Single $name
        $retryPassed = (Get-Result $retry $name).Passed
        $output += "`n--- rerun (single session) ---`n" + $retry
        [pscustomobject]@{
            Scenario = $name; Lane = $Lane; Status = $(if ($retryPassed) { "FLAKY" } else { "FAILED" })
            Batch = $first.Batch; FailedInBatch = $first.Batch; Output = $output
        }
    }
}

$jobs = @()
for ($i = 0; $i -lt $Lanes.Count; $i++) {
    $names = @(for ($j = $i; $j -lt $Scenarios.Count; $j += $Lanes.Count) { $Scenarios[$j] })
    $minFreeMemoryGb = if ($i -gt 0) { 10 } else { $null }
    if ($names.Count -gt 0) { $jobs += Start-Job -ScriptBlock $laneWork -ArgumentList $runSession, $Lanes[$i], $names, (-not $Fresh), $minFreeMemoryGb }
}
$serverJob = Start-Job -ScriptBlock { param($script) & $script *>&1 | Out-String } -ArgumentList (Join-Path $PSScriptRoot "Test-ServerSaves.ps1")

$results = @($jobs | Wait-Job | Receive-Job)
$serverOutput = $serverJob | Wait-Job | Receive-Job
$results += [pscustomobject]@{
    Scenario = "server-saves"; Lane = 0
    Status = $(if ($serverOutput -match "RESULT server-saves: PASSED") { "PASSED" } else { "FAILED" })
    Batch = $false; FailedInBatch = $false
    Output = $serverOutput
}
Get-Job | Remove-Job -Force

$runDir = Join-Path $repo "tools\runs"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$summary = [ordered]@{
    started = $started.ToString("s"); finished = (Get-Date).ToString("s")
    commit = (git -C $repo rev-parse --short HEAD)
    mode = $(if ($Fresh) { "fresh" } else { "batch" })
    passed = -not ($results | Where-Object { $_.Status -eq "FAILED" })
    results = @($results | ForEach-Object {
        [ordered]@{ scenario = $_.Scenario; lane = $_.Lane; status = $_.Status; batch = $_.Batch; failedInBatch = $_.FailedInBatch }
    })
}
$file = Join-Path $runDir ("{0}_regression.json" -f $started.ToString("yyyyMMdd-HHmmss"))
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $file -Encoding utf8
foreach ($r in $results | Where-Object { $_.Status -ne "PASSED" }) {
    Set-Content -LiteralPath ($file -replace "\.json$", "_$($r.Scenario).log") -Value $r.Output -Encoding utf8
}

$results | ForEach-Object { Write-Host ("{0,-7} L{1} {2}{3}" -f $_.Status, $_.Lane, $_.Scenario, $(if ($_.FailedInBatch) { " (failed in the batch)" } else { "" })) }
Write-Host ("REGRESSION {0} ({1:mm\:ss}) -> {2}" -f $(if ($summary.passed) { "PASSED" } else { "FAILED" }), ((Get-Date) - $started), $file)
if ($summary.passed) { exit 0 } else { exit 1 }

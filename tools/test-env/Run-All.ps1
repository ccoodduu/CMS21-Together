#Requires -Version 5.1
<#
Regression run: deploys this worktree to the given lanes, runs the selected scenarios (spread round-robin over the
lanes, which take turns with the game, see Run-Session.ps1) plus the server-only save checks, reruns a failed
scenario once to tell flaky from broken, and writes tools\runs\<timestamp>_regression.json.
By default each lane runs its scenarios as one batch (Run-Session.ps1 -Scenarios: the games start once); scenarios
that need fresh games run as single sessions after the batch. A scenario that fails in the batch is rerun alone in a
fresh session: PASSED there makes it FLAKY with failedInBatch, so batch contamination shows apart from a real
failure. -Fresh runs every scenario as its own session, as before.
Scenarios with a "# run-all: skip" header line only run when named in -Scenarios. -List prints the scenarios that
would run, with the reason, and exits.

Test areas (policy: a change merges after the scenarios of its areas plus the smoke set; the full set runs for
changes the path table cannot place and before a release). Every scenario carries a "# areas: a, b" header line;
the vocabulary and the path table are in TestAreas.psm1, and "smoke" in the list puts a scenario in the smoke set.
  -Areas a,b         scenarios with any of these areas, plus the smoke set (-NoSmoke leaves it out)
  -Smoke             the smoke set (alone: only the smoke set)
  -Changed [<ref>]   maps the files changed since the merge base with <ref> (default origin/main; committed,
                     uncommitted and untracked) to areas and runs those plus the smoke set; a changed scenario file
                     adds that scenario and its areas; a file outside the table runs every scenario; only docs and
                     other non-mod files run nothing
Without -Areas, -Smoke or -Changed every scenario runs; -Scenarios adds named scenarios to any selection.
#>
param(
    [int[]]$Lanes = @(1),
    [string[]]$Scenarios,
    [string[]]$Areas,
    [switch]$Smoke,
    [switch]$NoSmoke,
    [switch]$Changed,
    [Parameter(Position = 0)][string]$ChangedSince,
    [switch]$SkipDeploy,
    [switch]$Fresh,
    [switch]$List
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$scenarioDir = Join-Path $PSScriptRoot "scenarios"
Import-Module (Join-Path $PSScriptRoot "TestLanes.psm1") -Force
Import-Module (Join-Path $PSScriptRoot "TestAreas.psm1") -Force

if ($ChangedSince -and -not $Changed) { throw "A git ref ('$ChangedSince') only goes with -Changed." }
if ($Smoke -and $NoSmoke) { throw "-Smoke and -NoSmoke exclude each other." }
$knownAreas = Get-KnownAreas
$wantAreas = @($Areas | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ })
if ($wantAreas -contains "smoke") { $Smoke = [switch]$true; $wantAreas = @($wantAreas | Where-Object { $_ -ne "smoke" }) }
$unknown = @($wantAreas | Where-Object { $_ -notin $knownAreas })
if ($unknown) { throw "Unknown area(s): $($unknown -join ', ') (known: $($knownAreas -join ', '))" }

$headers = Get-ScenarioHeaders $scenarioDir
foreach ($h in $headers.Values) {
    if (-not $h.HasAreas) { Write-Warning "scenarios\$($h.Name).ps1 has no '# areas:' line; area selections always include it" }
    if ($h.UnknownAreas) { Write-Warning "scenarios\$($h.Name).ps1 names unknown area(s): $($h.UnknownAreas -join ', ')" }
}
foreach ($name in $Scenarios) { if (-not $headers.Contains($name)) { throw "Unknown scenario: $name" } }

$picks = [ordered]@{}
function Add-Pick([string]$Name, [string]$Reason) {
    if (-not $picks.Contains($Name)) { $picks[$Name] = New-Object System.Collections.Generic.List[string] }
    if (-not $picks[$Name].Contains($Reason)) { $picks[$Name].Add($Reason) }
}

$areaMode = $wantAreas.Count -gt 0 -or $Smoke -or $Changed
$fullReason = $null
$changedScenarios = @()
$useSmoke = $Smoke -or ($wantAreas.Count -gt 0 -and -not $NoSmoke)
if ($Changed) {
    $ref = if ($ChangedSince) { $ChangedSince } else { "origin/main" }
    $diff = Get-ChangedFiles $repo $ref
    Write-Host ("Changed since {0} (merge base {1}): {2} file(s)" -f $ref, $diff.Base.Substring(0, 7), $diff.Files.Count)
    foreach ($file in $diff.Files) {
        $hit = Resolve-ChangedFile $file $headers
        $why = switch ($hit.Kind) {
            "scenario" { "scenario $($hit.Scenario) + its areas ($($hit.Areas -join ', '))" }
            "areas" { $hit.Areas -join ', ' }
            "full" { "full set (harness core)" }
            "smoke" { "smoke set" }
            "none" { "no tests" }
            default { "full set (mod code not in the path table)" }
        }
        Write-Host ("  {0} -> {1}" -f $hit.File, $why)
        switch ($hit.Kind) {
            "scenario" { $changedScenarios += $hit.Scenario; $wantAreas += $hit.Areas }
            "areas" { $wantAreas += $hit.Areas }
            "smoke" { $useSmoke = $true }
            "none" { }
            default { if (-not $fullReason) { $fullReason = "$($hit.File): $why" } }
        }
    }
    if ($fullReason) { Write-Host "Full set because of $fullReason" }
    $wantAreas = @($wantAreas | Select-Object -Unique)
    if ($wantAreas.Count -gt 0 -or $changedScenarios.Count -gt 0) { $useSmoke = $useSmoke -or -not $NoSmoke }
    if ($wantAreas.Count -gt 0) { Write-Host "Areas: $($wantAreas -join ', ')" }
}

foreach ($name in $Scenarios) { Add-Pick $name "named" }
foreach ($h in $headers.Values) {
    if ($h.Skip) { continue }
    if (-not $areaMode -and -not $Scenarios) { Add-Pick $h.Name "all"; continue }
    if ($fullReason) { Add-Pick $h.Name "full set"; continue }
    if (-not $areaMode) { continue }
    if ($useSmoke -and $h.Smoke) { Add-Pick $h.Name "smoke" }
    $matched = @($h.Areas | Where-Object { $_ -in $wantAreas })
    if ($matched) { Add-Pick $h.Name "areas: $($matched -join ', ')" }
    elseif (-not $h.HasAreas -and $wantAreas.Count -gt 0) { Add-Pick $h.Name "no '# areas:' line" }
}
foreach ($name in $changedScenarios) {
    if ($headers[$name].Skip -and -not $picks.Contains($name)) {
        Write-Host "  (changed scenario $name is marked '# run-all: skip'; name it in -Scenarios to run it)"
    } elseif (-not $headers[$name].Skip) { Add-Pick $name "changed" }
}
$Scenarios = @($picks.Keys | Sort-Object)

if ($List) {
    foreach ($name in $Scenarios) {
        $single = -not $Fresh -and (Test-ScenarioNeedsFreshGame (Join-Path $scenarioDir "$name.ps1"))
        $reason = if ($areaMode) { "  <- " + ($picks[$name] -join "; ") } else { "" }
        Write-Host ("{0}{1}{2}" -f $name, $(if ($single) { " (single session)" } else { "" }), $reason)
    }
    Write-Host $(if ($Scenarios.Count) { "{0} scenario(s) + server-saves" -f $Scenarios.Count } else { "Nothing to run: no scenario selected." })
    return
}
if ($Scenarios.Count -eq 0) {
    Write-Host "Nothing to run: no scenario selected."
    exit 0
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

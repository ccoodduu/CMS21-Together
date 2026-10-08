#Requires -Version 5.1
<#
Long soak (multiplayer-soak-and-scale D5): runs Run-Session.ps1 -Scenario soak on a lane (default 3, four clients)
for -Hours with a seed, then Show-SoakReport.ps1 on its run folder. Storms every 20 minutes from one hour on
(-StormEveryMinutes overrides, 0 = none). It waits until nobody has touched the PC for 10 minutes before it starts
the games (-Now skips that; -MaxWaitHours gives up). -Replay <actions.jsonl> repeats a previous run's actions.
-InventoryCap sets the soak's inventory ceiling (default: the scenario's 300; 0 = none).
Contention groups (state-merges-and-contention D13, weight 15) are part of every run; -NoContention leaves them out and
-ContentionKinds a,b forces the kinds drawn.
#>
param(
    [double]$Hours = 0.25,
    [int]$Seed = 0,
    [int]$Lane = 3,
    [string[]]$Headless = @(),
    [double]$StormEveryMinutes = -1,
    [double]$CheckEveryMinutes = 0,
    [string]$Replay = "",
    [int]$InventoryCap = -1,
    [switch]$StopOnFailure,
    [switch]$NoContention,
    [string[]]$ContentionKinds = @(),
    [switch]$Deploy,
    [switch]$Now,
    [double]$MaxWaitHours = 12
)

$ErrorActionPreference = "Stop"
if ($Seed -eq 0) { $Seed = [int](Get-Date -Format "MMddHHmmss") }
if ($StormEveryMinutes -lt 0) { $StormEveryMinutes = if ($Hours -ge 1) { 20 } else { 0 } }

if (-not $Now) {
    $idleScript = Join-Path $PSScriptRoot "Get-UserIdleSeconds.ps1"
    $giveUp = (Get-Date).AddHours($MaxWaitHours)
    $lastNote = [datetime]::MinValue
    while ([double](& $idleScript) -lt 600) {
        if ((Get-Date) -gt $giveUp) { throw "The PC was in use for $MaxWaitHours hours; soak not started (use -Now to start anyway)." }
        if (((Get-Date) - $lastNote).TotalMinutes -ge 5) { Write-Host "Waiting until the PC has been idle for 10 minutes (-Now starts at once)"; $lastNote = Get-Date }
        Start-Sleep -Seconds 30
    }
}

$scenarioArgs = @{ Minutes = [math]::Round($Hours * 60, 2); Seed = $Seed; StormEveryMinutes = $StormEveryMinutes; CheckEveryMinutes = $CheckEveryMinutes }
if ($Replay) { $scenarioArgs.Replay = (Resolve-Path -LiteralPath $Replay).Path }
if ($StopOnFailure) { $scenarioArgs.StopOnFailure = $true }
if (-not $NoContention) { $scenarioArgs.Contention = $true }
if ($ContentionKinds.Count -gt 0) { $scenarioArgs.ContentionKinds = $ContentionKinds }
if ($InventoryCap -ge 0) { $scenarioArgs.InventoryCap = $InventoryCap }
Write-Host "SOAK lane $Lane, $Hours h, seed $Seed, storms every $StormEveryMinutes min$(if ($Headless) { ", headless $($Headless -join ',')" })"

$output = New-Object System.Collections.Generic.List[string]
& (Join-Path $PSScriptRoot "Run-Session.ps1") -Lane $Lane -Scenario soak -ScenarioArgs $scenarioArgs -Headless $Headless -Deploy:$Deploy *>&1 |
    ForEach-Object { $line = "$_"; $output.Add($line); Write-Host $line }

$runDir = @($output | ForEach-Object { if ($_ -match "^Run folder: (.+)$") { $Matches[1].Trim() } }) | Select-Object -Last 1
if (-not $runDir) { throw "Run-Session printed no run folder; see the output above." }
& (Join-Path $PSScriptRoot "Show-SoakReport.ps1") $runDir
Write-Host "Seed $Seed; replay with: Run-Soak.ps1 -Lane $Lane -Hours $Hours -Replay `"$(Join-Path $runDir 'actions.jsonl')`""

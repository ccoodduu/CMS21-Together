# run-all: lane 3
# areas: persistence, connect, presence, parts, tools, testdrive
# multiplayer-soak-and-scale D8 (tasks 6.2, 6.3): every disconnect storm kind once, in a seeded order, each followed
# by a quiesced checkpoint: K1 crash and relaunch, K2 leave/rejoin flaps, K3 stall past the heartbeat timeout
# (net-hold out), K4 three clients drop at once and rejoin together (three or more clients), K5 a client killed while
# it holds a part claim, the balancer and then a test drive, K6 graceful server stop, K7 server kill after a save and
# mid-activity, K8 server kill mid-join. -Kinds K1,K2,K3 runs only those (two-instance runs on lane 1).
param($Ctx, [int]$Seed = 0, [string[]]$Kinds = @())

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")
Import-Module (Join-Path $PSScriptRoot "..\PerfSampler.psm1")
Import-Module (Join-Path $PSScriptRoot "..\StormKinds.psm1")

$names = @($Ctx.Instances)
if ($Seed -eq 0) { $Seed = [int](Get-Date -Format "MMddHHmmss") }
$rng = New-Object System.Random($Seed)
$Ctx.Result["seed"] = $Seed
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

$available = @(Get-StormKinds -InstanceCount $names.Count)
$Kinds = @($Kinds | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ })
$order = if ($Kinds.Count -gt 0) { $Kinds } else { @($available | Sort-Object { $rng.Next() }) }
$unknown = @($order | Where-Object { $available -notcontains $_ })
if ($unknown) { throw "Storm kinds not available with $($names.Count) clients: $($unknown -join ', ') (available: $($available -join ', '))" }
Write-Host "STORMS seed $Seed, order $($order -join ', ')"

Set-ServerConfigValues $Ctx.ServerDir @{ perf_log_interval_seconds = 10; autosave_interval_seconds = 30; desync_check_interval_seconds = 5; desync_autofix = $true }
$scenarioStart = Get-Date
Restart-TestServer
$clientMarks = Get-ClientLogMarks $names

Wait-AllInMenu $names
Connect-ScaleInstances $names | Out-Null
Send-ServerCommand "money add 100000"
Send-HarnessCommand -Instance $names[0] -Verb car-spawn -Arguments "0 car_boltatlanta 0 auto" | Out-Null
$notReady = @(Wait-CarsReady $names 180)
Check ($notReady.Count -eq 0) "the starting car is Ready everywhere ($($notReady -join '; '))"
$start = Invoke-ScaleCheckpoint -Ctx $Ctx -Index 0 -Label "start" -Instances $names
Check $start.Passed "checkpoint before the storms"
Add-PerfSample -RunDir $Ctx.RunDir -Instances $names -ServerDir $Ctx.ServerDir | Out-Null

$index = 0
foreach ($kind in $order) {
    $index++
    $storm = Invoke-StormKind -Ctx $Ctx -Kind $kind -Index $index -Seed $rng.Next(1, [int]::MaxValue)
    Check $storm.passed "storm $index $kind$(if (-not $storm.passed) { ': ' + ($storm.failures -join '; ') })"
    foreach ($name in $names) {
        if (-not (Test-InGarage (Get-HarnessStatus $name))) {
            try { Connect-ScaleInstance $name | Out-Null } catch { Check $false "$name rejoins after $kind`: $($_.Exception.Message)" }
        }
    }
    $checkpoint = Invoke-ScaleCheckpoint -Ctx $Ctx -Index $index -Label $kind -Instances $names
    Check $checkpoint.Passed "checkpoint after $kind$(if (-not $checkpoint.Passed) { ': ' + ($checkpoint.Record.problems -join '; ') })"
    Add-PerfSample -RunDir $Ctx.RunDir -Instances $names -ServerDir $Ctx.ServerDir | Out-Null
}

$confirmed = @(Find-ServerLogLines -ServerDir $Ctx.ServerDir -Since $scenarioStart -Pattern "\[Desync\] .*(resending|is persistent|confirmed \(autofix off\))")
Check ($confirmed.Count -eq 0) "no desync was confirmed ($($confirmed.Count): $(@($confirmed | Select-Object -First 3) -join ' / '))"
$errors = @(Find-LogErrors -Ctx $Ctx -Since $scenarioStart -ClientMarks $clientMarks -Allow (Read-AllowList (Join-Path $PSScriptRoot "soak-allow.txt")))
Check ($errors.Count -eq 0) "no errors outside the allow-list ($($errors.Count)$(if ($errors) { ': ' + (@($errors | Select-Object -First 5) -join ' / ') }))"

$Ctx.Result.notes += "seed $Seed, kinds $($order -join ', ')"
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

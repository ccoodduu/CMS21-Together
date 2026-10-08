# areas: resync, parts, details, tools
# desync-detection-and-resync (b) and state-merges-and-contention D11/D12: both clients match the server on every
# digest key; B's car, inventory, car details, machines, warehouse and garage are then changed locally without a packet
# (part-corrupt, inv-corrupt, state-corrupt); the server confirms each mismatch over two rounds, writes a diff record
# and resends that key to B (resend turned on for every key in this run), after which B matches A again. A "not ready"
# round between two mismatches keeps the mismatch (forced rounds, long interval), and a key that stays not ready longer
# than desync_stall_seconds (20 s here) is reported once.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "0"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader 0 not Ready"
}

function Try-ServerLog([string]$Pattern, [int]$After, [int]$TimeoutSec) {
    try { Wait-ServerLog -Pattern $Pattern -After $After -TimeoutSec $TimeoutSec } catch { $null }
}

function Server-Lines([int]$After) { @(Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $After) }

$allKeys = "world, inventory, cars, car-placement, car-details, workshop-tools, warehouse, garage, jobs"
Set-ServerConfigValues $Ctx.ServerDir @{ desync_resend_keys = $allKeys; desync_stall_seconds = 20; desync_check_interval_seconds = 5 }
Restart-TestServer

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Send-HarnessCommand -Instance $a -Verb part-fast-unmount -Arguments "0" | Out-Null
Start-Sleep -Seconds 4

$da = Send-HarnessCommand -Instance $a -Verb digest-show
$db = Send-HarnessCommand -Instance $b -Verb digest-show
$keys = "world", "inventory", "car-placement", "workshop-tools", "warehouse", "garage", "jobs", "cars:0", "car-details:0"
foreach ($key in $keys) {
    Check ($da.$key.hash -and $da.$key.hash -eq $db.$key.hash) "A and B have the same $key digest ($($da.$key.hash) / $($db.$key.hash))"
}

$mark = Get-ServerLogMark
Send-ServerCommand "desync check"
foreach ($key in $keys) {
    Check ([bool](Try-ServerLog "\[Desync\] $key for client \d+: match" $mark 10)) "the server reports a $key match"
}
Start-Sleep -Seconds 2
$serverLog = (Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $mark) -join "`n"
Check (-not ($serverLog -match "mismatch")) "no mismatch in the forced check"

$mark = Get-ServerLogMark
$corrupt = Send-HarnessCommand -Instance $b -Verb part-corrupt -Arguments "0"
Write-Host "B corrupts $($corrupt.key)"
$repair = Try-ServerLog "\[Desync\] cars:0 .*resending" $mark 60
Check ([bool]$repair) "the server repairs B's car ($repair)"
Start-Sleep -Seconds 4
$ra = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "0"
$rb = Send-HarnessCommand -Instance $b -Verb car-ready -Arguments "0"
Check ($ra.stateHash -eq $rb.stateHash) "B's car matches A's after the repair ($($ra.stateHash) / $($rb.stateHash))"

$mark = Get-ServerLogMark
$removed = Send-HarnessCommand -Instance $b -Verb inv-corrupt
Write-Host "B drops $($removed.id) locally"
$repair = Try-ServerLog "\[Desync\] inventory .*resending" $mark 60
Check ([bool]$repair) "the server repairs B's inventory ($repair)"
Start-Sleep -Seconds 4
$diff = Compare-HarnessDumps (Send-HarnessCommand -Instance $a -Verb dump) (Send-HarnessCommand -Instance $b -Verb dump) -Sections @("inventory")
Check ($diff.Count -eq 0) "B's inventory matches A's after the repair"

# One corrupt step per new digest key (state-merges-and-contention task 9.3).
$playerB = (Get-HarnessStatus -Instance $b).playerId
function Test-Repaired([string]$Key, [string]$Verb, [string]$Arguments, [scriptblock]$Same) {
    $mark = Get-ServerLogMark
    $corrupt = Send-HarnessCommand -Instance $b -Verb $Verb -Arguments $Arguments
    Write-Host "B corrupts $Key ($($corrupt | ConvertTo-Json -Compress))"
    $repair = Try-ServerLog "\[Desync\] $([regex]::Escape($Key)) .*resending" $mark 60
    Check ([bool]$repair) "the server repairs B's $Key ($repair)"
    Start-Sleep -Seconds 5
    Check (& $Same) "B's $Key equals A's after the repair"
    $mark = Get-ServerLogMark
    Send-ServerCommand "desync check"
    Check ([bool](Try-ServerLog "\[Desync\] $([regex]::Escape($Key)) for client $playerB`: match" $mark 10)) "B's $Key matches the server again"
}
function Same-Sections([string[]]$Sections) {
    (Compare-HarnessDumps (Send-HarnessCommand -Instance $a -Verb dump) (Send-HarnessCommand -Instance $b -Verb dump) -Sections $Sections).Count -eq 0
}
function Same-Digest([string]$Key) {
    $ha = (Send-HarnessCommand -Instance $a -Verb digest-show -Arguments $Key).$Key.hash
    $hb = (Send-HarnessCommand -Instance $b -Verb digest-show -Arguments $Key).$Key.hash
    $ha -and $ha -eq $hb
}
Test-Repaired "car-details:0" state-corrupt "car-details 0" { Same-Sections @("carDetails") }
Test-Repaired "workshop-tools" state-corrupt "workshop-tools" { (Same-Sections @("tools", "inventory")) -and (Same-Digest "workshop-tools") }
Test-Repaired "warehouse" state-corrupt "warehouse" { (Same-Digest "warehouse") -and (Same-Sections @("inventory")) }
Test-Repaired "garage" state-corrupt "garage" { (Same-Sections @("stats", "skills")) -and (Same-Digest "garage") }

# A "not ready" answer between two mismatches keeps the mismatch (task 9.5): forced rounds only.
Send-ServerCommand "desync interval 600"
Start-Sleep -Seconds 6
$mark = Get-ServerLogMark
$removed = Send-HarnessCommand -Instance $b -Verb inv-corrupt
Write-Host "B drops $($removed.id) locally; forced rounds with one not-ready round"
Send-ServerCommand "desync check"
Try-ServerLog "\[Desync\] inventory for client $playerB`: mismatch" $mark 10 | Out-Null
Send-HarnessCommand -Instance $b -Verb digest-hold -Arguments "inventory notready" | Out-Null
$round = Get-ServerLogMark
Send-ServerCommand "desync check"
Check ([bool](Try-ServerLog "\[Desync\] inventory for client $playerB`: not ready" $round 10)) "the second forced round finds B's inventory not ready"
Send-HarnessCommand -Instance $b -Verb digest-hold -Arguments "inventory off" | Out-Null
Send-ServerCommand "desync check"
$repair = Try-ServerLog "\[Desync\] inventory .*resending" $mark 15
Check ([bool]$repair) "the mismatch is confirmed and repaired in the third forced round ($repair)"
Start-Sleep -Seconds 4
Check (Same-Sections @("inventory")) "B's inventory matches A's after the repair"
Send-ServerCommand "desync interval 5"

# Stall warning (task 10.4): desync_stall_seconds = 20, B's inventory not ready for 27 s.
Start-Sleep -Seconds 6
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb digest-hold -Arguments "inventory notready" | Out-Null
Start-Sleep -Seconds 27
Send-ServerCommand "desync"
Start-Sleep -Seconds 1
$held = Server-Lines $mark
Send-HarnessCommand -Instance $b -Verb digest-hold -Arguments "inventory off" | Out-Null
$warnings = @($held | Where-Object { $_ -match "\[WARN\] \[Desync\] inventory for client $playerB has not been ready for \d+ s" })
Check ($warnings.Count -eq 1) "one stall warning for B's inventory ($($warnings.Count))"
Check (@($held | Where-Object { $_ -match "open stall: inventory for client $playerB" }).Count -ge 1) "the desync output lists the open stall"
Start-Sleep -Seconds 15
$after = Server-Lines $mark
Check (@($after | Where-Object { $_ -match "\[WARN\] \[Desync\] inventory for client $playerB has not been ready" }).Count -eq 1) "no second warning after the hold ends"
Check (@($after | Where-Object { $_ -match "\[Desync\] inventory for client $playerB is ready again" }).Count -eq 1) "the server notes that B's inventory is ready again"
$otherStalls = @(Server-Lines 0 | Where-Object { $_ -match "\[WARN\] \[Desync\] .* has not been ready" -and $_ -notmatch "inventory for client $playerB " })
Check ($otherStalls.Count -eq 0) "no other key stalled during the run ($($otherStalls -join ' / '))"

$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb digest-hold -Arguments "world on" | Out-Null
$persistent = Try-ServerLog "\[Desync\] world for client \d+ is persistent" $mark 90
Check ([bool]$persistent) "a repair that does not hold becomes persistent ($persistent)"
Start-Sleep -Seconds 2
$clientLogB = Get-Content -LiteralPath (Join-Path $env:USERPROFILE "CMS21-TestInstalls\$b\MelonLoader\Latest.log") -Raw
Check ($clientLogB -match "\[Desync\] world\s+is out of sync \(persistent: True\)") "B got the persistent notice"
$resends = @((Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $mark) | Where-Object { $_ -match "\[Desync\] world .*resending" })
Check ($resends.Count -eq 1) "the world section was resent once before the backoff ($($resends.Count))"
Send-HarnessCommand -Instance $b -Verb digest-hold -Arguments "world off" | Out-Null

$records = @(Get-ChildItem -LiteralPath (Join-Path $Ctx.ServerDir "Log\desync") -Filter "*.json" -ErrorAction SilentlyContinue)
Check ($records.Count -ge 7) "diff records were written ($($records.Count))"
if ($records.Count -gt 0) { Copy-Item -LiteralPath $records.FullName -Destination $Ctx.RunDir }
Remove-Item -LiteralPath (Join-Path $Ctx.ServerDir "Log\desync") -Recurse -Force -ErrorAction SilentlyContinue

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

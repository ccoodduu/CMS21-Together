# desync-detection-and-resync (b): both clients match the server on every section; B's car and inventory are then
# changed locally without a packet (part-corrupt, inv-corrupt); the server confirms each mismatch over two rounds,
# writes a diff record and resends the section to B, after which B matches A again.
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

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
foreach ($key in "world", "inventory", "car-placement", "cars:0") {
    Check ($da.$key.hash -and $da.$key.hash -eq $db.$key.hash) "A and B have the same $key digest ($($da.$key.hash) / $($db.$key.hash))"
}

$mark = Get-ServerLogMark
Send-ServerCommand "desync check"
foreach ($key in "world", "inventory", "car-placement") {
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
Check ($records.Count -ge 2) "diff records were written ($($records.Count))"
if ($records.Count -gt 0) { Copy-Item -LiteralPath $records.FullName -Destination $Ctx.RunDir }
Remove-Item -LiteralPath (Join-Path $Ctx.ServerDir "Log\desync") -Recurse -Force -ErrorAction SilentlyContinue

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

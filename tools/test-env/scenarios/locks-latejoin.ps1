# areas: locks, persistence, visuals
# part-locks 10.3: late join (D11). B joins while A holds a part lock and pours engine oil. B's mirror equals the
# server's table before B plays, B's hover of A's part shows the label without a highlight, B's oil fill is refused on
# B's game, and no ghost or bolt replay starts on B for the locks of the snapshot. When A finishes, B sees the commit
# and the releases.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1")

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$oil = "f:EngineOil.0"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Cmd $Name car-ready "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Try-Wait([string]$Name, [string]$Arguments, [int]$TimeoutSec = 10, [switch]$UntilFinished) {
    $t = Cmd $Name lock-try $Arguments
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Cmd $Name lock-try "result $($t.tryId)"
        $settled = $r.result -ne "pending" -and (-not $UntilFinished -or $r.result -ne "granted" -or -not $r.started -or $r.finished -or $r.state -match "^not ")
        if ($settled) { return $r }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $r
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Cmd $a guard-set "Off" | Out-Null
$idA = (Get-HarnessStatus $a).playerId
Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Start-Sleep -Seconds 2

$part = @(Cmd $a vfx-parts "$loader")[0].key
Write-Host "part: $part"
$answer = Request-Lock $a "$loader unmount $part"
Check ($answer.result -eq "granted") "A holds $part"
$r = Try-Wait $a "$loader fill EngineOil 0"
Check ($r.result -eq "granted" -and $r.started) "A pours engine oil under a lock ($($r.result), started $($r.started))"
$server = Get-ServerLocks
Check ($server.Count -eq 2) "the server holds A's two locks ($($server.Line))"

Connect-HarnessInstance $b; Wait-InGarage $b
Cmd $b guard-set "Off" | Out-Null
Wait-Ready $b | Out-Null
$ids = @($server.Records | ForEach-Object { $_.LockId } | Sort-Object) -join ","
$mirror = Get-LockMirror $b
$mirrorIds = @($mirror.mirror | ForEach-Object { $_.lockId } | Sort-Object) -join ","
Check ($mirrorIds -eq $ids -and -not @($mirror.mirror | Where-Object { $_.owner -ne $idA })) "B's mirror equals the server's table after the join ([$mirrorIds] / [$ids])"

$hover = Cmd $b lock-hover "$loader $part"
Check (-not $hover.highlighted -and $hover.label -match "is working on this part") "B's hover of A's part has no highlight and names A ($($hover.label))"
$r = Try-Wait $b "$loader fill EngineOil 0"
Check ($r.result -eq "refusedLocally" -and $r.holder -eq $idA) "B's oil fill is refused on B's game while A pours ($($r.result) $($r.holder) $($r.conflictKey))"
$visuals = (Cmd $b dump).visuals
Check (@($visuals.ghostsActive).Count -eq 0 -and @($visuals.boltsActive).Count -eq 0) "no ghost or bolt replay started on B for the snapshot's locks ($($visuals | ConvertTo-Json -Compress -Depth 4))"

# A finishes: the oil fill ends, the part comes off under a real lock and commits.
Cmd $a lock-tool-end | Out-Null
Cmd $a lock-release "$loader" | Out-Null
Wait-LockMirror $b { param($l) @($l.mirror).Count -eq 0 } "A's locks released on B" | Out-Null
$r = Try-Wait $a "$loader unmount $part finish" -TimeoutSec 60 -UntilFinished
Check ($r.result -eq "granted" -and $r.finished) "A's unmount commits ($($r.state))"
Wait-LockMirror $b { param($l) @($l.mirror).Count -eq 0 } "the release reaches B" | Out-Null
$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Milliseconds 500
    $ra = Cmd $a car-ready "$loader"
    $rb = Cmd $b car-ready "$loader"
} while ($ra.stateHash -ne $rb.stateHash -and (Get-Date) -lt $deadline)
Check ($ra.stateHash -eq $rb.stateHash) "B sees A's commit ($($ra.stateHash) / $($rb.stateHash))"
Check ([math]::Abs((Cmd $a lock-fluid "$loader $oil").level - (Cmd $b lock-fluid "$loader $oil").level) -lt 0.001) "A and B have the same oil level"

$locks = Get-ServerLocks
Check ($locks.Count -eq 0 -and (Get-LockCounter $locks "overlapViolations") -eq 0) "no lock is left and no overlap was seen ($($locks.Line))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

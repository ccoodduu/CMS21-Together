# areas: locks, details, parts
# part-locks 7.2: fluids (D4). A part that holds coolant and a coolant fill exclude each other in both orders; two
# coolant parts are compatible. A's unmount of a coolant container drains the coolant, and B already has the drained
# level when A's release reaches B (the flush runs ahead of the change, M5). A brake fluid fill ends with a flush
# before its release, so B has A's level as soon as the lock is gone. The oil bin holds the oil exclusively, so the
# oil filter is refused while it drains. The extractor drains a fluid under its lock.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1")

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$coolant = "f:EngineCoolant.0"
$brake = "f:Brake.0"
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

function Wait-Mirror([string]$Name, [int]$Count) {
    Wait-LockMirror $Name { param($l) @($l.mirror).Count -eq $Count } "$Count lock(s)" | Out-Null
}

function Wait-NoLocks([string]$What) {
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $locks = Get-ServerLocks
        if ($locks.Count -eq 0) { break }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    Check ($locks.Count -eq 0 -and (Get-LockCounter $locks "overlapViolations") -eq 0) "$What`: no lock is left and no overlap was seen ($($locks.Line))"
    Wait-Mirror $a 0
    Wait-Mirror $b 0
}

function Level([string]$Name, [string]$Key) { (Cmd $Name lock-fluid "$loader $Key").level }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }
$idA = (Get-HarnessStatus $a).playerId
$idB = (Get-HarnessStatus $b).playerId
Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$relationsFile = Join-Path $Ctx.RunDir "relations-$car.tsv"
Cmd $a lock-trace "relations $loader $relationsFile" | Out-Null
$fluidsOf = @{}
foreach ($line in (Get-Content -LiteralPath $relationsFile | Where-Object { $_ -match "^s:" })) {
    $cols = $line -split "`t"
    $fluids = ($cols | Where-Object { $_ -like "fluids=*" }) -replace "^fluids=\[|\]$", ""
    $fluidsOf[$cols[0]] = @($fluids -split "," | Where-Object { $_ })
}
$subs = @((Cmd $a dump).cars | Where-Object { $_.index -eq $loader } | ForEach-Object { $_.subParts })
$free = @($subs | Where-Object { -not $_.unmounted -and -not $_.blocked })
$coolantParts = @($free | Where-Object { $fluidsOf[$_.key] -contains $coolant })
$oilParts = @($free | Where-Object { $fluidsOf[$_.key] -contains $oil -and $_.id -match "filtr" })
if (-not $oilParts) { $oilParts = @($free | Where-Object { $fluidsOf[$_.key] -contains $oil }) }
Write-Host "coolant parts: $(($coolantParts | ForEach-Object { "$($_.key) $($_.id)" }) -join ', ')"
Write-Host "oil part: $($oilParts[0].key) $($oilParts[0].id)"
Check ($coolantParts.Count -ge 2 -and $oilParts.Count -ge 1) "the car has two free coolant parts and a free oil part"
$container = $coolantParts[0].key
$second = $coolantParts[1].key

# A part that holds the coolant against a coolant fill, in both orders.
$answer = Request-Lock $a "$loader unmount $container"
Check ($answer.result -eq "granted") "A holds the coolant part $container"
Wait-Mirror $b 1
$r = Try-Wait $b "$loader fill EngineCoolant 0"
Check ($r.result -eq "refusedLocally" -and $r.holder -eq $idA) "B's coolant fill is refused while A holds $container ($($r.result) $($r.holder) $($r.conflictKey))"
Cmd $a lock-release "$loader" | Out-Null
Wait-NoLocks "after the part lock"

$r = Try-Wait $b "$loader fill EngineCoolant 0 level 0.8"
Check ($r.result -eq "granted" -and $r.started) "B's coolant fill starts ($($r.result) started $($r.started))"
Wait-Mirror $a 1
$r = Try-Wait $a "$loader unmount $container"
Check ($r.result -eq "refusedLocally" -and $r.holder -eq $idB) "A's $container is refused while B fills coolant ($($r.result) $($r.holder) $($r.conflictKey))"
$ended = Cmd $b lock-tool-end
Check (@($ended.ended).Count -ge 1) "B's fill ends ($(@($ended.ended) -join ', '))"
Wait-NoLocks "after the coolant fill"

# Two coolant parts at once.
$one = Request-Lock $a "$loader unmount $container"
$two = Request-Lock $b "$loader unmount $second"
Check ($one.result -eq "granted" -and $two.result -eq "granted") "two coolant parts are granted together ($container $($one.result), $second $($two.result))"
Cmd $a lock-release "$loader" | Out-Null
Cmd $b lock-release "$loader" | Out-Null
Wait-NoLocks "after the two coolant parts"

# Order check (M5): A drains the coolant by unmounting its container; B has the drained level when the release
# reaches it.
$r = Try-Wait $a "$loader fill EngineCoolant 0 level 0.8 finish" -TimeoutSec 15 -UntilFinished
Check ($r.finished) "A filled coolant to 0.8 ($($r.state))"
Wait-NoLocks "after A's coolant fill"
Start-Sleep -Seconds 1
$before = Level $b $coolant
Check ($before -ge 0.75) "B has the coolant level A poured ($before)"
Cmd $b lock-watch "$loader $coolant" | Out-Null
$r = Try-Wait $a "$loader unmount $container finish" -TimeoutSec 60 -UntilFinished
Check ($r.finished) "A's unmount of $container commits ($($r.state))"
Wait-NoLocks "after the container unmount"
$levelA = Level $a $coolant
$watch = Cmd $b lock-watch "report"
$release = @($watch.releases | Where-Object { $_.owner -eq $idA }) | Select-Object -Last 1
Write-Host "  A's coolant $levelA; B at A's release: $($release | ConvertTo-Json -Compress)"
Check ($levelA -lt 0.05) "A's unmount drained the coolant ($levelA)"
Check ($release -and $release.level -ge 0 -and $release.level -lt 0.05) "B already had the drained coolant when A's release arrived ($($release.level))"
Cmd $b lock-watch "off" | Out-Null
Cmd $a part-fast-mount "$loader $container" | Out-Null
Start-Sleep -Seconds 3

# Brake fluid: A's fill ends with a flush before the release.
Cmd $b lock-watch "$loader $brake" | Out-Null
$r = Try-Wait $a "$loader fill Brake 0 level 0.42 finish" -TimeoutSec 15 -UntilFinished
Check ($r.finished) "A's brake fluid fill ends ($($r.state))"
Wait-NoLocks "after the brake fluid fill"
$watch = Cmd $b lock-watch "report"
$release = @($watch.releases | Where-Object { $_.owner -eq $idA }) | Select-Object -Last 1
Check ($release -and [math]::Abs($release.level - 0.42) -lt 0.01) "B had A's brake fluid level when A's release arrived ($($release.level))"
Check ([math]::Abs((Level $b $brake) - (Level $a $brake)) -lt 0.001) "A and B have the same brake fluid level"
Cmd $b lock-watch "off" | Out-Null

# Oil bin against the oil filter.
$answer = Request-Lock $a "$loader oil $oil"
Check ($answer.result -eq "granted") "A holds the oil drain lock"
Wait-Mirror $b 1
$r = Try-Wait $b "$loader unmount $($oilParts[0].key)"
Check ($r.result -eq "refusedLocally" -and $r.holder -eq $idA) "B's oil part $($oilParts[0].key) is refused while A drains oil ($($r.result) $($r.conflictKey))"
Cmd $a lock-release "$loader" | Out-Null
Wait-NoLocks "after the oil lock"

# The real oil bin and the extractor, each under its lock.
$oilBefore = Level $a $oil
$r = Try-Wait $a "$loader oil finish" -TimeoutSec 30 -UntilFinished
Write-Host "  oil bin: $($r | ConvertTo-Json -Compress)"
if ($oilBefore -gt 0.05) {
    Check ($r.result -eq "granted" -and $r.started -and $r.finished) "A's oil bin drains under the lock ($($r.result) $($r.state))"
    Wait-NoLocks "after the oil bin"
    Check ((Level $b $oil) -lt 0.05) "B sees the drained oil ($(Level $b $oil))"
}
$r = Try-Wait $b "$loader drain Brake 0 finish" -TimeoutSec 30 -UntilFinished
Write-Host "  extractor: $($r | ConvertTo-Json -Compress)"
Check ($r.result -eq "granted" -and $r.started) "B's extractor starts under the lock ($($r.result) $($r.state))"
Cmd $b lock-tool-end | Out-Null
Wait-NoLocks "after the extractor"
Check ([math]::Abs((Level $b $brake) - (Level $a $brake)) -lt 0.001) "A and B have the same brake fluid level after the extractor ($(Level $a $brake) / $(Level $b $brake))"

$locks = Get-ServerLocks
Check ((Get-LockCounter $locks "overlapViolations") -eq 0) "overlapViolations is 0 ($($locks.Line))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

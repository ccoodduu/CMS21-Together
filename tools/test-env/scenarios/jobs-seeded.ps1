# areas: jobs
# Seeded job cars (server-game-logic task group 5, review M3): the same order gives the same car. A takes order X and
# records the job car's digest (parts, details, job tasks). The server reopens X (`jobs reopen`: the car is deleted and
# the order opens again, the path a lost car takes). B takes X: same digest. X is reopened again with another car on
# X's loader, so A takes X on another loader: same digest. Order Y gives another digest. First, when a story mission is
# open, it is taken, reopened and taken again: same digest. "Same digest" leaves out the known non-seeded rows
# (Test-NotSeeded). The jobcar trace of each take is kept in the run notes.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Note([string]$Text) { $Ctx.Result.notes += $Text; Write-Host $Text }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Jobs([string]$Name) { (Send-HarnessCommand -Instance $Name -Verb dump).jobs }
function Regular($Jobs) { @($Jobs.orders | Where-Object { -not $_.IsMission }) }
function Missions($Jobs) { @($Jobs.orders | Where-Object { $_.IsMission }) }

function Wait-Open([int]$Id, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 700
        $openA = @((Jobs $a).orders | Where-Object { $_.id -eq $Id }).Count -eq 1
        $openB = @((Jobs $b).orders | Where-Object { $_.id -eq $Id }).Count -eq 1
    } while (-not ($openA -and $openB) -and (Get-Date) -lt $deadline)
    Check ($openA -and $openB) "order $Id is open on both (A $openA, B $openB)"
}

function Take([string]$Name, [int]$Id, [string]$What) {
    Send-HarnessCommand -Instance $Name -Verb jobcar-trace -Arguments "on" | Out-Null
    Send-HarnessCommand -Instance $Name -Verb orders-accept -Arguments "$Id" | Out-Null
    $deadline = (Get-Date).AddSeconds(90)
    $active = $null
    do {
        Start-Sleep -Milliseconds 700
        $active = @((Jobs $Name).active | Where-Object { $_.id -eq $Id })
    } while ($active.Count -eq 0 -and (Get-Date) -lt $deadline)
    Check ($active.Count -eq 1) "$What`: $Name took order $Id"
    if ($active.Count -eq 0) { return $null }
    $loader = $active[0].carLoaderID
    $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$loader"
    Check ($r.loaded) "$What`: $Name has the job car on loader $loader ($($r.car))"
    Start-Sleep -Seconds 4
    $digest = Send-HarnessCommand -Instance $Name -Verb jobcar-digest -Arguments "$Id rows"
    $trace = @(Send-HarnessCommand -Instance $Name -Verb jobcar-trace -Arguments "report")
    Send-HarnessCommand -Instance $Name -Verb jobcar-trace -Arguments "off" | Out-Null
    $Ctx.Result.notes += "trace $What ($Name, order $Id, loader $loader):"
    $looping = @{}
    $Ctx.Result.notes += foreach ($line in $trace) {
        if ($line -match "^\S+ \S+ (\w+ (?:loader )?\d+) exit (\d+) -> (\d+) result True" -and $Matches[2] -eq $Matches[3]) { $looping[$Matches[1]] = $Matches[2]; continue }
        if ($line -match "^\S+ \S+ (\w+ (?:loader )?\d+) enter (\d+) " -and $looping[$Matches[1]] -eq $Matches[2]) { continue }
        $line
    }
    Note "$What`: $Name order $Id loader $($digest.loader) $($digest.car) hash $($digest.hash) sections $($digest.sections | ConvertTo-Json -Compress)"
    return $digest
}

# Known non-seeded sources (not UnityEngine.Random, see docs/spikes/seeded-job-cars.md): the Additionals task picks with
# the fluids they drain, and the licence plate number. They may differ on a retake and are left out of the comparison.
function Test-NotSeeded([string]$Key, [string]$Value) {
    $Key -eq "details:Fluids" -or $Key -eq "details:Plates" -or ($Key -like "job:task*" -and $Value -like "Additionals/*")
}

function Compare-Digest($Expected, $Actual, [string]$What) {
    if (-not $Expected -or -not $Actual) { Check $false "$What`: both digests exist"; return }
    $left = @{}; foreach ($row in $Expected.rows) { $k, $v = $row -split "=", 2; $left[$k] = $v }
    $right = @{}; foreach ($row in $Actual.rows) { $k, $v = $row -split "=", 2; $right[$k] = $v }
    $keys = @($left.Keys + $right.Keys | Sort-Object -Unique | Where-Object { $left[$_] -ne $right[$_] })
    $notSeeded = @($keys | Where-Object { Test-NotSeeded $_ "$($left[$_])$($right[$_])" })
    $seeded = @($keys | Where-Object { $notSeeded -notcontains $_ })
    Check ($seeded.Count -eq 0) "$What`: same job car ($($seeded.Count) of $($left.Count) rows differ; non-seeded rows that differ: $($notSeeded -join ', '))"
    foreach ($key in $seeded | Select-Object -First 12) { Note "  $key`n    was $($left[$key])`n    now $($right[$key])" }
}

function Reopen([int]$Id) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "jobs reopen $Id"
    $line = try { Wait-ServerLog -Pattern "Job $Id back to the open orders" -After $mark -TimeoutSec 15 } catch { $null }
    Check ([bool]$line) "the server reopened order $Id ($line)"
    Wait-Open $Id
    Start-Sleep -Seconds 2
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null
    Send-HarnessCommand -Instance $name -Verb orders-autogen -Arguments "off" | Out-Null
}
Start-Sleep -Seconds 2
$gen = if ((Get-HarnessStatus $b).isOrderGenerator) { $b } else { $a }

$mission = @(Missions (Jobs $a)) | Select-Object -First 1
if ($mission) {
    $m1 = Take $a $mission.id "mission"
    Reopen $mission.id
    $m2 = Take $b $mission.id "mission retake by B"
    Compare-Digest $m1 $m2 "B's retake of mission $($mission.id)"
    Reopen $mission.id
} else {
    Note "no story mission was open; the mission retake was not run"
}

$before = @(Regular (Jobs $a) | ForEach-Object { $_.id })
Send-HarnessCommand -Instance $gen -Verb orders-generate -Arguments "900" | Out-Null
Send-HarnessCommand -Instance $gen -Verb orders-generate -Arguments "900" | Out-Null
$deadline = (Get-Date).AddSeconds(30)
do { Start-Sleep -Milliseconds 700; $fresh = @(Regular (Jobs $b) | Where-Object { $before -notcontains $_.id }) } while ($fresh.Count -lt 2 -and (Get-Date) -lt $deadline)
Check ($fresh.Count -ge 2) "two new orders reached B ($(($fresh | ForEach-Object { "$($_.id) $($_.carFile)" }) -join ', '))"
$x = $fresh[0].id
$y = $fresh[1].id

$first = Take $a $x "first take"
Reopen $x
$second = Take $b $x "retake by B"
Compare-Digest $first $second "B's retake of order $x"

Reopen $x
$blocker = $first.loader
Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "$blocker car_boltatlanta 0 auto" | Out-Null
$deadline = (Get-Date).AddSeconds(60)
do { Start-Sleep -Milliseconds 700; $r = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "$blocker" } while (-not $r.loaded -and (Get-Date) -lt $deadline)
Check ($r.loaded) "another car stands on loader $blocker"
Start-Sleep -Seconds 3
$third = Take $a $x "retake on another loader"
if ($third) {
    Check ($third.loader -ne $first.loader) "the third take used another loader ($($first.loader) -> $($third.loader))"
    Compare-Digest $first $third "A's retake of order $x on loader $($third.loader)"
}

$other = Take $b $y "order Y"
if ($other -and $first) { Check ($other.hash -ne $first.hash) "order $y gives another job car ($($other.hash))" }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

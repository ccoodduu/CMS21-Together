# areas: placement, connect, persistence
# sync-car-placement-and-lifts late join and restart: A alone spawns three cars, puts car 0 on lift 1 and raises it to
# Up, moves car 1 to Entrance3 and parks car 2. B joins and must see the same placement; B lowers the lift one step.
# Then the server is saved, killed and restarted; after both reconnect, the placement equals the state before.
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

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$Loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}

function Placement([string]$Name) { (Send-HarnessCommand -Instance $Name -Verb dump).placement | ConvertTo-Json -Depth 6 -Compress }

function Wait-Placement([string]$Name, [string]$Expected, [int]$TimeoutSec = 40) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 700
        $p = Placement $Name
        if ($p -eq $Expected) { return $p }
    } while ((Get-Date) -lt $deadline)
    return $p
}

function Wait-Lift([string]$Name, [string]$State) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $l = @((Send-HarnessCommand -Instance $Name -Verb dump).placement.lifters | Where-Object { $_.index -eq 0 })[0]
    } while ($l.state -ne $State -and (Get-Date) -lt $deadline)
    Check ($l.state -eq $State) "$Name`: lift 0 is $State ($($l.state))"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments "Off" | Out-Null

foreach ($loader in 0, 1, 2) {
    Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "$loader $car 0" | Out-Null
    Wait-Ready $a $loader | Out-Null
}
Send-HarnessCommand -Instance $a -Verb car-move -Arguments "0 CarLifter1" | Out-Null
Start-Sleep -Seconds 4
Send-HarnessCommand -Instance $a -Verb lift -Arguments "0 up" | Out-Null
Wait-Lift $a "Middle"
Send-HarnessCommand -Instance $a -Verb lift -Arguments "0 up" | Out-Null
Wait-Lift $a "Up"
Send-HarnessCommand -Instance $a -Verb car-move -Arguments "1 Entrance3" | Out-Null
Start-Sleep -Seconds 4
Send-HarnessCommand -Instance $a -Verb park -Arguments "2" | Out-Null
Start-Sleep -Seconds 4
$expected = Placement $a
Write-Host "A: $expected"

Connect-HarnessInstance $b; Wait-InGarage $b
Send-HarnessCommand -Instance $b -Verb guard-set -Arguments "Off" | Out-Null
Wait-Ready $b 0 | Out-Null
Wait-Ready $b 1 | Out-Null
$deadline = (Get-Date).AddSeconds(40)
do {
    Start-Sleep -Milliseconds 700
    $pa = Placement $a
    $pb = Placement $b
} while ($pa -ne $pb -and (Get-Date) -lt $deadline)
Check ($pa -eq $pb) "late joiner B sees A's placement (A: $pa, B: $pb, A before the join: $expected)"

Send-HarnessCommand -Instance $b -Verb lift -Arguments "0 down" | Out-Null
Wait-Lift $b "Middle"
Wait-Lift $a "Middle"
$beforeRestart = Placement $a
Check ((Placement $b) -eq $beforeRestart) "A and B agree after B lowered the lift"

$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
Stop-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 30 -What "menu after kill" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
    Send-HarnessCommand -Instance $name -Verb mp-ui -Arguments "ok" | Out-Null
}
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Wait-Ready $name 0 | Out-Null; Wait-Ready $name 1 | Out-Null }
$pa = Wait-Placement $a $beforeRestart
$pb = Wait-Placement $b $beforeRestart
Check ($pa -eq $beforeRestart) "A after the restart equals the state before (A: $pa)"
Check ($pb -eq $beforeRestart) "B after the restart equals the state before (B: $pb)"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: jobs
# Server-owned order clock (docs/design/server-order-clock.md section 7). Fresh session, shared level 20 (limit 8),
# every generated order lasts 900 s, autogen on. Each new regular order is declined right after its server log line,
# so the clock never meets the limit before step 5 (a decline does not touch the clock). Times are server log
# timestamps, tolerance 3 s. Steps marked "regression" pass on the old code too; the others must fail there:
# 2 the generator's local timer does not decide; 3 a handover keeps the clock; 4 nobody in the garage freezes the
# clock and the expiry; 6 a server restart keeps the clock; 7 a non-generator's accept resets it.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
$tolerance = 3
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Note([string]$Text) { $Ctx.Result.notes += $Text; Write-Host $Text }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

# Seconds of the day, continued past midnight relative to the scenario's start.
$origin = [int](Get-Date).TimeOfDay.TotalSeconds
function DaySeconds([int]$Seconds) { if ($Seconds -lt $origin - 3600) { $Seconds + 86400 } else { $Seconds } }
function LogTime([string]$Line) {
    if ($Line -match '^\[(\d\d)\.(\d\d)\.(\d\d)\]') { return DaySeconds ([int]$Matches[1] * 3600 + [int]$Matches[2] * 60 + [int]$Matches[3]) }
    return -1
}
function Now { DaySeconds ([int](Get-Date).TimeOfDay.TotalSeconds) }
function Wait-Until([int]$Second) { $wait = $Second - (Now); if ($wait -gt 0) { Start-Sleep -Seconds $wait } }

# First server log line after index $After that matches; returns @{ Line; T; Next } or $null.
function Wait-Line([string]$Pattern, [int]$After, [int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $lines = @(Get-ServerLogLines)
        for ($i = $After; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match $Pattern) { return @{ Line = $lines[$i]; T = (LogTime $lines[$i]); Next = $i + 1; Match = $Matches } }
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

$regular = '\[Jobs\] Order (\d+): \S+, \d+ s\.'
function Wait-Order([string]$What, [int]$After, [int]$TimeoutSec = 50) {
    $hit = Wait-Line $regular $After $TimeoutSec
    Check ([bool]$hit) "$What`: a regular order was made"
    if ($hit) { $hit.Id = [int]$hit.Match[1]; Note "$What`: order $($hit.Id) at $($hit.T)" }
    return $hit
}
function Decline($Order) { if ($Order) { Send-HarnessCommand -Instance $b -Verb orders-decline-packet -Arguments "$($Order.Id)" | Out-Null } }
function Check-Time([string]$What, $Order, [int]$Expected) {
    if (-not $Order) { return }
    $diff = $Order.T - $Expected
    Check ([math]::Abs($diff) -le $tolerance) "$What`: the order came at $($Order.T), expected $Expected ($('{0:+0;-0;0}' -f $diff) s)"
}
function Remaining([int]$Id) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "jobs"
    $hit = Wait-Line "order $Id`: \S+ Open, (\d+) s" $mark 10
    if ($hit) { return [int]$hit.Match[1] }
    return -1
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
    Send-HarnessCommand -Instance $name -Verb orders-ttl -Arguments "900" | Out-Null
}
Stop-TestServer
$saves = Join-Path $Ctx.ServerDir "Saves"
if (Test-Path -LiteralPath $saves) { Move-Item -LiteralPath $saves -Destination (Join-Path $Ctx.RunDir "server_saves_before") }
Start-TestServer | Out-Null
Check ([bool](Wait-Line "No save found" 0 10)) "the server started a new session"
Send-ServerCommand "level set 20"
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }
Check ((Get-HarnessStatus $a).isOrderGenerator) "A is the order generator"

# 1 (regression): one order every 30 s while both are in the garage.
$o1 = Wait-Order "step 1 first" 0 60; Decline $o1
$o2 = Wait-Order "step 1 second" $o1.Next; Decline $o2
if ($o1 -and $o2) { Check-Time "step 1 (regression) 30 s apart" $o2 ($o1.T + 30) }

# 2: the generator's local timer does not decide.
Send-HarnessCommand -Instance $a -Verb orders-timer -Arguments "29 30" | Out-Null
$o3 = Wait-Order "step 2" $o2.Next; Decline $o3
Check-Time "step 2 local timer 29/30 on the generator" $o3 ($o2.T + 30)

# 3: a handover keeps the clock.
Wait-Until ($o3.T + 10)
Send-HarnessCommand -Instance $b -Verb orders-timer -Arguments "29 30" | Out-Null
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb travel -Arguments "Parking" | Out-Null
$handover = Wait-Line '\[Jobs\] Order generator: client \d+' $mark 30
Check ([bool]$handover) "step 3: B became the generator ($($handover.Line))"
$o4 = Wait-Order "step 3" $o3.Next
Check-Time "step 3 handover to B with local timer 29/30" $o4 ($o3.T + 30)

# 4: nobody in the garage freezes the clock and the expiry; A's return resumes it.
Wait-Until ($o4.T + 25)
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb travel -Arguments "Parking" | Out-Null
$nobody = Wait-Line '\[Jobs\] Order generator: nobody' $mark 30
Check ([bool]$nobody) "step 4: nobody is the generator"
$elapsed = if ($nobody) { $nobody.T - $o4.T } else { 25 }
Start-Sleep -Seconds 2
$left1 = Remaining $o4.Id
Start-Sleep -Seconds 45
Check (-not (Wait-Line $regular $o4.Next 1)) "step 4: no order while nobody is in the garage"
$left2 = Remaining $o4.Id
Check ($left1 -gt 0 -and $left1 - $left2 -le 2) "step 4: order $($o4.Id) does not expire while nobody is in the garage ($left1 s -> $left2 s)"
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb travel -Arguments "Garage" | Out-Null
$back = Wait-Line '\[Jobs\] Order generator: client \d+' $mark 120
Check ([bool]$back) "step 4: A is the generator again"
$o5 = Wait-Order "step 4" $o4.Next 60
if ($back) { Check-Time "step 4 resume after $elapsed s of clock" $o5 ($back.T + 30 - $elapsed) }
Decline $o4; Decline $o5
Send-HarnessCommand -Instance $b -Verb travel -Arguments "Garage" | Out-Null
Wait-InGarage $a; Wait-InGarage $b

# 5 (regression): at the limit nothing is made; a decline lets the clock run again.
$deadline = (Get-Date).AddSeconds(60)
while ((Send-HarnessCommand -Instance $a -Verb dump).jobs.openCount -lt 8 -and (Get-Date) -lt $deadline) {
    Send-HarnessCommand -Instance $a -Verb orders-generate -Arguments "900" | Out-Null
    Start-Sleep -Milliseconds 800
}
Start-Sleep -Seconds 2
$full = @((Send-HarnessCommand -Instance $a -Verb dump).jobs.orders | Where-Object { -not $_.IsMission })
Check ((Send-HarnessCommand -Instance $a -Verb dump).jobs.openCount -eq 8) "step 5: the open orders are at the limit (8)"
$mark = Get-ServerLogMark
Start-Sleep -Seconds 40
Check (-not (Wait-Line $regular $mark 1)) "step 5 (regression): no order at the limit for 40 s"
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb orders-decline-packet -Arguments "$($full[0].id)" | Out-Null
$declined = Wait-Line "\[Jobs\] Order $($full[0].id) declined" $mark 10
$o6 = Wait-Order "step 5" $mark 50
if ($declined) { Check-Time "step 5 (regression) 30 s after the decline" $o6 ($declined.T + 30) }
foreach ($order in $full | Select-Object -Skip 1) { Send-HarnessCommand -Instance $b -Verb orders-decline-packet -Arguments "$($order.id)" | Out-Null }
Decline $o6

# 6: a server restart keeps the clock.
$o7 = Wait-Order "step 6 reference" $o6.Next; Decline $o7
Wait-Until ($o7.T + 10)
$mark = Get-ServerLogMark
Send-ServerCommand "save"
$saved = Wait-Line "Session successfully saved" $mark 20
Stop-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 30 -What "menu after kill" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
    Send-HarnessCommand -Instance $name -Verb mp-ui -Arguments "ok" | Out-Null
}
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Send-HarnessCommand -Instance $a -Verb orders-timer -Arguments "29 30" | Out-Null
$elected = Wait-Line '\[Jobs\] Order generator: client \d+' 0 30
Connect-HarnessInstance $b
$o8 = Wait-Order "step 6" 0 60
if ($saved -and $elected) { Check-Time "step 6 after the restart (clock $($saved.T - $o7.T) s at the save)" $o8 ($elected.T + 30 - ($saved.T - $o7.T)) }
Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

# 7: the non-generator's accept resets the clock.
Wait-Until ($o8.T + 15)
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb orders-accept -Arguments "$($o8.Id)" | Out-Null
$claimed = Wait-Line "\[Jobs\] Order $($o8.Id) claimed by client" $mark 15
Check ([bool]$claimed) "step 7: B claimed order $($o8.Id)"
$o9 = Wait-Order "step 7" $o8.Next 60; Decline $o9
if ($claimed) { Check-Time "step 7 B's accept 15 s after an order" $o9 ($claimed.T + 30) }

# 8 (regression): taking the story mission leaves the clock alone.
$mission = @((Send-HarnessCommand -Instance $a -Verb dump).jobs.orders | Where-Object { $_.IsMission }) | Select-Object -First 1
if ($mission -and $o9) {
    Wait-Until ($o9.T + 10)
    $mark = Get-ServerLogMark
    Send-HarnessCommand -Instance $a -Verb orders-accept -Arguments "$($mission.id)" | Out-Null
    Check ([bool](Wait-Line "\[Jobs\] Order $($mission.id) claimed by client" $mark 15)) "step 8: A claimed the mission"
    $o10 = Wait-Order "step 8" $o9.Next 60
    Check-Time "step 8 (regression) a mission take leaves the clock" $o10 ($o9.T + 30)
} else {
    Note "step 8: no story mission was open; not run"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: details, cars
# Race and drift audit gap 6 (state-merges-and-contention D4-D7): car details travel as entries. Two players change
# different entries of one section at once (two fluids, two body panels, two wheels, two alignment fields): the server
# and both clients keep both. Both write the same fluid, in both server orders: every side ends on the value the server
# took last. An unsent local edit survives another player's update of the same section. A pour is never set back by
# its own echo (150 ms incoming delay on the pouring client).
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
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

function Entries([string]$Name) { (@((Cmd $Name dump).carDetails) | Where-Object { $_.loader -eq $loader }).entries }

function Server() {
    $mark = Get-ServerLogMark
    Send-ServerCommand "cardetails $loader"
    $line = Wait-ServerLog -Pattern "\[CarDetails\] Loader $loader`: \{" -After $mark -TimeoutSec 10
    ($line -replace "^.*?\[CarDetails\] Loader $loader`: ", "") | ConvertFrom-Json
}

function ServerFluid($Details, [int]$Type, [int]$Id) { [double](@($Details.Fluids | Where-Object { $_.Type -eq $Type -and $_.Id -eq $Id })[0].Level) }

function Near($x, $y) { [math]::Abs([double]$x - [double]$y) -lt 0.002 }

function Wait-Same([string]$What) {
    try {
        Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("carDetails") -TimeoutSec 15 | Out-Null
        Check $true "$What`: A and B have the same details"
    } catch { Check $false "$What`: $($_.Exception.Message)" }
}

function Pair([string]$What, [string]$VerbA, [string]$ArgsA, [string]$VerbB, [string]$ArgsB) {
    foreach ($name in $a, $b) { Cmd $name net-hold "on" | Out-Null }
    Cmd $a $VerbA $ArgsA | Out-Null
    Cmd $b $VerbB $ArgsB | Out-Null
    Start-Sleep -Seconds 3
    foreach ($name in $a, $b) { Cmd $name net-hold "off" | Out-Null }
    Start-Sleep -Seconds 3
    Wait-Same $What
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 4
Wait-Same "spawn"

$fluids = @((Entries $a).PSObject.Properties.Name | Where-Object { $_ -like "f:*" })
$other = @($fluids | Where-Object { $_ -ne "f:Brake.0" })[0]
$otherType = ($other -replace "^f:", "") -replace "\.\d+$", ""
$otherId = [int](($other -split "\.")[-1])
$otherTypeId = (Entries $a).$other.Type
Write-Host "fluids: $($fluids -join ', '); second fluid $other"

# Different entries of one section.
Pair "two fluids" cardetails-fluid "$loader Brake 0 0.31" cardetails-fluid "$loader $otherType $otherId 0.62"
$server = Server
Check ((Near (ServerFluid $server 2 0) 0.31) -and (Near (ServerFluid $server $otherTypeId $otherId) 0.62)) "two fluids: the server has A's brake fluid and B's $other"
foreach ($name in $a, $b) {
    $e = Entries $name
    Check ((Near $e.'f:Brake.0'.Level 0.31) -and (Near $e.$other.Level 0.62)) "two fluids: $name has both ($($e.'f:Brake.0'.Level), $($e.$other.Level))"
}

Pair "two body panels" cardetails-wash "$loader 0.81 0.11 1" cardetails-wash "$loader 0.42 0.52 3"
$server = Server
$p1 = @($server.BodyCosmetics | Where-Object { $_.PartIndex -eq 1 })[0]; $p3 = @($server.BodyCosmetics | Where-Object { $_.PartIndex -eq 3 })[0]
Check ((Near $p1.Dust 0.81) -and (Near $p3.Dust 0.42)) "two body panels: the server has both"
foreach ($name in $a, $b) {
    $e = Entries $name
    Check ((Near $e.'c:1'.Dust 0.81) -and (Near $e.'c:3'.Dust 0.42) -and (Near $e.'c:3'.WashFactor 0.52)) "two body panels: $name has both"
}

$w0 = (Entries $a).'w:0'; $w3 = (Entries $a).'w:3'
Pair "two wheels" cardetails-wheel "$loader 0 $($w0.Width) $($w0.RimSize) $($w0.TireSize) $($w0.ET + 3)" cardetails-wheel "$loader 3 $($w3.Width) $($w3.RimSize) $($w3.TireSize) $($w3.ET + 5)"
$server = Server
Check ($server.Wheels[0].ET -eq $w0.ET + 3 -and $server.Wheels[3].ET -eq $w3.ET + 5) "two wheels: the server has both ETs ($($server.Wheels[0].ET), $($server.Wheels[3].ET))"
foreach ($name in $a, $b) {
    $e = Entries $name
    Check ($e.'w:0'.ET -eq $w0.ET + 3 -and $e.'w:3'.ET -eq $w3.ET + 5) "two wheels: $name has both"
}

Pair "two alignment fields" cardetails-alignment "$loader 0.21 - - -" cardetails-alignment "$loader - - - -0.33"
$server = Server
Check ((Near $server.Alignment.FL 0.21) -and (Near $server.Alignment.RR -0.33)) "two alignment fields: the server has FL and RR"
foreach ($name in $a, $b) {
    $e = Entries $name
    Check ((Near $e.'a:FL' 0.21) -and (Near $e.'a:RR' -0.33)) "two alignment fields: $name has FL $($e.'a:FL') and RR $($e.'a:RR')"
}

# The same fluid by both, in both server orders.
foreach ($order in @(@($a, 0.44, $b, 0.66), @($b, 0.27, $a, 0.83))) {
    $first, $firstValue, $second, $secondValue = $order
    foreach ($name in $a, $b) { Cmd $name net-hold "out" | Out-Null }
    Cmd $first cardetails-fluid "$loader Brake 0 $firstValue" | Out-Null
    Cmd $second cardetails-fluid "$loader Brake 0 $secondValue" | Out-Null
    Start-Sleep -Seconds 2
    $mark = Get-ServerLogMark
    Cmd $first net-hold "off" | Out-Null
    try { Wait-ServerLog -Pattern "\[CarDetails\] Loader $loader`: update \d+ from client \d+ \(.*f:Brake\.0" -After $mark -TimeoutSec 10 | Out-Null } catch { Write-Host "no server line for $first's update" }
    Cmd $second net-hold "off" | Out-Null
    Start-Sleep -Seconds 4
    Check (Near (ServerFluid (Server) 2 0) $secondValue) "same fluid ($first first): the server has $second's $secondValue"
    foreach ($name in $a, $b) { Check (Near (Entries $name).'f:Brake.0'.Level $secondValue) "same fluid ($first first): $name has $second's $secondValue ($((Entries $name).'f:Brake.0'.Level))" }
}

# A's own brake edit is not sent yet when B's update of another fluid reaches A.
Cmd $a net-hold "on" | Out-Null
$mark = Get-ServerLogMark
Cmd $b cardetails-fluid "$loader $otherType $otherId 0.15" | Out-Null
try { Wait-ServerLog -Pattern "\[CarDetails\] Loader $loader`: update \d+ from client" -After $mark -TimeoutSec 10 | Out-Null } catch { }
Cmd $a cardetails-fluid "$loader Brake 0 0.58" | Out-Null
Cmd $a net-hold "off" | Out-Null
Start-Sleep -Seconds 4
$server = Server
Check ((Near (ServerFluid $server 2 0) 0.58) -and (Near (ServerFluid $server $otherTypeId $otherId) 0.15)) "unsent edit: the server has A's brake 0.58 and B's $other 0.15"
foreach ($name in $a, $b) { Check (Near (Entries $name).'f:Brake.0'.Level 0.58) "unsent edit: $name has A's brake 0.58" }
Wait-Same "unsent edit"

# A pours brake fluid for 3 s with its incoming packets 150 ms late: its echoes never set the level back.
Cmd $a cardetails-fluid "$loader Brake 0 0.2" | Out-Null
Start-Sleep -Seconds 3
Cmd $a net-delay "150" | Out-Null
Cmd $a cardetails-pour "$loader Brake 0 3" | Out-Null
Start-Sleep -Seconds 4
$pour = Cmd $a cardetails-pour "result"
Cmd $a net-delay "0" | Out-Null
Check ($pour.done -and $pour.decreases -eq 0 -and $pour.end -gt $pour.start + 0.2) "pour: the level only rose ($($pour.start) -> $($pour.end), $($pour.decreases) decreases in $($pour.frames) frames)"
Start-Sleep -Seconds 3
foreach ($name in $a, $b) { Check (Near (Entries $name).'f:Brake.0'.Level $pour.end) "pour: $name ends at A's level $($pour.end) ($((Entries $name).'f:Brake.0'.Level))" }
Wait-Same "pour"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

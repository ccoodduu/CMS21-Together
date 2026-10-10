# areas: placement, parts
# A car unparked while the other player is away comes back on that player's return with the server's part records
# (row 19 part 2 soak, 20261008-215819_L2: 7 parts examined on the returning game that the server has unexamined).
# The returning game loads the unparked car from its parked data; the server's records must still win once that load
# has finished. Desync autofix is off, so only the snapshot itself can make the games agree.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")

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
        $r = try { Cmd $Name car-ready "$loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Wait-Empty([string]$Name) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $c = @((Cmd $Name dump).cars | Where-Object { $_.index -eq $loader })[0]
        if (-not $c.carToLoad -and $c.syncState -eq "Empty") { return }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not empty"
}

function Examined([string]$Name) {
    $parts = @{}
    foreach ($part in @(@((Cmd $Name dump).cars | Where-Object { $_.index -eq $loader })[0].subParts)) { $parts[$part.key] = [bool]$part.examined }
    $parts
}

function Test-ServerCars([string]$What) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "desync check"
    Start-Sleep -Seconds 4
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] cars:$loader for client \d+: (match|mismatch|not ready)" })
    Check ($lines.Count -ge 2 -and @($lines | Where-Object { $_ -notmatch ": match" }).Count -eq 0) "$What`: both games match the server's part records ($($lines -join ' | '))"
    $warn = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] cars:$loader .*fields differ" })
    if ($warn) { Write-Host "  $($warn -join "`n  ")" }
}

Set-ServerConfigValues $Ctx.ServerDir @{ desync_check_interval_seconds = 600; desync_autofix = $false }
Restart-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null; Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$mark = Get-ServerLogMark
Cmd $a park "$loader" | Out-Null
$line = Wait-ServerLog -Pattern "\[Parking\] Loader $loader \(.*\) parked in slot (\d+)" -After $mark -TimeoutSec 20
$slot = [int]([regex]::Match($line, "parked in slot (\d+)").Groups[1].Value)
Wait-Empty $a; Wait-Empty $b

Cmd $b travel "Junkyard" | Out-Null
$s = Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "B in the junkyard" -Condition { param($s) $s.scene -ne "garage" -and $s.playable -and $s.connectionValid }
Check ($s.scene -match "(?i)junkyard") "B reached the junkyard ($($s.scene))"

$mark = Get-ServerLogMark
Cmd $a unpark "$slot $loader" | Out-Null
Wait-Ready $a | Out-Null
$took = try { Wait-ServerLog -Pattern "\[Parking\] Loader $loader`: the unparked car's baseline took (\d+) of the parked part records" -After $mark -TimeoutSec 20 } catch { "" }
Write-Host "  $took"
Start-Sleep -Seconds 3

Cmd $b travel "Garage" | Out-Null
Start-Sleep -Seconds 3
Wait-InGarage $b
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 5

$examinedA = Examined $a
$examinedB = Examined $b
$differ = @($examinedA.Keys | Where-Object { $examinedA[$_] -ne $examinedB[$_] } | Sort-Object)
Check ($examinedA.Count -gt 0 -and $differ.Count -eq 0) "after B's return A and B have the same examined parts ($($examinedA.Count) parts; differ: $($differ -join ', '))"
Test-ServerCars "after B's return"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: cars, resync
# A car snapshot that reaches a client after that car's delete must not rebuild the car (seen as economy-trades'
# batch failure: a digest resend of the car crossed another player's sale). B asks for a resync of the car while its
# incoming packets are held, A deletes the car, then B gets the snapshot and the delete in that order: B ends without
# the car, like A and the server.
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

function Has-Car([string]$Name) { [bool](@((Cmd $Name dump).cars | Where-Object { $_.index -eq $loader })[0].carToLoad) }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader $car 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

Cmd $b net-hold "on" | Out-Null
$mark = Get-ServerLogMark
Cmd $b car-resync "$loader" | Out-Null
$asked = try { Wait-ServerLog -Pattern "asked to resync loader $loader" -After $mark -TimeoutSec 10 } catch { $null }
Check ([bool]$asked) "the server sent B the car's snapshot ($asked)"
Cmd $a car-delete "$loader" | Out-Null
$cleared = try { Wait-ServerLog -Pattern "\[Cars\] Loader $loader`: .* cleared \(Deleted\)" -After $mark -TimeoutSec 10 } catch { $null }
Check ([bool]$cleared) "A deleted the car on the server ($cleared)"
Start-Sleep -Seconds 1
Cmd $b net-hold "off" | Out-Null
Start-Sleep -Seconds 8

Check (-not (Has-Car $a)) "A has no car on loader $loader"
Check (-not (Has-Car $b)) "B has no car on loader $loader after the late snapshot"
try {
    Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("cars", "inventory") -TimeoutSec 15 | Out-Null
    Check $true "A and B have equal cars and inventories"
} catch { Check $false "A and B have equal cars and inventories: $($_.Exception.Message)" }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

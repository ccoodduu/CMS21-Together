# areas: economy, connect, persistence
# economy-audit 7.3: A alone sells a car, resets the skills, uses a barn map and pays a travel fee; B joins later and
# gets the same money, scrap, level, XP, barn count and skills. After a server restart both get them back.
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$failures = @()
$skipped = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Skip([string]$Message) { $script:skipped += "skipped: $Message"; Write-Host "SKIP: $Message" -ForegroundColor Yellow }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Menu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$Loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $true }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Wait-Dump([string]$Name, [string]$What, [scriptblock]$Condition) {
    try { Wait-HarnessDump -Instance $Name -TimeoutSec 30 -What $What -Condition $Condition } catch { Check $false "$What`: $($_.Exception.Message)"; Dump $Name }
}

function Compare-Economy($Left, $Right, [string]$What) {
    $differ = @(Compare-HarnessDumps -Left $Left -Right $Right -Sections stats, skills)
    Check ($differ.Count -eq 0) "$What`: stats and skills are equal (differ: $($differ -join ', '); left $($Left.stats | ConvertTo-Json -Compress) $($Left.skills | ConvertTo-Json -Compress), right $($Right.stats | ConvertTo-Json -Compress) $($Right.skills | ConvertTo-Json -Compress))"
}

foreach ($name in $Ctx.Instances) { Wait-Menu $name }
Connect-HarnessInstance $a; Wait-InGarage $a
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments "enforce" | Out-Null
Send-ServerCommand "money set 200000"
Wait-Dump $a "money set" { param($d) $d.stats.money -eq 200000 } | Out-Null

Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
Check (Wait-Ready $a 0) "A has loader 0 ready"
Start-Sleep -Seconds 3
Send-HarnessCommand -Instance $a -Verb econ-sell-car -Arguments "0 15000" | Out-Null
Wait-Dump $a "the car is sold" { param($d) $d.stats.money -eq 215000 } | Out-Null

Send-HarnessCommand -Instance $a -Verb econ-skill-unlock -Arguments "fast_movement 0" | Out-Null
Wait-Dump $a "a skill is unlocked" { param($d) @($d.skills.unlocked).Count -eq 1 } | Out-Null
Send-HarnessCommand -Instance $a -Verb econ-skill-reset | Out-Null
Wait-Dump $a "the skills are reset" { param($d) @($d.skills.unlocked).Count -eq 0 -and $d.stats.money -eq 214000 } | Out-Null

$map = (Send-HarnessCommand -Instance $a -Verb inv-add-local -Arguments "specialMap").uid
Start-Sleep -Seconds 2
$barns = (Dump $a).stats.barns
Send-HarnessCommand -Instance $a -Verb econ-barn-map -Arguments "$map" | Out-Null
Wait-Dump $a "the barn map adds a barn" { param($d) $d.stats.barns -eq $barns + 1 } | Out-Null

$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-map-travel -Arguments "Junkyard" | Out-Null
$fee = try { Wait-ServerLog -Pattern "\[Economy\] client \d+ TravelFee\(5\) money -500" -After $mark -TimeoutSec 20 } catch { $null }
Check ([bool]$fee) "the junkyard trip cost 500 ($fee)"
$inJunkyard = try { Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "A in the junkyard" -Condition { param($s) $s.scene -match "(?i)junkyard" -and $s.playable }; $true } catch { $false }
if ($inJunkyard) {
    Send-HarnessCommand -Instance $a -Verb travel -Arguments "Garage" | Out-Null
    Wait-InGarage $a
}
Start-Sleep -Seconds 3

Connect-HarnessInstance $b; Wait-InGarage $b
Start-Sleep -Seconds 5
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "latejoin"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "latejoin"
Compare-Economy $dumpA $dumpB "B after the late join"
Check ($dumpB.stats.barns -eq $barns + 1) "B has the barn count ($($dumpB.stats.barns))"
foreach ($dump in $dumpA, $dumpB) { Check ($dump.economy.unattributed -eq 0) "$($dump.instance) has no unattributed call ($($dump.economy.unattributed))" }

# Restart.
$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
Stop-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 60 -What "menu after the server stopped" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
    Send-HarnessCommand -Instance $name -Verb mp-ui -Arguments "ok" | Out-Null
}
Start-TestServer | Out-Null
$loaded = try { Wait-ServerLog -Pattern "\[World\] Loaded: .*barns $($barns + 1)," -TimeoutSec 30 } catch { $null }
Check ([bool]$loaded) "the server loaded barns $($barns + 1) ($loaded)"
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
Start-Sleep -Seconds 5
$afterA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "restart"
$afterB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "restart"
Compare-Economy $dumpA $afterA "A after the restart"
Compare-Economy $afterA $afterB "A and B after the restart"

$Ctx.Result.notes += $failures
$Ctx.Result.notes += $skipped
$Ctx.Result.passed = ($failures.Count -eq 0)

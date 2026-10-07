# areas: economy, cars
# economy-audit 7.2: Trades ask the server first; the effect and the money or scrap change happen once for everyone.
# Car sale (refused while B works on the car, a race of two sellers, a job car), skill reset, scrapping, scrap per
# condition, the quality upgrade (and too little scrap), license plates (and too little money), a barn map, and crates
# (upstream #94: three crates each, a replayed loot request, a case closed before it was opened).
param($Ctx)

$a, $b = $Ctx.Instances
$car = "car_boltatlanta"
$part = "tuleja_1"
$failures = @()
$skipped = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Skip([string]$Message) { $script:skipped += "skipped: $Message"; Write-Host "SKIP: $Message" -ForegroundColor Yellow }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
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

function Has-Car([string]$Name, [int]$Loader) { [bool](Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "$Loader").loaded }

function Count-Items($Dump, [string]$Id) { @($Dump.inventory.items | Where-Object { $_.ID -eq $Id }).Count }

# Waits until A and B agree on the shared sections (and $Condition holds on A's dump), then checks for unattributed calls.
function Wait-Shared([string]$What, [scriptblock]$Condition = { param($d) $true }) {
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 700
        $da = Dump $a; $db = Dump $b
        $differ = @(Compare-HarnessDumps -Left $da -Right $db -Sections stats, inventory, skills, cars)
    } while (-not ($differ.Count -eq 0 -and (& $Condition $da)) -and (Get-Date) -lt $deadline)
    Check ($differ.Count -eq 0) "$What`: A and B agree on stats, inventory, skills and cars (differ: $($differ -join ', '))"
    if ($differ -contains "cars") {
        foreach ($carA in @($da.cars)) {
            $carB = @($db.cars | Where-Object { $_.index -eq $carA.index })[0]
            foreach ($prop in $carA.PSObject.Properties.Name) {
                $left = $carA.$prop | ConvertTo-Json -Depth 6 -Compress
                $right = if ($carB) { $carB.$prop | ConvertTo-Json -Depth 6 -Compress } else { "(no car)" }
                if ($left -ne $right) { Write-Host "  car $($carA.index) $prop A: $($left.Substring(0, [Math]::Min(300, $left.Length)))"; Write-Host "  car $($carA.index) $prop B: $($right.Substring(0, [Math]::Min(300, $right.Length)))" }
            }
        }
    }
    Check ([bool](& $Condition $da)) "$What`: expected state reached"
    foreach ($dump in $da, $db) { Check ($dump.economy.unattributed -eq 0) "$What`: $($dump.instance) has no unattributed call ($($dump.economy.unattributed))" }
    return $da
}

function Server-Lines([int]$Mark, [string]$Pattern) {
    @(Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $Mark | Where-Object { $_ -match $Pattern })
}

function Economy-Line([int]$Mark, [string]$Pattern, [int]$TimeoutSec = 20) {
    try { Wait-ServerLog -Pattern "\[Economy\] client \d+ $Pattern" -After $Mark -TimeoutSec $TimeoutSec } catch { $null }
}

function Add-Item([string]$Name, [string]$Id) { (Send-HarnessCommand -Instance $Name -Verb inv-add-local -Arguments $Id).uid }

function Hold([string]$Mode) { foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb net-hold -Arguments $Mode | Out-Null } }

$script:denied = @()
function Guarded([string]$Key) { $script:denied -contains $Key }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "enforce" | Out-Null }
$script:denied = @(Send-HarnessCommand -Instance $a -Verb guard-rules | Where-Object { $_ -like "deny *" } | ForEach-Object { ($_ -split '\s+')[1] })
foreach ($key in "Window:CaseOpening", "Window:Scrap", "Window:ScrapPerCondition", "Window:ShopLicenseBuy", "Action:SellCar") {
    Check (-not (Guarded $key)) "$key is allowed by the guard"
}

Send-ServerCommand "money set 200000"
Send-HarnessCommand -Instance $a -Verb stats-add -Arguments "500 0" | Out-Null
$casesA = @(1..3 | ForEach-Object { Add-Item $a "specialCase" })
$casesB = @(1..3 | ForEach-Object { Add-Item $b "specialCase" })
$map = Add-Item $a "specialMap"
$parts = @(1..3 | ForEach-Object { Add-Item $a $part })
Wait-Shared "items added" { param($d) (Count-Items $d "specialCase") -ge 6 -and (Count-Items $d $part) -ge 3 -and $d.stats.money -eq 200000 } | Out-Null

# Car sale.
Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
foreach ($name in $Ctx.Instances) { Check (Wait-Ready $name 0) "$name has loader 0 ready" }
Start-Sleep -Seconds 3
$repair = Send-HarnessCommand -Instance $b -Verb part-unmount -Arguments "0"
Start-Sleep -Seconds 3
Send-HarnessCommand -Instance $b -Verb part-claim -Arguments "0 $($repair.key)" | Out-Null
Start-Sleep -Seconds 1
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-sell-car -Arguments "0 20000" | Out-Null
Check ([bool](Economy-Line $mark "CarSale\(0\) refused Busy")) "A cannot sell the car B works on"
Start-Sleep -Seconds 2
Check ((Has-Car $a 0) -and (Has-Car $b 0)) "the car stays for both after the refused sale"
Send-HarnessCommand -Instance $b -Verb part-claim -Arguments "0 $($repair.key) release" | Out-Null
Start-Sleep -Seconds 2

$before = (Dump $a).stats.money
$mark = Get-ServerLogMark
Hold "on"
Send-HarnessCommand -Instance $a -Verb econ-sell-car -Arguments "0 20000" | Out-Null
Send-HarnessCommand -Instance $b -Verb econ-sell-car -Arguments "0 20000" | Out-Null
Start-Sleep -Seconds 1
Hold "off"
$sold = Economy-Line $mark "CarSale\(0\) money \+20000"
$gone = Economy-Line $mark "CarSale\(0\) refused Gone"
Check ([bool]$sold -and [bool]$gone) "two sellers: one sale, one refused Gone ($sold / $gone)"
Wait-Shared "after the sale" { param($d) $d.stats.money -eq $before + 20000 } | Out-Null
Check (-not (Has-Car $a 0) -and -not (Has-Car $b 0)) "the sold car is gone for both"

try {
    foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb orders-autogen -Arguments "off" | Out-Null }
    $gen = if ((Get-HarnessStatus $b).isOrderGenerator) { $b } else { $a }
    Send-HarnessCommand -Instance $gen -Verb orders-generate | Out-Null
    Start-Sleep -Seconds 4
    $order = @((Dump $a).jobs.orders)[-1].id
    Send-HarnessCommand -Instance $a -Verb orders-accept -Arguments "$order" | Out-Null
    $deadline = (Get-Date).AddSeconds(90)
    do { Start-Sleep -Seconds 1; $active = @((Dump $a).jobs.active | Where-Object { $_.id -eq $order }) } while ($active.Count -eq 0 -and (Get-Date) -lt $deadline)
    $jobLoader = $active[0].carLoaderID
    Check (Wait-Ready $a $jobLoader) "the job car is on loader $jobLoader"
    $mark = Get-ServerLogMark
    Send-HarnessCommand -Instance $a -Verb econ-sell-car -Arguments "$jobLoader 20000" | Out-Null
    Check ([bool](Economy-Line $mark "CarSale\(\d+\) refused Invalid: loader $jobLoader is a job car")) "a job car cannot be sold"
    Check (Has-Car $b $jobLoader) "the job car stays for B"
} catch { Skip "job car sale ($($_.Exception.Message))" }
Skip "selling a parked car (no client entry point found yet, task 1.4)"

# Skill reset.
Send-HarnessCommand -Instance $a -Verb econ-skill-unlock -Arguments "fast_movement 0" | Out-Null
Send-HarnessCommand -Instance $a -Verb econ-skill-unlock -Arguments "cheaper_parking 0" | Out-Null
$unlocked = Wait-Shared "two skills unlocked" { param($d) @($d.skills.unlocked).Count -eq 2 }
$before = $unlocked.stats.money
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-skill-reset | Out-Null
Check ([bool](Economy-Line $mark "SkillReset\(\d+\) money -3000")) "the reset costs 1000 per point (3 points)"
Wait-Shared "after the skill reset" { param($d) @($d.skills.unlocked).Count -eq 0 -and $d.stats.money -eq $before - 3000 } | Out-Null

# Scrapping.
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-scrap -Arguments "$($parts[0]) 0" | Out-Null
$line = Economy-Line $mark "ScrapItem\(0\) money"
Check ([bool]$line) "A scrapped a part ($line)"
Wait-Shared "after scrapping" { param($d) @($d.inventory.items | Where-Object { $_.UID -eq $parts[0] }).Count -eq 0 } | Out-Null

$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-scrap-upgrade -Arguments "$($parts[1])" | Out-Null
$line = Economy-Line $mark "ScrapUpgrade\(1\) money"
Check ([bool]$line) "A upgraded a part to quality 1 ($line)"
Wait-Shared "after the upgrade" | Out-Null
Send-HarnessCommand -Instance $a -Verb stats-add -Arguments "-1000000 0" | Out-Null
Wait-Shared "scrap emptied" { param($d) $d.stats.scrap -eq 0 } | Out-Null
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-scrap-upgrade -Arguments "$($parts[1])" | Out-Null
$noScraps = Economy-Line $mark "ScrapUpgrade\(2\) refused NoScraps" 10
$noRequest = -not $noScraps -and @(Server-Lines $mark "ScrapUpgrade").Count -eq 0
Check ([bool]$noScraps -or $noRequest) "an upgrade with no scrap is refused (server NoScraps or the game's own check)"
Send-HarnessCommand -Instance $a -Verb stats-add -Arguments "500 0" | Out-Null

$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb econ-scrap-condition -Arguments "60" | Out-Null
$line = Economy-Line $mark "ScrapPerCondition\(60\) money"
Check ([bool]$line) "B scrapped the worn parts ($line)"
Wait-Shared "after scrap per condition" { param($d) (Count-Items $d $part) -eq 0 } | Out-Null

# License plates.
$platesBefore = Count-Items (Dump $a) "LicensePlate"
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-license -Arguments "2 TEST" | Out-Null
$line = Economy-Line $mark "LicensePlates\(1\) money -\d+ .*'TEST'"
Check ([bool]$line) "A bought two plates with the text TEST ($line)"
Wait-Shared "after buying plates" { param($d) (Count-Items $d "LicensePlate") -eq $platesBefore + 2 } | Out-Null
Send-ServerCommand "money set 10"
Wait-Shared "money set 10" { param($d) $d.stats.money -eq 10 } | Out-Null
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-license -Arguments "2 TEST" | Out-Null
Start-Sleep -Seconds 4
Check (@(Server-Lines $mark "LicensePlates\(\d+\) money").Count -eq 0) "no plates are bought with 10 money"
Wait-Shared "after the refused plates" { param($d) (Count-Items $d "LicensePlate") -eq $platesBefore + 2 } | Out-Null
Send-ServerCommand "money set 200000"

# Barn map and barn trip.
$barns = (Dump $a).stats.barns
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-barn-map -Arguments "$map" | Out-Null
Check ([bool](Economy-Line $mark "BarnMap\(0\) money")) "the server applied the barn map"
Wait-Shared "after the barn map" { param($d) $d.stats.barns -eq $barns + 1 -and @($d.inventory.items | Where-Object { $_.UID -eq $map }).Count -eq 0 } | Out-Null
if (Guarded "Scene:Barn") { Skip "barn trip (Scene:Barn is guarded on this build)" }
else {
    $mark = Get-ServerLogMark
    Send-HarnessCommand -Instance $b -Verb econ-map-travel -Arguments "Barn" | Out-Null
    Check ([bool](Economy-Line $mark "TravelFee\(7\) money")) "the server applied the barn trip"
    try { Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "B in the barn" -Condition { param($s) $s.scene -ne "garage" -and $s.playable } | Out-Null } catch { }
    Send-HarnessCommand -Instance $b -Verb travel -Arguments "Garage" | Out-Null
    Wait-InGarage $b
    Wait-Shared "after the barn trip" { param($d) $d.stats.barns -eq $barns } | Out-Null
}

# Crates (upstream #94).
$mark = Get-ServerLogMark
for ($i = 0; $i -lt 3; $i++) {
    foreach ($pair in @(@($a, $casesA[$i]), @($b, $casesB[$i]))) {
        try { Send-HarnessCommand -Instance $pair[0] -Verb econ-crate -Arguments "$($pair[1]) $i" | Out-Null }
        catch { Check $false "$($pair[0]) crate $($pair[1]) card $i`: $($_.Exception.Message)" }
        Start-Sleep -Seconds 2
    }
}
Wait-Shared "after six crates" | Out-Null
$loots = @(Server-Lines $mark "\[Economy\] client \d+ Crate(Money|Exp|Scrap)\(0\) money")
Check ($loots.Count -eq 6) "six crate cards were applied ($($loots.Count))"
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-ledger -Arguments "resend" | Out-Null
Check ([bool](Economy-Line $mark "Crate(Money|Exp|Scrap)\(0\) refused Invalid")) "a replayed crate loot is refused"

$case = Add-Item $a "specialCase"
Wait-Shared "a case to close" { param($d) @($d.inventory.items | Where-Object { $_.UID -eq $case }).Count -eq 1 } | Out-Null
Send-HarnessCommand -Instance $a -Verb econ-crate-close -Arguments "$case" | Out-Null
Start-Sleep -Seconds 3
Wait-Shared "after closing the case unopened" { param($d) @($d.inventory.items | Where-Object { $_.UID -eq $case }).Count -eq 1 } | Out-Null
Send-ServerCommand "economy cases"
Send-ServerCommand "economy 60"

$Ctx.Result.notes += $failures
$Ctx.Result.notes += $skipped
$Ctx.Result.passed = ($failures.Count -eq 0)

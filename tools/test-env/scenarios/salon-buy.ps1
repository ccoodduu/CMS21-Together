# areas: economy, guard, placement
# shared-salon (ROADMAP row 26), guard enforcing: the guard lists the car version as part of the car salon and the
# showroom as main menu only. A and B travel to the car salon; A buys, through the configurator, a model with several
# versions in a non-default version with a non-default rim: the money drops by the price once on both, the car lands
# once in the shared parking of both, and after an unpark it has that version and rim on both. The server's NoMoney
# refusal: A holds its incoming packets, the server lowers the money, A buys (A's game still shows the old money, so
# its own check passes), A is answered "There is not enough shared money." and nothing changes. With the money already
# low, A's game refuses the purchase itself ("local refusal") and nothing is sent.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "salon_$Label.json") -Encoding utf8 }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Wait-InSalon([string]$Name) {
    $s = Wait-HarnessStatus -Instance $Name -TimeoutSec 180 -What "$Name in the car salon" -Condition {
        param($s) $s.scene -match "(?i)salon" -and $s.playable -and $s.connectionValid
    }
    Write-Host "$Name is in $($s.scene)"
}
function Dump([string]$Name) { Cmd $Name dump }
function Money([string]$Name) { (Dump $Name).stats.money }
function Slots([string]$Name) { @((Cmd $Name parking).slots) }
function Slots-Json([string]$Name) { Slots $Name | ConvertTo-Json -Depth 4 -Compress }
function Wait-Money([string]$What, [int]$Expected) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $ma = Money $a; $mb = Money $b
    } while (-not ($ma -eq $Expected -and $mb -eq $Expected) -and (Get-Date) -lt $deadline)
    Check ($ma -eq $Expected -and $mb -eq $Expected) "$What`: money is $Expected on A and B (A $ma, B $mb)"
}
function Wait-SlotCount([string]$What, [int]$Expected) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $na = @(Slots $a).Count; $nb = @(Slots $b).Count
    } while (-not ($na -eq $Expected -and $nb -eq $Expected) -and (Get-Date) -lt $deadline)
    Check ($na -eq $Expected -and $nb -eq $Expected) "$What`: $Expected parked cars on A and B (A $na, B $nb)"
    Check ((Slots-Json $a) -eq (Slots-Json $b)) "$What`: A and B have the same parking"
}
function Wait-SalonBuy([string]$Name, [int]$TimeoutSec = 90) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        Start-Sleep -Milliseconds 700
        $steps = @(Cmd $Name buy-car-last)
    } while (-not ($steps -match "^(pressed|failed|no ask window)") -and (Get-Date) -lt $deadline)
    Write-Host "salon-buy steps: $($steps -join ' | ')"
    return $steps
}
function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $true }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $false
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "enforce" | Out-Null }

# Guard clean-up (D3).
$rules = @(Cmd $a guard-rules)
Save "guard_rules" $rules
$version = @($rules | Where-Object { $_ -match "Window:CarVersion\b" })
Check ($version.Count -eq 1 -and $version[0] -match "^allow" -and $version[0] -match "row 26" -and $version[0] -match "Car version \(car salon\)") "the guard allows the car version as part of the car salon ($version)"
foreach ($key in "Scene:Showroom", "Window:Showroom") {
    $line = @($rules | Where-Object { $_ -match "$key\b" })
    Check ($line.Count -eq 1 -and $line[0] -match "^deny" -and $line[0] -match "The showroom \(main menu only\)") "the guard lists $key as main menu only ($line)"
}

Send-ServerCommand "money set 2000000"
Wait-Money "money set" 2000000
$slotsBefore = @(Slots $a).Count

# A configured purchase: several versions, a non-default version, a non-default rim.
foreach ($name in $Ctx.Instances) { Cmd $name travel "Salon" | Out-Null }
foreach ($name in $Ctx.Instances) { Wait-InSalon $name }
Start-Sleep -Seconds 3
$moneyBefore = Money $a
$mark = Get-ServerLogMark
Cmd $a salon-buy "multi other other" | Out-Null
$steps = Wait-SalonBuy $a
$bought = Cmd $a salon-last
Save "bought" @{ steps = $steps; salon = $bought }
Check ([bool]($steps -match "^pressed parking")) "A bought $($bought.car) in the salon and chose the parking"
Check ($bought.versions -ge 2 -and $bought.version -ne $bought.defaultVersion) "A configured a non-default version ($($bought.version) of $($bought.versions), default $($bought.defaultVersion))"
Check ($bought.rim -and $bought.rim -ne $bought.originalRim) "A chose a non-default rim ($($bought.rim), original $($bought.originalRim))"
$price = [int]$bought.price
$arrived = try { Wait-ServerLog -Pattern "\[Parking\] .* arrived in slot \d+ from client \d+ for $price\." -After $mark -TimeoutSec 20 } catch { $null }
Check ([bool]$arrived) "the server took the car into the parking for $price ($arrived)"
Wait-Money "after the purchase" ($moneyBefore - $price)
Wait-SlotCount "after the purchase" ($slotsBefore + 1)
Start-Sleep -Seconds 3
Check ((Money $a) -eq $moneyBefore - $price -and (Money $b) -eq $moneyBefore - $price) "the money went down exactly once"
$lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "arrived in slot" })
Check ($lines.Count -eq 1) "the server stored the car once ($($lines.Count))"
$guardLog = Cmd $a guard-log
Save "guard_log" $guardLog
Check (@($guardLog.keys).Count -eq 0) "the guard blocked nothing on the salon path ($(@($guardLog.keys) -join ', '))"
Check (@($guardLog.decided) -contains "Window:SalonSelectCar" -and @($guardLog.decided) -notcontains "Window:CarVersion") "the guard decided on the salon car list but never on the version window (it does not pass WindowManager.Show) ($(@($guardLog.decided) -join ', '))"

# Server refusal (D2): A's game still shows the old money when it buys.
$moneyNow = Money $a
$slotsNow = @(Slots $a).Count
Cmd $a net-hold "on" | Out-Null
$mark = Get-ServerLogMark
Send-ServerCommand "money set 1000"
$deadline = (Get-Date).AddSeconds(15)
do { Start-Sleep -Milliseconds 500 } while ((Money $b) -ne 1000 -and (Get-Date) -lt $deadline)
Check ((Money $b) -eq 1000 -and (Money $a) -eq $moneyNow) "B sees the lowered money, A still the old one (A $(Money $a), B $(Money $b))"
Cmd $a salon-buy "multi other other" | Out-Null
$steps = Wait-SalonBuy $a
Save "refused_steps" $steps
Check ([bool]($steps -match "^pressed parking")) "A's own check passed on the old money"
Start-Sleep -Seconds 2
Cmd $a net-hold "off" | Out-Null
$refused = try { Wait-ServerLog -Pattern "Arrival of .* from client \d+ refused: NoMoney" -After $mark -TimeoutSec 20 } catch { $null }
Check ([bool]$refused) "the server refused the purchase NoMoney ($refused)"
$deadline = (Get-Date).AddSeconds(15)
do { Start-Sleep -Milliseconds 500; $messages = @((Dump $a).session.messages) } while (-not ($messages -match "There is not enough shared money") -and (Get-Date) -lt $deadline)
Check ([bool]($messages -match "There is not enough shared money\.")) "A was told 'There is not enough shared money.' ($($messages -join ' | '))"
Wait-Money "after the refusal" 1000
Wait-SlotCount "after the refusal" $slotsNow
Check (@(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "arrived in slot" }).Count -eq 0) "no car was stored after the refusal"

# Local refusal: the money is already low on A.
$mark = Get-ServerLogMark
Cmd $a salon-buy "multi other other" | Out-Null
$steps = Wait-SalonBuy $a
Check ([bool]($steps -match "^no ask window")) "local refusal: A's game refuses the purchase itself"
Start-Sleep -Seconds 2
Check (@(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "Arrival of|arrived in slot" }).Count -eq 0) "local refusal: nothing reached the server"
Wait-SlotCount "after the local refusal" $slotsNow

# The bought car, unparked: the chosen version and rim on both.
foreach ($name in $Ctx.Instances) { Cmd $name travel "Garage" | Out-Null }
foreach ($name in $Ctx.Instances) { Wait-InGarage $name }
Send-ServerCommand "money set 2000000"
Wait-Money "money set back" 2000000
$slot = @(Slots $a | Where-Object { $_.carToLoad -eq $bought.car })
Check ($slot.Count -eq 1) "A's parking has $($bought.car) once ($(Slots-Json $a))"
if ($slot.Count -ge 1) {
    Cmd $a unpark "$($slot[0].index) 0" | Out-Null
    $ready = (Wait-Ready $a 0) -and (Wait-Ready $b 0)
    Check $ready "the unparked car is Ready on A and B"
    foreach ($name in $Ctx.Instances) {
        $loaded = Cmd $name car-loaded "0"
        $wheels = @(Cmd $name wheel-parts "0")
        Save "unparked_$name" @{ loaded = $loaded; wheels = $wheels }
        Check ($loaded.car -eq $bought.car -and $loaded.configVersion -eq $bought.version) "$name has $($bought.car) version $($bought.version) on loader 0 ($($loaded | ConvertTo-Json -Compress))"
        $rims = @($wheels | Where-Object { $_.id -eq $bought.rim })
        Check ($rims.Count -ge 4) "$name's car has the rim $($bought.rim) on every wheel ($(($wheels | ForEach-Object { $_.id }) -join ', '))"
    }
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# sync-car-parts DLC rule: a car that needs a DLC is only shared when every connected player owns that DLC (DLC ids
# are positions in the game's DLC list). A connects alone and claims the DLC, so its spawn request is accepted; once
# B joins without it, the same request is refused and A gets CarSpawnRejected. Requests are sent without loading the
# car, because the test installs own no DLC and loading a DLC car opens the game's missing-DLC window.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

$dlcCars = @(Send-HarnessCommand -Instance $a -Verb car-dlc-cars)
Check ($dlcCars.Count -gt 0) "the game lists DLC cars ($($dlcCars.Count))"
$pick = $dlcCars[0]
Write-Host "DLC car: $($pick.car) needs DLC $($pick.dlc)"
Check (@($dlcCars | Where-Object { $_.car -eq "car_boltatlanta" }).Count -eq 0) "car_boltatlanta is a base-game car"

Send-HarnessCommand -Instance $a -Verb compat-override -Arguments "dlc $($pick.dlc)" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb car-request -Arguments "1 $($pick.car)" | Out-Null
$accepted = Wait-ServerLog -Pattern "Loader 1: $([regex]::Escape($pick.car)) spawned by client" -After $mark -TimeoutSec 10
Check ([bool]$accepted) "A alone with DLC $($pick.dlc): the spawn is accepted ($accepted)"
Send-HarnessCommand -Instance $a -Verb car-request -Arguments "1 delete" | Out-Null
Start-Sleep -Seconds 1

Connect-HarnessInstance $b; Wait-InGarage $b
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb car-request -Arguments "2 $($pick.car)" | Out-Null
$refused = Wait-ServerLog -Pattern "Spawn of $([regex]::Escape($pick.car)) on loader 2 from client \d+ refused: DLC $($pick.dlc) is not shared" -After $mark -TimeoutSec 10
Check ([bool]$refused) "with B (no DLC) in the session the spawn is refused ($refused)"
Start-Sleep -Seconds 1
$clientLog = Get-Content -LiteralPath (Join-Path $env:USERPROFILE "CMS21-TestInstalls\$a\MelonLoader\Latest.log") -Raw
Check ($clientLog -match "CarSpawnRequest for Loader 2 was rejected by server") "A got the rejection"

$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb car-request -Arguments "3 car_boltatlanta" | Out-Null
Check ([bool](Wait-ServerLog -Pattern "Loader 3: car_boltatlanta spawned by client" -After $mark -TimeoutSec 10)) "a base-game car is still accepted"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

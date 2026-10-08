# areas: parts, details, cars
# Playtest 2026-10-07 finding 3: A takes a wheel off and mounts a wheel group back, first its own (same tire type),
# then a new one with another rim, tire and size. B must end with the same rim and tire on that wheel and the same
# car digest, and the server's forced desync check must find no car difference. The game writes a tire's or rim's
# tuned id in two ways (empty, or equal to its id), a new wheel changes the part id itself, and the car-details wheel
# apply rewrote the ids from the car's load-time wheel data. B then resyncs and still matches.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
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

function Wheels([string]$Name) {
    @(Cmd $Name wheel-parts "$loader" | ForEach-Object {
        $effective = if ($_.tuned) { $_.tuned } else { $_.id }
        "$($_.key)=$effective$(if ($_.unmounted) { ' (off)' })"
    }) -join ", "
}

function Wait-WheelsEqual([string]$What) {
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Seconds 1
        $wa = Wheels $a; $wb = Wheels $b
        $ra = Cmd $a car-ready "$loader"; $rb = Cmd $b car-ready "$loader"
        $da = (Cmd $a digest-show).("cars:$loader").hash; $db = (Cmd $b digest-show).("cars:$loader").hash
        $same = $wa -eq $wb -and $ra.stateHash -eq $rb.stateHash -and $da -eq $db
    } while (-not $same -and (Get-Date) -lt $deadline)
    Write-Host "A: $wa"
    Write-Host "B: $wb"
    Check ($wa -eq $wb) "$What`: B has A's rims and tires"
    Check ($ra.stateHash -eq $rb.stateHash) "$What`: A and B agree on the part state ($($ra.stateHash) / $($rb.stateHash))"
    Check ($da -and $da -eq $db) "$What`: A and B have the same car digest ($da / $db)"
    $wa
}

function Check-ServerDigest([string]$What) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "desync check"
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 500
        $lines = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Desync\] cars:$loader" })
    } while (@($lines | Where-Object { $_ -match "for client \d+: (match|mismatch)" }).Count -lt 2 -and (Get-Date) -lt $deadline)
    $lines | ForEach-Object { Write-Host "server: $_" }
    $matchCount = @($lines | Where-Object { $_ -match "for client \d+: match" }).Count
    Check ($matchCount -eq 2 -and -not ($lines -match "mismatch|differ")) "$What`: the server's car digest matches both players ($matchCount matches)"
}

function Mount-Wheel([string]$RimKey, [long]$GroupUid) {
    Cmd $a wheel-mount "$loader $RimKey $GroupUid" | Out-Null
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 500
        $rim = Cmd $a wheel-parts "$loader" | Where-Object { $_.key -eq $RimKey }
    } while ($rim.unmounted -and (Get-Date) -lt $deadline)
    Check (-not $rim.unmounted) "A mounted the wheel on $RimKey ($($rim.id))"
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
Start-Sleep -Seconds 2
$logB = Join-Path $env:USERPROFILE "CMS21-TestInstalls\$b\MelonLoader\Latest.log"
$logStart = @(Get-Content -LiteralPath $logB).Count

$rims = @(Cmd $a wheel-parts "$loader" | Where-Object { $_.id -like "rim*" } | ForEach-Object { $_.key })
Check ($rims.Count -ge 2) "the car has rims ($($rims -join ', '))"

$groupsBefore = @((Cmd $a dump).inventory.groups | ForEach-Object { $_.UID })
Cmd $a part-fast-unmount "$loader $($rims[0])" | Out-Null
Start-Sleep -Seconds 2
$own = @((Cmd $a dump).inventory.groups | Where-Object { $groupsBefore -notcontains $_.UID }) | Select-Object -First 1
Check ($null -ne $own) "the wheel came off as a group ($($own.ID))"
Mount-Wheel $rims[0] $own.UID
Wait-WheelsEqual "own wheel back on" | Out-Null
Check-ServerDigest "own wheel back on"

Cmd $a part-fast-unmount "$loader $($rims[1])" | Out-Null
Start-Sleep -Seconds 2
$new = Cmd $a give-group "wheel"
Mount-Wheel $rims[1] $new.UID
$wheels = Wait-WheelsEqual "new rim and tire type"
Check ($wheels -match "$([regex]::Escape($rims[1]))=rim_3" -and $wheels -match "$([regex]::Escape($rims[1]))\.\d+=tire_standard") "A's new wheel is rim_3 with tire_standard"
$sizeA = (Cmd $a cardetails-show "$loader").Wheels
$sizeB = (Cmd $b cardetails-show "$loader").Wheels
Check ($sizeA -eq $sizeB) "A and B have the same wheel sizes"
Start-Sleep -Seconds 3
Wait-WheelsEqual "after the wheel sizes were applied" | Out-Null
Check-ServerDigest "new rim and tire type"

$unresolved = @(Get-Content -LiteralPath $logB | Select-Object -Skip $logStart | Where-Object { $_ -match "does not resolve|asking the server for a resync" })
Check ($unresolved.Count -eq 0) "B resolved every wheel record ($($unresolved.Count) warnings: $($unresolved | Select-Object -First 2))"

Check ("$(Cmd $b resync)" -eq "reloading") "B resyncs"
Start-Sleep -Seconds 3
Wait-InGarage $b
Wait-Ready $b | Out-Null
Wait-WheelsEqual "after B's resync" | Out-Null

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

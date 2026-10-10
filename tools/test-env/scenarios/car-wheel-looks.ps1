# areas: parts, details, visuals
# Playtest 3 (2026-10-10): tires missing, rims hollow and magenta wheels, also after F7, and a wheel left in the air
# where another player had taken it off. A takes the front left wheel off and mounts a wheel of another rim, tire and
# size on the front right, so B applies the new front wheel size while its front left wheel is off. B's wheels must
# keep the game's materials (none destroyed, none replaced by Unity's default, the unmounted wheel still in the X-ray
# look the game hides it with), the rim and tire prefabs' materials must survive for the rest of the session, and B's
# rims and tires must have A's meshes, blend shapes and scales. Then A mounts the front left wheel back and B resyncs.
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
        $r = try { Cmd $Name car-ready "$loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Looks([string]$Name, [string]$Label) {
    $looks = Cmd $Name wheel-visuals "$loader"
    $looks | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "wheels_$($Label)_$Name.json") -Encoding utf8
    $looks
}

# What a player sees of each rim and tire: meshes, blend shapes and scales (tyl, the hub plate, is left out: its depth
# follows the ET, which the game scales from the current value).
function Shape($Looks) {
    @($Looks.parts | Sort-Object key | ForEach-Object {
        $renderers = @($_.renderers | Where-Object { $_.name -match "^(srodek|szerokosc|tire)$" } | ForEach-Object {
            "$($_.name):$($_.vertices):$(@($_.blendShapes) -join ','):$($_.localScale -join '/')"
        })
        "$($_.key)=$($_.id) $($_.localScale -join '/') $($renderers -join ' ')"
    }) -join "`n"
}

function Check-Materials([string]$Name, $Looks, [string]$What, [string[]]$OffKeys = @()) {
    foreach ($part in $Looks.parts) {
        $materials = @($part.renderers | Where-Object { $_.name -match "^(srodek|szerokosc|tyl|tire)$" } | ForEach-Object { $_.materials })
        $broken = @($materials | Where-Object { $_ -eq "<null>" -or $_ -match "\|(Standard|Hidden/InternalErrorShader)\|" })
        Check ($broken.Count -eq 0) "$What`: $Name's $($part.key) ($($part.id)) has the game's materials ($($broken.Count) destroyed or default: $(($broken | Select-Object -First 2) -join ', '))"
        $xray = @($materials | Where-Object { $_ -match "CMS21/Xray" })
        if ($OffKeys -contains $part.key) {
            Check ($xray.Count -eq $materials.Count) "$What`: $Name's unmounted $($part.key) stays hidden ($($xray.Count) of $($materials.Count) X-ray)"
        } elseif (-not $part.unmounted) {
            Check ($xray.Count -eq 0) "$What`: $Name's mounted $($part.key) is not drawn as X-ray"
        }
    }
    $prefabs = @($Looks.prefabs.PSObject.Properties | ForEach-Object { $_.Value } | Where-Object { $_ -match "<null>" })
    Check ($prefabs.Count -eq 0) "$What`: $Name's rim and tire prefabs keep their materials ($($prefabs -join '; '))"
    Check ($Looks.ghosts.count -eq 0) "$What`: $Name has no ghost left ($(@($Looks.ghosts.roots | ForEach-Object { $_.name }) -join ', '))"
}

function Wait-WheelsEqual([string]$What) {
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Seconds 1
        $wa = (Cmd $a cardetails-show "$loader").Wheels | ConvertTo-Json -Compress
        $wb = (Cmd $b cardetails-show "$loader").Wheels | ConvertTo-Json -Compress
        $pa = (Cmd $a car-ready "$loader").stateHash; $pb = (Cmd $b car-ready "$loader").stateHash
    } while (($wa -ne $wb -or $pa -ne $pb) -and (Get-Date) -lt $deadline)
    Check ($wa -eq $wb -and $pa -eq $pb) "$What`: A and B agree on the wheel sizes and the part state"
    Start-Sleep -Seconds 2
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
$rims = @(Cmd $a wheel-parts "$loader" | Where-Object { $_.id -like "rim*" } | ForEach-Object { $_.key })
Check ($rims.Count -eq 4) "the car has four rims ($($rims -join ', '))"
$fl, $fr = $rims[0], $rims[1]
$flTire = "$fl.4"

$groupsBefore = @((Cmd $a dump).inventory.groups | ForEach-Object { $_.UID })
Cmd $a part-fast-unmount "$loader $fl" | Out-Null
Start-Sleep -Seconds 2
$own = @((Cmd $a dump).inventory.groups | Where-Object { $groupsBefore -notcontains $_.UID }) | Select-Object -First 1
Check ($null -ne $own) "the front left wheel came off as a group"

Cmd $a part-fast-unmount "$loader $fr" | Out-Null
Start-Sleep -Seconds 2
$new = Cmd $a give-group "wheel rim_3,tire_standard 205/18/40"
Mount-Wheel $fr $new.UID
Wait-WheelsEqual "new front right wheel"
$la = Looks $a "1-new-wheel"; $lb = Looks $b "1-new-wheel"
Check-Materials $a $la "new front right wheel" @($fl, $flTire)
Check-Materials $b $lb "new front right wheel" @($fl, $flTire)
$sa = Shape $la; $sb = Shape $lb
Check ($sa -eq $sb) "new front right wheel: B's rims and tires have A's meshes, blend shapes and scales`nA:`n$sa`nB:`n$sb"

Mount-Wheel $fl $own.UID
Wait-WheelsEqual "front left wheel back on"
$la = Looks $a "2-own-wheel"; $lb = Looks $b "2-own-wheel"
Check-Materials $a $la "front left wheel back on"
Check-Materials $b $lb "front left wheel back on"
$sa = Shape $la; $sb = Shape $lb
Check ($sa -eq $sb) "front left wheel back on: B's rims and tires have A's meshes, blend shapes and scales`nA:`n$sa`nB:`n$sb"

Check ("$(Cmd $b resync)" -eq "reloading") "B resyncs"
Start-Sleep -Seconds 3
Wait-InGarage $b
Wait-Ready $b | Out-Null
Wait-WheelsEqual "after B's resync"
$lb = Looks $b "3-resync"
Check-Materials $b $lb "after B's resync"
$sb = Shape $lb
Check ($sa -eq $sb) "after B's resync: B's rims and tires have A's meshes, blend shapes and scales`nA:`n$sa`nB:`n$sb"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

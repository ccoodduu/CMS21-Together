# areas: parts, visuals
# Playtest 3 (2026-10-10): B takes a worn part off and mounts a new one at 100 %; on A the new part looked as rusty as
# the old one until F7. A's part must show the new part's wear: the RustWeight its materials carry once the remote
# mount has shown it equals what PartScript.UpdateShaderParams (F7) writes for its condition.
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0
$car = "car_boltatlanta"
$worn = 0.2
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

function Sub($Dump, [string]$Key) { @(@($Dump.cars | Where-Object { $_.index -eq $loader })[0].subParts | Where-Object { $_.key -eq $Key })[0] }

function Wait-Part([string]$Name, [string]$Key, [scriptblock]$Condition, [string]$What) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $part = Sub (Cmd $Name dump) $Key
        if (& $Condition $part) { return $part }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    Check $false "$Name`: $What ($($part | ConvertTo-Json -Compress))"
    $part
}

function Spread($Values) { if (@($Values).Count -eq 0) { "none" } else { "$((@($Values) | Measure-Object -Minimum).Minimum)..$((@($Values) | Measure-Object -Maximum).Maximum)" } }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$candidates = @(Cmd $b vfx-parts "$loader")
$part = @($candidates | Where-Object { $_.id -match "tarczaHamulcowa|pokrywa_glowicy|wentylator" } | Select-Object -First 1)[0]
if (-not $part) { $part = $candidates[0] }
$key = $part.key
Write-Host "part $key ($($part.id))"

Cmd $b part-condition "$loader $key $worn" | Out-Null
Wait-Part $a $key { param($p) [math]::Abs($p.condition - $worn) -lt 0.01 } "the worn condition arrives" | Out-Null
$shader = Cmd $a part-shader "$loader $key"
Write-Host "  A worn: $($shader | ConvertTo-Json -Compress)"
Check (@($shader.rust).Count -gt 0) "A reads RustWeight on the part's materials ($(@($shader.rust).Count) materials)"
Check (@($shader.rust | Where-Object { [math]::Abs($_ - (1 - $worn)) -gt 0.01 }).Count -eq 0) "A shows the worn part's rust ($(Spread $shader.rust), condition $($shader.condition))"

Cmd $b part-fast-unmount "$loader $key" | Out-Null
Wait-Part $a $key { param($p) $p.unmounted } "the part comes off" | Out-Null
$new = Cmd $b give-item "$($part.id) 1"
Write-Host "  B's new item: $($new | ConvertTo-Json -Compress)"
Start-Sleep -Seconds 2
Cmd $b part-domount "$loader $key $($new.UID)" | Out-Null
$mounted = Wait-Part $a $key { param($p) -not $p.unmounted -and $p.condition -ge 0.99 } "the new part is mounted at 100 %"
Start-Sleep -Seconds 2

$shaderB = Cmd $b part-shader "$loader $key"
Write-Host "  B new: $($shaderB | ConvertTo-Json -Compress)"
$shaderA = Cmd $a part-shader "$loader $key refresh"
Write-Host "  A new: $($shaderA | ConvertTo-Json -Compress)"
Check (@($shaderA.rust).Count -gt 0) "A reads RustWeight after the mount"
Check (@($shaderA.rust | Where-Object { $_ -gt 0.01 }).Count -eq 0) "A shows the new part without rust ($(Spread $shaderA.rust), condition $($shaderA.condition))"
Check ((@($shaderA.rust) -join ",") -eq (@($shaderA.fresh) -join ",")) "A's shader values equal what F7 writes (now $(Spread $shaderA.rust), after UpdateShaderParams $(Spread $shaderA.fresh))"
Check (@($shaderB.rust | Where-Object { $_ -gt 0.01 }).Count -eq 0) "B, who mounted it, shows no rust ($(Spread $shaderB.rust))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

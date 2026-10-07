# areas: locks
# run-all: skip
# part-locks spikes (tasks group 1). Logging only: relations and lock-set sizes of three cars (1.3), the game's own
# raycast, hover and hold path driven by lock-click (1.1, 1.6, 1.7), the mount flow with its item UIDs and back-outs
# (1.2), and every gated entry point blocked and re-invoked 150 ms later (1.4, 1.5). Findings go to
# docs/spikes/part-locks.md; this scenario only fails when a step throws.
param($Ctx, [string[]]$Only = @())

$a, $b = $Ctx.Instances
$failures = @()
$findings = [ordered]@{}
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Step([string]$Name, [scriptblock]$Body) {
    if ($Only.Count -gt 0 -and -not ($Only | Where-Object { $Name -like $_ })) { return }
    Write-Host "--- $Name"
    try { $script:findings[$Name] = & $Body }
    catch { $script:findings[$Name] = "ERROR: $($_.Exception.Message)"; Write-Host "  error: $($_.Exception.Message)" -ForegroundColor Yellow }
}

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Cmd $Name car-ready "$Loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $Loader not Ready"
}

function Wait-Part([string]$Key, [bool]$Unmounted, [int]$Seconds = 40) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $p = Cmd $a lock-probe "part 0 $Key"
        if ($p.unmounted -eq $Unmounted) { return $p }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    $p
}

function Report([string]$Label) {
    $r = Cmd $a lock-trace "report 4000"
    $r.lines | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "trace-$Label.txt")
    Cmd $a lock-trace "clear" | Out-Null
    $r.lines
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

$cars = @(@{ Loader = 0; Car = "car_boltatlanta"; Place = "CarLifter1" }, @{ Loader = 1; Car = "car_sixoncebulion"; Place = "Entrance1" })
foreach ($c in $cars) { Cmd $a car-spawn "$($c.Loader) $($c.Car) 0 $($c.Place)" | Out-Null }
foreach ($c in $cars) { Wait-Ready $a $c.Loader | Out-Null; Wait-Ready $b $c.Loader | Out-Null }
Start-Sleep -Seconds 2

Step "relations" {
    $out = [ordered]@{}
    foreach ($c in $cars) { $out[$c.Car] = Cmd $a lock-trace "relations $($c.Loader) $(Join-Path $Ctx.RunDir "relations-$($c.Car).tsv")" }
    $out
}
Step "relations-third" {
    $list = @(Cmd $a car-list | Where-Object { -not $_.dlc -and -not $_.mod -and $_.id -notin @("car_boltatlanta", "car_sixoncebulion") })
    $picked = $null
    foreach ($candidate in ($list | Select-Object -First 6)) {
        Cmd $a car-spawn "2 $($candidate.id) 0 Entrance2" | Out-Null
        try { Wait-Ready $a 2 | Out-Null } catch { continue }
        $r = Cmd $a lock-trace "relations 2 $(Join-Path $Ctx.RunDir "relations-$($candidate.id).tsv")"
        $types = @($r.fluids | ForEach-Object { ($_ -split '\.')[0] } | Sort-Object -Unique)
        Write-Host "  $($candidate.id): fluids $($r.fluids -join ',')"
        $picked = $r
        if ($types.Count -ge 5) { break }
        Cmd $a car-delete "2" | Out-Null
        Start-Sleep -Seconds 3
    }
    $picked
}

Cmd $a lock-trace "on" | Out-Null
$candidates = @(Cmd $a vfx-parts "0")
Write-Host "candidates: $(($candidates | Select-Object -First 12 | ForEach-Object { "$($_.key)=$($_.id)" }) -join ', ')"
$relationsFile = Join-Path $Ctx.RunDir "relations-car_boltatlanta.tsv"
$caliperLine = if (Test-Path -LiteralPath $relationsFile) { @(Get-Content -LiteralPath $relationsFile | Where-Object { $_ -match "^s:\S+\t\S*zacisk" } | Select-Object -First 1)[0] }
$part = if ($caliperLine) { [pscustomobject]@{ key = ($caliperLine -split "`t")[0]; id = ($caliperLine -split "`t")[1] } } else { $candidates[0] }
Write-Host "caliper: $($part.key) $($part.id)"
$others = @($candidates | Where-Object { $_.key -ne $part.key })

Step "click-hover-hold" {
    $c = Cmd $a lock-click "0 $($others[0].key) hold 2600"
    Start-Sleep -Seconds 4
    $state = Cmd $a lock-trace "state"
    [ordered]@{ click = $c; state = $state; status = (Cmd $a lock-click "status"); trace = @(Report "click-hover-hold") }
}
Step "click-finish" {
    Cmd $a lock-probe "bolts 0 $($others[0].key)" | Out-Null
    $part0 = Wait-Part $others[0].key $true
    $state = Cmd $a lock-trace "state"
    Cmd $a lock-probe "mode PartSelect 0" | Out-Null
    [ordered]@{ state = $state; trace = @(Report "click-finish") }
}
Step "click-short" {
    $c = Cmd $a lock-click "0 $($others[1].key) hold 300"
    Start-Sleep -Seconds 2
    [ordered]@{ click = $c; state = (Cmd $a lock-trace "state"); trace = @(Report "click-short") }
}

Cmd $a lock-trace "reinvoke on" | Out-Null
Step "reinvoke-unmount" {
    Cmd $a lock-probe "mode PartSelect 0" | Out-Null
    $r = Cmd $a lock-probe "unmount 0 $($others[2].key)"
    Start-Sleep -Milliseconds 800
    $state = Cmd $a lock-trace "state"
    Cmd $a lock-probe "bolts 0 $($others[2].key)" | Out-Null
    Wait-Part $others[2].key $true | Out-Null
    [ordered]@{ first = $r; after = $state; final = (Cmd $a lock-trace "state"); trace = @(Report "reinvoke-unmount") }
}
Step "reinvoke-refused" {
    Cmd $a lock-probe "mode PartSelect 0" | Out-Null
    $dump = Cmd $a dump
    $blocked = @($dump.cars | Where-Object { $_.index -eq 0 } | ForEach-Object { $_.subParts } | Where-Object { $_.blocked -and -not $_.unmounted } | Select-Object -First 1)[0]
    $r = Cmd $a lock-probe "unmount 0 $($blocked.key) noforce"
    Start-Sleep -Milliseconds 800
    [ordered]@{ part = $blocked; first = $r; after = (Cmd $a lock-trace "state"); trace = @(Report "reinvoke-refused") }
}
Step "reinvoke-mount-chooser-close" {
    Cmd $a lock-probe "mode PartSelectMount 0" | Out-Null
    $r = Cmd $a lock-probe "mount 0 $($others[2].key)"
    Start-Sleep -Milliseconds 800
    $state = Cmd $a lock-trace "state"
    $items = Cmd $a lock-probe "chooser-items"
    $closed = Cmd $a lock-probe "chooser-close"
    Start-Sleep -Milliseconds 500
    [ordered]@{ first = $r; open = $state; items = $items; closed = $closed; after = (Cmd $a lock-trace "state"); trace = @(Report "reinvoke-mount-chooser-close") }
}
Step "reinvoke-mount-item" {
    $r = Cmd $a lock-probe "mount 0 $($others[2].key)"
    Start-Sleep -Milliseconds 800
    $items = @(Cmd $a lock-probe "chooser-items")
    $uid = $null
    if ($items.Count -gt 0 -and $items[0] -match '#(\d+)') { $uid = $Matches[1] }
    $selected = if ($uid) { Cmd $a lock-probe "select $uid" } else { "no item in the chooser" }
    Start-Sleep -Milliseconds 800
    $state = Cmd $a lock-trace "state"
    Cmd $a lock-probe "bolts 0 $($others[2].key) mount" | Out-Null
    Wait-Part $others[2].key $false | Out-Null
    [ordered]@{ items = $items; selected = $selected; afterSelect = $state; final = (Cmd $a lock-trace "state"); trace = @(Report "reinvoke-mount-item") }
}
Step "caliper-group" {
    Cmd $a lock-trace "reinvoke off" | Out-Null
    Cmd $a lock-probe "mode PartSelect 0" | Out-Null
    $out = Cmd $a part-fast-unmount "0 $($part.key)"
    Start-Sleep -Seconds 3
    Cmd $a lock-probe "mode PartSelectMount 0" | Out-Null
    $r = Cmd $a lock-probe "mount 0 $($part.key)"
    Start-Sleep -Milliseconds 800
    $items = @(Cmd $a lock-probe "chooser-items")
    $inventory = (Cmd $a dump).inventory.items
    $uids = @($inventory | Where-Object { $_.ID -like "$($part.id -replace '_\d+$', '')*" } | Sort-Object { $_.ID.Length } | ForEach-Object { $_.UID })
    $group = if ($uids.Count -gt 1) { Cmd $a lock-probe "group $($uids -join ' ')" } else { "fewer than two items: $($items -join '; ')" }
    Start-Sleep -Seconds 1
    $state = Cmd $a lock-trace "state"
    [ordered]@{ part = $part; unmount = $out; items = $items; group = $group; after = $state; trace = @(Report "caliper-group") }
}
Cmd $a lock-trace "reinvoke on" | Out-Null
Step "reinvoke-body" {
    Cmd $a lock-probe "mode Garage 0" | Out-Null
    $dump = Cmd $a dump
    $panel = @($dump.cars | Where-Object { $_.index -eq 0 } | ForEach-Object { $_.bodyParts } | Where-Object { -not $_.unmounted -and $_.name -match "door|hood|bonnet|maska|drzwi" } | Select-Object -First 1)[0]
    $index = [int]($panel.key -replace '^b:', '')
    $r = Cmd $a lock-probe "body-off 0 $index"
    Start-Sleep -Milliseconds 800
    $after = Cmd $a dump
    $now = @($after.cars | Where-Object { $_.index -eq 0 } | ForEach-Object { $_.bodyParts } | Where-Object { $_.key -eq $panel.key })[0]
    [ordered]@{ panel = $panel; first = $r; after = $now; trace = @(Report "reinvoke-body") }
}
Step "reinvoke-lift" {
    $r = Cmd $a lock-probe "lift 0 up"
    Start-Sleep -Milliseconds 400
    $second = Cmd $a lock-probe "lift 0 up"
    Start-Sleep -Seconds 12
    [ordered]@{ first = $r; whileMoving = $second; lifters = (Cmd $a lifters); trace = @(Report "reinvoke-lift") }
}
Step "reinvoke-oil" {
    $r = Cmd $a lock-probe "oil 0"
    Start-Sleep -Seconds 8
    $again = Cmd $a lock-probe "oil 0"
    Start-Sleep -Seconds 2
    [ordered]@{ first = $r; empty = $again; trace = @(Report "reinvoke-oil") }
}
Step "reinvoke-fill" {
    $none = try { Cmd $a lock-probe "fill" } catch { $_.Exception.Message }
    Start-Sleep -Milliseconds 600
    $car = try { Cmd $a lock-probe "fill 0" } catch { $_.Exception.Message }
    Start-Sleep -Milliseconds 600
    $extract = try { Cmd $a lock-probe "extract 0" } catch { $_.Exception.Message }
    Start-Sleep -Seconds 4
    [ordered]@{ noCar = $none; car = $car; extract = $extract; state = (Cmd $a lock-trace "state"); trace = @(Report "reinvoke-fill") }
}
Step "move" {
    Cmd $a lock-trace "reinvoke off" | Out-Null
    Cmd $a car-move "1 Entrance3" | Out-Null
    Start-Sleep -Seconds 8
    [ordered]@{ placement = (Cmd $a placement); trace = @(Report "move") }
}
Cmd $a lock-trace "reinvoke on" | Out-Null
Step "reinvoke-crane" {
    $r = Cmd $a lock-probe "crane-out 1"
    Start-Sleep -Seconds 2
    [ordered]@{ first = $r; trace = @(Report "reinvoke-crane") }
}
Cmd $a lock-trace "reinvoke off" | Out-Null

$findings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "findings.json")
$errors = @($findings.Keys | Where-Object { "$($findings[$_])" -like "ERROR:*" })
Check ($errors.Count -eq 0) "every spike step ran ($($errors -join ', '))"
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

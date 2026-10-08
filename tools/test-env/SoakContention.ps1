# Soak contention mode (state-merges-and-contention D13), dot-sourced by scenarios\soak.ps1, whose script scope it
# shares (Invoke-Step, Add-Marker, Add-Failure, Pick, $rng, $results, $open, the session model).
#
# A contention group: a seeded kind picks 2-4 members and a target, runs its setup steps, then holds every member's
# outgoing packets (net-hold out), issues each member's verb, and releases the members one at a time in a seeded order,
# member i+1 only after the server logged member i's packet. The observed server order goes into the "contend" marker.
# After up to 10 s for the members' dumps to agree, rule 8 (conservation: items of a contended part's id, and every
# contended UID in at most one place, in none if it was mounted or sold) and rule 9 (the kind's outcome) are checked.
# A kind in soak-contention-known.txt is reported as "known gap" instead of failing; the random draw leaves such kinds
# out unless -ContentionIncludeKnown or -ContentionKinds names them, because the state they leave behind fails later
# checkpoints. -Replay reruns each group from its
# "contend-start" marker and the logged steps and compares the observed server order with the recorded one.

$ContentionKindsAll = @(
    "part-same", "mount-same-item", "examine-vs-unmount", "gone-inflight", "details-pair", "details-same",
    "machine-same-item", "machine-vs-sale", "mount-vs-sale", "machine-same-slot", "item-trades", "lift-same", "place-same", "park-stale"
)
$ContentionManyMembers = @("part-same", "details-same", "machine-same-slot", "item-trades", "lift-same")
$contentionFile = Join-Path $runDir "contention.jsonl"
$MaxHoldSeconds = 7  # a stalled client must answer a heartbeat within the server's 10 s
$script:contentionIndex = 0
$script:contentionStats = @{ groups = 0; passed = 0; failed = 0; known = 0; invalid = 0; replayOrder = 0; replayOrderSame = 0 }

function Read-ContentionKnown([string]$Path) {
    $known = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $known }
    foreach ($line in Get-Content -LiteralPath $Path) {
        if (-not $line.Trim() -or $line.TrimStart().StartsWith("#")) { continue }
        $fields = @($line -split '\|' | ForEach-Object { $_.Trim() })
        $known[$fields[0]] = "gap $($fields[1]), closed by $($fields[2])"
    }
    return $known
}
$contentionKnown = Read-ContentionKnown (Join-Path $PSScriptRoot "scenarios/soak-contention-known.txt")

function Get-MemberPattern([string]$Verb, [int]$PlayerId) {
    switch -Regex ($Verb) {
        '^(part-fast-unmount|part-fast-mount|wheel-mount|diag-examine)$' { return "\[Cars\] Change \S+ from client $PlayerId (on|for) loader" }
        '^cardetails-' { return "\[CarDetails\] Loader \d+: update \d+ from client $PlayerId\b" }
        '^tool-(put|take)$' { return "\[Tools\] \w+: .*from client $PlayerId\b" }
        '^sell-item$' { return "\[Shop\] Sale of .* from client $PlayerId`:" }
        '^econ-scrap$' { return "\[Economy\] client $PlayerId " }
        '^warehouse-move$' { return "\[Inventory\] Warehouse move of .* from client $PlayerId`:" }
        '^lift$' { return "(\[Locks\] (Lock \d+ granted to client $PlayerId`: .* Lift |Request \d+ by client $PlayerId .*\(Lift\))|\[Placement\] Lift .* client $PlayerId\b)" }
        '^car-move$' { return "(\[Locks\] (Lock \d+ granted to client $PlayerId`: .* Move |Request \d+ by client $PlayerId .*\(Move\))|\[Placement\] .*(Move of loader|moved|swapped).* client $PlayerId\b)" }
        '^park$' { return "\[Parking\] .*(Park of loader|parked in slot).* client $PlayerId\b" }
        '^car-delete$' { return "(Received CarSpawnDelete from client $PlayerId\b|Delete of empty loader \d+ from client $PlayerId\b)" }
        default { return "client $PlayerId\b" }
    }
}

function Get-FreeKeys([string]$Actor, [int]$Loader) {
    $dumpCar = @((Send-HarnessCommand -Instance $Actor -Verb dump).cars | Where-Object { $_.index -eq $Loader })[0]
    $openKeys = @($open | Where-Object { $_.Loader -eq $Loader } | ForEach-Object { $_.Key })
    $wheelKeys = @(Get-WheelKeys $Actor $Loader)
    @($dumpCar.subParts | Where-Object { -not $_.unmounted -and -not $_.blocked -and $openKeys -notcontains $_.key -and $wheelKeys -notcontains $_.key } | ForEach-Object { $_.key })
}

function Get-ContentionCar([string]$Actor, [switch]$Removable) {
    $cars = @(Get-ReadyCars $Actor | Where-Object { (Get-OpenCount $_.loader) -eq 0 })
    if ($Removable) {
        if (@(Get-Placement $Actor).Count -le 2) { return $null }
        $jobLoaders = Get-JobLoaders $Actor
        $cars = @($cars | Where-Object { $jobLoaders -notcontains [int]$_.loader })
    }
    Pick $cars
}

function Get-SlotUid($Dump, [string]$Tool) { [long]$Dump.tools.$Tool.uid }

function Get-ContentionState($Spec) {
    $actors = @($Spec.members | ForEach-Object { $_.actor } | Select-Object -Unique)
    $dump = Send-HarnessCommand -Instance $actors[0] -Verb dump
    $items = @{}
    foreach ($item in @($dump.inventory.items)) { $items[$item.ID] = 1 + [int]$items[$item.ID] }
    $groups = @{}
    foreach ($group in @($dump.inventory.groups)) { $groups[$group.ID] = 1 + [int]$groups[$group.ID] }
    $parts = @{}
    foreach ($p in @($Spec.parts)) {
        $car = @($dump.cars | Where-Object { $_.index -eq [int]$p.loader })[0]
        $sub = if ($car) { @($car.subParts | Where-Object { $_.key -eq $p.key })[0] } else { $null }
        $parts["$($p.loader)|$($p.key)"] = if ($sub) { [pscustomobject]@{ unmounted = [bool]$sub.unmounted; id = $sub.id } } else { $null }
    }
    $where = @{}
    foreach ($template in @($Spec.uids)) {
        $uid = Resolve-Template $template
        $where[$uid] = @($actors | ForEach-Object { (Send-HarnessCommand -Instance $_ -Verb item-where -Arguments $uid).where })
    }
    [pscustomobject]@{ dump = $dump; items = $items; groups = $groups; parts = $parts; where = $where; money = [long]$dump.stats.money; scrap = [long]$dump.stats.scrap }
}

# The members' sections agree within 10 s. The cars section is compared without the parts' blocked counters: a
# part-fast-mount without an item does not block on the actor what the receivers block (checkpoints still compare them).
function Wait-ContentionSettle([string[]]$Actors, [string[]]$Sections) {
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $texts = @($Actors | ForEach-Object {
            $dump = Send-HarnessCommand -Instance $_ -Verb dump
            (@($Sections | ForEach-Object {
                $value = $dump.$_
                if ($_ -eq "cars" -and $value) { $value = @($value | ForEach-Object { [pscustomobject]@{ index = $_.index; carToLoad = $_.carToLoad; placeNo = $_.placeNo; subParts = @($_.subParts | Select-Object -Property * -ExcludeProperty blocked) } }) }
                $value | ConvertTo-Json -Depth 10 -Compress
            }) -join "|")
        })
        if (@($texts | Select-Object -Unique).Count -le 1) { return $true }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Count-Lines($Lines, [string]$Pattern) { @($Lines | Where-Object { $_ -match $Pattern }).Count }

function Get-IdAlternation($Ids) { "(" + (@($Ids.Values) -join "|") + ")" }

# --- kinds: each builder runs its setup steps and returns the group spec, or $null when the session cannot host it now --
$ContentionBuilders = @{
    "part-same" = {
        param($Members)
        $car = Get-ContentionCar $Members[0]
        if (-not $car) { return $null }
        $key = Pick (Get-FreeKeys $Members[0] $car.loader)
        if (-not $key) { return $null }
        @{ loader = [int]$car.loader; car = $car.carToLoad; sections = @("cars", "inventory")
           members = @($Members | ForEach-Object { [ordered]@{ actor = $_; verb = "part-fast-unmount"; args = "$($car.loader) $key"; template = "" } })
           parts = @(@{ loader = [int]$car.loader; key = $key; consumes = $false }); uids = @(); data = @{ key = $key } }
    }
    "mount-same-item" = {
        param($Members)
        $car = Get-ContentionCar $Members[0]
        if (-not $car) { return $null }
        $twins = Invoke-Step $Members[0] part-twins "$($car.loader)" -Action "contention"
        if (-not $twins -or @($twins.keys).Count -lt 2) { return $null }
        $keys = @($twins.keys)
        foreach ($key in $keys) { Invoke-Step $Members[0] part-fast-unmount "$($car.loader) $key" -Action "contention" | Out-Null; $open.Add([pscustomobject]@{ Loader = [int]$car.loader; Car = $car.carToLoad; Key = $key; Step = $script:stepNo; By = $Members[0]; Due = (Get-Date).AddSeconds(30) }) }
        $given = Invoke-Step $Members[0] give-item "$($twins.id) 1.00" -Action "contention"
        if (-not $given) { return $null }
        $uid = "{step:$($script:stepNo):UID}"
        Start-Sleep -Seconds 2
        $pair = @($Members | Select-Object -First 2)
        @{ loader = [int]$car.loader; car = $car.carToLoad; sections = @("cars", "inventory")
           members = @(for ($i = 0; $i -lt 2; $i++) { [ordered]@{ actor = $pair[$i]; verb = "part-fast-mount"; args = ""; template = "$($car.loader) $($keys[$i]) $uid" } })
           parts = @($keys | ForEach-Object { @{ loader = [int]$car.loader; key = $_; consumes = $true } }); uids = @($uid); data = @{ keys = $keys } }
    }
    "examine-vs-unmount" = {
        param($Members)
        $car = Get-ContentionCar $Members[0]
        if (-not $car) { return $null }
        $tool = Pick @("OBD", "Compression", "Multimeter")
        $examined = Invoke-Step $Members[0] diag-examine "$($car.loader) $tool keys" -Action "contention"
        $free = Get-FreeKeys $Members[0] $car.loader
        $key = Pick @(@($examined.keys) | Where-Object { $free -contains $_ })
        if (-not $key) { return $null }
        @{ loader = [int]$car.loader; car = $car.carToLoad; sections = @("cars", "inventory")
           members = @([ordered]@{ actor = $Members[0]; verb = "diag-examine"; args = "$($car.loader) $tool"; template = "" }, [ordered]@{ actor = $Members[1]; verb = "part-fast-unmount"; args = "$($car.loader) $key"; template = "" })
           parts = @(@{ loader = [int]$car.loader; key = $key; consumes = $false }); uids = @(); data = @{ key = $key } }
    }
    "gone-inflight" = {
        param($Members)
        $car = Get-ContentionCar $Members[0] -Removable
        if (-not $car) { return $null }
        $key = Pick (Get-FreeKeys $Members[1] $car.loader)
        if (-not $key) { return $null }
        @{ loader = [int]$car.loader; car = $car.carToLoad; sections = @("cars", "inventory", "placement")
           members = @([ordered]@{ actor = $Members[1]; verb = "part-fast-unmount"; args = "$($car.loader) $key"; template = "" }, [ordered]@{ actor = $Members[0]; verb = "car-delete"; args = "$($car.loader)"; template = "" })
           parts = @(); uids = @(); data = @{ key = $key } }
    }
    "details-pair" = {
        param($Members)
        $car = Get-ContentionCar $Members[0]
        if (-not $car) { return $null }
        $fluids = @("Brake", "EngineCoolant")
        $values = @($fluids | ForEach-Object { [math]::Round(0.1 + 0.8 * $rng.NextDouble(), 2) })
        @{ loader = [int]$car.loader; car = $car.carToLoad; sections = @("carDetails")
           members = @(for ($i = 0; $i -lt 2; $i++) { [ordered]@{ actor = $Members[$i]; verb = "cardetails-fluid"; args = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0} {1} 0 {2:0.00}", $car.loader, $fluids[$i], $values[$i]); template = "" } })
           parts = @(); uids = @(); data = @{ fluids = $fluids; values = $values } }
    }
    "details-same" = {
        param($Members)
        $car = Get-ContentionCar $Members[0]
        if (-not $car) { return $null }
        $values = @($Members | ForEach-Object { [math]::Round(0.1 + 0.8 * $rng.NextDouble(), 2) })
        @{ loader = [int]$car.loader; car = $car.carToLoad; sections = @("carDetails")
           members = @(for ($i = 0; $i -lt $Members.Count; $i++) { [ordered]@{ actor = $Members[$i]; verb = "cardetails-fluid"; args = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0} Brake 0 {1:0.00}", $car.loader, $values[$i]); template = "" } })
           parts = @(); uids = @(); data = @{ values = $values } }
    }
    "machine-same-item" = {
        param($Members)
        $car = Get-ContentionCar $Members[1]
        if (-not $car) { return $null }
        $rim = Pick @(Send-HarnessCommand -Instance $Members[1] -Verb wheel-parts -Arguments "$($car.loader)" | Where-Object { $_.id -like "rim*" -and -not $_.unmounted } | ForEach-Object { $_.key })
        if (-not $rim) { return $null }
        if ((Get-SlotUid (Send-HarnessCommand -Instance $Members[0] -Verb dump) "TireChanger") -ne 0) { Invoke-Step $Members[0] tool-take "TireChanger" -Action "contention" | Out-Null; $script:tireUid = 0 }
        Invoke-Step $Members[1] part-fast-unmount "$($car.loader) $rim" -Action "contention" | Out-Null
        $open.Add([pscustomobject]@{ Loader = [int]$car.loader; Car = $car.carToLoad; Key = $rim; Step = $script:stepNo; By = $Members[1]; Due = (Get-Date).AddSeconds(30) })
        $group = Invoke-Step $Members[0] give-group "wheel" -Action "contention"
        if (-not $group) { return $null }
        $uid = "{step:$($script:stepNo):UID}"
        Start-Sleep -Seconds 2
        @{ loader = [int]$car.loader; car = $car.carToLoad; sections = @("cars", "inventory", "tools")
           members = @([ordered]@{ actor = $Members[0]; verb = "tool-put"; args = ""; template = "TireChanger $uid" }, [ordered]@{ actor = $Members[1]; verb = "wheel-mount"; args = ""; template = "$($car.loader) $rim $uid" })
           parts = @(); uids = @($uid); data = @{ key = $rim } }
    }
    "machine-vs-sale" = {
        param($Members)
        if ((Get-SlotUid (Send-HarnessCommand -Instance $Members[0] -Verb dump) "BrakeLathe") -ne 0) { Invoke-Step $Members[0] tool-take "BrakeLathe" -Action "contention" | Out-Null }
        $given = Invoke-Step $Members[0] give-item "tarczaHamulcowa_1 0.40" -Action "contention"
        if (-not $given) { return $null }
        $uid = "{step:$($script:stepNo):UID}"
        Start-Sleep -Seconds 2
        @{ loader = -1; car = ""; sections = @("inventory", "tools", "stats")
           members = @([ordered]@{ actor = $Members[0]; verb = "tool-put"; args = ""; template = "BrakeLathe $uid" }, [ordered]@{ actor = $Members[1]; verb = "sell-item"; args = ""; template = $uid })
           parts = @(); uids = @($uid); data = @{} }
    }
    "mount-vs-sale" = {
        param($Members)
        $car = Get-ContentionCar $Members[0]
        if (-not $car) { return $null }
        $key = Pick (Get-FreeKeys $Members[0] $car.loader)
        if (-not $key) { return $null }
        $off = Invoke-Step $Members[0] part-fast-unmount "$($car.loader) $key" -Action "contention"
        if (-not $off) { return $null }
        $open.Add([pscustomobject]@{ Loader = [int]$car.loader; Car = $car.carToLoad; Key = $key; Step = $script:stepNo; By = $Members[0]; Due = (Get-Date).AddSeconds(30) })
        $given = Invoke-Step $Members[0] give-item "$($off.id) 0.90" -Action "contention"
        if (-not $given) { return $null }
        $uid = "{step:$($script:stepNo):UID}"
        Start-Sleep -Seconds 2
        @{ loader = [int]$car.loader; car = $car.carToLoad; sections = @("cars", "inventory", "stats")
           members = @([ordered]@{ actor = $Members[0]; verb = "part-fast-mount"; args = ""; template = "$($car.loader) $key $uid" }, [ordered]@{ actor = $Members[1]; verb = "sell-item"; args = ""; template = $uid })
           parts = @(); uids = @($uid); data = @{ key = $key } }
    }
    "machine-same-slot" = {
        param($Members)
        $dump = Send-HarnessCommand -Instance $Members[0] -Verb dump
        $uids = @()
        if ($rng.NextDouble() -lt 0.5) {
            if ((Get-SlotUid $dump "TireChanger") -ne 0) { Invoke-Step $Members[0] tool-take "TireChanger" -Action "contention" | Out-Null; $script:tireUid = 0 }
            $memberSpecs = @()
            foreach ($member in $Members) {
                if (-not (Invoke-Step $member give-group "wheel" -Action "contention")) { return $null }
                $uid = "{step:$($script:stepNo):UID}"
                $uids += $uid
                $memberSpecs += [ordered]@{ actor = $member; verb = "tool-put"; args = ""; template = "TireChanger $uid" }
            }
            $variant = "put"
        } else {
            if ((Get-SlotUid $dump "TireChanger") -eq 0) {
                if (-not (Invoke-Step $Members[0] give-group "wheel" -Action "contention")) { return $null }
                $uid = "{step:$($script:stepNo):UID}"
                Start-Sleep -Seconds 1
                if (-not (Invoke-Step $Members[0] tool-put "" -Template "TireChanger $uid" -Action "contention")) { return $null }
                Start-Sleep -Seconds 2
                $dump = Send-HarnessCommand -Instance $Members[0] -Verb dump
            }
            $uids = @("$(Get-SlotUid $dump 'TireChanger')") + @($dump.tools.TireChanger.items | ForEach-Object { "$_" })
            $memberSpecs = @($Members | ForEach-Object { [ordered]@{ actor = $_; verb = "tool-take"; args = "TireChanger"; template = "" } })
            $variant = "take"
        }
        Start-Sleep -Seconds 2
        @{ loader = -1; car = ""; sections = @("inventory", "tools"); members = $memberSpecs; parts = @(); uids = $uids; data = @{ variant = $variant } }
    }
    "item-trades" = {
        param($Members)
        $given = Invoke-Step $Members[0] give-item "tarczaHamulcowa_1 0.70" -Action "contention"
        if (-not $given) { return $null }
        $uid = "{step:$($script:stepNo):UID}"
        Start-Sleep -Seconds 2
        $variant = Pick @("sale", "scrap", "warehouse")
        $verb, $tail = switch ($variant) { "sale" { "sell-item", "" } "scrap" { "econ-scrap", " 0" } default { "warehouse-move", " to" } }
        @{ loader = -1; car = ""; sections = @("inventory", "stats")
           members = @($Members | ForEach-Object { [ordered]@{ actor = $_; verb = $verb; args = ""; template = "$uid$tail" } })
           parts = @(); uids = @($uid); data = @{ variant = $variant } }
    }
    "lift-same" = {
        param($Members)
        $lifter = Pick @(Send-HarnessCommand -Instance $Members[0] -Verb lifters | Where-Object { [int]$_.connectedLoader -ge 0 })
        if (-not $lifter) { return $null }
        $index = [int]$lifter.index
        $direction = if ("$($lifter.state)" -eq "Up") { "down" } else { "up" }
        @{ loader = -1; car = ""; sections = @("placement")
           members = @($Members | ForEach-Object { [ordered]@{ actor = $_; verb = "lift"; args = "$index $direction"; template = "" } })
           parts = @(); uids = @(); data = @{ lifter = $index; direction = $direction } }
    }
    "place-same" = {
        param($Members)
        $placement = @(Get-Placement $Members[0])
        $taken = @($placement | ForEach-Object { $_.inPlace } | Where-Object { $_ })
        $place = Pick @($places | Where-Object { $taken -notcontains $_ })
        $cars = @(Get-ReadyCars $Members[0] | Sort-Object { $rng.Next() } | Select-Object -First 2)
        if (-not $place -or $cars.Count -lt 2) { return $null }
        @{ loader = -1; car = ""; sections = @("placement")
           members = @(for ($i = 0; $i -lt 2; $i++) { [ordered]@{ actor = $Members[$i]; verb = "car-move"; args = "$($cars[$i].loader) $place"; template = "" } })
           parts = @(); uids = @(); data = @{ place = $place; loaders = @($cars | ForEach-Object { [int]$_.loader }) } }
    }
    "park-stale" = {
        param($Members)
        $parking = Send-HarnessCommand -Instance $Members[0] -Verb parking
        if (@($parking.slots).Count -ge [int]$parking.max) { return $null }
        $car = Get-ContentionCar $Members[0] -Removable
        if (-not $car) { return $null }
        $key = Pick (Get-FreeKeys $Members[1] $car.loader)
        if (-not $key) { return $null }
        @{ loader = [int]$car.loader; car = $car.carToLoad; sections = @("cars", "inventory", "placement")
           members = @([ordered]@{ actor = $Members[1]; verb = "part-fast-unmount"; args = "$($car.loader) $key"; template = "" }, [ordered]@{ actor = $Members[0]; verb = "park"; args = "$($car.loader)"; template = "" })
           parts = @(); uids = @(); data = @{ key = $key; slots = @($parking.slots | ForEach-Object { [int]$_.index }) } }
    }
}

# --- outcome checks (rule 9); each returns problems and which contended UIDs were consumed (mounted or sold) -------------
function Get-PartState($Dump, [int]$Loader, [string]$Key) {
    $car = @($Dump.cars | Where-Object { $_.index -eq $Loader })[0]
    if (-not $car) { return $null }
    @($car.subParts | Where-Object { $_.key -eq $Key })[0]
}

function Get-FluidLevel($Dump, [int]$Loader, [string]$Fluid) {
    $entry = @($Dump.carDetails | Where-Object { $_.loader -eq $Loader })[0]
    if (-not $entry) { return $null }
    $value = $entry.entries."f:$($Fluid).0"
    if ($null -eq $value) { $null } else { [math]::Round([double]$value.Level, 2) }
}

$ContentionCheckers = @{
    "part-same" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $problems = @()
        $accepted = Count-Lines $Lines "\[Cars\] Change \S+ from client $(Get-IdAlternation $Ids) on loader $($Spec.loader): revision"
        if ($accepted -ne 1) { $problems += "$accepted accepted changes for $($Spec.data.key), expected 1" }
        $part = Get-PartState $After.dump $Spec.loader $Spec.data.key
        if (-not $part -or -not $part.unmounted) { $problems += "$($Spec.data.key) is not unmounted" }
        else {
            $winner = if ($Observed.Count -gt 0) { $Observed[0] } else { 0 }
            $open.Add([pscustomobject]@{ Loader = $Spec.loader; Car = $Spec.car; Key = $Spec.data.key; Step = $MemberSteps[$winner]; By = $Spec.members[$winner].actor; Due = (Get-Date).AddSeconds($rng.Next(2, 21)) })
        }
        @{ problems = $problems; consumed = @{} }
    }
    "mount-same-item" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $mounts = @($Lines | Where-Object { $_ -match "\[Cars\] Change \S+ from client $(Get-IdAlternation $Ids) on loader $($Spec.loader): revision .*inventory \+\d+ -1\)" })
        $problems = @()
        if ($mounts.Count -ne 1) { $problems += "$($mounts.Count) mounts with the one item accepted, expected 1" }
        $winners = @($mounts | ForEach-Object { if ($_ -match "from client (\d+)") { [int]$Matches[1] } })
        for ($i = 0; $i -lt $Spec.members.Count; $i++) {
            if ($winners -notcontains $Ids[$Spec.members[$i].actor]) { continue }
            foreach ($x in @($open | Where-Object { $_.Loader -eq $Spec.loader -and $_.Key -eq $Spec.data.keys[$i] })) { $open.Remove($x) | Out-Null }
        }
        $uid = Resolve-Template $Spec.uids[0]
        @{ problems = $problems; consumed = @{ $uid = ($mounts.Count -gt 0) } }
    }
    "examine-vs-unmount" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $problems = @()
        $part = Get-PartState $After.dump $Spec.loader $Spec.data.key
        if (-not $part -or -not $part.unmounted) { $problems += "$($Spec.data.key) is not unmounted after the examine" }
        else { $open.Add([pscustomobject]@{ Loader = $Spec.loader; Car = $Spec.car; Key = $Spec.data.key; Step = $MemberSteps[1]; By = $Spec.members[1].actor; Due = (Get-Date).AddSeconds($rng.Next(2, 21)) }) }
        @{ problems = $problems; consumed = @{} }
    }
    "gone-inflight" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $problems = @()
        $unmounter = $Spec.members[0].actor
        $transactions = @((Send-HarnessCommand -Instance $unmounter -Verb dump).parts.transactions | Where-Object { $_.loader -eq $Spec.loader -and ($_.open -gt 0 -or $_.committed -gt 0) })
        if ($transactions.Count -gt 0) { $problems += "$unmounter still holds transactions for loader $($Spec.loader): $($transactions | ConvertTo-Json -Compress)" }
        $still = @(Get-Placement $unmounter | Where-Object { $_.loader -eq $Spec.loader -and $_.carToLoad -eq $Spec.car }).Count
        if ($still -gt 0) { $problems += "the car on loader $($Spec.loader) was not deleted" }
        $partCache.Remove($Spec.loader)
        @{ problems = $problems; consumed = @{} }
    }
    "details-pair" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $problems = @()
        foreach ($member in @($Spec.members | ForEach-Object { $_.actor })) {
            $dump = Send-HarnessCommand -Instance $member -Verb dump
            for ($i = 0; $i -lt 2; $i++) {
                $level = Get-FluidLevel $dump $Spec.loader $Spec.data.fluids[$i]
                if ($level -ne [math]::Round([double]$Spec.data.values[$i], 2)) { $problems += "$member has $($Spec.data.fluids[$i]) $level, expected $($Spec.data.values[$i])" }
            }
        }
        @{ problems = $problems; consumed = @{} }
    }
    "details-same" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $problems = @()
        if ($Observed.Count -ne $Spec.members.Count) { $problems += "the server logged $($Observed.Count) of $($Spec.members.Count) updates" }
        else {
            $expected = [math]::Round([double]$Spec.data.values[$Observed[-1]], 2)
            foreach ($member in @($Spec.members | ForEach-Object { $_.actor })) {
                $level = Get-FluidLevel (Send-HarnessCommand -Instance $member -Verb dump) $Spec.loader "Brake"
                if ($level -ne $expected) { $problems += "$member has Brake $level, expected $expected (the member the server took last)" }
            }
        }
        @{ problems = $problems; consumed = @{} }
    }
    "machine-same-item" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $uid = Resolve-Template $Spec.uids[0]
        $wheel = @(Send-HarnessCommand -Instance $Spec.members[1].actor -Verb wheel-parts -Arguments "$($Spec.loader)" | Where-Object { $_.key -eq $Spec.data.key })[0]
        $mounted = $wheel -and -not $wheel.unmounted
        $onChanger = (Get-SlotUid $After.dump "TireChanger") -eq [long]$uid
        $problems = @()
        if (-not ($mounted -xor $onChanger)) { $problems += "the wheel is $(if ($mounted) { 'mounted' } else { 'not mounted' }) and $(if ($onChanger) { 'on' } else { 'not on' }) the tire changer; expected exactly one" }
        foreach ($x in @($open | Where-Object { $_.Loader -eq $Spec.loader -and $_.Key -eq $Spec.data.key })) { $open.Remove($x) | Out-Null }
        if (-not $mounted) {
            $mounter = $Spec.members[1].actor
            if (Invoke-Step $mounter give-group "wheel" -Action "contention" -Group $Spec.index -Phase "check" -Member 1) {
                Start-Sleep -Seconds 1
                Invoke-Step $mounter wheel-mount "" -Template "$($Spec.loader) $($Spec.data.key) {step:$($script:stepNo):UID}" -Action "contention" -Group $Spec.index -Phase "check" -Member 1 | Out-Null
            }
        }
        $script:tireUid = Get-SlotUid $After.dump "TireChanger"
        @{ problems = $problems; consumed = @{ $uid = $mounted } }
    }
    "machine-vs-sale" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $uid = Resolve-Template $Spec.uids[0]
        $onLathe = (Get-SlotUid $After.dump "BrakeLathe") -eq [long]$uid
        $sold = (Count-Lines $Lines "\[Shop\] Sale of item $uid from client \d+: sold") -gt 0
        $problems = @()
        if (-not ($onLathe -xor $sold)) { $problems += "on the lathe $onLathe, sold $sold; expected exactly one" }
        if (($After.money -ne $Before.money) -ne $sold) { $problems += "money $($Before.money) -> $($After.money) but sold $sold" }
        @{ problems = $problems; consumed = @{ $uid = $sold } }
    }
    "mount-vs-sale" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $uid = Resolve-Template $Spec.uids[0]
        $part = Get-PartState $After.dump $Spec.loader $Spec.data.key
        $mounted = $part -and -not $part.unmounted
        $sold = (Count-Lines $Lines "\[Shop\] Sale of item $uid from client \d+: sold") -gt 0
        $problems = @()
        if (-not ($mounted -xor $sold)) { $problems += "mounted $mounted, sold $sold; expected exactly one" }
        if ($mounted) { foreach ($x in @($open | Where-Object { $_.Loader -eq $Spec.loader -and $_.Key -eq $Spec.data.key })) { $open.Remove($x) | Out-Null } }
        @{ problems = $problems; consumed = @{ $uid = ($mounted -or $sold) } }
    }
    "machine-same-slot" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $problems = @()
        $slot = Get-SlotUid $After.dump "TireChanger"
        $uids = @($Spec.uids | ForEach-Object { [long](Resolve-Template $_) })
        if ($Spec.data.variant -eq "put") {
            if ($uids -notcontains $slot) { $problems += "the tire changer holds $slot, none of the contended wheels" }
            foreach ($uid in $uids) {
                $where = @($After.where["$uid"])[0]
                $expected = if ($uid -eq $slot) { "machine:TireChanger" } else { "inventory" }
                if ($where -ne $expected) { $problems += "$uid is in $where, expected $expected" }
            }
        } else {
            if ($slot -ne 0) { $problems += "the tire changer still holds $slot" }
            $wheelPlace = @($After.where["$($uids[0])"])[0]
            $partPlaces = @($uids | Select-Object -Skip 1 | ForEach-Object { @($After.where["$_"])[0] })
            if ($wheelPlace -ne "inventory" -and @($partPlaces | Where-Object { $_ -ne "inventory" }).Count -gt 0) {
                $problems += "the taken wheel is not in the inventory (group $wheelPlace, its parts $($partPlaces -join ', '))"
            }
        }
        $script:tireUid = $slot
        @{ problems = $problems; consumed = @{} }
    }
    "item-trades" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $uid = Resolve-Template $Spec.uids[0]
        $problems = @()
        $where = @($After.where[$uid])[0]
        switch ($Spec.data.variant) {
            "sale" {
                $n = Count-Lines $Lines "\[Shop\] Sale of item $uid from client \d+: sold"
                if ($n -ne 1) { $problems += "sold $n times" }
                if ($where -ne "none") { $problems += "the sold item is in $where" }
            }
            "scrap" {
                $n = Count-Lines $Lines "\[Economy\] client $(Get-IdAlternation $Ids) ScrapItem\(.* -> "
                if ($n -ne 1) { $problems += "scrapped $n times" }
                if ($where -ne "none") { $problems += "the scrapped item is in $where" }
            }
            default {
                $n = Count-Lines $Lines "\[Inventory\] Warehouse move of $uid to the warehouse from client \d+: moved"
                if ($n -ne 1) { $problems += "moved $n times" }
                if ($where -ne "warehouse") { $problems += "the item is in $where, expected the warehouse" }
            }
        }
        @{ problems = $problems; consumed = @{ $uid = ($Spec.data.variant -ne "warehouse") } }
    }
    "lift-same" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $accepted = Count-Lines $Lines "\[Placement\] Lift $($Spec.data.lifter) \S+ by client"
        @{ problems = @(if ($accepted -ne 1) { "$accepted lift steps accepted, expected 1" }); consumed = @{} }
    }
    "place-same" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $inPlace = @($After.dump.placement.cars | Where-Object { $_.inPlace -eq $Spec.data.place }).Count
        @{ problems = @(if ($inPlace -gt 1) { "$inPlace cars stand on $($Spec.data.place)" }); consumed = @{} }
    }
    "park-stale" = {
        param($Spec, $Before, $After, $Lines, $Ids, $Observed, $MemberSteps)
        $problems = @()
        $parker = $Spec.members[1].actor
        $parking = Send-HarnessCommand -Instance $parker -Verb parking
        $slot = @($parking.slots | Where-Object { $Spec.data.slots -notcontains [int]$_.index -and $_.carToLoad -eq $Spec.car })[0]
        if (-not $slot) {
            $part = Get-PartState $After.dump $Spec.loader $Spec.data.key
            if ($part -and $part.unmounted) { $open.Add([pscustomobject]@{ Loader = $Spec.loader; Car = $Spec.car; Key = $Spec.data.key; Step = $MemberSteps[0]; By = $Spec.members[0].actor; Due = (Get-Date).AddSeconds(10) }) }
            return @{ problems = $problems; consumed = @{}; note = "the park was refused" }
        }
        $itemId = $null
        $unmountResult = $results[$MemberSteps[0]]
        if ($unmountResult) { $itemId = $unmountResult.id }
        $itemsBefore = if ($itemId) { [int]$Before.items[$itemId] } else { 0 }
        $itemsParked = if ($itemId) { @($After.dump.inventory.items | Where-Object { $_.ID -eq $itemId }).Count } else { 0 }
        Invoke-Step $parker unpark "$($slot.index) $($Spec.loader)" -Group $Spec.index -Phase "check" -Member 1 | Out-Null
        $deadline = (Get-Date).AddSeconds(90)
        while ((Get-Date) -lt $deadline -and -not (Test-Ready $parker $Spec.loader)) { Start-Sleep -Seconds 1 }
        Start-Sleep -Seconds 3
        $dump = Send-HarnessCommand -Instance $parker -Verb dump
        $part = Get-PartState $dump $Spec.loader $Spec.data.key
        if (-not $part) { $problems += "no $($Spec.data.key) after the unpark" }
        elseif ($itemId) {
            $gained = $itemsParked - $itemsBefore
            if ($part.unmounted -and $gained -ne 1) { $problems += "$($Spec.data.key) is unmounted after the unpark but the inventory gained $gained items of $itemId" }
            if (-not $part.unmounted -and $gained -ne 0) { $problems += "$($Spec.data.key) is mounted after the unpark but the inventory gained $gained items of $itemId" }
            if ($part.unmounted) { $open.Add([pscustomobject]@{ Loader = $Spec.loader; Car = $Spec.car; Key = $Spec.data.key; Step = $MemberSteps[0]; By = $Spec.members[0].actor; Due = (Get-Date).AddSeconds(10) }) }
        }
        $partCache.Remove($Spec.loader)
        @{ problems = $problems; consumed = @{} }
    }
}

# Rule 8: the items of each contended part's id follow its mount flips, and each contended UID is in at most one place
# (the same on every member), in none when it was mounted or sold.
function Test-Conservation($Spec, $Before, $After, $Consumed) {
    $problems = @()
    foreach ($p in @($Spec.parts)) {
        $id = "$($p.loader)|$($p.key)"
        $b = $Before.parts[$id]; $a = $After.parts[$id]
        if (-not $b -or -not $a) { continue }
        $itemId = if ($a.unmounted) { $b.id } else { $a.id }
        if (-not $itemId) { $itemId = $b.id }
        $flip = 0
        if ($a.unmounted -and -not $b.unmounted) { $flip = 1 }
        if ($b.unmounted -and -not $a.unmounted -and $p.consumes) { $flip = -1 }
        $script:conservationIds[$itemId] = [int]$script:conservationIds[$itemId] + $flip
    }
    foreach ($itemId in @($script:conservationIds.Keys)) {
        $expected = [int]$Before.items[$itemId] + $script:conservationIds[$itemId]
        $actual = [int]$After.items[$itemId]
        if ($actual -ne $expected) { $problems += "$actual items of $itemId in the inventory, expected $expected" }
    }
    foreach ($uid in @($After.where.Keys)) {
        $places = @($After.where[$uid])
        if (@($places | Select-Object -Unique).Count -gt 1) { $problems += "the members disagree where $uid is ($($places -join ' / '))" }
        $place = $places[0]
        if ($place -match ",") { $problems += "$uid is in more than one place ($place)" }
        if ($Consumed.ContainsKey($uid) -and $Consumed[$uid] -and $place -ne "none") { $problems += "$uid was used up but is in $place" }
    }
    return $problems
}

function Invoke-ContentionAct($Spec, [int[]]$Order, $Recorded) {
    $index = [int]$Spec.index
    $members = @($Spec.members)
    $ids = @{}
    foreach ($m in $members) { $ids[$m.actor] = [int](Get-HarnessStatus $m.actor).playerId }
    function StepOf([string]$Phase, [int]$Member) {
        if (-not $Recorded) { return 0 }
        $entry = @($Recorded | Where-Object { $_.phase -eq $Phase -and [int]$_.member -eq $Member })[0]
        if ($entry) { [int]$entry.step } else { 0 }
    }
    $before = Get-ContentionState $Spec
    $memberSteps = @{}
    $failedVerbs = @()
    $holdStart = Get-Date
    for ($i = 0; $i -lt $members.Count; $i++) { Invoke-Step $members[$i].actor net-hold "out" -Action "contention" -Group $index -Phase "hold" -Member $i -Step (StepOf "hold" $i) | Out-Null }
    for ($i = 0; $i -lt $members.Count; $i++) {
        $r = Invoke-Step $members[$i].actor $members[$i].verb $members[$i].args -Template $members[$i].template -Action "contention" -Group $index -Phase "act" -Member $i -Step (StepOf "act" $i)
        $memberSteps[$i] = $script:stepNo
        if ($null -eq $r) { $failedVerbs += "$($members[$i].actor) $($members[$i].verb)" }
    }
    Start-Sleep -Milliseconds 1200
    $held = @($members | ForEach-Object { try { [int](Send-HarnessCommand -Instance $_.actor -Verb net-hold -Arguments "status").heldOut } catch { -1 } })
    $mark = Get-ServerLogMark
    foreach ($i in $Order) {
        Invoke-Step $members[$i].actor net-hold "off" -Action "contention" -Group $index -Phase "release" -Member $i -Step (StepOf "release" $i) | Out-Null
        $budget = $MaxHoldSeconds - ((Get-Date) - $holdStart).TotalSeconds
        if ($failedVerbs.Count -gt 0 -or $budget -lt 1) { continue }
        try { Wait-ServerLog -Pattern (Get-MemberPattern $members[$i].verb $ids[$members[$i].actor]) -After $mark -TimeoutSec ([int][math]::Floor($budget)) | Out-Null } catch { }
    }
    Start-Sleep -Seconds 1
    $lines = @(Get-ServerLogLines | Select-Object -Skip $mark)
    $positions = @{}
    for ($i = 0; $i -lt $members.Count; $i++) {
        $pattern = Get-MemberPattern $members[$i].verb $ids[$members[$i].actor]
        for ($n = 0; $n -lt $lines.Count; $n++) { if ($lines[$n] -match $pattern) { $positions[$i] = $n; break } }
    }
    $observed = @($positions.Keys | Sort-Object { $positions[$_] })
    $actors = @($members | ForEach-Object { $_.actor } | Select-Object -Unique)
    $settled = $true
    $settled = Wait-ContentionSettle $actors $Spec.sections
    $after = Get-ContentionState $Spec
    [pscustomobject]@{ Before = $before; After = $after; Lines = $lines; Ids = $ids; Observed = $observed; MemberSteps = $memberSteps; Held = $held; Settled = $settled; FailedVerbs = $failedVerbs }
}

function Complete-ContentionGroup($Spec, [int[]]$Order, $Act, $RecordedObserved) {
    $kind = $Spec.kind
    $script:contentionStats.groups++
    $script:conservationIds = @{}
    $outcome = $null
    $rule8 = @(); $rule9 = @()
    if ($Act.FailedVerbs.Count -gt 0) {
        $verdict = "invalid"
        $rule9 = @("member verbs failed: $($Act.FailedVerbs -join ', ')")
        $script:contentionStats.invalid++
    } else {
        try { $outcome = & $ContentionCheckers[$kind] $Spec $Act.Before $Act.After $Act.Lines $Act.Ids $Act.Observed $Act.MemberSteps }
        catch { $outcome = @{ problems = @("check failed: $($_.Exception.Message)"); consumed = @{} } }
        $rule9 = @($outcome.problems)
        if (-not $Act.Settled) { $rule9 += "the members' $($Spec.sections -join ', ') did not agree within 10 s" }
        $rule8 = @(Test-Conservation $Spec $Act.Before $Act.After $outcome.consumed)
        $known = $contentionKnown[$kind]
        if ($rule8.Count + $rule9.Count -eq 0) { $verdict = "passed"; $script:contentionStats.passed++ }
        elseif ($known) { $verdict = "known gap"; $script:contentionStats.known++ }
        else {
            $verdict = "failed"
            $script:contentionStats.failed++
            if ($rule8.Count) { Add-Failure 8 "contention $($Spec.index) $kind`: $($rule8 -join '; ')" }
            if ($rule9.Count) { Add-Failure 9 "contention $($Spec.index) $kind`: $($rule9 -join '; ')" }
        }
    }
    $sameOrder = $null
    if ($null -ne $RecordedObserved) {
        $script:contentionStats.replayOrder++
        $sameOrder = (@($RecordedObserved) -join ",") -eq (@($Act.Observed) -join ",")
        if ($sameOrder) { $script:contentionStats.replayOrderSame++ }
    }
    $record = [ordered]@{
        index = $Spec.index; t = (Elapsed); kind = $kind; members = @($Spec.members | ForEach-Object { $_.actor }); order = $Order; observed = $Act.Observed
        heldOut = $Act.Held; verdict = $verdict; known = $contentionKnown[$kind]; rule8 = $rule8; rule9 = $rule9; note = $outcome.note
        replayOf = if ($null -ne $RecordedObserved) { [ordered]@{ observed = @($RecordedObserved); sameOrder = $sameOrder } } else { $null }
    }
    Add-JsonLine $contentionFile $record
    Add-Marker "contend" ([ordered]@{ index = $Spec.index; kind = $kind; order = $Order; observed = $Act.Observed; verdict = $verdict })
    Write-Host ("CONTEND {0} {1} ({2}): order {3}, server {4} -> {5}{6}" -f $Spec.index, $kind, (($Spec.members | ForEach-Object { $_.actor }) -join ","), ($Order -join ","), ($Act.Observed -join ","), $verdict,
        $(if ($rule8.Count + $rule9.Count) { " - " + (@($rule8) + @($rule9) -join "; ") } else { "" }))
}

function Invoke-Contention([string]$Actor) {
    if ($script:netBusy -or $script:away) { return $false }
    $actors = @(Get-EligibleActors | Where-Object { -not $seated.ContainsKey($_) })
    if ($actors.Count -lt 2) { return $false }
    $kinds = if ($ContentionKinds.Count -gt 0) { @($ContentionKinds) } elseif ($ContentionIncludeKnown) { $ContentionKindsAll } else { @($ContentionKindsAll | Where-Object { -not $contentionKnown.ContainsKey($_) }) }
    $kind = Pick $kinds
    $count = if ($ContentionManyMembers -contains $kind -and $actors.Count -ge 3) { $rng.Next(2, [math]::Min(4, $actors.Count) + 1) } else { 2 }
    $members = @(@($Actor) + @($actors | Where-Object { $_ -ne $Actor } | Sort-Object { $rng.Next() }) | Select-Object -First $count)
    if ($members -notcontains $Actor) { $members = @($actors | Sort-Object { $rng.Next() } | Select-Object -First $count) }
    $spec = $null
    try { $spec = & $ContentionBuilders[$kind] $members } catch { Write-Host "contention $kind setup: $($_.Exception.Message)"; return $true }
    if (-not $spec) { return $false }
    $script:contentionIndex++
    $spec.index = $script:contentionIndex
    $spec.kind = $kind
    $order = @(0..($spec.members.Count - 1) | Sort-Object { $rng.Next() })
    Add-Marker "contend-start" ([ordered]@{ index = $spec.index; kind = $kind; order = $order; spec = $spec })
    $act = Invoke-ContentionAct $spec $order $null
    Complete-ContentionGroup $spec $order $act $null
    return $true
}

# Replay of one group: the "contend-start" marker holds the spec and the release order, the group's logged steps their
# step numbers, and the "contend" marker the server order seen in the replayed run.
function Invoke-ContentionReplay($StartEntry, $AllEntries) {
    $data = $StartEntry.args | ConvertFrom-Json
    $spec = @{
        index = [int]$data.index; kind = $data.kind; loader = [int]$data.spec.loader; car = $data.spec.car; sections = @($data.spec.sections)
        members = @($data.spec.members | ForEach-Object { [ordered]@{ actor = $_.actor; verb = $_.verb; args = "$($_.args)"; template = "$($_.template)" } })
        parts = @($data.spec.parts | Where-Object { $_ } | ForEach-Object { @{ loader = [int]$_.loader; key = $_.key; consumes = [bool]$_.consumes } })
        uids = @($data.spec.uids | Where-Object { $_ }); data = @{}
    }
    foreach ($property in @($data.spec.data.PSObject.Properties)) { $spec.data[$property.Name] = $property.Value }
    if ($spec.data.ContainsKey("keys")) { $spec.data.keys = @($spec.data.keys) }
    $script:contentionIndex = [math]::Max($script:contentionIndex, $spec.index)
    $recorded = @($AllEntries | Where-Object { $_.PSObject.Properties["group"] -and [int]$_.group -eq $spec.index })
    $endMarker = @($AllEntries | Where-Object { $_.verb -eq "contend" -and ($_.args | ConvertFrom-Json).index -eq $spec.index })[0]
    $recordedObserved = if ($endMarker) { @(($endMarker.args | ConvertFrom-Json).observed | ForEach-Object { [int]$_ }) } else { @() }
    $order = @($data.order | ForEach-Object { [int]$_ })
    Add-Marker "contend-start" ([ordered]@{ index = $spec.index; kind = $spec.kind; order = $order; spec = $spec; replay = $true })
    $act = Invoke-ContentionAct $spec $order $recorded
    Complete-ContentionGroup $spec $order $act $recordedObserved
}

function Get-ContentionSummary {
    $s = $script:contentionStats
    $text = "contention: $($s.groups) groups, $($s.passed) passed, $($s.failed) failed, $($s.known) known gaps, $($s.invalid) invalid"
    if ($s.replayOrder -gt 0) { $text += "; replay reproduced the server order of $($s.replayOrderSame) of $($s.replayOrder) groups" }
    return $text
}

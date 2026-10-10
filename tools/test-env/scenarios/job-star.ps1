# areas: jobs, parts
# User report 2026-10-10: a part starred in the order tab stays highlighted after it was replaced. A takes a job and
# stars its parts (the tab's own MarkAction). A replaces one starred part itself, B replaces another, B puts a third
# back below the job's condition: on A, the two that now count as repaired lose star and highlight, the third keeps
# them. B starred nothing and has no marked part.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name, [int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = try { Cmd $Name car-ready "$Loader" } catch { $null }
        if ($r.state -eq "Ready" -and $r.loaded) { return $true }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Sub([string]$Name, [int]$Loader, [string]$Key) {
    $dump = Cmd $Name dump
    @(@($dump.cars | Where-Object { $_.index -eq $Loader })[0].subParts | Where-Object { $_.key -eq $Key })[0]
}

function Wait-Part([string]$Name, [int]$Loader, [string]$Key, [scriptblock]$Condition, [string]$What) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $part = Sub $Name $Loader $Key
        if ($part -and (& $Condition $part)) { return $part }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    Check $false "$Name`: $What ($($part | ConvertTo-Json -Compress))"
    $part
}

function Star([string]$Name, [int]$Job, [string]$Key) { @((Cmd $Name job-star-state "$Job").starred | Where-Object { $_.key -eq $Key })[0] }

function Wait-Star([string]$Name, [int]$Job, [string]$Key, [bool]$Marked, [int]$Seconds = 6) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        Start-Sleep -Milliseconds 500
        $star = Star $Name $Job $Key
    } while ([bool]$star -ne $Marked -and (Get-Date) -lt $deadline)
    $star
}

function Replace([string]$Name, [int]$Loader, [string]$Key, [string]$Id, [double]$Target) {
    Cmd $Name part-fast-unmount "$Loader $Key" | Out-Null
    foreach ($other in $Ctx.Instances) { Wait-Part $other $Loader $Key { param($p) $p.unmounted } "$Key comes off" | Out-Null }
    $item = Cmd $Name give-item "$Id $([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, '{0}', $Target))"
    Start-Sleep -Seconds 1
    Cmd $Name part-domount "$Loader $Key $($item.UID)" | Out-Null
    foreach ($other in $Ctx.Instances) {
        Wait-Part $other $Loader $Key { param($p) -not $p.unmounted -and [math]::Abs($p.condition - $Target) -lt 0.01 } "$Key is mounted again at $Target" | Out-Null
    }
    Start-Sleep -Seconds 1
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Cmd $name guard-set "Off" | Out-Null
    Cmd $name orders-autogen "off" | Out-Null
}
Start-Sleep -Seconds 2

$candidates = @()
for ($attempt = 0; $attempt -lt 6 -and $candidates.Count -lt 3; $attempt++) {
    $known = @((Cmd $a dump).jobs.orders | ForEach-Object { $_.id })
    $gen = if ((Get-HarnessStatus $b).isOrderGenerator) { $b } else { $a }
    Cmd $gen orders-generate | Out-Null
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 700
        $new = @((Cmd $a dump).jobs.orders | Where-Object { -not $_.IsMission -and $known -notcontains $_.id })
    } while ($new.Count -eq 0 -and (Get-Date) -lt $deadline)
    if ($new.Count -eq 0) { continue }
    $id = $new[0].id
    Cmd $a orders-accept "$id" | Out-Null
    $deadline = (Get-Date).AddSeconds(90)
    do {
        Start-Sleep -Milliseconds 700
        $active = @((Cmd $a dump).jobs.active | Where-Object { $_.id -eq $id })
    } while ($active.Count -eq 0 -and (Get-Date) -lt $deadline)
    if ($active.Count -eq 0) { Write-Host "order $id did not become active"; continue }
    $loader = $active[0].carLoaderID
    if (-not ((Wait-Ready $a $loader) -and (Wait-Ready $b $loader))) { Write-Host "job car $id not ready"; continue }
    Start-Sleep -Seconds 2
    $starred = Cmd $a job-star "$id"
    $threshold = [double](Cmd $a job-star-state "$id").globalCondition
    Write-Host "job $id on loader $loader (condition $threshold) starred: $(@($starred.starred | ForEach-Object { "$($_.key)=$($_.id) c$([math]::Round($_.condition, 2))$(if ($_.blocked) { ' blocked' })$(if ($_.members) { ' group' })" }) -join ', ')"
    $bolted = @(Cmd $a vfx-parts "$loader" | ForEach-Object { $_.key })
    foreach ($s in @($starred.starred | Where-Object { $bolted -contains $_.key })) {
        $candidates += [pscustomobject]@{ Job = $id; Loader = $loader; Key = $s.key; Id = $s.id; Threshold = $threshold }
    }
}
Check ($candidates.Count -ge 3) "three replaceable starred job parts ($($candidates.Count))"
if ($candidates.Count -ge 3) {
    $first = $candidates[0]
    $before = Star $a $first.Job $first.Key
    Write-Host "A before: $($before | ConvertTo-Json -Compress)"
    Check ([bool]$before.mark) "A has $($first.Key) starred"
    Check (@((Cmd $b job-star-state "$($first.Job)").starred).Count -eq 0) "B, who starred nothing, has no marked part"

    $own = $candidates[0]
    Replace $a $own.Loader $own.Key $own.Id 1.0
    $after = Wait-Star $a $own.Job $own.Key $false
    Check (-not $after) "A replaced $($own.Key) itself at 100 %: no longer starred ($($after | ConvertTo-Json -Compress))"

    $remote = $candidates[1]
    Replace $b $remote.Loader $remote.Key $remote.Id 1.0
    $after = Wait-Star $a $remote.Job $remote.Key $false
    Check (-not $after) "B replaced $($remote.Key) at 100 %: A no longer stars it ($($after | ConvertTo-Json -Compress))"
    Check (@((Cmd $b job-star-state "$($remote.Job)").starred).Count -eq 0) "B still has no marked part"

    $low = $candidates[2]
    $lowCondition = [math]::Max(0.05, [math]::Round($low.Threshold - 0.2, 2))
    Replace $b $low.Loader $low.Key $low.Id $lowCondition
    $after = Wait-Star $a $low.Job $low.Key $true 4
    Check ([bool]$after) "B replaced $($low.Key) at $lowCondition (job wants $($low.Threshold)): A still stars it ($($after | ConvertTo-Json -Compress))"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

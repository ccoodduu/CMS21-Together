# areas: locks, parts, guard
# part-locks 9.2: a part in use is visible before the click (D9). While A holds a part, B's hover draws no highlight
# and the label names A, and B's click is refused on B's own game without a request. Through the game's own raycast
# and hold (lock-click): the label shows while B hovers, the part stays the game's mouse-over part (M2), and the
# completed hold is refused. After A's release the highlight and the label are back, and B's hold unmounts the part
# with the lock prefetched at hold start (D10). The guard runs on Enforce.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\LockSession.psm1")

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
        $r = Cmd $Name car-ready "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Try-Wait([string]$Name, [string]$Arguments) {
    $t = Cmd $Name lock-try $Arguments
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $r = Cmd $Name lock-try "result $($t.tryId)"
        if ($r.result -ne "pending") { return $r }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $r
}

function Wait-Mirror([string]$Name, [int]$Count) {
    Wait-LockMirror $Name { param($l) @($l.mirror).Count -eq $Count } "$Count lock(s)" | Out-Null
}

function Click([string]$Name, [string]$Key, [int]$HoldMs) {
    Cmd $Name lock-click "$loader $Key hold $HoldMs" | Out-Null
    $deadline = (Get-Date).AddSeconds(($HoldMs / 1000) + 12)
    do {
        Start-Sleep -Milliseconds 300
        $status = Cmd $Name lock-click "status"
    } while (-not $status.done -and (Get-Date) -lt $deadline)
    $status
}

function Counter([string]$Name, [string]$Counter) { $c = (Get-LockMirror $Name).counters; if ($c.$Counter) { [int]$c.$Counter } else { 0 } }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Enforce" | Out-Null }
$idA = (Get-HarnessStatus $a).playerId

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$candidates = @(Cmd $b vfx-parts "$loader")
$part = @($candidates | Where-Object { $_.id -match "wentylator|pokrywa_glowicy|filtr" } | Select-Object -First 1)[0]
if (-not $part) { $part = $candidates[0] }
$key = $part.key
Write-Host "part: $key $($part.id)"

$hover = Cmd $b lock-hover "$loader $key"
Check ($hover.highlighted -and $hover.label -notmatch "is working") "a free part is highlighted with its own label ($($hover | ConvertTo-Json -Compress))"

$answer = Request-Lock $a "$loader unmount $key"
Check ($answer.result -eq "granted") "A holds $key"
Wait-Mirror $b 1

$hover = Cmd $b lock-hover "$loader $key"
Write-Host "  hover on B: $($hover | ConvertTo-Json -Compress)"
Check (-not $hover.highlighted) "B's hover draws no highlight on A's part"
Check ($hover.label -match "is working on this part" -and $hover.partMouseOver) "B's label names the work and the part stays the mouse-over part ($($hover.label))"

$mark = Get-ServerLogMark
$refused = Counter $b "refusedLocally"
$r = Try-Wait $b "$loader unmount $key"
Check ($r.result -eq "refusedLocally" -and $r.holder -eq $idA -and -not $r.ran) "B's click on A's part is refused on B's game ($($r.result) $($r.holder) ran $($r.ran))"
Check ((Counter $b "refusedLocally") -eq $refused + 1) "B counted one local refusal"
Start-Sleep -Milliseconds 500
$idB = (Get-HarnessStatus $b).playerId
$requests = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Locks\] (Request \d+ by client $idB|Lock \d+ granted to client $idB)" })
Check ($requests.Count -eq 0) "no request reached the server"

# The game's own raycast, hover and hold on B.
$highlightBefore = Counter $b "blockedAtSelection.highlight"
$refused = Counter $b "refusedLocally"
$status = Click $b $key 1500
Write-Host "  click on B: $($status | ConvertTo-Json -Compress)"
Check ($status.aimed -and $status.hoverFrames -gt 0) "lock-click hovered the part through the game's raycast ($($status.aim))"
Check ($status.label -match "is working on this part") "the hover label names the work while B hovers ($($status.label))"
Check ($status.partMouseOverFrames -gt 0) "the part stays the game's mouse-over part while B hovers ($($status.partMouseOverFrames) of $($status.hoverFrames) frames)"
Check ((Counter $b "blockedAtSelection.highlight") -gt $highlightBefore) "the highlight was suppressed on the real hover path"
Check ((Counter $b "refusedLocally") -ge $refused + 1) "the completed hold is refused on B's game"
$probe = Cmd $b lock-probe "part $loader $key"
Check (-not $probe.unmounted -and $probe.mode -ne "PartUnMount") "B's game did not start the unmount (mode $($probe.mode))"

Cmd $a lock-release "$loader" | Out-Null
Wait-Mirror $b 0

$hover = Cmd $b lock-hover "$loader $key"
Check ($hover.highlighted -and $hover.label -notmatch "is working") "after A's release the highlight and the label are back ($($hover | ConvertTo-Json -Compress))"

$prefetched = Counter $b "prefetched"
$used = Counter $b "prefetchUsed"
$status = Click $b $key 1500
Start-Sleep -Seconds 1
$probe = Cmd $b lock-probe "part $loader $key"
Check ((Counter $b "prefetched") -eq $prefetched + 1 -and (Counter $b "prefetchUsed") -eq $used + 1) "B's hold prefetched the lock and the completed hold used it (prefetched +$((Counter $b "prefetched") - $prefetched), used +$((Counter $b "prefetchUsed") - $used))"
Check ($probe.mode -eq "PartUnMount") "B's hold opened the unmount (mode $($probe.mode))"
$locks = Get-ServerLocks
Check ($locks.Count -eq 1 -and @($locks.Records | Where-Object { $_.Owner -eq $idB -and $_.X -contains $key }).Count -eq 1) "the server holds B's lock on $key ($($locks.Line))"
$hover = Cmd $a lock-hover "$loader $key"
Check (-not $hover.highlighted -and $hover.label -match "is working") "now A sees B's part in use ($($hover.label))"

Cmd $b lock-probe "mode PartSelect $loader" | Out-Null
Start-Sleep -Seconds 1
$deadline = (Get-Date).AddSeconds(5)
do { $locks = Get-ServerLocks; if ($locks.Count -eq 0) { break }; Start-Sleep -Milliseconds 300 } while ((Get-Date) -lt $deadline)
Check ($locks.Count -eq 0 -and (Get-LockCounter $locks "overlapViolations") -eq 0) "leaving the bolt view released B's lock; no overlap ($($locks.Line))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

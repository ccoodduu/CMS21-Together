# areas: ping, presence
# coop-ping: A pings a part of a car both see; B shows a marker with A's name on that part (resolved on B's own car,
# at A's position), hears the sound, and the marker expires. A pings a spot; B shows it at that position. The client
# throttles a second press, and the server relays one ping of a raw burst (rate limit). While B is in the junkyard,
# A's ping does not reach B, and B's ping there does not reach A. The default hotkey is bound to nothing in the
# game's Rewired maps. The guard runs on Enforce.
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

function Pings([string]$Name) { Cmd $Name ping-markers }
function Counter($P, [string]$Name) { if ($P.counters.$Name) { [int]$P.counters.$Name } else { 0 } }
function Other-Markers($P) { , @($P.markers | Where-Object { -not $_.own }) }

function Wait-Marker([string]$Name, [scriptblock]$Condition, [int]$TimeoutSec = 5) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $p = Pings $Name
        $m = @((Other-Markers $p) | Where-Object { & $Condition $_ })
        if ($m.Count -gt 0) { return $m[0] }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $null
}

function Distance($p, $q) { [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Enforce" | Out-Null; Cmd $name ping-markers "clear" | Out-Null }
$idA = (Get-HarnessStatus $a).playerId
$nameA = (@(Cmd $b mp-players) | Where-Object { $_.id -eq $idA } | Select-Object -First 1).name
Write-Host "A is player $idA '$nameA'"

$hotkey = (Pings $a).defaultHotkey
$mouse = @(Cmd $a input-bindings | Where-Object { $_.controller -eq "Mouse" })
Write-Host "Rewired mouse bindings: $(($mouse | ForEach-Object { "$($_.category)/$($_.action)=$($_.element)" } | Sort-Object -Unique) -join '; ')"
$taken = if ($hotkey -eq "Mouse2") { @($mouse | Where-Object { $_.element -match "(?i)middle|mouse button 3$" }) } else { @(Cmd $a input-bindings $hotkey) }
Check ($hotkey -ne "None" -and $mouse.Count -gt 0 -and $taken.Count -eq 0) "the default ping key $hotkey is bound to no game action ($(($taken | ForEach-Object { "$($_.category)/$($_.action)" }) -join ', '))"
$g = @(Cmd $a input-bindings "G")
Write-Host "Rewired bindings of G (the other candidate): $(($g | ForEach-Object { "$($_.category)/$($_.action)" }) -join ', ')"

Cmd $a car-spawn "$loader $car 0 Entrance1" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 2

$candidates = @(Cmd $b vfx-parts "$loader")
$part = @($candidates | Where-Object { $_.id -match "pokrywa_glowicy|filtr|wentylator" } | Select-Object -First 1)[0]
if (-not $part) { $part = $candidates[0] }
$key = $part.key
Write-Host "part: $key $($part.id)"

# A part ping through the hotkey's path.
$before = Pings $b
$r = Cmd $a ping "$loader $key"
Check ($r.outcome -eq "Sent" -and $r.lastSent.key -eq $key -and $r.lastSent.loader -eq $loader) "A's ping resolved the part under the cursor ($($r | ConvertTo-Json -Compress -Depth 4))"
$own = @((Pings $a).markers | Where-Object { $_.own })
Write-Host "  A's own marker: $($own | ConvertTo-Json -Compress -Depth 4)"
Check ($own.Count -eq 1 -and $own[0].resolved -and $own[0].key -eq $key) "A sees its own marker on the part"
$m = Wait-Marker $b { param($x) $x.kind -eq "part" -and $x.key -eq $key }
Write-Host "  B's marker: $($m | ConvertTo-Json -Compress -Depth 4)"
Check ($null -ne $m) "B shows a marker on $key"
if ($m) {
    Check ($m.name -eq $nameA -and $m.playerId -eq $idA) "B's marker carries A's name ($($m.name))"
    Check ($m.resolved -and $m.loader -eq $loader) "B resolved the part on its own car"
    $gap = Distance $m.position $r.lastSent.position
    Check ($gap -lt 0.5 -and $m.renderers -gt 0) "B's marker sits on the same part as A's, boxed from its meshes ($([math]::Round($gap, 3)) m apart, $($m.renderers) renderer(s))"
    Check ($null -ne $m.screen) "B projects the marker to the screen ($($m.screen | ConvertTo-Json -Compress))"
}
$after = Pings $b
Check ((Counter $after "sounds") -eq (Counter $before "sounds") + 1) "B played the ping sound once"
Check ((Counter $after "shownOnPart") -eq (Counter $before "shownOnPart") + 1) "B counted one marker on a part"

$deadline = (Get-Date).AddSeconds(8)
do { Start-Sleep -Milliseconds 300; $p = Pings $b } while ((Other-Markers $p).Count -gt 0 -and (Get-Date) -lt $deadline)
Check ((Other-Markers $p).Count -eq 0 -and (Counter $p "expired") -ge 1) "B's marker expired after a few seconds ($((Other-Markers $p).Count) left)"

# A spot ping.
$players = (Cmd $b dump).players
$posA = $players."$idA"
$sx = [math]::Round($posA.x + 1.5, 3); $sy = [math]::Round($posA.y, 3); $sz = [math]::Round($posA.z + 1.0, 3)
$spot = [string]::Format([cultureinfo]::InvariantCulture, "{0},{1},{2}", $sx, $sy, $sz)
$r = Cmd $a ping-spot $spot
Check ($r.outcome -eq "Sent" -and $null -eq $r.lastSent.key) "A pinged the spot $spot"
$m = Wait-Marker $b { param($x) $x.kind -eq "spot" }
Write-Host "  B's spot marker: $($m | ConvertTo-Json -Compress -Depth 4)"
Check ($null -ne $m -and $m.name -eq $nameA) "B shows a spot marker with A's name"
if ($m) {
    $gap = Distance $m.position ([pscustomobject]@{ x = [double]$sx; y = [double]$sy; z = [double]$sz })
    Check ($gap -lt 0.01) "B's spot marker is at the pinged position ($([math]::Round($gap, 3)) m)"
}

# Rate limits: the client throttles a second press; the server relays one ping of a raw burst.
Start-Sleep -Seconds 1
$first = Cmd $a ping "$loader $key"
$second = Cmd $a ping "$loader $key"
Check ($first.outcome -eq "Sent" -and $second.outcome -eq "Throttled") "A's client throttles a second ping within 0.5 s ($($first.outcome), $($second.outcome))"
Start-Sleep -Seconds 1
$before = Pings $b
$mark = Get-ServerLogMark
Cmd $a ping-burst "10 $loader $key" | Out-Null
Start-Sleep -Seconds 2
$after = Pings $b
$relayed = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Ping\] Player $idA .* relayed to" })
$dropped = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Ping\] Rate limit: dropped a ping of player $idA" })
Check ((Counter $after "received") -eq (Counter $before "received") + 1) "B received one ping of A's burst of 10 ($((Counter $after "received") - (Counter $before "received")))"
Check ($relayed.Count -eq 1 -and $dropped.Count -ge 1) "the server relayed 1 and logged the rate limit ($($relayed.Count) relayed, $($dropped.Count) drop line(s))"

# Another scene.
Cmd $b travel "Junkyard" | Out-Null
$s = Wait-HarnessStatus -Instance $b -TimeoutSec 180 -What "B in the junkyard" -Condition { param($s) $s.scene -ne "garage" -and $s.playable -and $s.connectionValid }
Check ($s.scene -match "(?i)junkyard") "B reached the junkyard ($($s.scene))"
Start-Sleep -Seconds 3
Cmd $b ping-markers "clear" | Out-Null
$beforeA = Pings $a
$mark = Get-ServerLogMark
Start-Sleep -Milliseconds 600
$r = Cmd $a ping "$loader $key"
Start-Sleep -Seconds 2
$p = Pings $b
$line = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[Ping\] Player $idA .* relayed to (\d+)" }) | Select-Object -Last 1
Check ($r.outcome -eq "Sent" -and (Counter $p "received") -eq 0 -and (Other-Markers $p).Count -eq 0) "A's ping in the garage does not reach B in the junkyard ($($r.outcome), B received $(Counter $p "received"))"
Check ($line -match "relayed to 0 player") "the server relayed it to nobody ($line)"
$r = Cmd $b ping-spot "0,0,0"
Start-Sleep -Seconds 2
$afterA = Pings $a
Write-Host "  B's ping in the junkyard: $($r.outcome)"
Check ((Counter $afterA "received") -eq (Counter $beforeA "received")) "B's ping in the junkyard does not reach A in the garage"

Cmd $b travel "Garage" | Out-Null
Start-Sleep -Seconds 3
Wait-InGarage $b

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

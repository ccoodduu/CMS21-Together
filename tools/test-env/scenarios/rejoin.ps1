# session-persistence-and-rejoin 7.2 (and 4.1, 4.3, 4.4): A and B connect; A changes the shared stats and walks to
# another spot; A goes to the menu and reconnects. A must come back at that spot, with the same player key (created
# once in UserData\CMS21Together\player.json), and A's and B's stats and inventory must be equal.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Distance($p, $q) { [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.y - $q.y, 2) + [math]::Pow($p.z - $q.z, 2)) }

function Read-KeyFile([string]$Name) {
    $path = Join-Path (Split-Path (Get-HarnessDir $Name) -Parent) "CMS21Together\player.json"
    if (Test-Path -LiteralPath $path) { [pscustomobject]@{ Text = (Get-Content -LiteralPath $path -Raw); Written = (Get-Item -LiteralPath $path).LastWriteTimeUtc } } else { $null }
}

function Joined-Lines([string]$ShortKey, [int]$After) {
    @(Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") | Select-Object -Skip $After |
        Where-Object { $_ -match "Player \d+ '.*' joined as $([regex]::Escape($ShortKey)) \((new|returning)\)" })
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
$keyFileBefore = Read-KeyFile $a
$startMark = Get-ServerLogMark

Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
$keyFileAfterFirst = Read-KeyFile $a
Check ($null -ne $keyFileAfterFirst) "A has UserData\CMS21Together\player.json after connecting"
if ($null -ne $keyFileBefore) { Check ($keyFileAfterFirst.Text -eq $keyFileBefore.Text) "A's existing player.json was kept" }
$keyA = (Send-HarnessCommand -Instance $a -Verb player-key).key
$keyB = (Send-HarnessCommand -Instance $b -Verb player-key).key
Check ($keyA -ne $keyB) "A and B have different player keys"
$shortA = "guid:" + $keyA.Substring(0, [math]::Min(8, $keyA.Length))

$before = Send-HarnessCommand -Instance $a -Verb dump
Send-HarnessCommand -Instance $a -Verb stats-add -Arguments "7 300" | Out-Null
$deadline = (Get-Date).AddSeconds(20)
do { Start-Sleep -Milliseconds 500; $changed = Send-HarnessCommand -Instance $a -Verb dump } while ($changed.stats.scrap -eq $before.stats.scrap -and (Get-Date) -lt $deadline)
Check ($changed.stats.scrap -eq $before.stats.scrap + 7) "stats-add reached A ($($before.stats.scrap) -> $($changed.stats.scrap))"

$start = $changed.local.position
Send-HarnessCommand -Instance $a -Verb teleport -Arguments ([string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0},{1},{2},{3}", $start.x + 1.0, $start.y + 1.0, $start.z + 1.0, 135)) | Out-Null
Start-Sleep -Seconds 3
$left = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "before-leave"
Write-Host "A stands at $($left.local.position | ConvertTo-Json -Compress), yaw $($left.local.yaw)"
Check ((Distance $left.local.position $start) -gt 0.5) "A moved away from the spawn before leaving"

$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb to-menu | Out-Null
$recordLine = $null
try { $recordLine = Wait-ServerLog -Pattern "\[Players\] $([regex]::Escape($shortA)) .* Garage at \(" -After $mark -TimeoutSec 30 } catch { }
Check ($null -ne $recordLine) "the server stored A's garage position on leave"
if ($recordLine -match "Garage at \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)") {
    $stored = [pscustomobject]@{ x = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture); y = [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture); z = [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture) }
    Check ((Distance $stored $left.local.position) -lt 0.1) "the stored record matches A's dumped position (stored $($stored | ConvertTo-Json -Compress))"
}
Wait-HarnessStatus -Instance $a -TimeoutSec 60 -What "A in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Send-ServerCommand "players"

$mark = Get-ServerLogMark
Connect-HarnessInstance $a; Wait-InGarage $a
$restoreLine = $null
try { $restoreLine = Wait-ServerLog -Pattern "\[Players\] Client\[\d+\] restored to its last garage position" -After $mark -TimeoutSec 5 } catch { }
Check ($null -ne $restoreLine) "the server sent A's last position in the snapshot"
Start-Sleep -Seconds 3

$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "after-rejoin"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "after-rejoin"
$offset = Distance $dumpA.local.position $left.local.position
Check ($offset -lt 0.5) "A is back where it left ($([math]::Round($offset, 3)) m away; now $($dumpA.local.position | ConvertTo-Json -Compress))"
$yawDelta = [math]::Abs((($dumpA.local.yaw - $left.local.yaw + 540) % 360) - 180)
Check ($yawDelta -lt 10) "A faces the same way (yaw $($left.local.yaw) -> $($dumpA.local.yaw))"

$joins = Joined-Lines $shortA $startMark
Check ($joins.Count -ge 2 -and $joins[-1] -match "\(returning\)") "the server logged A's key $shortA on both connects, the second as returning ($($joins.Count) lines)"
$keyFileAfterSecond = Read-KeyFile $a
Check ($null -ne $keyFileAfterSecond -and $keyFileAfterSecond.Text -eq $keyFileAfterFirst.Text -and $keyFileAfterSecond.Written -eq $keyFileAfterFirst.Written) "player.json was written once"

$diff = Compare-HarnessDumps $dumpA $dumpB -Sections @("stats", "inventory")
Check ($diff.Count -eq 0) "A and B agree on stats and inventory (differ: $($diff -join ', '))"
Check ($dumpA.stats.scrap -eq $changed.stats.scrap -and $dumpA.stats.exp -eq $changed.stats.exp -and $dumpA.stats.level -eq $changed.stats.level) "A's stats survived the rejoin (scrap $($dumpA.stats.scrap), exp $($dumpA.stats.exp), level $($dumpA.stats.level))"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

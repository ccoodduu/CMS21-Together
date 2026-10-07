# run-all: lane 3
# areas: connect, presence, cars, parts
# multiplayer-soak-and-scale 1.5: every instance of the lane (two to four) connects, one after the other, capped at
# 30 fps. Every roster shows the others with their names, the first spawns a car and the second unmounts a part,
# all shared dump sections are equal on every client, and the server's players command lists one record each.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")

$names = @($Ctx.Instances)
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

Wait-AllInMenu $names
$times = Connect-ScaleInstances $names
$Ctx.Result.notes += "join times (s): $(($times.Keys | ForEach-Object { "$_ $($times[$_])" }) -join ', ')"
$ids = Get-PlayerIds $names

$others = $names.Count - 1
foreach ($name in $names) {
    $dump = $null
    try {
        $dump = Wait-HarnessDump -Instance $name -TimeoutSec 30 -What "$others named remote players" -Condition { param($d)
            $remote = @($d.roster.PSObject.Properties | Where-Object { $_.Name -ne "$($d.playerId)" })
            $remote.Count -eq $others -and @($remote | Where-Object { $_.Value.name }).Count -eq $others -and $d.remotePlayers -eq $others
        }
    } catch { $dump = Send-HarnessCommand -Instance $name -Verb dump }
    $remote = @($dump.roster.PSObject.Properties | Where-Object { $_.Name -ne "$($dump.playerId)" })
    Check ($remote.Count -eq $others -and $dump.remotePlayers -eq $others) "$name sees the other $others players ($(($remote | ForEach-Object { "$($_.Name)='$($_.Value.name)'" }) -join ', '); avatars $($dump.remotePlayers))"
}

$first = $names[0]
$second = if ($names.Count -gt 1) { $names[1] } else { $first }
Send-HarnessCommand -Instance $first -Verb car-spawn -Arguments "0 car_boltatlanta 0 auto" | Out-Null
$notReady = @(Wait-CarsReady $names 180)
Check ($notReady.Count -eq 0) "the car is Ready on every client ($($notReady -join '; '))"
$part = Send-HarnessCommand -Instance $second -Verb part-fast-unmount -Arguments "0"
Write-Host "$second unmounted $($part.key)"
Start-Sleep -Seconds 3

try {
    $dumps = Wait-HarnessDumpsAllEqual -Instances $names -TimeoutSec 60
    Check $true "all $($names.Count) clients agree on $((Get-SharedDumpSections) -join ', ')"
    $car = @($dumps[$first].cars | Where-Object { $_.index -eq 0 })[0]
    Check (@($car.subParts | Where-Object { $_.key -eq $part.key -and $_.unmounted }).Count -eq 1) "the unmounted part $($part.key) is off on every client"
} catch {
    Check $false $_.Exception.Message
    Save-Dumps (Get-LastHarnessDumps) (Join-Path $Ctx.RunDir "differ")
}

$digest = Invoke-ForcedDigestCheck $names
Check $digest.ok "the forced digest round matches every client ($($digest.problems -join '; '))"

$records = @(Get-ServerPlayerRecords)
Write-Host "players: $(($records | ForEach-Object { "$($_.Key) '$($_.Name)'" }) -join ', ')"
Check ($records.Count -eq $names.Count) "the server lists $($names.Count) player records ($($records.Count))"
Check (@($records | ForEach-Object { $_.Key } | Select-Object -Unique).Count -eq $records.Count) "every record has its own identity"
Check (@($ids.Values | Select-Object -Unique).Count -eq $names.Count) "every client has its own slot ($(($ids.Keys | ForEach-Object { "$_=$($ids[$_])" }) -join ', '))"

foreach ($name in $names) { Save-HarnessDump -Instance $name -RunDir $Ctx.RunDir -Label "end" | Out-Null }
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

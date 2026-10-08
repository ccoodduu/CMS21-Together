# areas: outdoor, economy, presence
# shared-outdoor-scenes 9.2, guard enforcing: with 2 barns, A travels to a barn and B follows: one barn is used, both
# are in the same instance with the same layout digest and cars, and see each other. With shared_outdoor_scenes
# without the barn (server restart) both barns are local and avatars hidden.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Dump([string]$Name) { Send-HarnessCommand -Instance $Name -Verb dump }

function Wait-InBarn([string]$Name, [bool]$Shared) {
    Wait-HarnessDump -Instance $Name -TimeoutSec 240 -What "$Name in the barn (shared $Shared)" -Condition {
        param($d) $d.local.scene -eq "Barn" -and ($d.outdoor.applied -or -not $Shared) -and [bool]$d.outdoor.shared -eq $Shared
    }
}

function Server-Lines([int]$Mark, [string]$Pattern) { @(Get-ServerLogLines | Select-Object -Skip $Mark | Where-Object { $_ -match $Pattern }) }

function Roster-Avatar($Dump, [string]$PlayerName) {
    $entry = @($Dump.roster.PSObject.Properties | Where-Object { $_.Value.name -eq $PlayerName } | ForEach-Object { $_.Value })[0]
    [bool]($entry -and $entry.avatarActive)
}

function Add-Barns([int]$Count) {
    for ($i = 0; $i -lt $Count; $i++) {
        $map = (Send-HarnessCommand -Instance $a -Verb inv-add-local -Arguments "specialMap").uid
        Send-HarnessCommand -Instance $a -Verb econ-barn-map -Arguments "$map" | Out-Null
        Start-Sleep -Seconds 2
    }
}

function Connect-Both {
    foreach ($name in $Ctx.Instances) {
        Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
    }
    Connect-HarnessInstance $a; Wait-InGarage $a
    Connect-HarnessInstance $b; Wait-InGarage $b
    foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "enforce" | Out-Null }
}

Send-HarnessCommand -Instance $a -Verb set-name -Arguments "Ann" | Out-Null
Send-HarnessCommand -Instance $b -Verb set-name -Arguments "Bob" | Out-Null
Connect-Both
Send-ServerCommand "money set 200000"
$barns = (Dump $a).stats.barns
Add-Barns (2 - $barns)
$barns = (Dump $a).stats.barns
Check ($barns -eq 2) "the shared barn count is 2 ($barns)"

$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-map-travel -Arguments "Barn" | Out-Null
$da = Wait-InBarn $a $true
Send-HarnessCommand -Instance $b -Verb econ-map-travel -Arguments "Barn" | Out-Null
$db = Wait-InBarn $b $true
$da = Dump $a
Check ($da.outdoor.instanceId -gt 0 -and $da.outdoor.instanceId -eq $db.outdoor.instanceId) "same barn instance ($($da.outdoor.instanceId), $($db.outdoor.instanceId))"
Check (@(Server-Lines $mark "TravelFee\(7\).*joins the open barn").Count -eq 1) "B's trip joined the open barn"
Start-Sleep -Seconds 3
Check ((Dump $b).stats.barns -eq 1) "the barn count is 1 after two trips into one barn ($((Dump $b).stats.barns))"
$layoutA = @($da.outdoor.digest | Where-Object { $_ -like "layout:*" })
$layoutB = @($db.outdoor.digest | Where-Object { $_ -like "layout:*" })
Check ($layoutA.Count -eq 1 -and "$layoutA" -eq "$layoutB") "same barn layout digest (A $layoutA; B $layoutB)"
$carsA = ($da.outdoor.cars | ForEach-Object { "$($_.index):$($_.carToLoad)/$($_.version)" }) -join ","
$carsB = ($db.outdoor.cars | ForEach-Object { "$($_.index):$($_.carToLoad)/$($_.version)" }) -join ","
Check (@($da.outdoor.cars).Count -eq 3 -and $carsA -eq $carsB) "the same 3 cars (A $carsA; B $carsB)"
Check (@(Server-Lines $mark "digest mismatch").Count -eq 0) "no digest mismatch"
Check (Roster-Avatar (Dump $a) "Bob") "A sees Bob in the barn"
Check (Roster-Avatar (Dump $b) "Ann") "B sees Ann in the barn"
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "shared-barn"
foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb travel -Arguments "Garage" | Out-Null }
foreach ($name in $a, $b) { Wait-InGarage $name }

# Barn not shared: today's behaviour.
Set-ServerConfigValues $Ctx.ServerDir @{ shared_outdoor_scenes = "junkyard,auction" }
foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb to-menu | Out-Null }
Restart-TestServer
Connect-Both
Add-Barns 2
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb econ-map-travel -Arguments "Barn" | Out-Null
Wait-InBarn $a $false | Out-Null
Send-HarnessCommand -Instance $b -Verb econ-map-travel -Arguments "Barn" | Out-Null
Wait-InBarn $b $false | Out-Null
Start-Sleep -Seconds 5
Check (-not (Roster-Avatar (Dump $a) "Bob") -and -not (Roster-Avatar (Dump $b) "Ann")) "with the barn not shared, avatars stay hidden"
Check (@(Server-Lines $mark "\[Outdoor\] Barn #\d+ opened").Count -eq 0) "no barn instance exists on the server"
foreach ($name in $a, $b) { Send-HarnessCommand -Instance $name -Verb travel -Arguments "Garage" | Out-Null }
foreach ($name in $a, $b) { Wait-InGarage $name }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

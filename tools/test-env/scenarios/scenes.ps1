# Scene tracking: who is where, avatars only in the same scene, leaving the garage and coming back is a late join,
# results sent while leaving reach the return snapshot. (Car spawn while away needs sync-car-parts' car verbs.)
param($Ctx)

$a, $b = $Ctx.Instances

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-LocalScene([string]$Name, [string]$Scene) {
    Wait-HarnessDump -Instance $Name -TimeoutSec 240 -What "local scene $Scene" -Condition {
        param($d) $d.local.scene -eq $Scene
    } | Out-Null
    Write-Host "$Name is in $Scene"
}

function Get-RosterEntry($Dump, [string]$PlayerName) {
    @($Dump.roster.PSObject.Properties | Where-Object { $_.Value.name -eq $PlayerName } | ForEach-Object { $_.Value })[0]
}

function Wait-Roster([string]$Viewer, [string]$PlayerName, [string]$Scene, [bool]$Avatar) {
    Wait-HarnessDump -Instance $Viewer -TimeoutSec 60 -What "$PlayerName in $Scene, avatar $Avatar" -Condition {
        param($d)
        $entry = Get-RosterEntry $d $PlayerName
        $entry -and $entry.scene -eq $Scene -and [bool]$entry.avatarActive -eq $Avatar
    } | Out-Null
    Write-Host "$Viewer sees $PlayerName in $Scene (avatar $Avatar)"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Send-HarnessCommand -Instance $a -Verb set-name -Arguments "Ann" | Out-Null
Send-HarnessCommand -Instance $b -Verb set-name -Arguments "Bob" | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Roster $b "Ann" "Garage" $true

Send-HarnessCommand -Instance $a -Verb travel -Arguments "Junkyard" | Out-Null
Wait-Roster $b "Ann" "Loading" $false
Wait-LocalScene $a "Junkyard"
Wait-Roster $b "Ann" "Junkyard" $false

Send-HarnessCommand -Instance $b -Verb travel -Arguments "Junkyard" | Out-Null
Wait-LocalScene $b "Junkyard"
Wait-Roster $b "Ann" "Junkyard" $true
Wait-Roster $a "Bob" "Junkyard" $true
Start-Sleep -Seconds 3
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "junkyard"

Send-HarnessCommand -Instance $b -Verb travel -Arguments "Garage" | Out-Null
Wait-LocalScene $b "Garage"
Wait-InGarage $b
Wait-Roster $a "Bob" "Garage" $false

$scrapBefore = (Send-HarnessCommand -Instance $b -Verb dump).stats.scrap
Send-HarnessCommand -Instance $a -Verb leave-mark -Arguments "3" | Out-Null
Send-HarnessCommand -Instance $a -Verb travel -Arguments "Garage" | Out-Null
Wait-LocalScene $a "Garage"
Wait-InGarage $a
Wait-Roster $a "Bob" "Garage" $true
Wait-Roster $b "Ann" "Garage" $true
Start-Sleep -Seconds 2
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "back"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "back"
$failures = @()
if ($dumpA.stats.scrap -ne $scrapBefore + 3) { $failures += "A's scrap after return is $($dumpA.stats.scrap), expected $($scrapBefore + 3)" }
$diff = Compare-HarnessDumps $dumpA $dumpB -Sections @("stats", "inventory")
foreach ($name in $Ctx.Instances) {
    $pref = Send-HarnessCommand -Instance $name -Verb profile-pref
    if ($pref.selected -ne 4) { $failures += "$name uses profile $($pref.selected) ($($pref.selectedName)) in session, expected slot 4" }
}
if ($diff.Count -gt 0) { $failures += "A and B differ after the return in: $($diff -join ', ')" }

Send-HarnessCommand -Instance $b -Verb disconnect | Out-Null
Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb travel -Arguments "Junkyard" | Out-Null
Wait-LocalScene $a "Junkyard"
Connect-HarnessInstance $b; Wait-InGarage $b
Wait-Roster $b "Ann" "Junkyard" $false

Send-HarnessCommand -Instance $a -Verb disconnect | Out-Null
Wait-HarnessDump -Instance $b -TimeoutSec 15 -What "roster empty" -Condition { param($d) @($d.roster.PSObject.Properties).Count -eq 0 } | Out-Null
Write-Host "A left from the junkyard; B's roster is empty"

$Ctx.Result.notes += "Car spawn while away not covered yet (needs sync-car-parts' car verbs)"
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

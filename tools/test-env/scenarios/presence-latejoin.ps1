# The reported bug: a player who joins late must see a player who stands still. A connects and never moves,
# B connects; both see the other's avatar where the other stands, with names; B leaves and A's roster empties;
# B comes back and both see each other again.
param($Ctx)

$a, $b = $Ctx.Instances

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Get-Distance($p, $q) {
    [math]::Sqrt([math]::Pow($p.x - $q.x, 2) + [math]::Pow($p.z - $q.z, 2))
}

function Assert-SeesAt([string]$Viewer, [string]$OtherName, $OtherPosition) {
    $dump = Wait-HarnessDump -Instance $Viewer -TimeoutSec 20 -What "avatar of $OtherName" -Condition {
        param($d) @($d.roster.PSObject.Properties | Where-Object { $_.Value.name -eq $OtherName -and $_.Value.avatarActive }).Count -eq 1
    }
    $entry = @($dump.roster.PSObject.Properties | Where-Object { $_.Value.name -eq $OtherName })[0].Value
    $distance = Get-Distance $entry.avatarPosition $OtherPosition
    if ($distance -gt 0.3) { throw "$Viewer sees $OtherName at $($entry.avatarPosition | ConvertTo-Json -Compress), expected $($OtherPosition | ConvertTo-Json -Compress) ($([math]::Round($distance, 2)) m off)" }
    Write-Host "$Viewer sees $OtherName within $([math]::Round($distance, 2)) m"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Send-HarnessCommand -Instance $a -Verb set-name -Arguments "Ann" | Out-Null
Send-HarnessCommand -Instance $b -Verb set-name -Arguments "Bob" | Out-Null

Connect-HarnessInstance $a
Wait-InGarage $a
Start-Sleep -Seconds 3
Connect-HarnessInstance $b
Wait-InGarage $b
Start-Sleep -Seconds 2

$localA = (Send-HarnessCommand -Instance $a -Verb dump).local.position
$localB = (Send-HarnessCommand -Instance $b -Verb dump).local.position
$apart = Get-Distance $localA $localB
if ($apart -lt 0.8) { throw "Spawn positions only $([math]::Round($apart, 2)) m apart" }
Assert-SeesAt $b "Ann" $localA
Assert-SeesAt $a "Bob" $localB
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "latejoin"

Send-HarnessCommand -Instance $b -Verb disconnect | Out-Null
Wait-HarnessDump -Instance $a -TimeoutSec 5 -What "B gone from roster" -Condition {
    param($d) @($d.roster.PSObject.Properties).Count -eq 0 -and $d.remotePlayers -eq 0
} | Out-Null
Write-Host "B left; A's roster is empty"

Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b
Wait-InGarage $b
Start-Sleep -Seconds 2
$localB = (Send-HarnessCommand -Instance $b -Verb dump).local.position
Assert-SeesAt $b "Ann" $localA
Assert-SeesAt $a "Bob" $localB

$Ctx.Result.passed = $true

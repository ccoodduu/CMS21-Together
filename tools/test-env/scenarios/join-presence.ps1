# areas: connect
# Steam rich presence join strings and warm join requests, without Steam: the join string comes from the server's
# public address (else no string, with the reason), and a join request while in a session waits for confirmation.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-Join([string]$Name, [string]$Status) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "joinStatus $Status" -Condition { param($s) $s.joinStatus -eq $Status -and ($Status -ne "InSession" -or ($s.scene -eq "garage" -and $s.playable)) }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

Connect-HarnessInstance $a
Wait-Join $a "InSession" | Out-Null
$presence = Send-HarnessCommand -Instance $a -Verb mp-presence
Check ($null -eq $presence.joinString -and $presence.reason -eq "loopback") "loopback server gives no join string (reason '$($presence.reason)')"

Send-HarnessCommand -Instance $a -Verb mp-join-string -Arguments "CMS21Together:ip:127.0.0.1:1" | Out-Null
Start-Sleep -Seconds 2
$session = (Send-HarnessCommand -Instance $a -Verb dump).session
Check ($session.joinStatus -eq "InSession" -and $session.pendingConfirmation -eq "127.0.0.1:1") "join request in session waits for confirmation (status $($session.joinStatus), pending $($session.pendingConfirmation))"
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "join-confirm"
Send-HarnessCommand -Instance $a -Verb mp-answer -Arguments "no" | Out-Null
Start-Sleep -Seconds 1
Check ((Get-HarnessStatus $a).joinStatus -eq "InSession") "answering no keeps the session"

Stop-TestServer
Start-TestServer -Arguments @("--public-address", "192.0.2.10") | Out-Null
$lost = Wait-HarnessStatus -Instance $a -TimeoutSec 60 -What "A back in menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected }
Send-HarnessCommand -Instance $a -Verb mp-ui -Arguments "ok" | Out-Null
Connect-HarnessInstance $b
Wait-Join $b "InSession" | Out-Null
$presence = Send-HarnessCommand -Instance $b -Verb mp-presence
Check ($presence.joinString -eq "CMS21Together:ip:192.0.2.10:$($Ctx.Lane.Port)") "public address gives the join string ($($presence.joinString))"

Send-HarnessCommand -Instance $b -Verb mp-join-string -Arguments $Ctx.Lane.ConnectAddress | Out-Null
Start-Sleep -Seconds 1
Send-HarnessCommand -Instance $b -Verb mp-answer -Arguments "yes" | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 60 -What "B left the session" -Condition { param($s) $s.joinStatus -ne "InSession" } | Out-Null
Wait-Join $b "InSession" | Out-Null
Check ((Get-HarnessStatus $b).syncAcked) "answering yes leaves and joins the requested server again"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# Joining with readable failures: unreachable server, wrong version, invalid input, server full, kick; the server
# reports its settings, the join panel and the failure message render (screenshots), command-line overrides work.
param($Ctx)

$a, $b = $Ctx.Instances
$address = $Ctx.Lane.ConnectAddress
$overridePort = $Ctx.Lane.Port + 5

function Wait-Join([string]$Name, [string]$Status, [int]$TimeoutSec = 120) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "joinStatus $Status" -Condition {
        param($s) $s.joinStatus -eq $Status
    }
}

function Wait-Menu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 120 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

foreach ($name in $Ctx.Instances) { Wait-Menu $name }

Send-HarnessCommand -Instance $b -Verb mp-ui -Arguments "open join" | Out-Null
Start-Sleep -Seconds 1
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "join-panel"
Send-HarnessCommand -Instance $b -Verb mp-ui -Arguments "close" | Out-Null

$started = Get-Date
Send-HarnessCommand -Instance $b -Verb mp-join -Arguments "127.0.0.1:1" | Out-Null
$s = Wait-Join $b "Failed" 20
Check ($s.lastDisconnect.reason -eq "Unreachable") "unreachable server ends Failed/Unreachable (got $($s.lastDisconnect.reason))"
Check (((Get-Date) - $started).TotalSeconds -le 12) "unreachable reported within 12 s"
Start-Sleep -Seconds 1
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "unreachable-message"
Send-HarnessCommand -Instance $b -Verb mp-ui -Arguments "ok" | Out-Null

$invalid = $null
try { Send-HarnessCommand -Instance $b -Verb mp-join -Arguments "abc:xyz" | Out-Null } catch { $invalid = $_.Exception.Message }
Check ($invalid -match "not a valid port") "invalid target is rejected without a connection attempt ($invalid)"
Start-Sleep -Milliseconds 1500
Check ((Get-HarnessStatus $b).joinStatus -eq "Idle") "status stays Idle after invalid input"

Send-HarnessCommand -Instance $b -Verb mp-fake-version -Arguments "0.0.1" | Out-Null
Send-HarnessCommand -Instance $b -Verb mp-join -Arguments $address | Out-Null
$s = Wait-Join $b "Failed" 30
Check ($s.lastDisconnect.reason -eq "VersionMismatch") "wrong version ends Failed/VersionMismatch (got $($s.lastDisconnect.reason))"
Check ($s.lastDisconnect.message -match "0\.0\.1" -and $s.lastDisconnect.message -match "you have") "version message names both versions: $($s.lastDisconnect.message)"
Send-HarnessCommand -Instance $b -Verb mp-ui -Arguments "ok" | Out-Null
Send-HarnessCommand -Instance $b -Verb mp-fake-version -Arguments "reset" | Out-Null
Wait-Menu $b

Stop-TestServer
Start-TestServer -Arguments @("--max-players", "1", "--port", "$overridePort", "--server-name", "`"Harness Lane $($Ctx.Lane.Lane)`"") | Out-Null
Check ((Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") -Raw) -match "Settings: name 'Harness Lane \d+', port $overridePort, max players 1") "server logs the effective settings"

Send-HarnessCommand -Instance $a -Verb mp-join -Arguments "127.0.0.1:$overridePort" | Out-Null
Wait-Join $a "InSession" 300 | Out-Null
$dumpA = Send-HarnessCommand -Instance $a -Verb dump
Check ($dumpA.session.serverInfo.port -eq $overridePort -and $dumpA.session.serverInfo.maxPlayers -eq 1 -and $dumpA.session.serverInfo.steamId -eq "0") "A sees the server info (port $($dumpA.session.serverInfo.port), max $($dumpA.session.serverInfo.maxPlayers), steam $($dumpA.session.serverInfo.steamId))"

Send-HarnessCommand -Instance $b -Verb mp-join -Arguments "127.0.0.1:$overridePort" | Out-Null
$s = Wait-Join $b "Failed" 30
Check ($s.lastDisconnect.reason -eq "ServerFull") "second client ends Failed/ServerFull (got $($s.lastDisconnect.reason))"
Start-Sleep -Seconds 2
Check ((Get-HarnessStatus $a).joinStatus -eq "InSession") "A stays InSession"

Send-ServerCommand "kick 1"
$s = Wait-Join $a "Disconnected" 30
Check ($s.lastDisconnect.reason -eq "Kicked") "kicked player ends Disconnected/Kicked (got $($s.lastDisconnect.reason))"
Wait-Menu $a
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "kicked-message"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

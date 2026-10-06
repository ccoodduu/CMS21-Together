# Session admin over DirectIP: the server has a password and an admin key. A joins with both and is admin; B is
# refused without and with a wrong password, then joins; toasts, the player list with ping, a refused kick from B
# and A kicking B (B in the menu with "kicked", A told "was kicked"); B rejoins with the remembered password.
param($Ctx)

$a, $b = $Ctx.Instances
$address = $Ctx.Lane.ConnectAddress
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "$Name in session in the garage" -Condition {
        param($s) $s.joinStatus -eq "InSession" -and $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Menu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 120 -What "$Name in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

function Wait-Toast([string]$Name, [string]$Pattern) {
    try {
        Wait-HarnessDump -Instance $Name -TimeoutSec 15 -What "toast '$Pattern'" -Condition { param($d) @($d.session.toasts | Where-Object { $_ -match $Pattern }).Count -ge 1 }
    } catch {
        Send-HarnessCommand -Instance $Name -Verb dump
    }
}

function Join-Refused([string]$Arguments, [string]$What) {
    Send-HarnessCommand -Instance $b -Verb mp-join -Arguments $Arguments | Out-Null
    $s = Wait-HarnessStatus -Instance $b -TimeoutSec 60 -What "B refused ($What)" -Condition { param($s) $s.joinStatus -eq "Failed" }
    Check ($s.lastDisconnect.reason -eq "WrongPassword") "B $What ends Failed/WrongPassword (got $($s.lastDisconnect.reason))"
    Wait-Menu $b
    Send-HarnessCommand -Instance $b -Verb mp-ui -Arguments "ok" | Out-Null
    $session = (Send-HarnessCommand -Instance $b -Verb dump).session
    Check ($session.panel -eq "join" -and $session.passwordField -eq $true) "after $What the join panel shows the password field (panel $($session.panel), field $($session.passwordField))"
}

foreach ($name in $Ctx.Instances) { Wait-Menu $name }

Stop-TestServer
Start-TestServer -Arguments @("--admin-key", "k", "--password", "pw") | Out-Null
Check ((Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") -Raw) -match "password set, admin key set") "server logs password and admin key as set, not their values"

Send-HarnessCommand -Instance $a -Verb mp-join -Arguments "$address password=pw adminKey=k" | Out-Null
Wait-InGarage $a
Check ((Send-HarnessCommand -Instance $a -Verb dump).session.serverInfo.isAdmin -eq $true) "A with the admin key is admin"

Join-Refused $address "without a password"
Save-HarnessScreenshot -Instance $b -RunDir $Ctx.RunDir -Label "password-field"
Join-Refused "$address password=wrong" "with a wrong password"
Send-HarnessCommand -Instance $b -Verb mp-ui -Arguments "close" | Out-Null

Send-HarnessCommand -Instance $b -Verb mp-join -Arguments "$address password=pw" | Out-Null
Wait-InGarage $b
$sessionB = (Send-HarnessCommand -Instance $b -Verb dump).session
Check ($sessionB.serverInfo.isAdmin -eq $false) "B without the admin key is not admin"

$dumpA = Wait-Toast $a "joined$"
Check (@($dumpA.session.toasts | Where-Object { $_ -match "joined$" }).Count -eq 1) "A got one join toast: $(@($dumpA.session.toasts) -join ' | ')"
$toastsB = @((Send-HarnessCommand -Instance $b -Verb dump).session.toasts)
Check (@($toastsB | Where-Object { $_ -match "other players? online" }).Count -eq 1 -and @($toastsB | Where-Object { $_ -match "joined$" }).Count -eq 0) "B got one summary toast and no join toasts: $($toastsB -join ' | ')"

foreach ($name in @($a, $b)) {
    $deadline = (Get-Date).AddSeconds(8)
    do {
        $rows = @(Send-HarnessCommand -Instance $name -Verb mp-players)
        $pinged = @($rows | Where-Object { $null -ne $_.pingMs }).Count
        if ($rows.Count -eq 2 -and $pinged -eq 2) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Check ($rows.Count -eq 2) "$name lists two players ($($rows.Count))"
    Check (@($rows | Where-Object { $_.scene -eq "Garage" }).Count -eq 2) "$name shows both in the garage ($(@($rows | ForEach-Object { $_.scene }) -join ', '))"
    Check ($pinged -eq 2) "$name has a ping for both players within 8 s ($(@($rows | ForEach-Object { $_.pingMs }) -join ', '))"
    $kickable = @($rows | Where-Object { $_.kickable }).Count
    if ($name -eq $a) { Check ($kickable -eq 1) "A (admin) can kick B only ($kickable kick controls)" }
    else { Check ($kickable -eq 0) "B sees no kick controls ($kickable)" }
}

$idA = (Get-HarnessStatus $a).playerId
$idB = (Get-HarnessStatus $b).playerId
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb mp-kick -Arguments "$idA" | Out-Null
$refused = $null
try { $refused = Wait-ServerLog -Pattern "Kick request from client $idB for player $idA refused: not an admin" -After $mark -TimeoutSec 10 } catch { }
Check ($null -ne $refused) "the server logs B's refused kick request"
Start-Sleep -Seconds 2
Check ((Get-HarnessStatus $a).joinStatus -eq "InSession") "A unaffected by B's kick request"

Send-HarnessCommand -Instance $a -Verb mp-ui -Arguments "open session" | Out-Null
Start-Sleep -Seconds 1
Save-HarnessScreenshot -Instance $a -RunDir $Ctx.RunDir -Label "session-panel"
Send-HarnessCommand -Instance $a -Verb mp-ui -Arguments "close session" | Out-Null

Send-HarnessCommand -Instance $a -Verb mp-kick -Arguments "$idB" | Out-Null
$s = Wait-HarnessStatus -Instance $b -TimeoutSec 30 -What "B kicked" -Condition { param($s) $s.joinStatus -eq "Disconnected" }
Check ($s.lastDisconnect.reason -eq "Kicked") "B ends Disconnected/Kicked (got $($s.lastDisconnect.reason))"
Wait-Menu $b
$dumpA = Wait-Toast $a "was kicked$"
Check (@($dumpA.session.toasts | Where-Object { $_ -match "was kicked$" }).Count -eq 1) "A got the 'was kicked' toast"
Check ((Get-HarnessStatus $a).joinStatus -eq "InSession") "A stays in session after kicking B"

Send-HarnessCommand -Instance $b -Verb mp-ui -Arguments "ok" | Out-Null
Send-HarnessCommand -Instance $b -Verb mp-join -Arguments $address | Out-Null
Wait-InGarage $b
Check ((Get-HarnessStatus $b).joinStatus -eq "InSession") "B rejoins after the kick with the remembered password"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

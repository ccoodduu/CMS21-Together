# areas: connect
# Compatibility checks at join: a different game version, a gameplay mod, a different mod version or build is refused
# with a readable reason while A stays in the session; a visual mod and a different DLC set are accepted, and the
# server tracks the DLC set shared by all players; an operator can ignore a gameplay mod in server_config.ini.
param($Ctx)

$a, $b = $Ctx.Instances
$address = $Ctx.Lane.ConnectAddress

$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-Menu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 120 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
}

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
    Write-Host "$Name is in the garage"
}

function Compat([string]$Name, [string]$Arguments) { Send-HarnessCommand -Instance $Name -Verb compat-override -Arguments $Arguments | Out-Null }

function Save-Report([string]$Name, [string]$Label) {
    $report = Send-HarnessCommand -Instance $Name -Verb compat-report -Arguments "patches"
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "compat-report_${Label}_$Name.json") -Encoding utf8
    return $report
}

function Join-Refused([string]$Expected, [string]$Label) {
    Wait-HarnessStatus -Instance $b -TimeoutSec 20 -What "B idle before joining ($Label)" -Condition { param($s) $s.joinStatus -ne "Failed" } | Out-Null
    $mark = Get-ServerLogMark
    Send-HarnessCommand -Instance $b -Verb mp-join -Arguments $address | Out-Null
    $s = Wait-HarnessStatus -Instance $b -TimeoutSec 60 -What "B refused ($Label)" -Condition { param($s) $s.joinStatus -eq "Failed" }
    Check ($s.lastDisconnect.reason -eq $Expected) "$Label ends Failed/$Expected (got $($s.lastDisconnect.reason): $($s.lastDisconnect.message))"
    Wait-Menu $b
    Send-HarnessCommand -Instance $b -Verb mp-ui -Arguments "ok" | Out-Null
    Check ((Get-HarnessStatus $a).joinStatus -eq "InSession") "A stays in the session while B is refused ($Label)"
    return [pscustomobject]@{ Status = $s; Mark = $mark }
}

function Shared-Dlc([string]$Name) { (@((Send-HarnessCommand -Instance $Name -Verb dump).session.serverInfo.sharedDlc) | Where-Object { $_ }) -join "," }

function Wait-SharedDlc([string]$Name, [string]$Expected) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $value = Shared-Dlc $Name
        if ($value -eq $Expected) { return $value }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $value
}

foreach ($name in $Ctx.Instances) { Wait-Menu $name }

$menuReport = Save-Report $a "menu"
Check ($menuReport.gameVersion -eq "1.0.40") "A's game version is 1.0.40 (got $($menuReport.gameVersion))"
Check (@($menuReport.ownedDlc).Count -eq 0) "A owns no DLC on the test install (got $(@($menuReport.ownedDlc) -join ','))"
$names = @($menuReport.mods | ForEach-Object { $_.name })
Check (-not ($names | Where-Object { $_ -match "Together|TogetherTestHarness" })) "the report lists neither CMS21-Together nor the harness ($($names -join ', '))"
$optimizer = $menuReport.mods | Where-Object { $_.name -match "LoadOptimizer" }
if ($optimizer) { Check ($optimizer.class -eq "Visual") "CMS21LoadOptimizer is visual (got $($optimizer.class): $($optimizer.reasons -join ', '))" }

Compat $a "dlc 1001,1002"
Connect-HarnessInstance $a
Wait-InGarage $a
Save-Report $a "garage" | Out-Null
Wait-ServerLog -Pattern "Game version pinned to 1\.0\.40" -TimeoutSec 10 | Out-Null
Check ((Wait-SharedDlc $a "1001,1002") -eq "1001,1002") "A alone: shared DLC 1001,1002"

Compat $b "game 1.0.39"
$r = Join-Refused "GameVersionMismatch" "game version 1.0.39"
Check ($r.Status.lastDisconnect.message -match "1\.0\.39" -and $r.Status.lastDisconnect.message -match "1\.0\.40") "game version message names both versions"
Wait-ServerLog -Pattern "Refusing client 2: GameVersionMismatch" -After $r.Mark -TimeoutSec 5 | Out-Null

Compat $b "reset"
Compat $b "mod-add FakeGameplay Inventory.Add"
$r = Join-Refused "ModMismatch" "gameplay mod"
Check ($r.Status.lastDisconnect.message -match "FakeGameplay" -and $r.Status.lastDisconnect.message -match "Inventory\.Add") "mod message names FakeGameplay and Inventory.Add"
Wait-ServerLog -Pattern "Refusing client 2: ModMismatch" -After $r.Mark -TimeoutSec 5 | Out-Null

Compat $b "mod-clear"
Compat $b "mod-add FakeVisual FPSCamera.Update"
Compat $b "dlc 1002,1003"
Send-HarnessCommand -Instance $b -Verb mp-join -Arguments $address | Out-Null
Wait-InGarage $b
Check ((Wait-SharedDlc $a "1002") -eq "1002") "A sees shared DLC 1002 with B in the session"
Check ((Wait-SharedDlc $b "1002") -eq "1002") "B sees shared DLC 1002"

$mark = Get-ServerLogMark
Send-ServerCommand "compat"
$line = Wait-ServerLog -Pattern "Shared DLC \(2 players\): 1002" -After $mark -TimeoutSec 10
Check ([bool]$line) "compat prints the shared DLC set"
Wait-ServerLog -Pattern "Game version: 1\.0\.40 \(pinned by the first client\)" -After $mark -TimeoutSec 5 | Out-Null
Wait-ServerLog -Pattern "client 2 '.*': ModMismatch" -After $mark -TimeoutSec 5 | Out-Null
Write-Host "ok: compat prints the pinned game version and the last refusals"

Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Wait-Menu $b
Check ((Wait-SharedDlc $a "1001,1002") -eq "1001,1002") "after B leaves, A sees shared DLC 1001,1002 again"
Compat $b "mod-add FakeGameplay Inventory.Add"

Stop-TestServer
$config = Join-Path $Ctx.ServerDir "server_config.ini"
$lines = @(Get-Content -LiteralPath $config | Where-Object { $_ -notmatch '^\s*mods_ignored\s*=' }) + "mods_ignored = FakeGameplay"
Set-Content -LiteralPath $config -Value $lines -Encoding ascii
Wait-HarnessStatus -Instance $a -TimeoutSec 60 -What "A back in the menu after the server stop" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
Send-HarnessCommand -Instance $a -Verb mp-ui -Arguments "ok" | Out-Null
Start-TestServer | Out-Null
Check ((Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") -Raw) -match "ignored \[FakeGameplay\]") "server logs mods_ignored = FakeGameplay"

$mark = Get-ServerLogMark
Connect-HarnessInstance $a
Wait-InGarage $a
Send-HarnessCommand -Instance $b -Verb mp-join -Arguments $address | Out-Null
Wait-InGarage $b
Wait-ServerLog -Pattern "FakeGameplay.*ignored by configuration" -After $mark -TimeoutSec 5 | Out-Null
Write-Host "ok: B joins with FakeGameplay ignored by configuration"

Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Wait-Menu $b
Compat $b "reset"

Send-HarnessCommand -Instance $b -Verb mp-fake-version -Arguments "0.0.1" | Out-Null
$r = Join-Refused "VersionMismatch" "mod version 0.0.1"
Check ($r.Status.lastDisconnect.message -match "0\.0\.1" -and $r.Status.lastDisconnect.message -match "you have") "version message names both versions: $($r.Status.lastDisconnect.message)"
Send-HarnessCommand -Instance $b -Verb mp-fake-version -Arguments "reset" | Out-Null

# Playtest finding 7: another build number of the same version and build kind joins when the protocol matches, so
# server-only fixes need no reinstall; a release-style version without the build label is still refused.
$own = [regex]::Match((Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt") -Raw), "Together (\d+\.\d+\.\d+)-(\w+)[^ ]* \(protocol").Groups
Check ($own.Count -eq 3 -and $own[1].Success) "the server log names A's version with a build label"
Send-HarnessCommand -Instance $b -Verb mp-fake-version -Arguments "$($own[1].Value)-$($own[2].Value).9999" | Out-Null
$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $b -Verb mp-join -Arguments $address | Out-Null
Wait-InGarage $b
Write-Host "ok: B joins as $($own[1].Value)-$($own[2].Value).9999"
Send-HarnessCommand -Instance $b -Verb to-menu | Out-Null
Wait-Menu $b
Send-HarnessCommand -Instance $b -Verb mp-fake-version -Arguments $own[1].Value | Out-Null
$r = Join-Refused "VersionMismatch" "release version $($own[1].Value)"
Send-HarnessCommand -Instance $b -Verb mp-fake-version -Arguments "reset" | Out-Null

Compat $b "protocol-sent x"
$r = Join-Refused "VersionMismatch" "sent protocol x"
Check ($r.Status.lastDisconnect.message -match "builds differ") "protocol message says the builds differ"
Wait-ServerLog -Pattern "Refusing client 2: VersionMismatch" -After $r.Mark -TimeoutSec 5 | Out-Null
Write-Host "ok: the server refused the sent protocol x"

Compat $b "reset"
Compat $b "protocol x"
$r = Join-Refused "VersionMismatch" "local protocol x"
Check ($r.Status.lastDisconnect.message -match "builds differ") "local protocol message says the builds differ"
Start-Sleep -Seconds 2
$after = @(Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "Log\Latest.txt"))
$received = @(if ($after.Count -gt $r.Mark) { $after[$r.Mark..($after.Count - 1)] | Where-Object { $_ -match "Received info:|Client\[\d\] '.*': Together" } })
Check ($received.Count -eq 0) "the server got no ConnectPacket from B after its local refusal ($($received -join ' | '))"

Compat $b "reset"
Check ((Get-HarnessStatus $a).joinStatus -eq "InSession") "A is still in the session at the end"

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

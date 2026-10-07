# areas: bugreport, resync, hosting
# desync-detection-and-resync (d): B reports a bug while not connected (client-only bundle). Then the server runs with
# a GSLT token, a password and an admin key, A and B join, B's car drifts (a desync record), A sets
# CMS21Together.AdminKey and reports: A, B and the server each write a bundle with the same id, the secrets are
# redacted everywhere, the save copy has no player keys, both stay in the session and a second report is refused.
param($Ctx)

$a, $b = $Ctx.Instances
$address = $Ctx.Lane.ConnectAddress
$car = "car_boltatlanta"
$gslt = "gslt-5d1e"; $password = "pw-7f3a"; $adminKey = "adm-91c2"
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "$Name in session in the garage" -Condition {
        param($s) $s.joinStatus -eq "InSession" -and $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $Name -Verb car-ready -Arguments "0"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader 0 not Ready"
}

function Try-ServerLog([string]$Pattern, [int]$After, [int]$TimeoutSec) {
    try { Wait-ServerLog -Pattern $Pattern -After $After -TimeoutSec $TimeoutSec } catch { $null }
}

function UserData([string]$Name) { Split-Path -Parent (Get-HarnessDir $Name) }

function Wait-File([string]$Path, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while (-not (Test-Path -LiteralPath $Path) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    Test-Path -LiteralPath $Path
}

function Open-Bundle([string]$Zip, [string]$Label) {
    $into = Join-Path $Ctx.RunDir "bugreport_$Label"
    if (Test-Path -LiteralPath $into) { Remove-Item -LiteralPath $into -Recurse -Force }
    Copy-Item -LiteralPath $Zip -Destination $Ctx.RunDir
    Expand-Archive -LiteralPath $Zip -DestinationPath $into -Force
    return $into
}

function Has([string]$Root, [string]$Relative) { Test-Path -LiteralPath (Join-Path $Root $Relative) }

function Find-Text([string]$Root, [string[]]$Values) {
    @(Get-ChildItem -LiteralPath $Root -Recurse -File | Select-String -SimpleMatch -Pattern $Values | ForEach-Object { "$($_.Filename): $($_.Line)" })
}

function Check-ClientFiles([string]$Root, [string]$Who, [bool]$WithState) {
    foreach ($file in "client\info.json", "client\MelonLoader\Latest.log", "client\MelonPreferences.cfg", "client\files.txt", "client\mods.json", "client\guard.log") {
        Check (Has $Root $file) "$Who's bundle has $file"
    }
    Check (@(Get-ChildItem -LiteralPath (Join-Path $Root "client\MelonLoader\Logs") -Filter "*.log" -ErrorAction SilentlyContinue).Count -ge 1) "$Who's bundle has MelonLoader\Logs\*.log"
    Check (@(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter "player.json").Count -eq 0) "$Who's bundle has no player.json"
    Check (-not (Has $Root "client\errors.txt")) "$Who's bundle has no errors.txt"
    if ($WithState) {
        foreach ($key in "world", "inventory", "car-placement", "cars") { Check (Has $Root "client\state\$key.json") "$Who's bundle has state\$key.json" }
    } else {
        Check (-not (Has $Root "client\state")) "$Who's offline bundle has no state"
    }
}

$prefsPath = Join-Path (UserData $a) "MelonPreferences.cfg"
$originalAdminLine = $null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$playerKeys = @()
foreach ($name in $Ctx.Instances) {
    $identity = Join-Path (UserData $name) "CMS21Together\player.json"
    if (Test-Path -LiteralPath $identity) {
        $playerKeys += @((Get-Content -LiteralPath $identity -Raw | ConvertFrom-Json).PSObject.Properties | Where-Object { $_.Value -is [string] -and $_.Value.Length -ge 4 } | ForEach-Object { $_.Value })
    }
}

try {
    foreach ($name in $Ctx.Instances) {
        Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
    }

    # Not connected: client-only bundle.
    $offline = Send-HarnessCommand -Instance $b -Verb bug-report
    Check ($offline.id -match '^\d{8}-\d{6}-[0-9a-f]{4}$' -and -not $offline.connected) "B's offline report starts ($($offline.id), connected $($offline.connected))"
    Check (Wait-File $offline.path) "B's offline bundle is written ($($offline.path))"
    if (Test-Path -LiteralPath $offline.path) {
        $root = Open-Bundle $offline.path "offline_B"
        Check-ClientFiles $root "B (offline)" $false
        $info = Get-Content -LiteralPath (Join-Path $root "client\info.json") -Raw | ConvertFrom-Json
        Check ($info.id -eq $offline.id -and $info.connection.serverPart -eq $false) "B's offline info.json has the id and no server part"
    }
    Start-Sleep -Seconds 1
    $clientLogB = Get-Content -LiteralPath (Join-Path (Split-Path -Parent (UserData $b)) "MelonLoader\Latest.log") -Raw
    Check ($clientLogB -match "Bug report $([regex]::Escape($offline.id)) saved: .*no server part") "B was told the offline report has no server part"

    Stop-TestServer
    $config = Join-Path $Ctx.ServerDir "server_config.ini"
    $lines = @(Get-Content -LiteralPath $config | Where-Object { $_ -notmatch '^\s*(gslt_token|password|admin_key)\s*=' })
    $lines += "GSLT_Token = `"$gslt`"", "password = $password", "admin_key = $adminKey"
    Set-Content -LiteralPath $config -Value $lines -Encoding ascii
    Start-TestServer | Out-Null

    Send-HarnessCommand -Instance $a -Verb mp-join -Arguments "$address password=$password" | Out-Null
    Wait-InGarage $a
    Send-HarnessCommand -Instance $b -Verb mp-join -Arguments "$address password=$password" | Out-Null
    Wait-InGarage $b
    foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }

    Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "0 $car 0" | Out-Null
    Wait-Ready $a | Out-Null
    Wait-Ready $b | Out-Null
    Start-Sleep -Seconds 2
    $mark = Get-ServerLogMark
    $corrupt = Send-HarnessCommand -Instance $b -Verb part-corrupt -Arguments "0"
    $repair = Try-ServerLog "\[Desync\] cars:0 .*resending" $mark 60
    Check ([bool]$repair) "B's corrupted part $($corrupt.key) left a desync record ($repair)"

    $prefs = [System.IO.File]::ReadAllText($prefsPath)
    $originalAdminLine = [regex]::Match($prefs, '(?m)^AdminKey = .*$').Value.TrimEnd("`r")
    [System.IO.File]::WriteAllText($prefsPath, [regex]::Replace($prefs, '(?m)^AdminKey = .*?(\r?)$', "AdminKey = `"$adminKey`"`$1"), $utf8)

    # Connected: A reports, the server and B join in.
    $mark = Get-ServerLogMark
    $report = Send-HarnessCommand -Instance $a -Verb bug-report
    $id = $report.id
    Check ($id -match '^\d{8}-\d{6}-[0-9a-f]{4}$' -and $report.connected) "A's report starts while connected ($id)"
    $serverZip = Join-Path $Ctx.ServerDir "BugReports\$id.zip"
    $zipB = Join-Path (UserData $b) "CMS21Together\BugReports\$id.zip"
    Check (Wait-File $report.path) "A's bundle is written ($($report.path))"
    Check (Wait-File $zipB) "B's bundle with the same id is written ($zipB)"
    Check (Wait-File $serverZip) "the server's bundle with the same id is written ($serverZip)"
    Check ([bool](Try-ServerLog "\[BugReport\] $id written to BugReports/$id\.zip" $mark 30)) "the server logs its bundle"

    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 500; $row = @(Send-HarnessCommand -Instance $a -Verb bug-report -Arguments "list") | Where-Object { $_.id -eq $id } | Select-Object -First 1 } while (-not ($row.done -and $row.serverFile) -and (Get-Date) -lt $deadline)
    Check ($row.written -and $row.serverFile -eq "BugReports/$id.zip") "A's report is written and names the server file ($($row.serverFile))"
    Check ($row.frames -ge 1 -and $row.longestFrameMs -lt 1000) "A kept rendering while writing ($($row.frames) frames, longest $([math]::Round($row.longestFrameMs)) ms, write $([math]::Round($row.writeMs)) ms, capture $([math]::Round($row.captureMs)) ms)"
    $Ctx.Result.notes += "A bundle: capture $([math]::Round($row.captureMs)) ms, write $([math]::Round($row.writeMs)) ms, longest frame $([math]::Round($row.longestFrameMs)) ms over $($row.frames) frames, $([math]::Round($row.bytes / 1024)) KB"

    $secretValues = @($gslt, $password, $adminKey) + $playerKeys
    $roots = @()
    if (Test-Path -LiteralPath $report.path) {
        $rootA = Open-Bundle $report.path "A"; $roots += $rootA
        Check-ClientFiles $rootA "A" $true
        $infoA = Get-Content -LiteralPath (Join-Path $rootA "client\info.json") -Raw | ConvertFrom-Json
        Check ($infoA.id -eq $id -and $infoA.origin -eq "reporter" -and $infoA.connection.serverPart -eq $true) "A's info.json: id, reporter, server part"
        $prefsA = Get-Content -LiteralPath (Join-Path $rootA "client\MelonPreferences.cfg") -Raw
        Check ($prefsA -match 'AdminKey = "<redacted>"' -and $prefsA -match 'ResyncHotkey = "F7"') "A's preferences show AdminKey redacted and ResyncHotkey kept"
    }
    if (Test-Path -LiteralPath $zipB) {
        $rootB = Open-Bundle $zipB "B"; $roots += $rootB
        Check-ClientFiles $rootB "B" $true
        $infoB = Get-Content -LiteralPath (Join-Path $rootB "client\info.json") -Raw | ConvertFrom-Json
        Check ($infoB.id -eq $id -and $infoB.origin -eq "collect") "B's info.json has the same id and origin collect"
    }
    if (Test-Path -LiteralPath $serverZip) {
        $rootServer = Open-Bundle $serverZip "server"; $roots += $rootServer
        foreach ($file in "server\info.json", "server\Log\Latest.txt", "server\server_config.ini", "server\save.json", "server\players.json", "server\state\world.json", "server\state\cars.json") {
            Check (Has $rootServer $file) "the server bundle has $file"
        }
        Check (@(Get-ChildItem -LiteralPath (Join-Path $rootServer "server\Log\desync") -Filter "*.json" -ErrorAction SilentlyContinue).Count -ge 1) "the server bundle has the desync record"
        $infoS = Get-Content -LiteralPath (Join-Path $rootServer "server\info.json") -Raw | ConvertFrom-Json
        Check ($infoS.id -eq $id -and @($infoS.askedSlots).Count -eq 1) "the server info.json has the id and asked one other player"
        $configS = Get-Content -LiteralPath (Join-Path $rootServer "server\server_config.ini") -Raw
        Check ($configS -match 'GSLT_Token = "<redacted>"' -and $configS -match '(?m)^password = <redacted>' -and $configS -match '(?m)^admin_key = <redacted>') "the bundled config shows the token, password and admin key as <redacted>"
        $save = Get-Content -LiteralPath (Join-Path $rootServer "server\save.json") -Raw | ConvertFrom-Json
        $playersText = if ($save.Sections.players) { $save.Sections.players | ConvertTo-Json -Depth 20 -Compress } else { "" }
        Check (-not ($playersText -match '"Key"\s*:')) "save.json has no players[].Key"
        $playersJson = Get-Content -LiteralPath (Join-Path $rootServer "server\players.json") -Raw | ConvertFrom-Json
        Check ($playersJson.Count -eq 2) "players.json lists both players ($($playersJson.Count))"
    }
    $leaks = @(foreach ($root in $roots) { Find-Text $root $secretValues })
    Check ($leaks.Count -eq 0) "no secret or player key appears in any bundle ($($leaks -join ' | '))"

    foreach ($name in $Ctx.Instances) { Check ((Get-HarnessStatus $name).joinStatus -eq "InSession") "$name stayed in the session" }
    $again = Send-HarnessCommand -Instance $a -Verb bug-report
    Check ("$again" -match "^Wait \d+ s") "a second report within 30 s is refused ($again)"

    $mark = Get-ServerLogMark
    Send-ServerCommand "bugreport"
    Check ([bool](Try-ServerLog ([regex]::Escape("$id.zip")) $mark 10)) "the bugreport command lists the bundle"
}
finally {
    if ($originalAdminLine) {
        $prefs = [System.IO.File]::ReadAllText($prefsPath)
        [System.IO.File]::WriteAllText($prefsPath, [regex]::Replace($prefs, '(?m)^AdminKey = .*?(\r?)$', "$originalAdminLine`$1"), $utf8)
    }
    foreach ($folder in (Join-Path $Ctx.ServerDir "BugReports"), (Join-Path $Ctx.ServerDir "Log\desync"), (Join-Path (UserData $a) "CMS21Together\BugReports"), (Join-Path (UserData $b) "CMS21Together\BugReports")) {
        Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

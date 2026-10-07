#Requires -Version 5.1
<#
Server-only checks of save robustness (no game needed): crash-safe writes, backups, start copies, fallback,
quarantine, refusal, --check-save and saving on /stop and window close. Runs in its own copy of the server
(CMS21-TestInstalls\ServerSaveTest, port 7807) built by Deploy-Mod.ps1 from this worktree.
#>
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Import-Module (Join-Path $PSScriptRoot "HarnessClient.psm1") -Force

$serverDir = "$env:USERPROFILE\CMS21-TestInstalls\ServerSaveTest"
$exe = Join-Path $serverDir "CMS21_Together_Server.exe"
$saves = Join-Path $serverDir "Saves"
$main = Join-Path $saves "server_save.json"
$backups = Join-Path $saves "backups"
$commandFile = Join-Path $serverDir "commands.txt"
$fixture = Join-Path $PSScriptRoot "fixtures\server_save_v1.json"
$results = [ordered]@{}

if (Test-Path -LiteralPath $serverDir) { Remove-Item -LiteralPath $serverDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $serverDir | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo "CMS21-Together-Server\bin\$Configuration") |
    Where-Object { $_.Name -notin @("Log", "server_config.ini", "Saves") } | Copy-Item -Destination $serverDir -Recurse -Force

function Set-Config([int]$Autosave = 300) {
    Set-Content -LiteralPath (Join-Path $serverDir "server_config.ini") -Encoding ascii -Value @(
        "max_players = 4", "use_steam = False", 'GSLT_Token = ""', "log_level = 1", "port = 7807",
        "autosave_interval_seconds = $Autosave", "backup_count = 5")
}

Initialize-TestServer -ServerDir $serverDir -CommandFile $commandFile -ConnectAddress "127.0.0.1:7807"

function Get-LogText { Get-Content -LiteralPath (Join-Path $serverDir "Log\Latest.txt") -Raw -ErrorAction SilentlyContinue }

function Start-Raw {
    Remove-Item -LiteralPath (Join-Path $serverDir "Log\Latest.txt") -ErrorAction SilentlyContinue
    Start-Process -FilePath $exe -WorkingDirectory $serverDir -WindowStyle Minimized -ArgumentList @("--command-file", "`"$commandFile`"") -PassThru
}

function Wait-Log([string]$Pattern, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ((Get-LogText) -match $Pattern) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

function Get-DirState([string]$Dir) {
    if (-not (Test-Path -LiteralPath $Dir)) { return "" }
    (Get-ChildItem -LiteralPath $Dir -Recurse -File | Sort-Object FullName |
        ForEach-Object { "{0}|{1}" -f $_.FullName, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA1).Hash }) -join "`n"
}

function Count-Saves { ([regex]::Matches((Get-LogText), "Session successfully saved")).Count }

function Reset-Saves { Stop-TestServer; if (Test-Path -LiteralPath $saves) { Remove-Item -LiteralPath $saves -Recurse -Force } }

try {
    # 3.1 / 3.2: unchanged state is not rewritten by autosave
    Reset-Saves; Set-Config -Autosave 1
    Start-TestServer | Out-Null
    Start-Sleep -Seconds 5
    $results["autosave writes only when changed"] = ((Count-Saves) -eq 1)
    Send-ServerCommand "money add 5"
    Start-Sleep -Seconds 3
    $results["autosave writes after a change"] = ((Count-Saves) -eq 2)

    # 3.1: kill during autosave, every restart loads
    $killOk = $true
    for ($i = 1; $i -le 10; $i++) {
        Send-ServerCommand "money add 1"
        Start-Sleep -Milliseconds (900 + (Get-Random -Maximum 400))
        Stop-TestServer
        Start-TestServer | Out-Null
        if ((Get-LogText) -notmatch "Game session loaded from .*server_save\.json") { $killOk = $false; Write-Host "restart $i did not load the main save" }
    }
    $results["10 kills during autosave, every restart loads main"] = $killOk
    $results["no quarantine after kills"] = -not (Test-Path -LiteralPath (Join-Path $saves "corrupt"))

    # 3.3: start copies, keep 3
    Stop-TestServer; Set-Config -Autosave 0
    for ($i = 1; $i -le 4; $i++) { Start-TestServer | Out-Null; Stop-TestServer; Start-Sleep -Milliseconds 1100 }
    $results["start copies keep 3"] = (@(Get-ChildItem -LiteralPath $backups -Filter "start_*.json").Count -eq 3)

    # 3.4: truncated main + valid bak1 -> loads bak1, quarantines main, saves a new main
    Reset-Saves; Set-Config -Autosave 0
    Start-TestServer | Out-Null; Send-ServerCommand "money set 7777"; Send-ServerCommand "save"
    Wait-ServerLog -Pattern "money|Money" -TimeoutSec 10 | Out-Null; Start-Sleep -Seconds 1
    Send-ServerCommand "save"; Start-Sleep -Seconds 1; Stop-TestServer
    $text = Get-Content -LiteralPath $main -Raw
    Set-Content -LiteralPath $main -Value $text.Substring(0, [int]($text.Length / 2)) -Encoding utf8 -NoNewline
    Start-TestServer | Out-Null
    $log = Get-LogText
    $results["truncated main falls back to bak1"] = ($log -match "Loaded the fallback .*server_save_bak1\.json")
    $results["truncated main quarantined"] = (@(Get-ChildItem -LiteralPath (Join-Path $saves "corrupt") -Filter "server_save_*.json").Count -eq 1)
    $results["fallback saved as new main"] = ((Get-Content -LiteralPath $main -Raw) -match '"Money": 7777')
    Stop-TestServer

    # 3.4: every file garbage -> refuses, accepts no connection
    Reset-Saves; New-Item -ItemType Directory -Force -Path $backups | Out-Null
    Set-Content -LiteralPath $main -Value "garbage" -Encoding ascii
    Set-Content -LiteralPath (Join-Path $backups "server_save_bak1.json") -Value "{ nope" -Encoding ascii
    $p = Start-Raw
    $refused = Wait-Log "the server will not start" 30
    $listening = [bool](Get-NetTCPConnection -LocalPort 7807 -State Listen -ErrorAction SilentlyContinue)
    $results["all garbage refuses to start"] = ($refused -and -not $listening)
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500

    # 3.4: SaveVersion 99 -> refuses, nothing moved or written
    Reset-Saves; New-Item -ItemType Directory -Force -Path $saves | Out-Null
    Set-Content -LiteralPath $main -Value '{ "SaveVersion": 99, "Sections": {} }' -Encoding ascii
    $before = Get-DirState $saves
    $p = Start-Raw
    $refused = Wait-Log "newer than this server supports" 30
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500
    $results["newer version refuses, no file touched"] = ($refused -and (Get-DirState $saves) -eq $before)

    # 3.5: --check-save
    $out = & $exe --check-save $fixture | Out-String
    $results["check-save fixture exit 0"] = ($LASTEXITCODE -eq 0 -and $out -match "money 23456, level 11" -and $out -match "inventory: 3 items, 1 groups, warehouse 1 items" -and $out -match "cars: 0 loaded" -and $out -match "Migrated section 'cars' v1 -> v2" -and $out -match "Dropping car_dnb_censor")
    Write-Host $out
    $garbage = Join-Path $serverDir "garbage.json"; Set-Content -LiteralPath $garbage -Value "nope" -Encoding ascii
    & $exe --check-save $garbage | Out-Null
    $results["check-save garbage exit 1"] = ($LASTEXITCODE -eq 1)

    # 3.6: /stop saves before exit
    Reset-Saves; Set-Config -Autosave 0
    Start-TestServer | Out-Null; Send-ServerCommand "money set 4242"
    Start-Sleep -Seconds 1
    $stamp = (Get-Item -LiteralPath $main).LastWriteTimeUtc
    Send-ServerCommand "stop"
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Process -Name "CMS21_Together_Server" -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $exe }) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    $results["stop saves and exits"] = ((Get-Item -LiteralPath $main).LastWriteTimeUtc -gt $stamp -and (Get-Content -LiteralPath $main -Raw) -match '"Money": 4242')

    # 3.6: closing the console window saves
    Start-TestServer | Out-Null; Send-ServerCommand "money set 5151"
    Start-Sleep -Seconds 1
    $proc = Get-Process -Name "CMS21_Together_Server" | Where-Object { $_.Path -ieq $exe }
    [void]$proc.CloseMainWindow()
    $deadline = (Get-Date).AddSeconds(15)
    while (-not $proc.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    $results["window close saves"] = ((Get-Content -LiteralPath $main -Raw) -match '"Money": 5151')
}
finally {
    Stop-TestServer
}

$failed = @($results.GetEnumerator() | Where-Object { -not $_.Value })
$results.GetEnumerator() | ForEach-Object { Write-Host ("{0,-6} {1}" -f $(if ($_.Value) { "ok" } else { "FAIL" }), $_.Key) }
Write-Host ("RESULT server-saves: {0}" -f $(if ($failed.Count -eq 0) { "PASSED" } else { "FAILED ($($failed.Count))" }))
if ($failed.Count -gt 0) { exit 1 } else { exit 0 }

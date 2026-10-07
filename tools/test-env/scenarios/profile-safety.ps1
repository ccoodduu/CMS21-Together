# run-all: fresh
# A multiplayer session never writes the player's own profiles: the save folder and the selected-profile
# pref are unchanged after connect, a menu exit with saving, a reconnect and quit; each install made a backup.
param($Ctx)

Import-Module (Join-Path $PSScriptRoot "..\TestLanes.psm1") -Force
$a, $b = $Ctx.Instances

function Get-SaveFolderHashes([string]$Name) {
    $dir = Get-InstanceSaveDir $Name
    Get-ChildItem -LiteralPath $dir -Recurse -File | Where-Object { $_.Name -notlike "Player*.log" } | Sort-Object FullName |
        ForEach-Object { "{0}|{1}" -f $_.FullName.Substring($dir.Length), (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA1).Hash }
}

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-InMenu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

foreach ($name in $Ctx.Instances) { Wait-InMenu $name }

$before = @{}
$prefBefore = @{}
foreach ($name in $Ctx.Instances) {
    $before[$name] = (Get-SaveFolderHashes $name) -join "`n"
    $prefBefore[$name] = Send-HarnessCommand -Instance $name -Verb profile-pref
    Write-Host "$name pref before: $($prefBefore[$name] | ConvertTo-Json -Compress)"
}

foreach ($name in $Ctx.Instances) { Connect-HarnessInstance $name; Wait-InGarage $name }
$during = Send-HarnessCommand -Instance $a -Verb profile-pref
Write-Host "$a pref during session: $($during | ConvertTo-Json -Compress)"
$sessionFailures = @()
if ($during.selected -ne 4) { $sessionFailures += "$a uses profile $($during.selected) ($($during.selectedName)) during the session, expected slot 4" }
if ($during.pref -ne $prefBefore[$a].pref) { $sessionFailures += "$a selectedProfile pref is $($during.pref) during the session, was $($prefBefore[$a].pref)" }

Send-HarnessCommand -Instance $a -Verb to-menu -Arguments "save" | Out-Null
Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
Connect-HarnessInstance $a
Wait-InGarage $a
Start-Sleep -Seconds 3

foreach ($name in $Ctx.Instances) {
    Send-HarnessCommand -Instance $name -Verb to-menu | Out-Null
    Wait-InMenu $name
}
$failures = @($sessionFailures)
foreach ($name in $Ctx.Instances) {
    $after = Send-HarnessCommand -Instance $name -Verb profile-pref
    Write-Host "$name pref after: $($after | ConvertTo-Json -Compress)"
    if ($after.pref -ne $prefBefore[$name].pref -or $after.field -ne $prefBefore[$name].field -or $after.profileSlots -ne $prefBefore[$name].profileSlots) {
        $failures += "$name profile selection changed: before $($prefBefore[$name] | ConvertTo-Json -Compress), after $($after | ConvertTo-Json -Compress)"
    }
}

foreach ($name in $Ctx.Instances) {
    try { Send-HarnessCommand -Instance $name -Verb quit -TimeoutSec 5 | Out-Null } catch { }
}
Start-Sleep -Seconds 15

foreach ($name in $Ctx.Instances) {
    $now = (Get-SaveFolderHashes $name) -join "`n"
    if ($now -ne $before[$name]) {
        $failures += "$name save folder changed:`n" + ((Compare-Object ($before[$name] -split "`n") ($now -split "`n") | ForEach-Object { "  $($_.SideIndicator) $($_.InputObject)" }) -join "`n")
    }
    $backups = Join-Path $env:USERPROFILE "CMS21-TestInstalls\$name\UserData\CMS21Together\ProfileBackups"
    if (-not (Test-Path -LiteralPath $backups) -or -not (Get-ChildItem -LiteralPath $backups -Directory)) { $failures += "$name made no profile backup" }
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

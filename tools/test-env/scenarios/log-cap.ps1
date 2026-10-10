# areas: bugreport
# MelonLoader stops writing warnings and errors to Latest.log after 100 of each per game process unless the game is
# started with --melonloader.maxwarnings 0 / --melonloader.maxerrors 0. In a batch the games run through many
# scenarios, so a scenario late in the batch that reads a warning from the client log missed it (desync-autofix,
# regression 2026-10-10). Here every client logs 150 warnings and 150 errors; the last of each must reach Latest.log.
param($Ctx)

$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

$tag = "cap$(Get-Date -Format 'HHmmssfff')"
foreach ($name in $Ctx.Instances) {
    Send-HarnessCommand -Instance $name -Verb log-burst -Arguments "150 warn $tag" | Out-Null
    Send-HarnessCommand -Instance $name -Verb log-burst -Arguments "150 error $tag" | Out-Null
}
Start-Sleep -Seconds 1

foreach ($name in $Ctx.Instances) {
    $log = Get-Content -LiteralPath (Join-Path $env:USERPROFILE "CMS21-TestInstalls\$name\MelonLoader\Latest.log")
    $warnings = @($log | Where-Object { $_ -match "\[WARNING\] \[LogBurst\] $tag " }).Count
    $errors = @($log | Where-Object { $_ -match "\[ERROR\] \[LogBurst\] $tag " }).Count
    Check ($warnings -eq 150) "all 150 of $name's warnings reach Latest.log ($warnings)"
    Check ($errors -eq 150) "all 150 of $name's errors reach Latest.log ($errors)"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

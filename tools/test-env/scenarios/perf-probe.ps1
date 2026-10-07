# run-all: skip
# areas: connect
# multiplayer-soak-and-scale groups 2-3 smoke check: the server's perf log, perf command and snapshot ack line, and
# the client perf/fps-cap verbs. The perf log interval is set for this run only (Run-Session restores the config).
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

$config = Join-Path $Ctx.ServerDir "server_config.ini"
$text = Get-Content -LiteralPath $config -Raw
if ($text -match "(?m)^perf_log_interval_seconds\s*=") { $text = $text -replace "(?m)^perf_log_interval_seconds\s*=.*$", "perf_log_interval_seconds = 5" }
else { $text += "`r`nperf_log_interval_seconds = 5`r`n" }
Set-Content -LiteralPath $config -Value $text -NoNewline -Encoding utf8
Stop-TestServer
$started = Get-Date
Start-TestServer | Out-Null

foreach ($name in $a, $b) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
$mark = Get-ServerLogMark
foreach ($name in $a, $b) {
    Connect-HarnessInstance $name
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
}
$ack = try { Wait-ServerLog -Pattern "snapshot \S+ acked after \d+ ms, \d+ bytes" -After $mark -TimeoutSec 20 } catch { $null }
Check ([bool]$ack) "the server logs the snapshot ack with time and bytes ($ack)"

for ($i = 0; $i -lt 10; $i++) { Send-HarnessCommand -Instance $a -Verb stats-add -Arguments "1 1" | Out-Null }
Start-Sleep -Seconds 2
$mark = Get-ServerLogMark
Send-ServerCommand "perf top 10"
Send-ServerCommand "save"
Start-Sleep -Seconds 2
Send-ServerCommand "perf top 10"
Start-Sleep -Seconds 3
$serverLog = Get-ChildItem -LiteralPath (Join-Path $Ctx.ServerDir "Log") -Filter "Log_*.txt" | Sort-Object LastWriteTime | Select-Object -Last 1
$lines = @(Get-Content -LiteralPath $serverLog.FullName | Select-Object -Skip $mark)
$lines | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "perf_command_output.txt") -Encoding utf8
Check (@($lines | Where-Object { $_ -match "StatsAction|stats" }).Count -ge 1) "perf top lists the stats handler"
Check (@($lines | Where-Object { $_ -match "save-build" }).Count -ge 1) "perf top lists save-build after a save"

Send-HarnessCommand -Instance $a -Verb fps-cap -Arguments "30" | Out-Null
Start-Sleep -Seconds 12
$perf = Send-HarnessCommand -Instance $a -Verb perf
Write-Host "A perf: $($perf | ConvertTo-Json -Compress)"
$Ctx.Result.notes += "A perf at fps-cap 30: $($perf | ConvertTo-Json -Compress)"
Check ($perf.avgMs -ge 25 -and $perf.avgMs -le 45) "A's frame time is about 33 ms at fps-cap 30 ($($perf.avgMs))"

$perfLog = Get-ChildItem -LiteralPath (Join-Path $Ctx.ServerDir "Log") -Filter "perf_*.jsonl" -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $started } | Sort-Object LastWriteTime | Select-Object -Last 1
$entries = if ($perfLog) { @(Get-Content -LiteralPath $perfLog.FullName) } else { @() }
Check ($entries.Count -ge 3) "the perf log has a line every 5 s ($($entries.Count) lines)"
if ($entries.Count -gt 0) { $Ctx.Result.notes += "last perf line: $($entries[-1])" }

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

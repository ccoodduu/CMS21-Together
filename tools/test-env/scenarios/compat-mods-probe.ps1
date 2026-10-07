# run-all: skip
# areas: connect
# Probe for real mods copied into A's Mods folder by hand (mod-compatibility task 7.1): records A's mod report in the
# menu, the server's verdict and the refusal A is shown, and checks each known mod's class against $expected. Then
# the server ignores every flagged mod, A joins and the report is taken again in the garage, so patches a mod applies
# only after the menu show up as a difference. Remove the copied mods from A afterwards.
param($Ctx)

$a = $Ctx.Instances[0]
$address = $Ctx.Lane.ConnectAddress

$expected = [ordered]@{
    "Autosave Mod"          = "Gameplay"
    "CMS21 Load Optimizer"  = "Visual"
    "Lvx Better Car Spawns" = "Gameplay"
    "Lvx Owned Cars Only"   = "Gameplay"
    "QoLmod"                = "Gameplay"
    "QuickShop"             = "Gameplay"
    "TK Aftermarket"        = "Gameplay"
    "TK Basics"             = "Gameplay"
}

$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-Menu([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 120 -What "menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
}

function Save-Report([string]$Label) {
    $report = Send-HarnessCommand -Instance $a -Verb compat-report -Arguments "patches"
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "compat-report_${Label}_$a.json") -Encoding utf8
    $lines = foreach ($mod in $report.mods) {
        "{0} {1} [{2}] known: {3}; {4} targets: {5}" -f $mod.name, $mod.version, $mod.class, $mod.knownReason, @($mod.targets).Count, (@($mod.reasons) -join ", ")
    }
    $lines | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "verdicts_$Label.txt") -Encoding utf8
    $lines | ForEach-Object { Write-Host "  $_" }
    return $report
}

foreach ($name in $Ctx.Instances) { Wait-Menu $name }

$menu = Save-Report "menu"
foreach ($name in $expected.Keys) {
    $mod = $menu.mods | Where-Object { $_.name -eq $name }
    if (-not $mod) { Check $false "$name is loaded in $a (copy it into $a's Mods folder first)"; continue }
    Check ($mod.class -eq $expected[$name]) "$name is $($expected[$name]) (got $($mod.class): $(@($mod.reasons) -join ', '))"
}
foreach ($mod in $menu.mods | Where-Object { -not $expected.Contains($_.name) }) {
    $Ctx.Result.notes += "not in the expected table: $($mod.name) [$($mod.class)]"
}

$mark = Get-ServerLogMark
Send-HarnessCommand -Instance $a -Verb mp-join -Arguments $address | Out-Null
$s = Wait-HarnessStatus -Instance $a -TimeoutSec 60 -What "A refused" -Condition { param($s) $s.joinStatus -eq "Failed" -or $s.connectionValid }
$serverLine = Wait-ServerLog -Pattern "Client\[\d+\] '.*': Together" -After $mark -TimeoutSec 10
Set-Content -LiteralPath (Join-Path $Ctx.RunDir "refusal_$a.txt") -Encoding utf8 -Value @(
    "reason: $($s.lastDisconnect.reason)", "message:", $s.lastDisconnect.message, "", "server:", $serverLine)
Write-Host "Refusal shown to ${a}: $($s.lastDisconnect.reason)`n$($s.lastDisconnect.message)"
Check ($s.lastDisconnect.reason -eq "ModMismatch") "$a is refused with ModMismatch (got $($s.lastDisconnect.reason))"
$flagged = @($menu.mods | Where-Object { $_.class -in @("Gameplay", "Unknown") } | ForEach-Object { $_.name })
foreach ($name in $flagged) {
    Check ($s.lastDisconnect.message -match [regex]::Escape($name)) "the refusal names $name"
}
Check ($s.lastDisconnect.message -match "remove (this mod|these mods) from the game's Mods folder") "the refusal says what to do"
Wait-Menu $a
Send-HarnessCommand -Instance $a -Verb mp-ui -Arguments "ok" | Out-Null

Stop-TestServer
$config = Join-Path $Ctx.ServerDir "server_config.ini"
$lines = @(Get-Content -LiteralPath $config | Where-Object { $_ -notmatch '^\s*mods_ignored\s*=' }) + "mods_ignored = $($flagged -join ', ')"
Set-Content -LiteralPath $config -Value $lines -Encoding ascii
Start-TestServer | Out-Null

try {
    Connect-HarnessInstance $a
    Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
    $garage = Save-Report "garage"
    foreach ($mod in $garage.mods) {
        $before = $menu.mods | Where-Object { $_.name -eq $mod.name }
        $added = @($mod.targets | Where-Object { $before -eq $null -or @($before.targets) -notcontains $_ })
        if ($before -eq $null -or $before.class -ne $mod.class -or $added.Count -gt 0) {
            $Ctx.Result.notes += "garage differs from menu: $($mod.name) [$($before.class) -> $($mod.class)], new targets: $($added -join ', ')"
        }
    }
    Send-HarnessCommand -Instance $a -Verb to-menu | Out-Null
    Wait-Menu $a
}
catch {
    $Ctx.Result.notes += "garage phase: $($_.Exception.Message)"
    Write-Host "garage phase failed: $($_.Exception.Message)" -ForegroundColor Yellow
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: economy, outdoor
# Soak 2026-10-08 (rule 4 on client D at 09:48:01 and 11:20:25): an inventory change of another player that arrived
# while D was loading the junkyard threw in InventoryGroupItemAction/InventoryItemAction. A travels to the junkyard
# and back while B adds items and groups; A must log no handler error and end with B's inventory.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += $Message; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Give-During([string]$Destination, [string]$SceneName) {
    $log = Join-Path $env:USERPROFILE "CMS21-TestInstalls\$a\MelonLoader\Latest.log"
    $mark = (Get-Item -LiteralPath $log).Length
    Cmd $a travel $Destination | Out-Null
    $given = 0
    $deadline = (Get-Date).AddSeconds(90)
    do {
        if ($given % 2 -eq 0) { Cmd $b give-item "akumulator 0.5" | Out-Null } else { Cmd $b give-group "wheel" | Out-Null }
        $given++
        Start-Sleep -Milliseconds 150
        $status = try { Get-HarnessStatus $a } catch { $null }
    } while (-not ($status -and $status.scene -eq $SceneName -and $status.playable) -and (Get-Date) -lt $deadline)
    Start-Sleep -Seconds 3
    $stream = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try { $stream.Position = $mark; $text = (New-Object System.IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Dispose() }
    $errors = @($text -split "\r?\n" | Where-Object { $_ -match "Error in handler Inventory|Error reading packet" })
    Check ($errors.Count -eq 0) "A travelled to $Destination while B added $given items and groups: no inventory handler error on A ($($errors -join ' | '))"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "Off" | Out-Null }

Give-During "Junkyard" "Junkyard"
Give-During "Garage" "garage"
Wait-InGarage $a
try {
    Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("inventory") -TimeoutSec 20 | Out-Null
    Check $true "A and B have the same inventory after the trip"
} catch {
    Check $false "A and B have the same inventory after the trip ($($_.Exception.Message))"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: economy
# economy-audit 1.2 (spike): A alone, connected, drives every money path once with econ-trace on. The trace report
# (every mutator call with the traced caller that was running), the economy ledger and the guard log go to the run
# folder for design.md "Runtime trace results". Steps that fail are noted, not failed: this run collects facts.
param($Ctx)

$a = $Ctx.Instances[0]
$car = "car_boltatlanta"
$notes = @()

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([int]$Loader) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Send-HarnessCommand -Instance $a -Verb car-ready -Arguments "$Loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $true }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Step([string]$Verb, [string]$Arguments = "", [int]$Settle = 3) {
    try {
        $r = Send-HarnessCommand -Instance $a -Verb $Verb -Arguments $Arguments
        Write-Host "$Verb $Arguments -> $($r | ConvertTo-Json -Compress -Depth 4)"
        $script:notes += "$Verb $Arguments`: $($r | ConvertTo-Json -Compress -Depth 4)"
        Start-Sleep -Seconds $Settle
        return $r
    } catch {
        Write-Host "$Verb $Arguments failed: $($_.Exception.Message)" -ForegroundColor Yellow
        $script:notes += "$Verb $Arguments failed: $($_.Exception.Message)"
        return $null
    }
}

function Back-To-Garage {
    try { Wait-HarnessStatus -Instance $a -TimeoutSec 90 -What "A left the garage" -Condition { param($s) $s.scene -ne "garage" -and $s.playable } | Out-Null } catch { return }
    Step "travel" "Garage" 1 | Out-Null
    Wait-InGarage $a
    Wait-Ready 0 | Out-Null
    Wait-Ready 1 | Out-Null
}

function Add([string]$Id) { (Step "inv-add-local" $Id 1).uid }

Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Step "guard-set" "enforce" 0 | Out-Null
foreach ($key in "Scene:Auction", "Scene:Barn", "Window:Paintshop", "Window:Tinting", "Window:RepairPart", "Pie:equipment_use", "Pie:paintshop_car") {
    Step "guard-allow" $key 0 | Out-Null
}
Send-ServerCommand "money set 200000"
Step "stats-add" "500 0" 1 | Out-Null
Step "econ-trace" "on" 0 | Out-Null

$cases = @(1..5 | ForEach-Object { Add "specialCase" })
$map = Add "specialMap"
$parts = @(1..3 | ForEach-Object { Add "tuleja_1" })
Step "car-spawn" "0 $car 0" 0 | Out-Null
Step "car-spawn" "1 $car 0" 0 | Out-Null
Wait-Ready 0 | Out-Null
Wait-Ready 1 | Out-Null
Step "econ-trace" "report" 0 | Out-Null

Step "econ-map-travel" "Junkyard" | Out-Null; Back-To-Garage
Step "econ-map-travel" "Auction" | Out-Null; Back-To-Garage
Step "econ-map-travel" "Barn" | Out-Null; Back-To-Garage
Step "econ-fee" "spill 0" 6 | Out-Null
Step "econ-fee" "refill 0" | Out-Null
Step "econ-fee" "wash-paint 0" | Out-Null
Step "econ-fee" "wash-tint 0" | Out-Null
Step "econ-fee" "tint 0 4" | Out-Null
Step "tool-paint-car" "0 0.8,0.1,0.1" | Out-Null
Step "tool-use" "Welder 0 paid" | Out-Null
Step "tool-use" "InteriorDetailing 0 paid" | Out-Null
$worn = (Step "give-item" "tarczaHamulcowa_1 0.3").UID
Step "tool-repair" "$worn success paid" | Out-Null
for ($card = 0; $card -le 3; $card++) { Step "econ-crate" "$($cases[$card]) $card" | Out-Null }
Step "econ-crate-close" "$($cases[4])" | Out-Null
Step "econ-skill-unlock" "fast_movement 0" 2 | Out-Null
Step "econ-skill-reset" | Out-Null
Step "econ-sell-car" "1 12000" 6 | Out-Null
Step "econ-scrap" "$($parts[0]) 1" | Out-Null
Step "econ-scrap-upgrade" "$($parts[1])" | Out-Null
Step "econ-scrap-condition" "30" | Out-Null
Step "econ-license" "2 TEST" | Out-Null
Step "econ-barn-map" "$map" | Out-Null
Step "econ-unmount" "0" 6 | Out-Null

foreach ($verb in "econ-trace report", "econ-ledger", "guard-log", "dump") {
    $verbParts = $verb -split ' ', 2
    try {
        $r = Send-HarnessCommand -Instance $a -Verb $verbParts[0] -Arguments $(if ($verbParts.Count -gt 1) { $verbParts[1] } else { "" })
        $r | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "$($verbParts[0]).json") -Encoding utf8
    } catch { $notes += "$verb failed: $($_.Exception.Message)" }
}
$report = Get-Content -LiteralPath (Join-Path $Ctx.RunDir "econ-trace.json") -Raw | ConvertFrom-Json
$notes += "mutator calls without a traced caller: $($report.mutatorCallsWithoutCaller)"
$notes += "unpatched trace targets: $(($report.failures | ConvertTo-Json -Compress))"
Send-ServerCommand "economy 80"

$Ctx.Result.notes += $notes
$Ctx.Result.passed = $true

# run-all: skip
# Spike for sync-car-parts: does every client build the same part hierarchy for a car? A spawns each car model on
# loader 0, B gets it through the server; both dump body part indices/names and mechanical part sibling paths/ids,
# and the files are compared. Optional: $env:PART_IDENTITY_LIMIT limits the number of cars (default all).
param($Ctx)

$a, $b = $Ctx.Instances
$loader = 0

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Car([string]$Name, [string]$Car, [int]$TimeoutSec = 90) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $state = Send-HarnessCommand -Instance $Name -Verb car-loaded -Arguments "$loader"
        if (($Car -and $state.loaded -and $state.car -eq $Car) -or (-not $Car -and -not $state.car)) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b

foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "Off" | Out-Null }
Send-HarnessCommand -Instance $a -Verb car-delete -Arguments "$loader" | Out-Null
Wait-Car $a $null 30 | Out-Null; Wait-Car $b $null 30 | Out-Null

$cars = @(Send-HarnessCommand -Instance $a -Verb car-list) | Where-Object { -not $_.mod }
$cars | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "cars.json") -Encoding utf8
$limit = if ($env:PART_IDENTITY_LIMIT) { [int]$env:PART_IDENTITY_LIMIT } else { $cars.Count }
$keysDir = Join-Path $Ctx.RunDir "part-keys"
New-Item -ItemType Directory -Force -Path $keysDir | Out-Null

$identical = 0
$results = @()
foreach ($car in ($cars | Select-Object -First $limit)) {
    $id = $car.id
    Send-HarnessCommand -Instance $a -Verb car-spawn -Arguments "$loader $id 0" | Out-Null
    $okA = Wait-Car $a $id
    $okB = $okA -and (Wait-Car $b $id)
    if (-not ($okA -and $okB)) {
        $results += [pscustomobject]@{ car = $id; dlc = $car.dlc; result = "not loaded (A $okA, B $okB)" }
    } else {
        Start-Sleep -Milliseconds 500
        $fileA = Join-Path $keysDir "${id}_A.txt"; $fileB = Join-Path $keysDir "${id}_B.txt"
        $countA = Send-HarnessCommand -Instance $a -Verb part-keys -Arguments "$loader $fileA"
        Send-HarnessCommand -Instance $b -Verb part-keys -Arguments "$loader $fileB" | Out-Null
        $diff = @(Compare-Object (Get-Content -LiteralPath $fileA) (Get-Content -LiteralPath $fileB))
        if ($diff.Count -eq 0) {
            $identical++
            Remove-Item -LiteralPath $fileB
            $results += [pscustomobject]@{ car = $id; dlc = $car.dlc; result = "identical"; body = $countA.body; sub = $countA.sub }
        } else {
            $results += [pscustomobject]@{ car = $id; dlc = $car.dlc; result = "DIFFERENT ($($diff.Count) lines)"; body = $countA.body; sub = $countA.sub }
        }
    }
    Send-HarnessCommand -Instance $a -Verb car-delete -Arguments "$loader" | Out-Null
    Wait-Car $a $null 30 | Out-Null; Wait-Car $b $null 30 | Out-Null
    Write-Host ("{0,-28} {1}" -f $id, $results[-1].result)
}

$results | Format-Table -AutoSize | Out-String -Width 200 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "part-identity.txt") -Encoding utf8
$different = @($results | Where-Object { $_.result -ne "identical" })
$Ctx.Result.notes += "cars checked: $($results.Count), identical: $identical, other: $($different.Count)"
$Ctx.Result.notes += @($different | ForEach-Object { "$($_.car): $($_.result)" })
$Ctx.Result.passed = ($different.Count -eq 0)

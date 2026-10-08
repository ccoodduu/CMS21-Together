# run-all: skip
# areas: persistence, placement, cars, parts, tools, jobs
# multiplayer-soak-and-scale 5.1/5.2, the fixture maker for latejoin-full: the first instance fills the session
# (FullGarage.psm1: parking to -ParkingLevels, a car on every loader with 20 parts off, every slot machine loaded,
# 300+ items, one accepted job), the other instances join and must agree, and the server's save is copied to
# CMS21-TestInstalls\fixtures\full-garage_L<levels>_<version tag>.json. Works on any lane (lane 1 takes two clients).
# latejoin-full builds the same fixture by itself when it is missing; this scenario rebuilds it on purpose.
param($Ctx, [int]$ParkingLevels = 2)

Import-Module (Join-Path $PSScriptRoot "..\ScaleSession.psm1")
Import-Module (Join-Path $PSScriptRoot "..\FullGarage.psm1")

$names = @($Ctx.Instances)
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

$version = Get-SaveVersionTag $Ctx.ServerDir
$fixture = Get-FullGarageFixturePath $ParkingLevels $version.Tag
Write-Host "Fixture $fixture ($($version.Text))"

Wait-AllInMenu $names
Connect-ScaleInstance $names[0] | Out-Null
$fill = Invoke-FullGarageFill -Filler $names[0] -ParkingLevels $ParkingLevels
$Ctx.Result.notes += "fill: $($fill | ConvertTo-Json -Compress)"
Check ([int]$fill.parkingLevels -ge $ParkingLevels) "parking unlocked to $ParkingLevels levels ($($fill.parkingLevels))"
Check ($fill.parked -ge 10 * $ParkingLevels) "the parking is full ($($fill.parked) cars)"
Check ($fill.garageCars -ge $fill.loaders) "a car on every garage loader ($($fill.garageCars) of $($fill.loaders))"
Check ($fill.models -ge 3) "three models or more ($($fill.models))"
Check ($fill.items -ge 300) "300 items or more ($($fill.items))"
Check ($fill.machines -ge 5) "an item on every slot machine ($($fill.machines))"
Check ($fill.activeJobs -ge 1) "one accepted job ($($fill.activeJobs))"

foreach ($name in @($names | Select-Object -Skip 1)) { Connect-ScaleInstance $name | Out-Null }
try {
    Wait-HarnessDumpsAllEqual -Instances $names -TimeoutSec 120 | Out-Null
    Check $true "all clients agree after the fill"
} catch {
    Check $false $_.Exception.Message
    Save-Dumps (Get-LastHarnessDumps) (Join-Path $Ctx.RunDir "differ")
}

$bytes = Save-FullGarageFixture $Ctx.ServerDir $fixture $version.Text
$check = Test-FullGarageFixture $Ctx.ServerDir $fixture
Check ([bool]$check) "--check-save loads the fixture ($([math]::Round($bytes / 1MB, 2)) MB)"
if ($check) { Set-Content -LiteralPath (Join-Path $Ctx.RunDir "check-save.txt") -Value $check }
$Ctx.Result.notes += "fixture: $fixture"
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# run-all: skip
# run-all: fresh
# Smoke test of a release install (tools\release\Install-ReleaseToTestEnv.ps1 first; fails against a dev deploy):
# both clients and the server run the zip's build (version and dll hashes equal release.json), both clients
# connect, reach the garage and end up with the same shared state.
param($Ctx)

$a, $b = $Ctx.Instances

function Get-InstanceDir([string]$Name) { Split-Path (Split-Path (Get-HarnessDir $Name) -Parent) -Parent }

$serverManifest = Get-Content -LiteralPath (Join-Path $Ctx.ServerDir "release.json") -Raw | ConvertFrom-Json
$Ctx.Result.notes += "Release $($serverManifest.fullVersion)"

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null

    $manifestPath = Join-Path (Get-InstanceDir $name) "CMS21-Together-release.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "$name has no CMS21-Together-release.json; install a release first" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.fullVersion -ne $serverManifest.fullVersion) {
        throw "$name has release $($manifest.fullVersion), the server $($serverManifest.fullVersion)"
    }

    $info = Send-HarnessCommand -Instance $name -Verb build-info
    if ($info.fullVersion -ne $manifest.fullVersion) { throw "$name runs $($info.fullVersion), release.json says $($manifest.fullVersion)" }
    foreach ($file in $info.files.PSObject.Properties) {
        $expected = $manifest.files.($file.Name)
        if (-not $file.Value.loaded) { throw "$name has not loaded $($file.Name)" }
        if ($file.Value.sha256 -ne $expected) { throw "$name loaded $($file.Value.path) with hash $($file.Value.sha256), release.json has $expected" }
    }
    $Ctx.Result.notes += "$name runs $($info.fullVersion), steam_api64.dll present: $($info.steamLibPresent), Steam available: $($info.steamAvailable)"
    Write-Host "$name runs the release build ($($info.fullVersion))"
}

Wait-ServerLog -Pattern ("CMS21 Together Server v" + [regex]::Escape($serverManifest.fullVersion) + "$") -TimeoutSec 5 | Out-Null
Write-Host "Server runs $($serverManifest.fullVersion)"

foreach ($name in $Ctx.Instances) {
    Connect-HarnessInstance $name
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "garage after connect" -Condition {
        param($s) $s.connectionValid -and $s.initialSyncFinished -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
    Write-Host "$name is connected and in the garage"
}

Start-Sleep -Seconds 5
$dumpA = Save-HarnessDump -Instance $a -RunDir $Ctx.RunDir -Label "garage"
$dumpB = Save-HarnessDump -Instance $b -RunDir $Ctx.RunDir -Label "garage"
$differences = Compare-HarnessDumps $dumpA $dumpB
if ($differences.Count -gt 0) { $Ctx.Result.notes += "Shared state differs in: $($differences -join ', ')" }

foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb quit -TimeoutSec 5 | Out-Null }
$Ctx.Result.passed = ($differences.Count -eq 0)

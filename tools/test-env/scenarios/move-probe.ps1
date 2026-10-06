# run-all: skip
# Tool move probe (instance A only): move each movable tool to a few places before anything else happens.
param($Ctx)

$a = $Ctx.Instances[0]
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $a
Wait-HarnessStatus -Instance $a -TimeoutSec 300 -What "garage" -Condition { param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable } | Out-Null
Send-HarnessCommand -Instance $a -Verb guard-set -Arguments "Off" | Out-Null
foreach ($tool in "Welder", "Oilbin", "EngineCrane") {
    foreach ($place in "CarLifter1", "CarLifter2", "Entrance1") {
        try { Write-Host "$tool $place : $(Send-HarnessCommand -Instance $a -Verb tool-move -Arguments "$tool $place" | ConvertTo-Json -Compress)" }
        catch { Write-Host "$tool $place : ERROR $($_.Exception.Message.Split("`n")[0])" }
        try { Send-HarnessCommand -Instance $a -Verb tool-move -Arguments "$tool default" | Out-Null } catch { }
    }
}
$Ctx.Result.passed = $true

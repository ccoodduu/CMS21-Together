# areas: connect
# Cold start from a join string (what a Steam invite with the game closed does): B is launched with
# +connect CMS21Together:ip:<lane address> (see join-coldstart.launch.psd1) and joins by itself once the menu
# is ready; A, started without it, stays in the menu.
param($Ctx)

$a, $b = $Ctx.Instances
Wait-HarnessStatus -Instance $b -TimeoutSec 300 -What "B in session without a harness join" -Condition {
    param($s) $s.joinStatus -eq "InSession" -and $s.scene -eq "garage" -and $s.playable
} | Out-Null
Write-Host "B joined from its start arguments"
$statusA = Get-HarnessStatus $a
$failures = @()
if ($statusA.scene -ne "Menu" -or $statusA.connected) { $failures += "A left the menu or connected (scene $($statusA.scene), connected $($statusA.connected))" }
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

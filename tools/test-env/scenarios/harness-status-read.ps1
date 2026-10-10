# areas: connect
# The harness rewrites status.json every second (delete, then move a new file in). A single Get-HarnessStatus that
# fell in between returned $null, so a one-shot check such as compat-refusal's "A stays in the session" failed while A
# was in the session (regression 2026-10-10). Here 3000 reads of A's and B's status in a row must all succeed.
param($Ctx)

$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}

foreach ($name in $Ctx.Instances) {
    $missed = 0
    $started = Get-Date
    for ($i = 0; $i -lt 3000; $i++) {
        $s = Get-HarnessStatus $name
        if ($null -eq $s -or $s.scene -ne "Menu") { $missed++ }
    }
    $seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
    Check ($missed -eq 0) "3000 reads of $name's status all return it ($missed missed, $seconds s)"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

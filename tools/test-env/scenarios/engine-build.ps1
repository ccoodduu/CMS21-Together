# areas: tools, guard
# sync-tuning-bonus-and-new-engines group 4 (ROADMAP row 25), guard enforcing. The engine build runs through the game's
# CreateEngineWindow.CreateEngineAction; "stand-nofade on" steps the build coroutine every frame, because its end-of-frame
# waits never come in a headless game (spike 1.1b). A builds on the empty stand: B's stand shows the same engine, the
# money is unchanged and the server counts one built engine. A and B build on the occupied stand: both are refused with
# "Take the engine off the stand first." and the engine stays. A takes the engine off; A and B build at once: one engine
# on the stand for both, the other player answered, no built engine in the shared inventory, money unchanged.
# Old-code failure: A's build on the occupied stand replaces the engine without a message.
param($Ctx, [int]$BuildSec = 25)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Try-Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { "ERROR: $($_.Exception.Message.Split("`n")[0])" } }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "build_$Label.json") -Encoding utf8 }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Stand([string]$Name) { Cmd $Name stand-state }
function Wait-Stands([string]$What, [scriptblock]$Condition, [int]$Seconds = $BuildSec) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $sa = Stand $a; $sb = Stand $b
        $ok = & $Condition $sa $sb
        if (-not $ok) { Start-Sleep -Milliseconds 500 }
    } while (-not $ok -and $sw.Elapsed.TotalSeconds -lt $Seconds)
    Check $ok "$What (A $($sa.group) $($sa.appliedUid); B $($sb.group) $($sb.appliedUid), mirror $($sb.mirrorUid); $([int]$sw.Elapsed.TotalSeconds) s)"
    return @($sa, $sb)
}
function Money { @((Cmd $a dump).stats.money, (Cmd $b dump).stats.money) -join "/" }
function Groups([string]$Name) { @((Cmd $Name dump).inventory.groups) }
function Engines([string]$Name) { @(Groups $Name | Where-Object { $_.ID -like "engine_*" }) }
function Toasts([string]$Name) { @((Cmd $Name dump).session.toasts) }
function Created {
    $mark = Get-ServerLogMark
    Send-ServerCommand "tools"
    $line = try { Wait-ServerLog -Pattern "createdEngines" -After $mark -TimeoutSec 10 } catch { "" }
    if ("$line" -match "createdEngines (\d+)") { return [int]$Matches[1] }
    return -1
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Cmd $name guard-set "enforce" | Out-Null
    Cmd $name guard-allow "Window:CreateEngine" | Out-Null
    Cmd $name stand-nofade "on" | Out-Null
}
$rule = @(Cmd $a guard-rules) | Where-Object { $_ -match "Pie:engine_new\b" }
Write-Host "guard rule: $rule"
$money = Money

# Build on the empty stand.
$built = Cmd $a tool-stand-create "engine_v8_stary"
Save "built_A" $built
$sa, $sb = Wait-Stands "A's new engine is on both stands" { param($x, $y) $x.group -eq "engine_v8_stary" -and $y.group -eq "engine_v8_stary" -and $y.mirrorUid -eq $x.appliedUid -and $x.appliedUid -ne 0 -and $y.appliedUid -eq $x.appliedUid }
Check ($sa.parts -gt 0 -and $sa.parts -eq $sb.parts -and $sa.unmountedParts -eq $sb.unmountedParts) "B's engine has the same parts ($($sa.parts) parts, $($sa.unmountedParts) off on A; $($sb.parts), $($sb.unmountedParts) on B)"
Check ((Money) -eq $money) "building costs nothing ($(Money), was $money)"
Check ((Created) -eq 1) "the server counts one built engine"
$firstUid = $sa.appliedUid

# The occupied stand.
$toastsA = @(Toasts $a).Count; $toastsB = @(Toasts $b).Count
Save "occupied_A" (Cmd $a tool-stand-create "engine_v8_stary_2carb")
Save "occupied_B" (Cmd $b tool-stand-create "engine_r4")
Start-Sleep -Seconds 10
$sa = Stand $a; $sb = Stand $b
Check ($sa.group -eq "engine_v8_stary" -and $sa.appliedUid -eq $firstUid -and $sb.group -eq "engine_v8_stary" -and $sb.appliedUid -eq $firstUid) "the engine on the stand stays for both (A $($sa.group) $($sa.appliedUid), B $($sb.group) $($sb.appliedUid))"
Check (@(Toasts $a | Select-Object -Skip $toastsA | Where-Object { $_ -eq "Take the engine off the stand first." }).Count -ge 1) "A is told 'Take the engine off the stand first.'"
Check (@(Toasts $b | Select-Object -Skip $toastsB | Where-Object { $_ -eq "Take the engine off the stand first." }).Count -ge 1) "B is told 'Take the engine off the stand first.'"
Check ((Created) -eq 1) "no build on the occupied stand is counted"

# Both build at once on the empty stand.
Cmd $a tool-take "EngineStand1" | Out-Null
Wait-Stands "A took the engine off" { param($x, $y) -not $x.group -and -not $y.group -and $y.mirrorUid -eq 0 } | Out-Null
Start-Sleep -Seconds 2
$enginesA = @(Engines $a); $enginesB = @(Engines $b)
Check ($enginesA.Count -eq 1 -and $enginesB.Count -eq 1) "the taken engine is in the shared inventory once (A $($enginesA.Count), B $($enginesB.Count))"
$toastsA = @(Toasts $a).Count; $toastsB = @(Toasts $b).Count
Cmd $a tool-stand-create "engine_v8_stary_2carb" | Out-Null
Cmd $b tool-stand-create "engine_r4" | Out-Null
$sa, $sb = Wait-Stands "one engine is on both stands" { param($x, $y) $x.group -and $x.group -eq $y.group -and $x.appliedUid -eq $y.appliedUid -and $y.mirrorUid -eq $x.appliedUid -and $x.mirrorUid -eq $x.appliedUid }
Start-Sleep -Seconds 4
$sa = Stand $a; $sb = Stand $b
Save "race_A" $sa; Save "race_B" $sb
Check ($sa.group -eq $sb.group -and $sa.appliedUid -eq $sb.appliedUid -and @("engine_v8_stary_2carb", "engine_r4") -contains $sa.group) "after the race both stands hold the same engine ($($sa.group) $($sa.appliedUid) / $($sb.group) $($sb.appliedUid))"
$loser = if ($sa.group -eq "engine_v8_stary_2carb") { $b } else { $a }
$loserToasts = if ($loser -eq $a) { @(Toasts $a | Select-Object -Skip $toastsA) } else { @(Toasts $b | Select-Object -Skip $toastsB) }
Check (@($loserToasts | Where-Object { $_ -match "first\.$" }).Count -ge 1) "the loser ($loser) is answered ($($loserToasts -join ' | '))"
$enginesA = @(Engines $a); $enginesB = @(Engines $b)
Check ($enginesA.Count -eq 1 -and $enginesB.Count -eq 1 -and $enginesA[0].ID -eq "engine_v8_stary") "no built engine reached the shared inventory (A $(@($enginesA | ForEach-Object { $_.ID }) -join ','), B $(@($enginesB | ForEach-Object { $_.ID }) -join ','))"
Check ((Money) -eq $money) "money is unchanged ($(Money))"
Check ((Created) -eq 2) "the server counts two built engines"

foreach ($name in $Ctx.Instances) {
    $guardLog = Cmd $name guard-log
    Save "guard_log_$name" $guardLog
    Check (@($guardLog.keys | Where-Object { $_ -match "CreateEngine|engine_new" }).Count -eq 0) "the guard blocked nothing on $name's engine build path"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

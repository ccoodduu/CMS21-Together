# areas: garage, guard, persistence, resync
# shared-garage-look (ROADMAP row 28), guard enforcing. A opens the garage customisation window through the
# #garageLook click and picks wall A (section 2) material 3 and floor A (section 0) material 5; while A has the window
# open the server's desync check counts no garage mismatch, and B is refused ("A is customising the garage.") without
# a fade or a window. A closes: B shows the same indexes and the same renderer materials within the bound. B joins
# again and the server restarts: the look is back on both. A sets wall A back to default: default on both, and B's
# next open/close without changes keeps -1 on the server. A sets a texture pack B does not have: B keeps the default
# textures, shows the notice once, and B's own close keeps the pack on the server.
# Old-code failure: the window is allowed with guard-allow (a no-op on the new code); B's indexes stay -1.
param($Ctx, [int]$ApplyBoundSec = 10)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Try-Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { "ERROR: $($_.Exception.Message.Split("`n")[0])" } }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "look_$Label.json") -Encoding utf8 }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Look([string]$Name) { (Cmd $Name dump).garageLook }
function Toasts([string]$Name) { @((Cmd $Name dump).session.toasts) }
function Indexes($Look) { ($Look.indexes | ForEach-Object { "$_" }) -join "," }
function Materials($Look) { ($Look.materials | ForEach-Object { "$_" }) -join "," }
function Server-Look {
    $mark = Get-ServerLogMark
    Send-ServerCommand "look"
    $line = try { Wait-ServerLog -Pattern "  look: " -After $mark -TimeoutSec 10 } catch { "" }
    return "$line"
}
function Wait-Window([string]$Name, [bool]$Open, [int]$Seconds = 20) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        Start-Sleep -Milliseconds 300
        $l = Look $Name
    } while ([bool]$l.windowOpen -ne $Open -and (Get-Date) -lt $deadline)
    return ([bool]$l.windowOpen -eq $Open)
}
function Open-Window([string]$Name) {
    Cmd $Name look-open | Out-Null
    $opened = Wait-Window $Name $true
    if ($opened) { Start-Sleep -Seconds 2 }
    return $opened
}
function Close-Window([string]$Name) {
    Cmd $Name look-close | Out-Null
    $closed = Wait-Window $Name $false
    Start-Sleep -Seconds 2
    return $closed
}
function Wait-Section([string]$What, [int]$Section, [int]$Expected, [int]$Seconds = $ApplyBoundSec) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $la = Look $a; $lb = Look $b
        $ok = $la.indexes[$Section] -eq $Expected -and $lb.indexes[$Section] -eq $Expected -and -not $la.applying -and -not $lb.applying
        if (-not $ok) { Start-Sleep -Milliseconds 300 }
    } while (-not $ok -and $sw.Elapsed.TotalSeconds -lt $Seconds)
    Check $ok "$What`: section $Section is $Expected on A and B within $Seconds s (A $($la.indexes[$Section]), B $($lb.indexes[$Section]), $([int]$sw.Elapsed.TotalMilliseconds) ms)"
    return @($la, $lb)
}
function Check-Same([string]$What, $La, $Lb) {
    Check ((Indexes $La) -eq (Indexes $Lb)) "$What`: A and B have the same section indexes (A $(Indexes $La); B $(Indexes $Lb))"
    Check ((Materials $La) -eq (Materials $Lb)) "$What`: A and B show the same renderer materials"
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Cmd $name guard-set "enforce" | Out-Null
    Cmd $name guard-allow "Window:GarageCustomization" | Out-Null
}
$rule = @(Cmd $a guard-rules) | Where-Object { $_ -match "Window:GarageCustomization\b" }
Check ("$rule" -match "^allow" -and "$rule" -match "row 28") "the guard allows the garage customisation window (row 28) ($rule)"

$baseA = Look $a; $baseB = Look $b
Save "base_A" $baseA; Save "base_B" $baseB
Check (@($baseA.indexes | Where-Object { $_ -ne -1 }).Count -eq 0) "A starts with the default look ($(Indexes $baseA))"
Check-Same "start" $baseA $baseB
$defaultWall = $baseA.materials[2]

# A customises; B is refused while A has the window open.
Check (Open-Window $a) "A's #garageLook click opens the window"
$openA = Look $a
Check ([bool]$openA.claimHeld) "A holds the look claim while the window is open"
Save "pick_wall" (Cmd $a look-pick "2 3")
Save "pick_floor" (Cmd $a look-pick "0 5")
$previewA = Look $a
Check ($previewA.indexes[2] -eq 3 -and $previewA.indexes[0] -eq 5) "A's preview shows wall A 3 and floor A 5 ($(Indexes $previewA))"

$mark = Get-ServerLogMark
foreach ($round in 1..2) { Send-ServerCommand "desync check"; Start-Sleep -Seconds 4 }
$checkLines = @(Get-ServerLogLines | Select-Object -Skip $mark)
$desync = @($checkLines | Where-Object { $_ -match "\[Desync\] garage for client \d+: mismatch" })
$matched = @($checkLines | Where-Object { $_ -match "\[Desync\] garage for client \d+: match\." })
Check ($matched.Count -ge 2 -and $desync.Count -eq 0) "the garage digest matches and is never a mismatch while A has the window open ($($matched.Count) matches; $($desync -join ' | '))"

$beforeB = Look $b
Cmd $b look-open | Out-Null
Start-Sleep -Seconds 3
$refusedB = Look $b
Save "refused_B" $refusedB
Check (-not $refusedB.windowOpen -and -not $refusedB.claimHeld) "B's click while A customises opens no window"
Check (-not $refusedB.faded -and $refusedB.mode -ne "UI") "B's screen is not faded and B is not in a window mode (faded $($refusedB.faded), mode $($refusedB.mode))"
Check ("$($refusedB.lastRefusal)" -match "^\S.* is customising the garage\.$" -and "$($refusedB.lastRefusal)" -notmatch "^Player \d+ ") "B is told by name who customises the garage ($($refusedB.lastRefusal))"
Check ([bool](Toasts $b | Where-Object { $_ -match "is customising the garage\." })) "B shows the refusal as a notice"
Check ((Indexes $refusedB) -eq (Indexes $beforeB)) "B's look did not change while A browses"

Check (Close-Window $a) "A closes the window"
$applied = Wait-Section "after A's close" 2 3
Wait-Section "after A's close" 0 5 | Out-Null
$la = Look $a; $lb = Look $b
Save "after_close_A" $la; Save "after_close_B" $lb
Check-Same "after A's close" $la $lb
Check ($lb.materials[2] -ne $defaultWall) "B's wall A renderer has another material than the default ($($lb.materials[2]))"
Check (-not $la.claimHeld) "A released the claim"
$server = Server-Look
Check ($server -match "0=5" -and $server -match "2=3") "the server stores wall A 3 and floor A 5 ($server)"
$lookAfterClose = Indexes $lb

# B joins again.
Cmd $b to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 90 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
$pair = Wait-Section "after B's rejoin" 2 3
Save "rejoin_B" $pair[1]
Check ((Indexes $pair[1]) -eq $lookAfterClose) "B has the look after the rejoin ($(Indexes $pair[1]))"
Check-Same "after B's rejoin" $pair[0] $pair[1]

# Server restart.
$mark = Get-ServerLogMark
Send-ServerCommand "save"
Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 20 | Out-Null
Stop-TestServer
foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 30 -What "menu after kill" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
    Cmd $name mp-ui "ok" | Out-Null
}
Start-TestServer | Out-Null
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Cmd $name guard-set "enforce" | Out-Null
    Cmd $name guard-allow "Window:GarageCustomization" | Out-Null
}
$ra, $rb = Wait-Section "after the server restart" 2 3
Save "restart_A" $ra; Save "restart_B" $rb
Check ((Indexes $ra) -eq $lookAfterClose -and (Indexes $rb) -eq $lookAfterClose) "after the server restart A and B have the look (A $(Indexes $ra); B $(Indexes $rb))"
Check-Same "after the restart" $ra $rb

# Back to default, and B's close without changes keeps it.
Check (Open-Window $a) "A opens the window again"
Cmd $a look-pick "2 -1" | Out-Null
Check (Close-Window $a) "A closes after setting wall A back to default"
$pair = Wait-Section "after the reset" 2 -1
Check-Same "after the reset" $pair[0] $pair[1]
Check ($pair[1].materials[2] -eq $defaultWall) "B's wall A shows the default material again ($($pair[1].materials[2]), default $defaultWall)"
$mark = Get-ServerLogMark
Check (Open-Window $b) "B opens the window"
Check (Close-Window $b) "B closes without changes"
$stored = @(Get-ServerLogLines | Select-Object -Skip $mark | Where-Object { $_ -match "\[GarageLook\] client \d+ stored" })
$server = Server-Look
Check ($stored.Count -eq 0) "B's close without changes sends no look ($($stored -join ' | '))"
Check ($server -match "0=5" -and $server -notmatch "\b2=") "the server keeps wall A at default after B's close ($server)"

# A texture pack B does not have.
Check (Open-Window $a) "A opens the window for the texture pack"
Save "pack_A" (Cmd $a look-pack "fake")
Check (Close-Window $a) "A closes with the pack 'fake'"
Start-Sleep -Seconds 3
$lb = Look $b
Save "pack_B" $lb
Check ($lb.lastApplied.pack -eq "fake") "B received the pack id ($($lb.lastApplied.pack))"
Check ($null -eq $lb.pack) "B keeps the default textures ($($lb.pack))"
Check (@(Toasts $b | Where-Object { $_ -match "^fake is not installed" }).Count -eq 1) "B shows the missing-pack notice once"
Check (Open-Window $a) "A opens the window once more"
Cmd $a look-pick "0 6" | Out-Null
Check (Close-Window $a) "A closes after another change"
Wait-Section "after the second change" 0 6 | Out-Null
Check (@(Toasts $b | Where-Object { $_ -match "^fake is not installed" }).Count -eq 1) "B does not repeat the missing-pack notice"
Check (Open-Window $b) "B opens the window with the pack missing"
Check (Close-Window $b) "B closes without changes"
$server = Server-Look
Check ($server -match "pack fake") "the server keeps the pack after B's close ($server)"

foreach ($name in $Ctx.Instances) {
    $guardLog = Cmd $name guard-log
    Save "guard_log_$name" $guardLog
    Check (@($guardLog.keys | Where-Object { $_ -match "GarageCustomization" }).Count -eq 0) "the guard blocked nothing on $name's garage look path"
}

$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

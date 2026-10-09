# run-all: skip
# areas: garage
# Spike for shared-garage-look task 1.1: the per-section apply through the game's UpdateMaterials coroutine (set, back
# to default, the profile after GarageLookManager.Save, all 41 sections and the time it takes), the texture pack
# setters, and the claim gate at the #garageLook click (window, fade, refusal for the second player).
param($Ctx)

$a, $b = $Ctx.Instances
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Save([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "look_$Label.json") -Encoding utf8; $text = $Value | ConvertTo-Json -Depth 6 -Compress; Write-Host "== $Label"; if ($text.Length -lt 3000) { Write-Host $text } else { Write-Host "($($text.Length) chars, see file)" } }
function Try-Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { try { Cmd $Name $Verb $Arguments } catch { "ERROR: $($_.Exception.Message.Split("`n")[0])" } }
function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}
function Look([string]$Name) { (Cmd $Name dump).garageLook }
function Wait-Applied([string]$Name, [int]$Applies, [int]$Seconds = 120) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        Start-Sleep -Milliseconds 250
        $l = Look $Name
    } while (($l.applying -or $l.applies -lt $Applies) -and (Get-Date) -lt $deadline)
    return $l
}
function Apply-Local([string]$Label, [string]$Spec) {
    $before = (Look $a).applies
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Cmd $a look-apply-local $Spec | Out-Null
    $l = Wait-Applied $a ($before + 1)
    $sw.Stop()
    Save "apply_$Label" @{ spec = $Spec; wallMs = $sw.ElapsedMilliseconds; applyMs = $l.lastApplyMs; changed = $l.lastApplyChanged; indexes = $l.indexes; materials = $l.materials; applying = $l.applying }
    return $l
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
Send-ServerCommand "desync interval 3600"
foreach ($name in $Ctx.Instances) { Cmd $name guard-set "enforce" | Out-Null }

Save "start_A" (Look $a)
Save "read_before_0" (Cmd $a look-read "0")
Save "read_before_2" (Cmd $a look-read "2")

Apply-Local "set" "2=3 0=5" | Out-Null
Save "read_set_0" (Cmd $a look-read "0")
Save "read_set_2" (Cmd $a look-read "2")
Save "save_set" (Cmd $a look-save)

Apply-Local "reset2" "0=5" | Out-Null
Save "read_reset_2" (Cmd $a look-read "2")
Save "save_reset" (Cmd $a look-save)

Apply-Local "all0" "all=0" | Out-Null
Apply-Local "allDefault" "" | Out-Null
Save "read_end_40" (Cmd $a look-read "40")
Save "pack_try" (Try-Cmd $a look-pack-try)

$open = Try-Cmd $a look-open
Save "open_A" $open
$deadline = (Get-Date).AddSeconds(15)
do { Start-Sleep -Milliseconds 300; $l = Look $a } while (-not $l.windowOpen -and (Get-Date) -lt $deadline)
Save "opened_A" ($l | Select-Object windowOpen, faded, mode, claimHeld, claimPending)
Start-Sleep -Seconds 2
Save "opened_A_settled" ((Look $a) | Select-Object windowOpen, faded, mode, claimHeld)
Save "open_B" (Try-Cmd $b look-open)
Start-Sleep -Seconds 2
Save "refused_B" ((Look $b) | Select-Object windowOpen, faded, mode, claimHeld, claimPending, lastRefusal)
Save "pick_A" (Try-Cmd $a look-pick "2 4")
Save "close_A" (Try-Cmd $a look-close)
$deadline = (Get-Date).AddSeconds(15)
do { Start-Sleep -Milliseconds 300; $l = Look $a } while ($l.windowOpen -and (Get-Date) -lt $deadline)
Start-Sleep -Seconds 3
Save "closed_A" ((Look $a) | Select-Object windowOpen, faded, mode, claimHeld, lastApplied)
Save "after_B" ((Look $b) | Select-Object indexes, materials, lastApplied, lastApplyMs, lastApplyChanged)
Save "after_A" ((Look $a) | Select-Object indexes, materials)
$mark = Get-ServerLogMark
Send-ServerCommand "look"
Start-Sleep -Seconds 1
Save "server_look" @(Get-ServerLogLines | Select-Object -Skip $mark)

$Ctx.Result.passed = $true

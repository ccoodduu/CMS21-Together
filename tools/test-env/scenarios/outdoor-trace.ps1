# run-all: skip
# areas: outdoor
# shared-outdoor-scenes spikes 1.1, 1.2, 1.4 and 1.5 (runtime halves): A and B connected with the outdoor trace on.
# 1.5 catalog in the garage on both; 1.1 junkyard, barn and auction call order, and the generator hold for 2, 5 and
# 15 s (net-delay on A's incoming packets delays its instance); 1.2 and 1.4 A and B in one junkyard and one barn:
# digests, pile keys and the item states. Every report goes to the run folder; this run collects facts, it does not
# fail on them.
param($Ctx)

$a, $b = $Ctx.Instances
$notes = @()
function Note([string]$Text) { $script:notes += $Text; Write-Host $Text }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Save-Json([string]$Label, $Value) { $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "$Label.json") -Encoding utf8 }

function Visit([string]$Name, [string]$Scene, [string]$Label) {
    Send-HarnessCommand -Instance $Name -Verb outdoor-trace -Arguments "on" | Out-Null
    $started = Get-Date
    Send-HarnessCommand -Instance $Name -Verb travel -Arguments $Scene | Out-Null
    $dump = try {
        Wait-HarnessDump -Instance $Name -TimeoutSec 240 -What "$Name in $Scene" -Condition { param($d) $d.local.scene -eq $Scene -and ($d.outdoor.applied -or $d.outdoor.visit -eq "Local") }
    } catch { Note "$Label`: $Name did not arrive: $($_.Exception.Message)"; $null }
    Note "$Label`: $Name in $Scene after $([int]((Get-Date) - $started).TotalSeconds) s, visit $($dump.outdoor.visit), hold $($dump.outdoor.holdSeconds) s, reseed steps $($dump.outdoor.reseedSteps)"
    Save-Json "$Label-$Name-trace" (Send-HarnessCommand -Instance $Name -Verb outdoor-trace -Arguments "report")
    Save-Json "$Label-$Name-outdoor" $dump.outdoor
    Send-HarnessCommand -Instance $Name -Verb outdoor-trace -Arguments "off" | Out-Null
    return $dump
}

function Home([string]$Name) {
    Send-HarnessCommand -Instance $Name -Verb travel -Arguments "Garage" | Out-Null
    Wait-InGarage $Name
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) { Send-HarnessCommand -Instance $name -Verb guard-set -Arguments "logonly" | Out-Null }
Send-ServerCommand "money set 200000"

foreach ($name in $a, $b) { Save-Json "catalog-$name" (Send-HarnessCommand -Instance $name -Verb outdoor-catalog) }
Send-ServerCommand "outdoor catalog"

Visit $a "Junkyard" "junkyard" | Out-Null
Home $a
foreach ($delay in 2000, 5000, 15000) {
    Send-HarnessCommand -Instance $a -Verb net-delay -Arguments "$delay" | Out-Null
    Visit $a "Junkyard" "hold-$delay" | Out-Null
    Send-HarnessCommand -Instance $a -Verb net-delay -Arguments "0" | Out-Null
    Home $a
}

$ja = Visit $a "Junkyard" "pair-junkyard"
$jb = Visit $b "Junkyard" "pair-junkyard"
Save-Json "pair-junkyard-piles-A" (Send-HarnessCommand -Instance $a -Verb piles)
Save-Json "pair-junkyard-piles-B" (Send-HarnessCommand -Instance $b -Verb piles)
Note "pair junkyard: same instance $($ja.outdoor.instanceId -eq $jb.outdoor.instanceId), digest equal $((@($ja.outdoor.digest) -join ';') -eq (@($jb.outdoor.digest) -join ';')), piles A $(@($ja.outdoor.piles).Count) B $(@($jb.outdoor.piles).Count), unmatched on B $($jb.outdoor.loot.unmatchedPiles), group items left local A $($ja.outdoor.loot.groupItemsLeftLocal)"
Send-ServerCommand "outdoor junkyard"
foreach ($name in $a, $b) { Home $name }

$barns = (Send-HarnessCommand -Instance $a -Verb dump).stats.barns
if ($barns -lt 1) {
    $map = (Send-HarnessCommand -Instance $a -Verb inv-add-local -Arguments "specialMap").uid
    Send-HarnessCommand -Instance $a -Verb econ-barn-map -Arguments "$map" | Out-Null
    Start-Sleep -Seconds 2
}
Send-HarnessCommand -Instance $a -Verb econ-map-travel -Arguments "Barn" | Out-Null
$ba = Wait-HarnessDump -Instance $a -TimeoutSec 240 -What "A in the barn" -Condition { param($d) $d.local.scene -eq "Barn" -and ($d.outdoor.applied -or $d.outdoor.visit -eq "Local") }
Send-HarnessCommand -Instance $b -Verb econ-map-travel -Arguments "Barn" | Out-Null
$bb = Wait-HarnessDump -Instance $b -TimeoutSec 240 -What "B in the barn" -Condition { param($d) $d.local.scene -eq "Barn" -and ($d.outdoor.applied -or $d.outdoor.visit -eq "Local") }
Save-Json "pair-barn-A" $ba.outdoor
Save-Json "pair-barn-B" $bb.outdoor
Note "pair barn: same instance $($ba.outdoor.instanceId -eq $bb.outdoor.instanceId), layout A $(@($ba.outdoor.digest) -like 'layout:*'), layout B $(@($bb.outdoor.digest) -like 'layout:*')"
Send-ServerCommand "outdoor barn"
foreach ($name in $a, $b) { Home $name }

Visit $a "Auction" "auction" | Out-Null
foreach ($type in "normal", "salvage") {
    try { Save-Json "auction-$type" (Send-HarnessCommand -Instance $a -Verb auction-open -Arguments $type) } catch { Note "auction-open $type`: $($_.Exception.Message)" }
}
Send-ServerCommand "outdoor auction"
Home $a

$Ctx.Result.notes += $notes
$Ctx.Result.passed = $true

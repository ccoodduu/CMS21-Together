# areas: shoplist, persistence
# shared-shopping-list: one shopping list for the group. A adds items (one twice, a tire with its sizes) and B sees
# them; B removes a stack and A sees it. Race: B holds incoming packets while A removes a stack and adds an item, then
# B removes the same stack (the server refuses it and B follows the server) and adds its own item; both end equal.
# A clears the list; both add again; B goes to the menu, A adds while B is away and B rejoins with the list. Finally
# the server saves, is killed and restarted, and both get the list back.
param($Ctx)

$a, $b = $Ctx.Instances
$failures = @()
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }

function Wait-InGarage([string]$Name) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec 300 -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function ListText([string]$Name) { @((Cmd $Name shoplist).game) -join " | " }

function Wait-Lists([string]$What, [string[]]$Expected, [int]$TimeoutSec = 30) {
    $want = @($Expected) -join " | "
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $listA = ListText $a
        $listB = ListText $b
        if ($listA -eq $want -and $listB -eq $want) { Check $true "$What`: A and B show [$want]"; return }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    Check $false "$What`: expected [$want], A [$listA], B [$listB]"
}

$tire = "opona tire width=205 size=16 profile=55"
$tireRow = "opona tire 205/16/55 ET0 x1"
$rim = "felga rim width=7 size=17 et=35"
$rimRow = "felga rim 7/17/0 ET35 x1"

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b

Check ((Cmd $a shoplist).windowManagerSame -eq $true) "UIManager's shopping-list window is the one WindowManager opens"
Cmd $a shoplist-clear | Out-Null
Wait-Lists "empty at the start" @()

Cmd $a shoplist-add "akumulator" | Out-Null
Cmd $a shoplist-add "akumulator" | Out-Null
Cmd $a shoplist-add "tarczaHamulcowa_1" | Out-Null
Cmd $a shoplist-add $tire | Out-Null
Wait-Lists "A added items" @("akumulator x2", "tarczaHamulcowa_1 x1", $tireRow)

Cmd $b shoplist-remove "tarczaHamulcowa_1" | Out-Null
Wait-Lists "B removed a stack" @("akumulator x2", $tireRow)

Cmd $b net-hold "on" | Out-Null
Cmd $a shoplist-remove "akumulator" | Out-Null
Cmd $a shoplist-add "filtrOleju_1" | Out-Null
Start-Sleep -Seconds 1
$mark = Get-ServerLogMark
$heldB = ListText $b
Check ($heldB -eq "akumulator x2 | $tireRow") "B still shows the akumulator stack while holding ($heldB)"
Cmd $b shoplist-remove "akumulator" | Out-Null
Cmd $b shoplist-add "klockiHamulcowe_1" | Out-Null
Start-Sleep -Seconds 1
Cmd $b net-hold "off" | Out-Null
Wait-Lists "race settled" @($tireRow, "filtrOleju_1 x1", "klockiHamulcowe_1 x1")
$refusal = $null
try { $refusal = Wait-ServerLog -Pattern "\[ShopList\] Refused for client \d+: remove akumulator" -After $mark -TimeoutSec 10 } catch { }
Check ($null -ne $refusal) "the server refused B's removal of a stack A had removed and logged it"

Cmd $a shoplist-clear | Out-Null
Wait-Lists "A cleared the list" @()

Cmd $a shoplist-add "akumulator" | Out-Null
Start-Sleep -Milliseconds 500
Cmd $b shoplist-add $rim | Out-Null
Wait-Lists "both added after the clear" @("akumulator x1", $rimRow)

Cmd $b to-menu | Out-Null
Wait-HarnessStatus -Instance $b -TimeoutSec 120 -What "B in the menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable -and -not $s.connected } | Out-Null
Cmd $a shoplist-add "tarczaHamulcowa_1" | Out-Null
Connect-HarnessInstance $b; Wait-InGarage $b
$expected = @("akumulator x1", $rimRow, "tarczaHamulcowa_1 x1")
Wait-Lists "B rejoined" $expected
$mirrorB = @((Cmd $b shoplist).mirror) -join " | "
Check ($mirrorB -eq ($expected -join " | ")) "B's mirror after the rejoin is the server list ($mirrorB)"

$mark = Get-ServerLogMark
Send-ServerCommand "shoplist"
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
Wait-Lists "after the server restart" $expected 60
Send-ServerCommand "shoplist"

foreach ($name in $Ctx.Instances) {
    (Cmd $name shoplist) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "shoplist_end_$name.json") -Encoding utf8
}
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

# areas: parts, details
# run-all: skip
# state-merges-and-contention spikes 1.2 and 1.3 plus the setters of task 1.4. 1.4: each cardetails setter changes
# A's carDetails dump, and a fluid set on A reaches B within 3 s. 1.2: the record fields that diag-examine (every
# ToolType), the welder, and a repaired and painted item mounted back change. 1.3: the detail entries that change on
# their own on an idle car, with the engine running, after a test drive and after a wheel swap, and which entries then
# differ between A and B. Findings go to findings.json; only the 1.4 checks fail the run.
param($Ctx, [string[]]$Only = @(), [int]$IdleSeconds = 120)

$a, $b = $Ctx.Instances
$loader = 0
$failures = @()
$findings = [ordered]@{}
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { $script:failures += "FAIL: $Message"; Write-Host "FAIL: $Message" -ForegroundColor Red } else { Write-Host "ok: $Message" } }
function Cmd([string]$Name, [string]$Verb, [string]$Arguments = "") { Send-HarnessCommand -Instance $Name -Verb $Verb -Arguments $Arguments }
function Step([string]$Name, [scriptblock]$Body) {
    if ($Only.Count -gt 0 -and -not ($Only | Where-Object { $Name -like $_ })) { return }
    Write-Host "--- $Name"
    try { $script:findings[$Name] = & $Body }
    catch { $script:findings[$Name] = "ERROR: $($_.Exception.Message)"; Write-Host "  error: $($_.Exception.Message)" -ForegroundColor Yellow }
}

function Wait-InGarage([string]$Name, [int]$TimeoutSec = 300) {
    Wait-HarnessStatus -Instance $Name -TimeoutSec $TimeoutSec -What "garage, sync acked" -Condition {
        param($s) $s.connectionValid -and $s.syncAcked -and $s.scene -eq "garage" -and $s.playable
    } | Out-Null
}

function Wait-Ready([string]$Name) {
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $r = Cmd $Name car-ready "$loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "$Name loader $loader not Ready"
}

function Entries([string]$Name) { (@((Cmd $Name dump).carDetails) | Where-Object { $_.loader -eq $loader }).entries }

function Entry([string]$Name, [string]$Id) { (Entries $Name).$Id | ConvertTo-Json -Compress -Depth 6 }

function Differing() {
    $ea = Entries $a; $eb = Entries $b
    $ids = @($ea.PSObject.Properties.Name) + @($eb.PSObject.Properties.Name) | Sort-Object -Unique
    @($ids | Where-Object { ($ea.$_ | ConvertTo-Json -Compress -Depth 6) -ne ($eb.$_ | ConvertTo-Json -Compress -Depth 6) } | ForEach-Object {
        "$_ A=$($ea.$_ | ConvertTo-Json -Compress -Depth 6) B=$($eb.$_ | ConvertTo-Json -Compress -Depth 6)"
    })
}

function Watch-Drift([string]$Label, [int]$Seconds) {
    foreach ($name in $a, $b) { Cmd $name cardetails-drift "$loader snap $Label" | Out-Null }
    $seen = @{}
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        foreach ($name in $a, $b) {
            foreach ($e in @((Cmd $name cardetails-drift "$loader diff $Label").entries)) {
                $seen["$name $($e.entry)"] = "$($e.before) -> $($e.after)"
            }
        }
    }
    [ordered]@{ drifted = $seen; differAtEnd = @(Differing) }
}

function Records([string]$Label, [scriptblock]$Action, [int]$SettleSeconds = 3) {
    foreach ($name in $a, $b) { Cmd $name part-records "$loader snap $Label" | Out-Null }
    $result = & $Action
    Start-Sleep -Seconds $SettleSeconds
    [ordered]@{
        action = $result
        A = Cmd $a part-records "$loader diff $Label"
        B = Cmd $b part-records "$loader diff $Label"
    }
}

foreach ($name in $Ctx.Instances) {
    Wait-HarnessStatus -Instance $name -TimeoutSec 300 -What "main menu" -Condition { param($s) $s.scene -eq "Menu" -and $s.playable } | Out-Null
}
Connect-HarnessInstance $a; Wait-InGarage $a
Connect-HarnessInstance $b; Wait-InGarage $b
foreach ($name in $Ctx.Instances) {
    Cmd $name guard-set "Off" | Out-Null
    Cmd $name guard-allow "Mode:CarDrive" | Out-Null
}

Cmd $a car-spawn "$loader car_boltatlanta 0" | Out-Null
Wait-Ready $a | Out-Null
Wait-Ready $b | Out-Null
Start-Sleep -Seconds 4

Step "1.4 setters" {
    $fluid = Cmd $a cardetails-fluid "$loader Brake 0 0.42 0.8"
    Check ([math]::Abs($fluid.'f:Brake.0'.Level - 0.42) -lt 0.001 -and [math]::Abs($fluid.'f:Brake.0'.Condition - 0.8) -lt 0.001) "cardetails-fluid sets brake fluid on A ($($fluid.'f:Brake.0' | ConvertTo-Json -Compress))"
    Check ((Entry $a "f:Brake.0") -eq ($fluid.'f:Brake.0' | ConvertTo-Json -Compress -Depth 6)) "A's carDetails dump has the new brake fluid"
    $deadline = (Get-Date).AddSeconds(3)
    do { Start-Sleep -Milliseconds 300; $onB = Entry $b "f:Brake.0" } while ($onB -ne (Entry $a "f:Brake.0") -and (Get-Date) -lt $deadline)
    Check ($onB -eq (Entry $a "f:Brake.0")) "B has A's brake fluid within 3 s ($onB)"

    $wheel = (Entries $a).'w:3'
    $set = Cmd $a cardetails-wheel "$loader 3 $($wheel.Width) $($wheel.RimSize) $($wheel.TireSize) $($wheel.ET + 7)"
    Check ($set.'w:3'.ET -eq $wheel.ET + 7) "cardetails-wheel sets wheel 3's ET ($($wheel.ET) -> $($set.'w:3'.ET))"
    Check ((Entries $a).'w:3'.ET -eq $wheel.ET + 7) "A's carDetails dump has the new wheel 3"

    $align = Cmd $a cardetails-alignment "$loader 0.25 - - -0.3"
    $after = Entries $a
    Check ([math]::Abs($after.'a:FL' - 0.25) -lt 0.001 -and [math]::Abs($after.'a:RR' + 0.3) -lt 0.001) "cardetails-alignment sets FL and RR ($($align | ConvertTo-Json -Compress))"

    $wash = Cmd $a cardetails-wash "$loader 0.7 0.2 3"
    $panel = (Entries $a).'c:3'
    Check ($wash.panels -eq 1 -and [math]::Abs($panel.Dust - 0.7) -lt 0.001 -and [math]::Abs($panel.WashFactor - 0.2) -lt 0.001) "cardetails-wash sets panel 3 (dust $($panel.Dust), wash $($panel.WashFactor))"

    try {
        Wait-HarnessDumpsEqual -Left $a -Right $b -Sections @("carDetails") -TimeoutSec 15 | Out-Null
        Check $true "B follows every setter (carDetails equal)"
    } catch { Check $false "B follows every setter: $($_.Exception.Message); $(Differing)" }
    "done"
}

Step "1.2 examine" {
    $result = [ordered]@{}
    foreach ($tool in "OBD", "Compression", "Multimeter", "TireTreadDepthTester", "CompoundMeter", "OilBayonet", "TestDrive", "PathTest") {
        $keys = (Cmd $a diag-examine "$loader $tool keys").keys
        $result[$tool] = Records "examine-$tool" { $r = Cmd $a diag-examine "$loader $tool"; "examined $($r.examined), keys $(@($keys).Count): $(@($keys) | Select-Object -First 6)" }
    }
    $result
}

Step "1.2 welder" {
    Records "welder" { Cmd $a tool-use "Welder $loader" } 8
}

Step "1.2 repaired and painted item mounted back" {
    $before = @((Cmd $a dump).inventory.items | ForEach-Object { $_.UID })
    $part = Cmd $a part-fast-unmount "$loader"
    Start-Sleep -Seconds 3
    $item = @((Cmd $a dump).inventory.items | Where-Object { $before -notcontains $_.UID }) | Select-Object -First 1
    if (-not $item) { throw "no item from unmounting $($part.key)" }
    Cmd $a tool-repair "$($item.UID) fail" | Out-Null
    Cmd $a tool-paint-part "$($item.UID) 0.9,0.1,0.1" | Out-Null
    Start-Sleep -Seconds 2
    Records "mount-back" { Cmd $a part-domount "$loader $($part.key) $($item.UID)" } 6
}

Step "1.3 idle" { Watch-Drift "idle" $IdleSeconds }

Step "1.3 engine running" {
    Cmd $a sit "$loader left" | Out-Null
    Start-Sleep -Seconds 2
    Cmd $a engine "on" | Out-Null
    $r = Watch-Drift "engine" $IdleSeconds
    Cmd $a engine "off" | Out-Null
    Cmd $a stand | Out-Null
    Start-Sleep -Seconds 3
    $r
}

Step "1.3 test drive" {
    foreach ($name in $a, $b) { Cmd $name cardetails-drift "$loader snap drive" | Out-Null }
    Cmd $a testdrive-go "$loader" | Out-Null
    Wait-HarnessStatus -Instance $a -TimeoutSec 120 -What "test track" -Condition { param($s) $s.scene -match "(?i)track" -and $s.playable } | Out-Null
    Start-Sleep -Seconds 3
    Cmd $a testdrive-drive "800" | Out-Null
    Cmd $a testdrive-finish "all" | Out-Null
    Wait-InGarage $a 180
    Wait-Ready $a | Out-Null
    Start-Sleep -Seconds 8
    [ordered]@{
        A = Cmd $a cardetails-drift "$loader diff drive"
        B = Cmd $b cardetails-drift "$loader diff drive"
        differ = @(Differing)
        after = Watch-Drift "after-drive" 30
    }
}

Step "1.3 wheel swap" {
    foreach ($name in $a, $b) { Cmd $name cardetails-drift "$loader snap wheel" | Out-Null }
    $rims = @(Cmd $a wheel-parts "$loader" | Where-Object { $_.id -like "rim*" -and -not $_.unmounted } | ForEach-Object { $_.key })
    Cmd $a part-fast-unmount "$loader $($rims[0])" | Out-Null
    Start-Sleep -Seconds 2
    $new = Cmd $a give-group "wheel"
    Cmd $a wheel-mount "$loader $($rims[0]) $($new.UID)" | Out-Null
    Start-Sleep -Seconds 8
    [ordered]@{
        A = Cmd $a cardetails-drift "$loader diff wheel"
        B = Cmd $b cardetails-drift "$loader diff wheel"
        differ = @(Differing)
        after = Watch-Drift "after-wheel" 30
    }
}

$findings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Ctx.RunDir "findings.json")
$errors = @($findings.Keys | Where-Object { "$($findings[$_])" -like "ERROR:*" })
foreach ($e in $errors) { Write-Host "step error: $e $($findings[$e])" -ForegroundColor Yellow }
$Ctx.Result.notes += $failures
$Ctx.Result.passed = ($failures.Count -eq 0)

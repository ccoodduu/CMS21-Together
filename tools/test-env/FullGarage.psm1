# The full garage of multiplayer-soak-and-scale D7: parking unlocked to N levels and filled, a car on every garage
# loader (three models or more, 20 parts off, details randomized), an item on every slot machine, 300+ inventory
# items and one accepted job. The fill takes about 15 minutes, so its server save is kept as a fixture outside the
# repo (CMS21-TestInstalls\fixtures\full-garage_L<levels>_<version tag>.json, several MB) and reused while the
# server's --check-save loads it and the save and section versions match. Uses HarnessClient and ScaleSession.

$script:FixtureDir = "$env:USERPROFILE\CMS21-TestInstalls\fixtures"

# A tag for the save layout of the running server: its save version and the "Save sections" line it logs at start.
function Get-SaveVersionTag([string]$ServerDir) {
    $sections = @(Get-ServerLogLines | Where-Object { $_ -match "\[SessionRegistry\] Save sections: (.*)$" } | ForEach-Object { $Matches[1] }) | Select-Object -Last 1
    $save = Join-Path $ServerDir "Saves\server_save.json"
    $version = if (Test-Path -LiteralPath $save) { [regex]::Match([System.IO.File]::ReadAllText($save), '"SaveVersion"\s*:\s*(\d+)').Groups[1].Value } else { "" }
    if (-not $sections) { throw "The server log has no '[SessionRegistry] Save sections' line; cannot tag the fixture." }
    $text = "save v$version; sections $sections"
    $sha = [System.Security.Cryptography.SHA1]::Create()
    $hash = -join ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text)) | Select-Object -First 4 | ForEach-Object { $_.ToString("x2") })
    [pscustomobject]@{ Tag = $hash; Text = $text }
}

function Get-FullGarageFixturePath([int]$ParkingLevels, [string]$Tag) {
    Join-Path $script:FixtureDir "full-garage_L${ParkingLevels}_$Tag.json"
}

# Runs the lane server's --check-save on the file; returns its output when it loads, $null otherwise.
function Test-FullGarageFixture([string]$ServerDir, [string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $output = & (Join-Path $ServerDir "CMS21_Together_Server.exe") --check-save $Path | Out-String
    if ($LASTEXITCODE -ne 0) { Write-Host "--check-save refused $Path : $output"; return $null }
    return $output
}

# Stops the lane server, puts the fixture in place as its main save and starts it again (Run-Session restores the
# lane's own save after the run). Returns the start time in seconds and the load line.
function Install-FullGarageFixture([string]$ServerDir, [string]$Path) {
    Stop-TestServer
    $saves = Join-Path $ServerDir "Saves"
    New-Item -ItemType Directory -Force -Path $saves | Out-Null
    Get-ChildItem -LiteralPath $saves -Filter "server_save*.json" -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item -LiteralPath $Path -Destination (Join-Path $saves "server_save.json") -Force
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Start-TestServer | Out-Null
    $seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1)
    $lines = @(Get-ServerLogLines)
    [pscustomobject]@{
        StartS = $seconds
        Loaded = @($lines | Where-Object { $_ -match "Game session loaded from|Loaded the fallback|Cannot load" }) -join " / "
        Migrations = @($lines | Where-Object { $_ -match "Migrat" })
    }
}

function Save-FullGarageFixture([string]$ServerDir, [string]$Path, [string]$VersionText) {
    $mark = Get-ServerLogMark
    Send-ServerCommand "save"
    Wait-ServerLog -Pattern "Session successfully saved" -After $mark -TimeoutSec 60 | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path $Path -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $ServerDir "Saves\server_save.json") -Destination $Path -Force
    Set-Content -LiteralPath ([System.IO.Path]::ChangeExtension($Path, ".versions.txt")) -Value $VersionText -Encoding utf8
    return (Get-Item -LiteralPath $Path).Length
}

function Wait-LoaderReady([string]$Instance, [int]$Loader, [int]$TimeoutSec = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $r = Send-HarnessCommand -Instance $Instance -Verb car-ready -Arguments "$Loader"
        if ($r.state -eq "Ready" -and $r.loaded) { return $true }
        Start-Sleep -Milliseconds 700
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Get-FreeLoader([string]$Instance, [int]$LoaderCount) {
    $used = @(Send-HarnessCommand -Instance $Instance -Verb placement | ForEach-Object { [int]$_.loader })
    @(0..($LoaderCount - 1) | Where-Object { $used -notcontains $_ }) | Select-Object -First 1
}

# Fills the session through one connected client (-Filler). Returns a summary; problems are listed, not thrown,
# except when the garage cannot be filled at all.
function Invoke-FullGarageFill {
    param([string]$Filler, [int]$ParkingLevels = 2, [int]$PartsPerCar = 20, [int]$MinItems = 300, [string[]]$Models)
    $started = Get-Date
    $notes = New-Object System.Collections.Generic.List[string]
    Send-ServerCommand "money add 10000000"
    Start-Sleep -Seconds 2
    if (-not $Models -or $Models.Count -lt 3) { $Models = @(Get-BaseGameCars $Filler | Select-Object -First 6) }
    $loaderCount = @((Send-HarnessCommand -Instance $Filler -Verb dump).cars).Count
    Write-Host "FILL: $ParkingLevels parking levels, $loaderCount garage loaders, models $($Models -join ', ')"

    for ($guard = 0; $guard -lt 10; $guard++) {
        $parking = Send-HarnessCommand -Instance $Filler -Verb parking
        if ([int]$parking.levels -ge $ParkingLevels) { break }
        Send-HarnessCommand -Instance $Filler -Verb parking-unlock | Out-Null
        $deadline = (Get-Date).AddSeconds(15)
        do { Start-Sleep -Milliseconds 700; $now = Send-HarnessCommand -Instance $Filler -Verb parking } while ([int]$now.levels -eq [int]$parking.levels -and (Get-Date) -lt $deadline)
        if ([int]$now.levels -eq [int]$parking.levels) { $notes.Add("parking-unlock did not raise the level above $($parking.levels)"); break }
    }

    $jobId = $null
    try {
        Send-HarnessCommand -Instance $Filler -Verb orders-generate | Out-Null
        $deadline = (Get-Date).AddSeconds(30)
        do { Start-Sleep -Seconds 1; $orders = @((Send-HarnessCommand -Instance $Filler -Verb orders-list).jobs | Where-Object { -not $_.IsMission }) } while ($orders.Count -eq 0 -and (Get-Date) -lt $deadline)
        if ($orders.Count -gt 0) {
            $jobId = $orders[0].id
            Send-HarnessCommand -Instance $Filler -Verb orders-accept -Arguments "$jobId" | Out-Null
            Wait-HarnessDump -Instance $Filler -TimeoutSec 90 -What "accepted job active" -Condition { param($d) @($d.jobs.active).Count -ge 1 } | Out-Null
        } else { $notes.Add("no order to accept") }
    } catch { $notes.Add("job: $($_.Exception.Message)"); $jobId = $null }

    $parked = 0
    $modelIndex = 0
    while ($true) {
        $parking = Send-HarnessCommand -Instance $Filler -Verb parking
        if (@($parking.slots).Count -ge [int]$parking.max) { break }
        $loader = Get-FreeLoader $Filler $loaderCount
        if ($null -eq $loader) { $notes.Add("no free loader to fill the parking"); break }
        $model = $Models[$modelIndex++ % $Models.Count]
        Send-HarnessCommand -Instance $Filler -Verb car-spawn -Arguments "$loader $model 0" | Out-Null
        if (-not (Wait-LoaderReady $Filler $loader)) { $notes.Add("parking car $model on loader $loader not Ready"); Send-HarnessCommand -Instance $Filler -Verb car-delete -Arguments "$loader" | Out-Null; continue }
        try { Send-HarnessCommand -Instance $Filler -Verb cardetails-randomize -Arguments "$loader" | Out-Null } catch { }
        $before = @($parking.slots).Count
        Send-HarnessCommand -Instance $Filler -Verb park -Arguments "$loader" | Out-Null
        $deadline = (Get-Date).AddSeconds(30)
        do { Start-Sleep -Milliseconds 700; $count = @((Send-HarnessCommand -Instance $Filler -Verb parking).slots).Count } while ($count -le $before -and (Get-Date) -lt $deadline)
        if ($count -le $before) { $notes.Add("park of loader $loader did not fill a slot"); break }
        $parked = $count
    }

    $filled = 0
    for ($loader = 0; $loader -lt $loaderCount; $loader++) {
        $occupied = @(Send-HarnessCommand -Instance $Filler -Verb placement | Where-Object { [int]$_.loader -eq $loader }).Count -gt 0
        if (-not $occupied) {
            $model = $Models[$modelIndex++ % $Models.Count]
            Send-HarnessCommand -Instance $Filler -Verb car-spawn -Arguments "$loader $model 0" | Out-Null
        }
        if (-not (Wait-LoaderReady $Filler $loader)) { $notes.Add("garage loader $loader not Ready"); continue }
        $filled++
        $unmounted = 0
        for ($i = 0; $i -lt $PartsPerCar; $i++) {
            try { Send-HarnessCommand -Instance $Filler -Verb part-fast-unmount -Arguments "$loader" | Out-Null; $unmounted++ } catch { break }
        }
        if ($unmounted -lt $PartsPerCar) { $notes.Add("loader $loader`: $unmounted of $PartsPerCar parts off") }
        try { Send-HarnessCommand -Instance $Filler -Verb cardetails-randomize -Arguments "$loader" | Out-Null } catch { $notes.Add("loader $loader details: $($_.Exception.Message)") }
    }

    $machines = [ordered]@{
        TireChanger = { (Send-HarnessCommand -Instance $Filler -Verb give-group -Arguments "wheel").UID }
        WheelBalancer = { (Send-HarnessCommand -Instance $Filler -Verb give-group -Arguments "wheel").UID }
        SpringClamp = { (Send-HarnessCommand -Instance $Filler -Verb give-group -Arguments "shock").UID }
        BrakeLathe = { (Send-HarnessCommand -Instance $Filler -Verb give-item -Arguments "tarczaHamulcowa_1 0.4").UID }
        BatteryCharger = { (Send-HarnessCommand -Instance $Filler -Verb give-item -Arguments "akumulator 0.3").UID }
    }
    $onMachines = 0
    foreach ($tool in $machines.Keys) {
        try {
            $uid = & $machines[$tool]
            Send-HarnessCommand -Instance $Filler -Verb tool-put -Arguments "$tool $uid" | Out-Null
            $onMachines++
        } catch { $notes.Add("$tool`: $($_.Exception.Message)") }
    }

    $ids = @("tarczaHamulcowa_1", "akumulator")
    $items = @((Send-HarnessCommand -Instance $Filler -Verb dump).inventory.items).Count
    for ($i = 0; $items -lt $MinItems -and $i -lt $MinItems * 2; $i++) {
        try { Send-HarnessCommand -Instance $Filler -Verb give-item -Arguments "$($ids[$i % $ids.Count]) 0.5" | Out-Null; $items++ } catch { $notes.Add("give-item: $($_.Exception.Message)"); break }
    }
    Start-Sleep -Seconds 3
    $dump = Send-HarnessCommand -Instance $Filler -Verb dump
    $summary = [ordered]@{
        minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
        parkingLevels = $dump.placement.parking.levels
        parked = @($dump.placement.parking.slots).Count
        garageCars = @($dump.placement.cars).Count
        loaders = $loaderCount
        models = @($dump.placement.cars | ForEach-Object { $_.carToLoad } | Select-Object -Unique).Count
        machines = $onMachines
        items = @($dump.inventory.items).Count
        groups = @($dump.inventory.groups).Count
        activeJobs = @($dump.jobs.active).Count
        notes = @($notes)
    }
    Write-Host "FILL done: $($summary | ConvertTo-Json -Compress)"
    if ($summary.garageCars -lt 3) { throw "The fill put only $($summary.garageCars) cars in the garage ($($notes -join '; '))" }
    return [pscustomobject]$summary
}

Export-ModuleMember -Function Get-SaveVersionTag, Get-FullGarageFixturePath, Test-FullGarageFixture, Install-FullGarageFixture,
    Save-FullGarageFixture, Invoke-FullGarageFill

# Test areas for Run-All.ps1 -Areas/-Smoke/-Changed. Every scenario names its areas in a header line
# "# areas: a, b" (the pseudo-area "smoke" puts it in the smoke set); "# run-all: lane 3" marks a scale-lane scenario. $PathAreaTable maps a changed file to areas,
# first match wins (-like patterns, so * also crosses folders). "full" (harness core) and "unmapped" (mod code the
# table cannot place) run every scenario, "smoke" only the smoke set, "none" nothing. Files under
# tools/test-env/scenarios are handled before the table.

$script:KnownAreas = @(
    "connect", "presence", "guard", "cars", "parts", "placement", "details", "jobs", "economy", "tools",
    "testdrive", "persistence", "resync", "hosting", "bugreport", "release", "visuals", "driving", "outdoor", "locks", "ping", "shoplist"
)

$script:PathAreaTable = @'
*.md                                                           none
tools/test-env/HarnessClient.psm1                             full
tools/test-env/Run-Session.ps1                                 full
tools/test-env/TestLanes.psm1                                  full
tools/test-env/Test-ServerSaves.ps1                            none
tools/test-env/fixtures/mod-targets/*                          connect
tools/test-env/fixtures/server_save_*                          persistence
tools/test-env/LockSession.psm1                                locks
tools/test-env/*                                               smoke
tools/TestHarness/Features/BugReport*                          bugreport
tools/TestHarness/Features/Build*                              release, connect
tools/TestHarness/Features/CarDetails*                         details, cars
tools/TestHarness/Features/CarPlacement*                       placement
tools/TestHarness/Features/Car*                                cars, parts
tools/TestHarness/Features/Compat*                             connect
tools/TestHarness/Features/Digest*                             resync
tools/TestHarness/Features/Economy*                            economy
tools/TestHarness/Features/Outdoor*                            outdoor, economy, presence
tools/TestHarness/Features/Purchase*                           economy
tools/TestHarness/Features/GeneratorProbe*                     jobs
tools/TestHarness/Features/Jobs*                               jobs
tools/TestHarness/Features/Guard*                              guard
tools/TestHarness/Features/Join*                               connect, hosting
tools/TestHarness/Features/PlacementTrace*                     placement
tools/TestHarness/Features/Presence*                           presence
tools/TestHarness/Features/Scene*                              presence
tools/TestHarness/Features/SeatEngine*                         presence
tools/TestHarness/Features/ShopList*                           shoplist
tools/TestHarness/Features/Profile*                            persistence
tools/TestHarness/Features/TestDrive*                          testdrive
tools/TestHarness/Features/Tools*                              tools
tools/TestHarness/Features/Visual*                             visuals
tools/TestHarness/Features/Drive*                              driving
tools/TestHarness/Features/Lock*                               locks
tools/TestHarness/Features/Ping*                               ping
tools/TestHarness/*                                            full
tools/release/*                                                release
CMS21-Together-*/Network/Handlers/Admin*                       hosting
CMS21-Together-*/Network/Handlers/Auth*                        connect
CMS21-Together-*/Network/Handlers/BugReport*                   bugreport
CMS21-Together-*/Network/Handlers/CarDetails*                  details, cars
CMS21-Together-*/Network/Handlers/CarParts*                    parts, cars
CMS21-Together-*/Network/Handlers/Lock*                        locks, parts, placement, economy
CMS21-Together-*/Network/Handlers/Car*                         cars, parts
CMS21-Together-*/Network/Handlers/Digest*                      resync
CMS21-Together-*/Network/Handlers/Economy*                     economy
CMS21-Together-*/Network/Handlers/Outdoor*                     outdoor, economy, presence
CMS21-Together-*/Network/Handlers/GarageUpgrade*               economy, placement
CMS21-Together-*/Network/Handlers/Inventory*                   parts, economy
CMS21-Together-*/Network/Handlers/Job*                         jobs
CMS21-Together-*/Network/Handlers/Parking*                     placement
CMS21-Together-*/Network/Handlers/Placement*                   placement
CMS21-Together-*/Network/Handlers/Player*                      presence
CMS21-Together-*/Network/Handlers/ShopList*                    shoplist
CMS21-Together-*/Network/Handlers/Shop*                        economy
CMS21-Together-*/Network/Handlers/Stats*                       economy, persistence
CMS21-Together-*/Network/Handlers/TestDrive*                   testdrive
CMS21-Together-*/Network/Handlers/Tool*                        tools
CMS21-Together-*/Network/Handlers/Visual*                      visuals, presence
CMS21-Together-*/Network/Handlers/Drive*                       driving, testdrive
CMS21-Together-*/Network/Handlers/Ping*                        ping
CMS21-Together-*/Network/Handlers/WorldStates*                 connect, persistence
CMS21-Together-*/Diagnostics/*                                 bugreport
CMS21-Together-*/Properties/*                                  smoke
CMS21-Together-*/*.csproj                                      smoke
CMS21-Together-Core/Network/Packets/LockPackets.cs             locks, parts, placement, economy
CMS21-Together-Core/Network/Packets/Car*                       cars, parts, details
CMS21-Together-Core/Network/Packets/Digest*                    resync
CMS21-Together-Core/Network/Packets/Diagnostics*               bugreport
CMS21-Together-Core/Network/Packets/Economy*                   economy
CMS21-Together-Core/Network/Packets/Outdoor*                   outdoor, economy, presence
CMS21-Together-Core/Data/Outdoor/*                             outdoor, economy, presence
CMS21-Together-Core/Network/Packets/Garage*                    economy, placement
CMS21-Together-Core/Network/Packets/Inventory*                 parts, economy
CMS21-Together-Core/Network/Packets/Job*                       jobs
CMS21-Together-Core/Network/Packets/Placement*                 placement
CMS21-Together-Core/Network/Packets/Player*                    presence
CMS21-Together-Core/Network/Packets/Session*                   connect, hosting
CMS21-Together-Core/Network/Packets/ShopList*                  shoplist
CMS21-Together-Core/Network/Packets/Shop*                      economy
CMS21-Together-Core/Network/Packets/Start*                     connect
CMS21-Together-Core/Network/Packets/Stats*                     economy, persistence
CMS21-Together-Core/Network/Packets/TestDrive*                 testdrive
CMS21-Together-Core/Network/Packets/Tool*                      tools
CMS21-Together-Core/Network/Packets/VisualPackets.cs           visuals, presence
CMS21-Together-Core/Network/Packets/DrivePackets.cs            driving, testdrive
CMS21-Together-Core/Network/Packets/PingPackets.cs             ping
CMS21-Together-Core/Network/Packets/WorldStates*               connect, persistence
CMS21-Together-Core/PacketTypes.cs                             smoke
CMS21-Together-Core/Data/Compatibility/*                       connect
CMS21-Together-Core/Data/Digest/*                              resync
CMS21-Together-Core/Data/JobsState.cs                          jobs
CMS21-Together-Core/Data/ShopListState.cs                      shoplist
CMS21-Together-Core/Data/InventoryState.cs                     parts, economy
CMS21-Together-Core/Data/UidRanges.cs                          parts
CMS21-Together-Core/Data/ParkingLayout.cs                      placement
CMS21-Together-Core/Data/Player*                               presence, persistence
CMS21-Together-Core/Data/GameType/ModJob*                      jobs
CMS21-Together-Core/Data/GameType/ModTool*                     tools
CMS21-Together-Core/Data/GameType/ModCarDetails*               details
CMS21-Together-Core/Data/GameType/ModPaint*                    details, tools
CMS21-Together-Core/Data/GameType/ModTuning*                   details
CMS21-Together-Core/Data/GameType/ModWheel*                    details, tools
CMS21-Together-Core/Data/GameType/ModLPData*                   details
CMS21-Together-Core/Data/GameType/ModItem*                     parts, economy
CMS21-Together-Core/Data/GameType/ModGroupItem*                parts, economy
CMS21-Together-Core/Data/GameType/ModPartInfo*                 parts
CMS21-Together-Core/Data/GameType/PartProperty*                parts
CMS21-Together-Client/Logic/Car/Locks/*                        locks, parts, placement
CMS21-Together-Client/Logic/Car/Parts/*                        parts, cars
CMS21-Together-Client/Logic/Car/Details/*                      details, cars
CMS21-Together-Client/Logic/Car/Placement/*                    placement, cars
CMS21-Together-Client/Logic/Car/Away/*                         testdrive, cars
CMS21-Together-Client/Logic/Car/CarDlc.cs                      cars, connect
CMS21-Together-Client/Logic/Car/*                              cars
CMS21-Together-Client/Logic/Economy/*                          economy
CMS21-Together-Client/Logic/Outdoor/*                          outdoor, economy, presence
CMS21-Together-Client/Logic/Garage/*                           economy, placement
CMS21-Together-Client/Logic/Hook/CarSpawnHooks.cs              cars
CMS21-Together-Client/Logic/Hook/InventoryHook.cs              parts, economy
CMS21-Together-Client/Logic/Hook/NotificationCenterItemsHook.cs parts
CMS21-Together-Client/Logic/Hook/SceneHooks.cs                 presence
CMS21-Together-Client/Logic/Hook/*WindowHook.cs                economy
CMS21-Together-Client/Logic/Jobs/*                             jobs
CMS21-Together-Client/Logic/ShopList/*                         shoplist
CMS21-Together-Client/Logic/Player/*                           presence
CMS21-Together-Client/Logic/PlayerInstance.cs                  presence
CMS21-Together-Client/Logic/Reconciliation/*                   resync
CMS21-Together-Client/Logic/Tools/*                            tools
CMS21-Together-Client/Logic/Visuals/*                          visuals, presence
CMS21-Together-Client/Logic/Driving/*                          driving, testdrive
CMS21-Together-Client/Logic/Pings/*                            ping
CMS21-Together-Client/Logic/UidRange.cs                        parts
CMS21-Together-Client/Logic/LoaderAddition.cs                  connect
CMS21-Together-Client/Guard/*                                  guard
CMS21-Together-Client/Compatibility/*                          connect
CMS21-Together-Client/Persistence/*                            persistence
CMS21-Together-Client/Session/LocalServerHost.cs               hosting
CMS21-Together-Client/Session/ResyncController.cs              resync
CMS21-Together-Client/Session/ServerWatchdog.cs                persistence
CMS21-Together-Client/Session/*                                connect, hosting
CMS21-Together-Client/UI/*                                     connect, hosting
CMS21-Together-Client/Data/ClientScene.cs                      presence, connect
CMS21-Together-Server/Data/Cars/CarLocks*                      locks, parts, placement, economy
CMS21-Together-Server/Data/Cars/CarDetails*                    details, cars
CMS21-Together-Server/Data/Cars/CarAway*                       testdrive, cars
CMS21-Together-Server/Data/Cars/InventoryChanges.cs            parts, economy
CMS21-Together-Server/Data/Cars/*                              cars, parts
CMS21-Together-Server/Data/Economy/*                           economy
CMS21-Together-Server/Data/Outdoor/*                           outdoor, economy, presence
CMS21-Together-Server/Data/PricingCalculator.cs                economy
CMS21-Together-Server/Data/Jobs/*                              jobs
CMS21-Together-Server/Data/ShopList/*                          shoplist
CMS21-Together-Server/Data/Persistence/*                       persistence
CMS21-Together-Server/Data/Placement/*                         placement
CMS21-Together-Server/Data/Presence/*                          presence, persistence
CMS21-Together-Server/Data/Reconciliation/*                    resync
CMS21-Together-Server/Data/Tools/*                             tools
CMS21-Together-Server/Data/CompatibilityPolicy.cs              connect
CMS21-Together-Server/Data/ModCheck.cs                         connect
CMS21-Together-Server/Data/SharedDlc.cs                        connect, cars
CMS21-Together-Server/Data/ServerConfig.cs                     hosting, connect
CMS21-Together-Server/Network/Command*                         hosting, persistence
CMS21-Together-Client/*                                        unmapped
CMS21-Together-Server/*                                        unmapped
CMS21-Together-Core/*                                          unmapped
Directory.Build.props                                          smoke
*.sln                                                          smoke
*                                                              none
'@ -split "\r?\n" | Where-Object { $_.Trim() } | ForEach-Object {
    $pattern, $rest = $_.Trim() -split '\s+', 2
    [pscustomobject]@{ Pattern = $pattern; Areas = @($rest -split '[,\s]+' | Where-Object { $_ }) }
}

foreach ($row in $script:PathAreaTable) {
    foreach ($area in $row.Areas) {
        if ($area -notin $script:KnownAreas + @("full", "unmapped", "smoke", "none")) { throw "TestAreas.psm1: unknown area '$area' for $($row.Pattern)" }
    }
}

function Get-KnownAreas { $script:KnownAreas }

function Get-ScenarioHeader([string]$Path) {
    $runAll = @()
    $areas = $null
    foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8 -TotalCount 40) {
        $line = $line.TrimStart([char]0xFEFF)
        if ($line -notmatch '^\s*#') {
            if ($line.Trim()) { break }
            continue
        }
        if ($line -match '^\s*#\s*run-all\s*:(.*)$') { $runAll += @($Matches[1] -split '[,\s]+' | Where-Object { $_ }) }
        elseif ($line -match '^\s*#\s*areas\s*:(.*)$') { $areas = @($Matches[1] -split '[,\s]+' | Where-Object { $_ }) }
    }
    [pscustomobject]@{
        Name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
        Skip = $runAll -contains "skip"
        Lane3 = ($runAll -join " ") -match '\blane 3\b'
        Smoke = $areas -contains "smoke"
        HasAreas = $null -ne $areas
        Areas = @($areas | Where-Object { $_ -ne "smoke" })
        UnknownAreas = @($areas | Where-Object { $_ -ne "smoke" -and $_ -notin $script:KnownAreas })
    }
}

function Get-ScenarioHeaders([string]$ScenarioDir) {
    $headers = [ordered]@{}
    Get-ChildItem -LiteralPath $ScenarioDir -Filter "*.ps1" | Sort-Object Name |
        ForEach-Object { $headers[$_.BaseName] = Get-ScenarioHeader $_.FullName }
    return $headers
}

function Get-ChangedFiles([string]$Repo, [string]$Ref) {
    $base = git -C $Repo merge-base $Ref HEAD
    if ($LASTEXITCODE -ne 0 -or -not $base) { throw "No merge base between '$Ref' and HEAD" }
    $tracked = @(git -C $Repo -c core.quotepath=off diff --name-only --no-renames $base)
    $untracked = @(git -C $Repo -c core.quotepath=off ls-files --others --exclude-standard)
    [pscustomobject]@{ Base = $base; Files = @($tracked + $untracked | Where-Object { $_ } | Sort-Object -Unique) }
}

function Resolve-ChangedFile([string]$File, $Headers) {
    $path = $File -replace '\\', '/'
    if ($path -match '^tools/test-env/scenarios/([^/]+?)(\.launch\.psd1|\.ps1)$') {
        $name = $Matches[1]
        if ($Headers.Contains($name)) {
            return [pscustomobject]@{ File = $path; Kind = "scenario"; Scenario = $name; Areas = @() }
        }
        return [pscustomobject]@{ File = $path; Kind = "none"; Scenario = $null; Areas = @() }
    }
    foreach ($row in $script:PathAreaTable) {
        if ($path -like $row.Pattern) {
            $kind = if ($row.Areas[0] -in @("full", "unmapped", "smoke", "none")) { $row.Areas[0] } else { "areas" }
            return [pscustomobject]@{ File = $path; Kind = $kind; Scenario = $null; Areas = @($row.Areas | Where-Object { $_ -in $script:KnownAreas }) }
        }
    }
}

Export-ModuleMember -Function Get-KnownAreas, Get-ScenarioHeader, Get-ScenarioHeaders, Get-ChangedFiles, Resolve-ChangedFile
